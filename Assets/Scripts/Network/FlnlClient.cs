using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace Fitts.Net
{
    /// <summary>
    /// TCP client speaking the libFLNL framing used by CORC's FLNLHelper
    /// (255-byte frames, 'V' = values, 'C' = 4-character command + double parameters,
    /// last byte = XOR checksum over bytes [2 .. 253]).
    /// Reference for the frame layout: libFLNL by V. Crocher (github.com/vcrocher/libFLNL).
    ///
    /// This implementation differs from the reference Unity demo client in three ways that
    /// matter for an experiment rather than a demo:
    ///
    ///  1. Framed, buffered reads. TCP is a byte stream: a single Read() can return a partial
    ///     frame or several coalesced frames. Discarding anything that is not exactly 255 bytes
    ///     (as the demo client does) silently drops protocol-critical commands such as TRIA and
    ///     HITT. Here bytes are accumulated and complete frames are extracted, with resynchronisation
    ///     if the stream ever loses alignment.
    ///
    ///  2. Commands are queued, not overwritten. CORC runs at 500 Hz and Unity polls at frame rate,
    ///     so two commands can easily arrive within one rendered frame (e.g. HITT then RETN). A
    ///     single-slot buffer loses the first. State *values* remain latest-only, which is the
    ///     correct real-time behaviour for a continuous signal.
    ///
    ///  3. Cooperative shutdown (flag + socket close + join) instead of Thread.Abort, which is
    ///     deprecated, unsupported outside Mono, and leaks threads across Editor domain reloads.
    ///
    /// The class is Unity-independent (no UnityEngine types) so that the framing can be exercised
    /// by a plain unit test.
    /// </summary>
    public class FlnlClient
    {
        private const int MessageSize = 255;
        private const int CmdSize = 4;
        private const int DoubleSize = 8;
        private const byte InitValueCode = (byte)'V';
        private const byte InitCmdCode = (byte)'C';

        /// <summary>Maximum number of doubles a frame can carry: floor((255 - 3 - 4) / 8) = 31.</summary>
        public static int MaxValues => (MessageSize - 3 - CmdSize) / DoubleSize;

        private TcpClient client;
        private NetworkStream stream;
        private Thread rxThread;
        private volatile bool running;

        private readonly ConcurrentQueue<FlnlCommand> cmdQueue = new ConcurrentQueue<FlnlCommand>();
        private readonly object valuesLock = new object();
        private double[] latestValues;
        private volatile bool hasValues;

        private readonly byte[] txBuffer = new byte[MessageSize];
        private readonly object txLock = new object();

        private readonly Stopwatch clock = Stopwatch.StartNew();

        /// <summary>Frames discarded because of a checksum or header error. Should stay at 0.</summary>
        public long CorruptFrames { get; private set; }
        /// <summary>Total value frames decoded, for a sanity check on the effective stream rate.</summary>
        public long ValueFrames { get; private set; }
        public long CommandFrames { get; private set; }

        public string LastError { get; private set; } = "";

        public bool IsConnected => running && client != null && client.Connected;

        public bool Connect(string ip, int port, int timeoutMs = 2000)
        {
            if (!BitConverter.IsLittleEndian)
            {
                LastError = "Big-endian host: libFLNL frames carry native-endian doubles and CORC " +
                            "runs little-endian. Refusing to connect rather than decode garbage.";
                return false;
            }

            Disconnect();

            try
            {
                client = new TcpClient();
                client.NoDelay = true;                          // commands must not wait for Nagle
                client.ReceiveBufferSize = MessageSize * 256;
                client.SendBufferSize = MessageSize * 8;

                var connectTask = client.ConnectAsync(ip, port);
                if (!connectTask.Wait(timeoutMs) || !client.Connected)
                {
                    LastError = $"Timed out connecting to {ip}:{port}.";
                    CloseSocket();
                    return false;
                }

                stream = client.GetStream();
                running = true;
                rxThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "FLNL-rx" };
                rxThread.Start();
                LastError = "";
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                CloseSocket();
                return false;
            }
        }

        public void Disconnect()
        {
            running = false;
            CloseSocket();                       // unblocks the blocking Read in the rx thread

            if (rxThread != null && rxThread.IsAlive)
            {
                rxThread.Join(500);
                rxThread = null;
            }

            while (cmdQueue.TryDequeue(out _)) { }
            hasValues = false;
        }

        private void CloseSocket()
        {
            try { stream?.Close(); } catch { /* closing a broken stream is not an error here */ }
            try { client?.Close(); } catch { }
            stream = null;
            client = null;
        }

        // ------------------------------------------------------------------ receive

        private void ReceiveLoop()
        {
            byte[] chunk = new byte[MessageSize * 16];
            byte[] acc = new byte[MessageSize * 32];
            int accLen = 0;

            try
            {
                while (running)
                {
                    int n = stream.Read(chunk, 0, chunk.Length);
                    if (n <= 0) break;                       // server closed the connection

                    // Grow-free accumulation: drop the oldest bytes rather than reallocate if the
                    // buffer ever fills (only possible if this thread is starved for >1 s).
                    if (accLen + n > acc.Length)
                    {
                        int keep = acc.Length - n;
                        Buffer.BlockCopy(acc, accLen - keep, acc, 0, keep);
                        accLen = keep;
                    }
                    Buffer.BlockCopy(chunk, 0, acc, accLen, n);
                    accLen += n;

                    int consumed = 0;
                    while (accLen - consumed >= MessageSize)
                    {
                        if (TryDecode(acc, consumed))
                        {
                            consumed += MessageSize;
                        }
                        else
                        {
                            // Lost alignment (or a corrupt frame): skip one byte and resynchronise
                            // on the next plausible header.
                            CorruptFrames++;
                            consumed++;
                            while (consumed < accLen &&
                                   acc[consumed] != InitValueCode && acc[consumed] != InitCmdCode)
                            {
                                consumed++;
                            }
                        }
                    }

                    if (consumed > 0)
                    {
                        accLen -= consumed;
                        if (accLen > 0) Buffer.BlockCopy(acc, consumed, acc, 0, accLen);
                    }
                }
            }
            catch (Exception e)
            {
                if (running) LastError = e.Message;
            }
            finally
            {
                running = false;
            }
        }

        private bool TryDecode(byte[] buf, int offset)
        {
            byte header = buf[offset];
            if (header != InitValueCode && header != InitCmdCode) return false;
            if (Checksum(buf, offset) != buf[offset + MessageSize - 1]) return false;

            int count = buf[offset + 1];
            if (count > MaxValues) return false;

            if (header == InitValueCode)
            {
                double[] v = new double[count];
                for (int i = 0; i < count; i++)
                    v[i] = BitConverter.ToDouble(buf, offset + 2 + i * DoubleSize);

                lock (valuesLock) { latestValues = v; }
                hasValues = true;                 // latest-only: a stale state frame is worthless
                ValueFrames++;
            }
            else
            {
                char[] c = new char[CmdSize];
                for (int i = 0; i < CmdSize; i++) c[i] = (char)buf[offset + 2 + i];

                double[] p = new double[count];
                for (int i = 0; i < count; i++)
                    p[i] = BitConverter.ToDouble(buf, offset + 2 + CmdSize + i * DoubleSize);

                cmdQueue.Enqueue(new FlnlCommand
                {
                    // libFLNL pads short commands with NUL, so "OK" arrives as "OK\0\0".
                    Cmd = new string(c).TrimEnd('\0', ' '),
                    Params = p,
                    ArrivalTime = clock.Elapsed.TotalSeconds
                });
                CommandFrames++;
            }
            return true;
        }

        // ------------------------------------------------------------------ send

        public bool SendCommand(string cmd, double[] parameters = null)
        {
            if (!IsConnected) return false;
            if (string.IsNullOrEmpty(cmd) || cmd.Length > CmdSize)
            {
                LastError = $"Command '{cmd}' must be 1..{CmdSize} characters.";
                return false;
            }
            parameters ??= Array.Empty<double>();
            if (parameters.Length > MaxValues)
            {
                LastError = $"Too many parameters ({parameters.Length} > {MaxValues}).";
                return false;
            }

            lock (txLock)
            {
                Array.Clear(txBuffer, 0, MessageSize);
                txBuffer[0] = InitCmdCode;
                txBuffer[1] = (byte)parameters.Length;
                for (int i = 0; i < cmd.Length; i++) txBuffer[2 + i] = (byte)cmd[i];
                for (int i = 0; i < parameters.Length; i++)
                    BitConverter.GetBytes(parameters[i]).CopyTo(txBuffer, 2 + CmdSize + i * DoubleSize);
                txBuffer[MessageSize - 1] = Checksum(txBuffer, 0);

                try
                {
                    stream.Write(txBuffer, 0, MessageSize);
                    return true;
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    running = false;
                    return false;
                }
            }
        }

        // ------------------------------------------------------------------ polling

        public bool TryDequeueCommand(out FlnlCommand cmd) => cmdQueue.TryDequeue(out cmd);

        public bool TryGetValues(out double[] values)
        {
            if (!hasValues)
            {
                values = null;
                return false;
            }
            lock (valuesLock) { values = latestValues; }
            hasValues = false;
            return values != null;
        }

        /// <summary>Unity-independent monotonic clock shared with command arrival stamps.</summary>
        public double Now => clock.Elapsed.TotalSeconds;

        private static byte Checksum(byte[] buf, int offset)
        {
            byte ck = 0;
            for (int i = 2; i < MessageSize - 1; i++) ck ^= buf[offset + i];
            return ck;
        }
    }
}
