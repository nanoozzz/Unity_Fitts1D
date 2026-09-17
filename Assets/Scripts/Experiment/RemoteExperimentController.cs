using System.Collections;
using UnityEngine;
using Fitts.Net;

namespace Fitts.Experiment
{
    /// <summary>
    /// Renders the experiment that CORC is running. This script performs NO detection and NO
    /// timing that enters the analysis: it draws the target CORC has announced, moves the cursor
    /// to the position CORC has measured, and reports back only display time stamps.
    ///
    /// The division of labour is deliberate. Movement time, target entry, dwell validation and
    /// the index of difficulty are all quantities of the participant's hand, and the hand is
    /// sampled at 500 Hz by the control loop. Re-deriving any of them here would resample them
    /// at frame rate and add display latency, which enters MT differently at different indices of
    /// difficulty and would therefore bias the slope of the Fitts regression - the parameter the
    /// experiment exists to estimate.
    /// </summary>
    public class RemoteExperimentController : MonoBehaviour
    {
        [Header("Network")]
        public M2Link link;

        [Header("Scene")]
        public CursorController cursor;
        public TargetController target;
        public UIController ui;
        public Transform originMarker;
        [Tooltip("Optional ring scaled 0..1 by the dwell progress CORC computes. Purely indicative.")]
        public Transform dwellRing;

        [Header("Geometry")]
        public RobotFrame frame = new RobotFrame();

        [Header("Logging")]
        public DisplayLogger displayLogger;

        [Header("Participant controls")]
        public KeyCode goKey = KeyCode.Space;
        public KeyCode abortKey = KeyCode.Escape;

        [Header("Trial feedback")]
        public Color hitColour = new Color(0.15f, 0.80f, 0.25f);
        public Color missColour = new Color(0.45f, 0.45f, 0.45f);

        // Rest countdown: display only. CORC owns the real break timer and will move on regardless.
        private bool resting;
        private float restEndsAt;
        private string restMessage = "";

        private TrialInfo currentTrial;
        private bool trialVisible;

        void Awake()
        {
            if (link == null) link = FindFirstObjectByType<M2Link>();

            if (dwellRing != null && target != null &&
                (dwellRing == target.transform || target.transform.IsChildOf(dwellRing)))
            {
                Debug.LogError("[Remote] dwellRing points at the Target (or a parent of it). The dwell " +
                               "indicator toggles its GameObject every frame, which would switch the target " +
                               "off. Ignoring it — assign a separate sprite or leave the field empty.");
                dwellRing = null;
            }
        }

        void OnEnable()
        {
            if (link == null) return;
            link.OnSession += HandleSession;
            link.OnTrial += HandleTrial;
            link.OnHit += HandleHit;
            link.OnMiss += HandleMiss;
            link.OnReturn += HandleReturn;
            link.OnDrag += HandleDrag;
            link.OnOrigin += HandleOrigin;
            link.OnRest += HandleRest;
            link.OnReady += HandleReady;
            link.OnWaiting += HandleWaiting;
            link.OnStandby += HandleStandby;
            link.OnEnd += HandleEnd;
            link.OnProtocolError += HandleProtocolError;
            link.OnConnectionChanged += HandleConnectionChanged;
            link.OnAnyCommand += HandleAnyCommand;
        }

        void OnDisable()
        {
            if (link == null) return;
            link.OnSession -= HandleSession;
            link.OnTrial -= HandleTrial;
            link.OnHit -= HandleHit;
            link.OnMiss -= HandleMiss;
            link.OnReturn -= HandleReturn;
            link.OnDrag -= HandleDrag;
            link.OnOrigin -= HandleOrigin;
            link.OnRest -= HandleRest;
            link.OnReady -= HandleReady;
            link.OnWaiting -= HandleWaiting;
            link.OnStandby -= HandleStandby;
            link.OnEnd -= HandleEnd;
            link.OnProtocolError -= HandleProtocolError;
            link.OnConnectionChanged -= HandleConnectionChanged;
            link.OnAnyCommand -= HandleAnyCommand;
        }

        void Start()
        {
            if (target != null) target.Hide();
            if (dwellRing != null) dwellRing.gameObject.SetActive(false);
            if (ui != null)
            {
                ui.HideRest();
                ui.ShowState("CONNECTING");
                ui.ShowInstruction("Waiting for the robot controller...");
            }
        }

        void Update()
        {
            if (link == null) return;

            UpdateCursor();
            UpdateDwellIndicator();
            UpdateRestCountdown();
            UpdateInput();
        }

        /// <summary>
        /// Cursor position comes from the robot, transformed into the scene. The cursor is not
        /// interpolated or smoothed: any filtering here would decouple what the participant sees
        /// from what CORC scores.
        /// </summary>
        private void UpdateCursor()
        {
            RobotState s = link.State;
            if (!s.Valid || cursor == null) return;
            cursor.SetPosition(frame.RobotToScene(s.X, s.Y));
        }

        private void UpdateDwellIndicator()
        {
            if (dwellRing == null) return;
            RobotState s = link.State;
            bool show = s.Valid && s.DwellProgress > 0.0 && trialVisible;
            if (dwellRing.gameObject.activeSelf != show) dwellRing.gameObject.SetActive(show);
            if (!show) return;

            dwellRing.position = target.transform.position;
            float k = Mathf.Clamp01((float)s.DwellProgress);
            float d = frame.ScaleLength(currentTrial.W_cm / 100.0) * (0.25f + 0.75f * k);
            dwellRing.localScale = new Vector3(d, d, 1f);
        }

        private void UpdateRestCountdown()
        {
            if (!resting || ui == null) return;
            float remaining = Mathf.Max(0f, restEndsAt - Time.unscaledTime);
            ui.ShowRest(restMessage, remaining);
        }

        private void UpdateInput()
        {
            // The keys forward a request to CORC; they never advance anything locally.
            if (Input.GetKeyDown(goKey))
            {
                link.SendGo();
            }
            if (Input.GetKeyDown(abortKey))
            {
                link.SendAbort();
            }
        }

        // ------------------------------------------------------------------ events

        private void HandleSession(SessionInfo s)
        {
            frame.ApplySession(s);

            if (originMarker != null)
                originMarker.position = frame.RobotToScene(s.OriginX, s.OriginY);

            if (ui != null)
            {
                ui.ShowState("READY");
                ui.ShowInstruction($"Block {s.Block}: {s.WarmupTrials} warm-up reps, " +
                                   $"then {s.Rounds} x {s.TrialsPerRound} trials.");
            }
            displayLogger?.StartNewFile(s);
            Debug.Log($"[Remote] Session: block {s.Block}, {s.Rounds}x{s.TrialsPerRound} trials, " +
                      $"origin ({s.OriginX:F3}, {s.OriginY:F3}) m, task direction {s.TaskDirection}, " +
                      $"dwell {s.DwellTime:F2} s, display gain {s.DisplayGain:F3}.");
        }

        private void HandleTrial(TrialInfo t)
        {
            currentTrial = t;
            trialVisible = true;
            resting = false;
            if (ui != null) ui.HideRest();

            if (target != null)
            {
                Vector2 scenePos = frame.RobotToScene(t.TargetX, t.TargetY);
                target.SetTarget(scenePos, frame.ScaleLength(t.W_cm / 100.0));
                target.Show();
            }

            if (ui != null)
            {
                int total = t.Phase == FittsPhase.Warmup
                    ? link.Session.WarmupTrials
                    : link.Session.Rounds * link.Session.TrialsPerRound;
                ui.ShowState(t.Phase == FittsPhase.Warmup ? "WARM-UP" : $"BLOCK {link.Session.Block}");
                ui.ShowTrial(t.TrialIndex, Mathf.Max(total, t.TrialIndex));
                ui.ShowInstruction("Move to the target and hold.");
            }

            // Mark the frame on which the target is actually presented, not the frame on which the
            // command was decoded. The difference is exactly the display latency we want on record.
            StartCoroutine(MarkAfterPresentation(t.TrialIndex));
        }

        private IEnumerator MarkAfterPresentation(int trialIndex)
        {
            yield return new WaitForEndOfFrame();
            link.SendMark(trialIndex);
            displayLogger?.LogPresentation(trialIndex, Time.realtimeSinceStartupAsDouble,
                                           link.ClockOffsetValid ? link.ClockOffset : double.NaN,
                                           link.roundTripMs);
        }

        private void HandleHit(int trialIndex, double mt, int nEntries, double xSelCm)
        {
            trialVisible = false;
            /*if (target != null) target.Hide();
            if (dwellRing != null) dwellRing.gameObject.SetActive(false);
            if (ui != null)
            {
                ui.ShowTimer((float)mt);
                ui.ShowInstruction("Good. Bring the handle back to the start.");
            }
            displayLogger?.LogTrialOutcome(trialIndex, currentTrial, true, mt, nEntries, xSelCm);*/
            if (target != null) target.SetColour(hitColour);   // stays visible
            if (ui != null) { ui.ShowTimer((float)mt); ui.ShowInstruction("Hit — hold still."); }
            displayLogger?.LogTrialOutcome(trialIndex, currentTrial, true, mt, nEntries, xSelCm);
        }

        private void HandleMiss(int trialIndex)
        {
            trialVisible = false;
            /*if (target != null) target.Hide();
            if (dwellRing != null) dwellRing.gameObject.SetActive(false);
            if (ui != null) ui.ShowInstruction("Too slow - moving on.");
            displayLogger?.LogTrialOutcome(trialIndex, currentTrial, false, double.NaN, 0, double.NaN);*/
            if (target != null) target.SetColour(missColour);
            if (ui != null) ui.ShowInstruction("Too slow.");
            displayLogger?.LogTrialOutcome(trialIndex, currentTrial, false, double.NaN, 0, double.NaN);
        }

        private void HandleReturn(Vector2 awayRobot)
        {
            //if (ui != null) ui.ShowInstruction("Let the robot move the handle.");
            trialVisible = false;
            if (target != null) { target.Hide(); target.ResetColour(); }
            if (ui != null) ui.ShowInstruction("Let the robot move the handle.");
        }

        private void HandleDrag(Vector2 originRobot, double tolerance, double maxTime)
        {
            if (originMarker != null) originMarker.position = frame.RobotToScene(originRobot);
            if (ui != null)
                ui.ShowInstruction($"Bring the handle back to the start (within {maxTime:F0} s).");
        }

        private void HandleOrigin(Vector2 originRobot)
        {
            if (originMarker != null) originMarker.position = frame.RobotToScene(originRobot);
            if (ui != null) ui.ShowInstruction("Hold at the start.");
        }

        private void HandleRest(double duration, int trialsDone, FittsPhase phase)
        {
            resting = true;
            restEndsAt = Time.unscaledTime + (float)duration;
            restMessage = phase == FittsPhase.Warmup
                ? "Warm-up complete. Rest."
                : $"Rest. {trialsDone} trials done.";
            if (target != null) target.Hide();
            if (ui != null)
            {
                ui.ShowState("REST");
                ui.ShowRest(restMessage, (float)duration);
                ui.ShowInstruction("Press SPACE to continue earlier.");
            }
        }

        private void HandleReady(bool requiresGo)
        {
            resting = false;
            if (ui != null)
            {
                ui.HideRest();
                ui.ShowState("READY");
                ui.ShowInstruction(requiresGo
                    ? "Hold the handle at the start, then press SPACE to begin."
                    : "Hold the handle at the start - resuming shortly.");
            }
        }

        private void HandleWaiting()
        {
            if (ui != null)
            {
                ui.ShowState("STANDBY");
                ui.ShowInstruction("Robot controller ready. Waiting to start.");
            }
        }

        private void HandleStandby()
        {
            trialVisible = false;
            resting = false;
            if (target != null) target.Hide();
            if (ui != null)
            {
                ui.HideRest();
                ui.ShowState("ABORTED");
                ui.ShowInstruction("Session interrupted. Completed trials have been saved.");
            }
        }

        private void HandleEnd(int nTrials)
        {
            trialVisible = false;
            resting = false;
            if (target != null) target.Hide();
            if (ui != null)
            {
                ui.HideRest();
                ui.ShowState("FINISHED");
                ui.ShowInstruction($"Complete - {nTrials} trials. Thank you.");
            }
            displayLogger?.Close();
        }

        private void HandleProtocolError(string message)
        {
            Debug.LogError($"[Remote] Protocol error: {message}");
            if (ui != null)
            {
                ui.ShowState("PROTOCOL ERROR");
                ui.ShowInstruction(message);
            }
        }

        private void HandleConnectionChanged(bool isConnected)
        {
            if (ui == null) return;
            if (!isConnected)
            {
                ui.ShowState("DISCONNECTED");
                ui.ShowInstruction("Lost the robot controller. Reconnecting...");
                if (target != null) target.Hide();
            }
        }

        private void HandleAnyCommand(FlnlCommand c)
        {
            displayLogger?.LogCommand(c, link.State, Time.realtimeSinceStartupAsDouble);
        }
    }
}
