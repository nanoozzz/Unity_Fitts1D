using UnityEngine;
using Fitts.Net;
using Fitts.Experiment;

namespace Fitts.Visual
{
    /// <summary>
    /// Marks the post-trial return point on the task axis.
    ///
    /// Two reasons this is driven by the protocol rather than placed by hand in the scene:
    ///
    ///   - `return_offset` lives in M2FittsHumanMachine.cfg. A hardcoded scene position silently
    ///     stops being correct the moment that value changes, and nothing warns you.
    ///   - A line is a position claim, so its width is a precision claim. A Unity builtin sprite at
    ///     localScale 0.01 is 2.56 cm wide - wider than most of the targets in this design, so it
    ///     cannot resolve the few-mm errors you are trying to see. Sizing in metres fixes that.
    ///
    /// With `showActualStop` the component also draws a second, dimmer line where the robot really
    /// stopped, read from the two extra parameters appended to DRAG. The gap between the two lines
    /// is the MOVE_AWAY tracking error, visible in real time instead of inferred from the log.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ReturnLineMarker : MonoBehaviour
    {
        [Header("References")]
        public M2Link link;
        public RemoteExperimentController remote;

        [Header("Real-world size")]
        [Tooltip("Line thickness along the task axis, in metres. Keep it well under the narrowest " +
                 "target width (0.889 cm here) or it cannot mark a position precisely. 1.5 mm is a " +
                 "reasonable default.")]
        public float lineWidthMetres = 0.0015f;

        [Tooltip("Line length across the task axis, in metres. Matching the target band height " +
                 "reads well.")]
        public float lineHeightMetres = 0.05f;

        [Header("Actual stop position (needs the extra DRAG parameters)")]
        public bool showActualStop = true;
        [Tooltip("Second SpriteRenderer drawn where the robot actually stopped. Leave empty to skip.")]
        public SpriteRenderer actualStopMarker;
        public Color actualStopColour = new Color(1f, 0.6f, 0.1f, 0.6f);

        [Tooltip("Log the commanded-vs-actual gap on every trial.")]
        public bool logError = true;

        [Header("Result (read only)")]
        public float lastErrorMm;

        private SpriteRenderer sr;
        private float nativeW = 1f, nativeH = 1f;
        private bool sized;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (link == null) link = FindFirstObjectByType<M2Link>();
            if (remote == null) remote = FindFirstObjectByType<RemoteExperimentController>();

            if (sr != null && sr.sprite != null)
            {
                nativeW = sr.sprite.bounds.size.x;
                nativeH = sr.sprite.bounds.size.y;
            }
            if (nativeW <= 0f) nativeW = 1f;
            if (nativeH <= 0f) nativeH = 1f;

            /*gameObject.SetActive(false);
            if (actualStopMarker != null)
            {
                actualStopMarker.color = actualStopColour;
                actualStopMarker.gameObject.SetActive(false);
            }*/
            if (sr != null) sr.enabled = false;
            if (actualStopMarker != null)
            {
                actualStopMarker.color = actualStopColour;
                actualStopMarker.enabled = false;
            }
        }

        void OnEnable()
        {
            if (link == null) return;
            link.OnReturn += HandleReturn;
            link.OnAnyCommand += HandleAnyCommand;
            link.OnTrial += HandleTrial;
        }

        void OnDisable()
        {
            if (link == null) return;
            link.OnReturn -= HandleReturn;
            link.OnAnyCommand -= HandleAnyCommand;
            link.OnTrial -= HandleTrial;
        }

        private void ApplySize(Transform t)
        {
            float gain = (remote != null) ? remote.frame.displayGain : 1f;
            t.localScale = new Vector3(lineWidthMetres * gain / nativeW,
                                       lineHeightMetres * gain / nativeH, 1f);
        }

        /// <summary>RETN: the robot has started driving the handle to this point.</summary>
        private void HandleReturn(Vector2 awayRobot)
        {
            if (remote == null) return;

            transform.position = remote.frame.RobotToScene(awayRobot);
            ApplySize(transform);
            //gameObject.SetActive(true);
            if (sr != null) sr.enabled = true;
            sized = true;

            if (actualStopMarker != null) actualStopMarker.gameObject.SetActive(false);
        }

        /// <summary>
        /// DRAG carries the origin, tolerance and max time, plus - once CORC appends them - the
        /// position at which MOVE_AWAY actually ended, at indices 4 and 5.
        /// </summary>
        private void HandleAnyCommand(FlnlCommand c)
        {
            if (c.Cmd != FittsProtocol.CmdDrag || !showActualStop || remote == null) return;
            if (c.Params == null || c.Params.Length < 6) return;   // older CORC: nothing to draw

            Vector2 stopRobot = new Vector2((float)c.P(4), (float)c.P(5));
            Vector2 stopScene = remote.frame.RobotToScene(stopRobot);

            if (actualStopMarker != null)
            {
                //actualStopMarker.transform.position = stopScene;
                actualStopMarker.enabled = true;
                ApplySize(actualStopMarker.transform);
                actualStopMarker.gameObject.SetActive(true);
            }

            // Signed along the task axis: positive means it went past the line, towards the origin.
            lastErrorMm = (stopScene.x - transform.position.x) * 1000f
                          * (remote.frame.sceneAxisSign * remote.frame.taskDirection);

            if (logError)
                Debug.Log($"[Return] commanded x={transform.position.x:F4}, actual x={stopScene.x:F4}, " +
                          $"error {lastErrorMm:+0.0;-0.0} mm " +
                          (Mathf.Abs(lastErrorMm) > 5f ? "(MOVE_AWAY did not converge)" : ""));
        }

        /// <summary>Hide both lines while a reach is in progress - they only describe the return.</summary>
        private void HandleTrial(TrialInfo t)
        {
            if (sr != null) sr.enabled = false;
            if (actualStopMarker != null) actualStopMarker.enabled = false;
        }

        void Update()
        {
            // Re-apply if the inspector values are being tuned during play.
            //if (sized && gameObject.activeSelf && Application.isEditor) ApplySize(transform);
            if (sized && sr != null && sr.enabled && Application.isEditor) ApplySize(transform);
        }
    }
}
