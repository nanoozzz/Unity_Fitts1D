using System;

namespace Fitts.Net
{
    /// <summary>
    /// The wire contract between CORC (M2FittsHumanMachine, authority) and this client.
    ///
    /// Every field order here MUST match M2FittsHumanMachine.h / M2FittsHumanStates.cpp.
    /// The version is checked at handshake: if CORC replies VERR, the session is refused
    /// rather than run with a silently mis-parsed protocol.
    /// </summary>
    public static class FittsProtocol
    {
        public const int Version = 1;

        // ---- commands received from CORC
        public const string CmdSession = "SESS";
        public const string CmdTrial = "TRIA";
        public const string CmdHit = "HITT";
        public const string CmdMiss = "MISS";
        public const string CmdReturn = "RETN";
        public const string CmdDrag = "DRAG";
        public const string CmdOrigin = "ORIG";
        public const string CmdRest = "REST";
        public const string CmdReady = "RDY!";
        public const string CmdWait = "WAIT";
        public const string CmdStandby = "STBY";
        public const string CmdEnd = "ENDE";
        public const string CmdPong = "PONG";
        public const string CmdVersionError = "VERR";
        public const string CmdAck = "OK";

        // ---- commands sent to CORC
        public const string CmdHello = "HELO";
        public const string CmdGo = "GTNS";
        public const string CmdSkip = "SKIP";
        public const string CmdAbort = "ABRT";
        public const string CmdPing = "PING";
        public const string CmdRoundTrip = "RTTR";
        public const string CmdMark = "MARK";

        /// <summary>Number of doubles in the streamed state vector.</summary>
        public const int StateLength = 7; // 13

        // Indices into the streamed state vector.
        public const int SITime = 0;
        public const int SIPosX = 1;
        public const int SIPosY = 2;
        public const int SIVelX = 3;
        public const int SIVelY = 4;
        public const int SIForceX = 5;
        public const int SIForceY = 6;
        /*public const int SIStateCode = 7;
        public const int SIPhase = 8;
        public const int SITrialIndex = 9;
        public const int SITargetX = 10;
        public const int SIHalfWidth = 11;
        public const int SIDwell = 12;*/
    }

    /// <summary>Mirrors FittsStateCode in M2FittsHumanStates.h.</summary>
    public enum FittsStateCode
    {
        Calibration = 0,
        Standby = 1,
        Ready = 2,
        Reach = 3,
        Return = 4,
        Break = 5,
        End = 6,
        WaitUI = 7
    }

    /// <summary>Mirrors FittsPhase in M2FittsHumanStates.h.</summary>
    public enum FittsPhase
    {
        Warmup = 0,
        Block = 1,
        Done = 2
    }

    /// <summary>
    /// Latest robot state, decoded from a 'V' frame. All quantities are in the robot frame
    /// and in SI units. CorcTime is the state machine running time: the same clock as
    /// t_onset/t_end in the CORC results csv and as the Time column of the raw log.
    /// </summary>
    public struct RobotState
    {
        public double CorcTime;
        public double X, Y;
        public double VX, VY;
        public double FX, FY;
        public FittsStateCode StateCode;
        public FittsPhase Phase;
        public int TrialIndex;
        public double TargetX;
        public double HalfWidth;
        public double DwellProgress;   // 0..1, computed by CORC - authoritative
        public bool Valid;

        public static RobotState FromValues(double[] v)
        {
            RobotState s = default;
            if (v == null || v.Length < FittsProtocol.StateLength) return s;

            s.CorcTime = v[FittsProtocol.SITime];
            s.X = v[FittsProtocol.SIPosX];
            s.Y = v[FittsProtocol.SIPosY];
            s.VX = v[FittsProtocol.SIVelX];
            s.VY = v[FittsProtocol.SIVelY];
            s.FX = v[FittsProtocol.SIForceX];
            s.FY = v[FittsProtocol.SIForceY];
            /*s.StateCode = (FittsStateCode)(int)Math.Round(v[FittsProtocol.SIStateCode]);
            s.Phase = (FittsPhase)(int)Math.Round(v[FittsProtocol.SIPhase]);
            s.TrialIndex = (int)Math.Round(v[FittsProtocol.SITrialIndex]);
            s.TargetX = v[FittsProtocol.SITargetX];
            s.HalfWidth = v[FittsProtocol.SIHalfWidth];
            s.DwellProgress = v[FittsProtocol.SIDwell];*/
            s.Valid = true;
            return s;
        }
    }

    /// <summary>Session descriptor (SESS). Field order fixed by M2FittsHumanMachine::sessionDescriptor().</summary>
    public struct SessionInfo
    {
        public int Version;
        public int Block;
        public int Rounds;
        public int TrialsPerRound;
        public int WarmupTrials;
        public double OriginX, OriginY;
        public double TaskDirection;      // +1 or -1
        public double DwellTime;
        public double MaxTrialTime;
        public double OriginTolerance;
        public double ReturnOffset;
        public bool UseYChannel;
        public double DisplayGain;
        public bool Valid;

        public static SessionInfo FromParams(double[] p)
        {
            SessionInfo s = default;
            if (p == null || p.Length < 14) return s;
            s.Version = (int)Math.Round(p[0]);
            s.Block = (int)Math.Round(p[1]);
            s.Rounds = (int)Math.Round(p[2]);
            s.TrialsPerRound = (int)Math.Round(p[3]);
            s.WarmupTrials = (int)Math.Round(p[4]);
            s.OriginX = p[5];
            s.OriginY = p[6];
            s.TaskDirection = p[7] < 0 ? -1.0 : 1.0;
            s.DwellTime = p[8];
            s.MaxTrialTime = p[9];
            s.OriginTolerance = p[10];
            s.ReturnOffset = p[11];
            s.UseYChannel = p[12] > 0.5;
            s.DisplayGain = p[13];
            s.Valid = true;
            return s;
        }
    }

    /// <summary>Target onset (TRIA). Field order fixed by M2FittsReachState::entryCode().</summary>
    public struct TrialInfo
    {
        public FittsPhase Phase;
        public int Round;
        public int TrialInRound;
        public int TrialIndex;
        public double A_cm, W_cm, ID_bits;
        public double TargetX, TargetY;   // robot frame, m
        public double HalfWidth;          // m
        public double OriginX, OriginY;   // robot frame, m
        public double DwellTime;
        public double MaxTrialTime;
        public bool Valid;

        public static TrialInfo FromParams(double[] p)
        {
            TrialInfo t = default;
            if (p == null || p.Length < 14) return t;
            t.Phase = (FittsPhase)(int)Math.Round(p[0]);
            t.Round = (int)Math.Round(p[1]);
            t.TrialInRound = (int)Math.Round(p[2]);
            t.TrialIndex = (int)Math.Round(p[3]);
            t.A_cm = p[4];
            t.W_cm = p[5];
            t.ID_bits = p[6];
            t.TargetX = p[7];
            t.TargetY = p[8];
            t.HalfWidth = p[9];
            t.OriginX = p[10];
            t.OriginY = p[11];
            t.DwellTime = p[12];
            t.MaxTrialTime = p[13];
            t.Valid = true;
            return t;
        }
    }

    /// <summary>A decoded command frame.</summary>
    public struct FlnlCommand
    {
        public string Cmd;
        public double[] Params;
        /// <summary>Unity realtime clock at the moment the receiving thread parsed the frame.</summary>
        public double ArrivalTime;

        public double P(int i, double fallback = 0.0)
        {
            return (Params != null && i < Params.Length) ? Params[i] : fallback;
        }
    }
}
