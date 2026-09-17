using UnityEngine;
using Fitts.Net;

namespace Fitts.Experiment
{
    /// <summary>
    /// Sets the orthographic camera from the geometry CORC reports, and states in the log the two
    /// things a hand-tuned orthographic size hides:
    ///
    ///   1. Whether the narrowest target is actually legible. With displayGain = 1 the scene is in
    ///      metres, so the view is 2 x orthographicSize metres tall. At size 1.0 the 0.889 cm target
    ///      is 5 px on a 1080p display; at 0.25 it is 19 px.
    ///
    ///   2. The PHYSICAL visuomotor gain, which is not displayGain:
    ///          physical gain = screenHeightMetres / (2 * orthographicSize * displayGain)
    ///      i.e. metres the cursor travels on the panel per metre the hand travels. It does not
    ///      corrupt the index of difficulty (A and W scale together) but it is a non-natural
    ///      mapping that must be identical in Blocks 1 and 2 and reported in the methods.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TaskCameraFramer : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Smallest framing that shows the whole reachable workspace.</summary>
            FitWorkspace,
            /// <summary>Framing that gives a 1:1 visuomotor gain, from screenHeightMetres.</summary>
            OneToOnePhysical,
            /// <summary>Leave orthographicSize alone; only audit and report.</summary>
            AuditOnly
        }

        [Header("References")]
        public M2Link link;
        public RemoteExperimentController remote;

        [Header("Framing")]
        public Mode mode = Mode.FitWorkspace;

        [Tooltip("Extra scene units (metres) kept clear around the content.")]
        public float margin = 0.03f;

        [Tooltip("Physical height of the display's ACTIVE AREA in metres. Measure it; do not infer " +
                 "it from the diagonal unless you are certain of the aspect ratio.")]
        public float screenHeightMetres = 0.336f;

        [Tooltip("Narrowest target below this many pixels is reported as illegible.")]
        public float legibilityMinPixels = 12f;

        [Header("Robot travel (M2), metres")]
        public Vector2 robotTravelMin = new Vector2(0f, 0f);
        public Vector2 robotTravelMax = new Vector2(0.625f, 0.440f);

        [Header("Result (read only)")]
        public float physicalGain;
        public float narrowestTargetPixels;
        public bool farthestTargetVisible;

        private Camera cam;
        private double maxAmplitudeMetres = 0.30375;  // widened from TRIA if a larger one arrives
        private double minWidthMetres = 0.00889;      // narrowed from TRIA likewise

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (link == null) link = FindFirstObjectByType<M2Link>();
            if (remote == null) remote = FindFirstObjectByType<RemoteExperimentController>();
        }

        void OnEnable()
        {
            if (link == null) return;
            link.OnSession += HandleSession;
            link.OnTrial += HandleTrial;
        }

        void OnDisable()
        {
            if (link == null) return;
            link.OnSession -= HandleSession;
            link.OnTrial -= HandleTrial;
        }

        private void HandleSession(SessionInfo s) => Reframe();

        private void HandleTrial(TrialInfo t)
        {
            bool changed = false;

            double a = t.A_cm / 100.0;
            if (a > maxAmplitudeMetres + 1e-9) { maxAmplitudeMetres = a; changed = true; }

            double w = t.W_cm / 100.0;
            if (w > 0 && w < minWidthMetres - 1e-9) { minWidthMetres = w; changed = true; }

            if (changed) Reframe();
        }

        [ContextMenu("Reframe")]
        public void Reframe()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (remote == null || cam == null || !cam.orthographic) return;

            RobotFrame f = remote.frame;
            float aspect = cam.aspect;

            if (mode == Mode.FitWorkspace)
            {
                // Scene-space bounds of everything that must be drawable: the whole robot travel,
                // because the cursor can be anywhere in it (notably at the calibration corner
                // before homing, which is where it was invisible before).
                Vector2 c0 = f.RobotToScene(robotTravelMin);
                Vector2 c1 = f.RobotToScene(new Vector2(robotTravelMax.x, robotTravelMin.y));
                Vector2 c2 = f.RobotToScene(new Vector2(robotTravelMin.x, robotTravelMax.y));
                Vector2 c3 = f.RobotToScene(robotTravelMax);

                float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x)) - margin;
                float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x)) + margin;
                float minY = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y)) - margin;
                float maxY = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y)) + margin;

                cam.orthographicSize = Mathf.Max((maxY - minY) * 0.5f, (maxX - minX) * 0.5f / aspect);
                transform.position = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f,
                                                 transform.position.z);
            }
            else if (mode == Mode.OneToOnePhysical)
            {
                // orthographicSize is fixed by the panel; only the centre is free.
                cam.orthographicSize = screenHeightMetres * 0.5f / Mathf.Max(f.displayGain, 1e-6f);

                // Centre on the task band rather than the whole travel, because at 1:1 the whole
                // travel may not fit on the display.
                float originX = f.sceneOrigin.x;
                float farX = originX + f.sceneAxisSign * f.displayGain * (float)maxAmplitudeMetres;
                transform.position = new Vector3((originX + farX) * 0.5f, f.sceneOrigin.y,
                                                 transform.position.z);
            }
            // AuditOnly: leave the camera untouched and just report.

            Report(f, aspect);
        }

        private void Report(RobotFrame f, float aspect)
        {
            float pixelsPerSceneUnit = Screen.height / (2f * cam.orthographicSize);
            narrowestTargetPixels = (float)minWidthMetres * f.displayGain * pixelsPerSceneUnit;

            physicalGain = screenHeightMetres /
                           (2f * cam.orthographicSize * Mathf.Max(f.displayGain, 1e-6f));

            float farX = f.sceneOrigin.x + f.sceneAxisSign * f.displayGain * (float)maxAmplitudeMetres;
            float viewHalfW = cam.orthographicSize * aspect;
            farthestTargetVisible = Mathf.Abs(farX - transform.position.x) <= viewHalfW;

            Debug.Log(
                $"[Camera] orthographicSize {cam.orthographicSize:F4}, centre " +
                $"({transform.position.x:F4}, {transform.position.y:F4}), aspect {aspect:F3}, " +
                $"view {2f * cam.orthographicSize:F3} m tall.\n" +
                $"         Narrowest target (W = {minWidthMetres * 100.0:F3} cm) renders at " +
                $"{narrowestTargetPixels:F0} px.\n" +
                $"         Physical visuomotor gain {physicalGain:F3} " +
                $"(1.000 = cursor travels as far on the panel as the hand does).\n" +
                $"         Farthest target (A = {maxAmplitudeMetres * 100.0:F2} cm) " +
                (farthestTargetVisible ? "fits." : "DOES NOT FIT."));

            if (!farthestTargetVisible)
            {
                Debug.LogError("[Camera] The largest-amplitude target cannot be drawn. Shift " +
                               "RobotFrame.sceneOrigin towards the start side, use a larger display, " +
                               "or switch to FitWorkspace and accept a physical gain below 1.");
            }

            if (narrowestTargetPixels < legibilityMinPixels)
            {
                Debug.LogError(
                    $"[Camera] The narrowest target is {narrowestTargetPixels:F0} px, below the " +
                    $"{legibilityMinPixels:F0} px threshold. Reduce orthographicSize " +
                    $"(to about {(float)minWidthMetres * f.displayGain * Screen.height / (2f * legibilityMinPixels):F3} " +
                    "or less) rather than enlarging the sprites - scaling the cursor up to compensate " +
                    "changes the width the participant is effectively aiming at.");
            }
        }
    }
}