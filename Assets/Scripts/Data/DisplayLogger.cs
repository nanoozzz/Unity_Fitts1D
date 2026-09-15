using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using Fitts.Net;
using Fitts.Experiment;

namespace Fitts.Experiment
{
    /// <summary>
    /// Writes the *display* record: what was shown, when it was shown, and how far behind the
    /// robot the display was. This is an audit trail, not the experimental data - the analysable
    /// measurements are the two files CORC writes (..._trials.csv and ..._raw.csv).
    ///
    /// Why this file is worth keeping anyway. The participant closes the loop through the screen,
    /// so the visual feedback delay is part of the task, not an artefact of it. It only becomes a
    /// confound if it is unknown or if it differs between Block 1 (no support) and Block 2 (robot
    /// support). Logging round trip, clock offset and per-target presentation time makes the delay
    /// a reportable quantity and lets the two blocks be compared on it, rather than assumed equal.
    ///
    /// Every row carries CORC time (via the estimated offset) as well as Unity time, so this file
    /// can be joined to the CORC logs on a single clock.
    /// </summary>
    public class DisplayLogger : MonoBehaviour
    {
        [Tooltip("Also log one row per rendered frame with the cursor position. Large files; " +
                 "useful when characterising latency, unnecessary during data collection.")]
        public bool logEveryFrame = false;

        private StreamWriter writer;
        private string path;
        private M2Link link;

        void Awake() => link = FindFirstObjectByType<M2Link>();

        public void StartNewFile(SessionInfo s)
        {
            Close();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            path = Path.Combine(Application.persistentDataPath, $"display_B{s.Block}_{stamp}.csv");

            writer = new StreamWriter(path, false);
            writer.WriteLine("# Display audit log. Experimental data are the CORC files " +
                             "(M2FittsHuman_*_trials.csv, M2FittsHuman_*_raw.csv).");
            writer.WriteLine($"# protocol_version,{s.Version}");
            writer.WriteLine($"# block,{s.Block}");
            writer.WriteLine($"# origin_m,{F(s.OriginX)},{F(s.OriginY)}");
            writer.WriteLine($"# task_direction,{F(s.TaskDirection)}");
            writer.WriteLine($"# dwell_s,{F(s.DwellTime)}");
            writer.WriteLine($"# display_gain,{F(s.DisplayGain)}");
            writer.WriteLine($"# target_frame_rate,{Application.targetFrameRate}");
            writer.WriteLine($"# vsync_count,{QualitySettings.vSyncCount}");
            writer.WriteLine($"# screen,{Screen.currentResolution.width}x{Screen.currentResolution.height}" +
                             $"@{Screen.currentResolution.refreshRateRatio.value:F2}Hz");
            writer.WriteLine("event,unity_time_s,corc_time_s,clock_offset_s,rtt_ms,frame_dt_ms," +
                             "cmd,trial_index,A_cm,W_cm,ID_bits,success,MT_s,n_entries,x_sel_cm," +
                             "robot_x_m,robot_y_m,state_code,dwell_progress");
            writer.Flush();
            Debug.Log($"[DisplayLogger] {path}");
        }

        void Update()
        {
            if (writer == null || !logEveryFrame || link == null) return;
            Row("frame", "", 0, double.NaN, double.NaN, double.NaN, null, double.NaN, 0, double.NaN);
        }

        public void LogCommand(FlnlCommand c, RobotState state, double unityTime)
        {
            if (writer == null) return;
            Row("cmd", c.Cmd, 0, double.NaN, double.NaN, double.NaN, null, double.NaN, 0, double.NaN);
        }

        /// <summary>Called on the frame a target is actually presented (after WaitForEndOfFrame).</summary>
        public void LogPresentation(int trialIndex, double unityTime, double clockOffset, double rttMs)
        {
            if (writer == null) return;
            Row("present", FittsProtocol.CmdTrial, trialIndex, double.NaN, double.NaN, double.NaN,
                null, double.NaN, 0, double.NaN);
        }

        public void LogTrialOutcome(int trialIndex, TrialInfo t, bool success,
                                    double mt, int nEntries, double xSelCm)
        {
            if (writer == null) return;
            Row(success ? "hit" : "miss", success ? FittsProtocol.CmdHit : FittsProtocol.CmdMiss,
                trialIndex, t.A_cm, t.W_cm, t.ID_bits, success, mt, nEntries, xSelCm);
        }

        private void Row(string ev, string cmd, int trialIndex,
                         double a, double w, double id,
                         bool? success, double mt, int nEntries, double xSelCm)
        {
            RobotState s = link != null ? link.State : default;
            double unityTime = Time.realtimeSinceStartupAsDouble;
            double offset = (link != null && link.ClockOffsetValid) ? link.ClockOffset : double.NaN;
            double corcTime = s.Valid ? s.CorcTime : unityTime + offset;

            writer.WriteLine(string.Join(",",
                ev,
                F(unityTime), F(corcTime), F(offset),
                F(link != null ? link.roundTripMs : double.NaN),
                F(Time.unscaledDeltaTime * 1000.0),
                cmd,
                trialIndex.ToString(CultureInfo.InvariantCulture),
                F(a), F(w), F(id),
                success.HasValue ? (success.Value ? "1" : "0") : "",
                F(mt), nEntries.ToString(CultureInfo.InvariantCulture), F(xSelCm),
                F(s.Valid ? s.X : double.NaN), F(s.Valid ? s.Y : double.NaN),
                s.Valid ? ((int)s.StateCode).ToString(CultureInfo.InvariantCulture) : "",
                F(s.Valid ? s.DwellProgress : double.NaN)));
        }

        private static string F(double v)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString("G9", CultureInfo.InvariantCulture);
        }

        public void Close()
        {
            if (writer == null) return;
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        void OnDisable() => Close();
        void OnApplicationQuit() => Close();
    }
}
