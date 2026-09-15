using System;
using UnityEngine;

namespace Fitts.Net
{
    /// <summary>
    /// Main-thread bridge to the CORC M2 state machine.
    ///
    /// Owns the socket, performs the handshake, drains the receive queues once per Update and
    /// re-raises everything as typed C# events, so that no other script ever touches the network.
    /// Also maintains the round-trip estimate and the Unity-to-CORC clock offset, which the
    /// display log needs in order to state what the visual feedback latency actually was.
    /// </summary>
    public class M2Link : MonoBehaviour
    {
        [Header("Server (CORC)")]
        [Tooltip("Address CORC binds its FLNL server to - the 'ip' key of M2FittsHuman.conf.")]
        public string serverIp = "169.254.105.3";
        public int serverPort = 2048;

        [Header("Behaviour")]
        public bool connectOnStart = true;
        [Tooltip("Seconds between reconnection attempts while disconnected.")]
        public float reconnectInterval = 2f;
        [Tooltip("Seconds between PING probes used to estimate round trip and clock offset.")]
        public float pingInterval = 1f;

        [Header("Status (read only)")]
        public bool connected;
        public bool handshakeDone;
        public float roundTripMs;
        public long corruptFrames;
        public float measuredStreamHz;

        // ---- events (all raised on the main thread, inside Update)
        public event Action<SessionInfo> OnSession;
        public event Action<TrialInfo> OnTrial;
        public event Action<int, double, int, double> OnHit;   // trialIndex, MT, nEntries, x_sel_cm
        public event Action<int> OnMiss;                       // trialIndex
        public event Action<Vector2> OnReturn;                 // away position, robot frame
        public event Action<Vector2, double, double> OnDrag;   // origin, tolerance, max time
        public event Action<Vector2> OnOrigin;
        public event Action<double, int, FittsPhase> OnRest;   // duration, trials done, phase
        public event Action<bool> OnReady;                     // requires an explicit go signal?
        public event Action OnWaiting;
        public event Action OnStandby;
        public event Action<int> OnEnd;                        // total trials
        public event Action<string> OnProtocolError;
        public event Action<bool> OnConnectionChanged;
        /// <summary>Every decoded command, for the display log. Raised after the typed event.</summary>
        public event Action<FlnlCommand> OnAnyCommand;

        public RobotState State { get; private set; }
        public SessionInfo Session { get; private set; }

        /// <summary>
        /// corcTime ≈ unityTime + ClockOffset. Estimated from PING/PONG assuming a symmetric
        /// round trip; accurate to roughly half the round trip (sub-millisecond on a direct link).
        /// </summary>
        public double ClockOffset { get; private set; }
        public bool ClockOffsetValid { get; private set; }

        private readonly FlnlClient client = new FlnlClient();
        private float nextReconnect;
        private float nextPing;
        private double pingSentAt;
        private bool pingPending;
        private long lastValueFrames;
        private float lastRateSample;

        void Start()
        {
            if (connectOnStart) TryConnect();
        }

        public void TryConnect()
        {
            if (client.IsConnected) return;

            if (client.Connect(serverIp, serverPort))
            {
                Debug.Log($"[M2Link] Connected to CORC at {serverIp}:{serverPort}.");
                handshakeDone = false;
                Debug.Log(
                    $"[M2Link] Sending HELLO: " +
                    $"version={FittsProtocol.Version}"
                );
                client.SendCommand(FittsProtocol.CmdHello,
                                   new double[] { FittsProtocol.Version, Time.realtimeSinceStartupAsDouble });
            }
            else
            {
                Debug.LogWarning($"[M2Link] Connection failed: {client.LastError}");
            }
        }

        public void Disconnect() => client.Disconnect();

        void Update()
        {
            bool nowConnected = client.IsConnected;
            if (nowConnected != connected)
            {
                connected = nowConnected;
                if (!connected) handshakeDone = false;
                OnConnectionChanged?.Invoke(connected);
            }

            if (!connected)
            {
                if (Time.unscaledTime >= nextReconnect)
                {
                    nextReconnect = Time.unscaledTime + reconnectInterval;
                    TryConnect();
                }
                return;
            }

            DrainCommands();
            DrainState();
            UpdateProbes();

            corruptFrames = client.CorruptFrames;
        }

        private void DrainCommands()
        {
            while (client.TryDequeueCommand(out FlnlCommand c))
            {
                Debug.Log(
                    $"[M2Link] Received command: {c.Cmd}, " +
                    $"params = {c.Params?.Length ?? 0}"
                );

                switch (c.Cmd)
                {
                    case FittsProtocol.CmdSession:
                        Session = SessionInfo.FromParams(c.Params);
                        if (Session.Valid)
                        {
                            handshakeDone = true;
                            if (Session.Version != FittsProtocol.Version)
                                OnProtocolError?.Invoke(
                                    $"CORC speaks protocol v{Session.Version}, this client v{FittsProtocol.Version}.");
                            OnSession?.Invoke(Session);
                        }
                        break;

                    case FittsProtocol.CmdTrial:
                    {
                        TrialInfo t = TrialInfo.FromParams(c.Params);
                        if (t.Valid) OnTrial?.Invoke(t);
                        break;
                    }

                    case FittsProtocol.CmdHit:
                        OnHit?.Invoke((int)c.P(0), c.P(1), (int)c.P(2), c.P(3));
                        break;

                    case FittsProtocol.CmdMiss:
                        OnMiss?.Invoke((int)c.P(0));
                        break;

                    case FittsProtocol.CmdReturn:
                        OnReturn?.Invoke(new Vector2((float)c.P(0), (float)c.P(1)));
                        break;

                    case FittsProtocol.CmdDrag:
                        OnDrag?.Invoke(new Vector2((float)c.P(0), (float)c.P(1)), c.P(2), c.P(3));
                        break;

                    case FittsProtocol.CmdOrigin:
                        OnOrigin?.Invoke(new Vector2((float)c.P(0), (float)c.P(1)));
                        break;

                    case FittsProtocol.CmdRest:
                        OnRest?.Invoke(c.P(0), (int)c.P(1), (FittsPhase)(int)c.P(2));
                        break;

                    case FittsProtocol.CmdReady:
                        OnReady?.Invoke(c.P(0) > 0.5);
                        break;

                    case FittsProtocol.CmdWait:
                        OnWaiting?.Invoke();
                        break;

                    case FittsProtocol.CmdStandby:
                        OnStandby?.Invoke();
                        break;

                    case FittsProtocol.CmdEnd:
                        OnEnd?.Invoke((int)c.P(0));
                        break;

                    case FittsProtocol.CmdPong:
                        HandlePong(c);
                        break;

                    case FittsProtocol.CmdVersionError:
                        handshakeDone = false;
                        OnProtocolError?.Invoke(
                            $"CORC refused the handshake: server protocol v{(int)c.P(0)}, client v{FittsProtocol.Version}.");
                        break;

                    case FittsProtocol.CmdAck:
                        break;

                    default:
                        Debug.LogWarning($"[M2Link] Unknown command '{c.Cmd}' ignored.");
                        break;
                }

                OnAnyCommand?.Invoke(c);
            }
        }

        private void DrainState()
        {
            if (!client.TryGetValues(out double[] v)) return;

            if (v.Length != FittsProtocol.StateLength)
            {
                OnProtocolError?.Invoke(
                    $"State vector has {v.Length} values, expected {FittsProtocol.StateLength}. " +
                    "The registration order in M2FittsHumanMachine::init() and FittsProtocol.cs disagree.");
                return;
            }
            State = RobotState.FromValues(v);
        }

        private void UpdateProbes()
        {
            // Effective stream rate, to confirm ui_stream_divider is doing what the log claims.
            if (Time.unscaledTime - lastRateSample >= 1f)
            {
                measuredStreamHz = (client.ValueFrames - lastValueFrames) / (Time.unscaledTime - lastRateSample);
                lastValueFrames = client.ValueFrames;
                lastRateSample = Time.unscaledTime;
            }

            if (!handshakeDone || Time.unscaledTime < nextPing) return;
            nextPing = Time.unscaledTime + pingInterval;
            pingSentAt = client.Now;
            pingPending = true;
            client.SendCommand(FittsProtocol.CmdPing, new double[] { Time.realtimeSinceStartupAsDouble });
        }

        private void HandlePong(FlnlCommand c)
        {
            if (!pingPending) return;
            pingPending = false;

            double rtt = client.Now - pingSentAt;
            roundTripMs = (float)(rtt * 1000.0);
            client.SendCommand(FittsProtocol.CmdRoundTrip, new double[] { roundTripMs });

            // c.P(0) is the Unity stamp echoed back; c.P(1) is the CORC time at which the server
            // handled the PING. Assuming a symmetric round trip, the server handled it about
            // rtt/2 after the stamp was taken.
            double unitySendStamp = c.P(0);
            double corcTimeAtServer = c.P(1);
            ClockOffset = corcTimeAtServer - (unitySendStamp + rtt * 0.5);
            ClockOffsetValid = true;
        }

        // ------------------------------------------------------------------ outbound

        public void SendGo() => client.SendCommand(FittsProtocol.CmdGo);
        public void SendSkip() => client.SendCommand(FittsProtocol.CmdSkip);
        public void SendAbort() => client.SendCommand(FittsProtocol.CmdAbort);

        /// <summary>
        /// Time-stamp a display-side event in CORC's 500 Hz log (UIMarkCode / UIMarkClientTime /
        /// UIMarkServerTime columns). Use it on the frame a target is actually presented, so that
        /// display latency can be quantified offline rather than assumed.
        /// </summary>
        public void SendMark(double code)
        {
            client.SendCommand(FittsProtocol.CmdMark,
                               new double[] { code, Time.realtimeSinceStartupAsDouble });
        }

        void OnDisable() => Disconnect();
        void OnApplicationQuit() => Disconnect();
    }
}
