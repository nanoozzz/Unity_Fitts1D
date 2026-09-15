using System;
using UnityEngine;
using Fitts.Net;

namespace Fitts.Experiment
{
    /// <summary>
    /// Affine map from the M2 end-effector frame (metres, x in [0, 0.625], y in [0, 0.440],
    /// origin at the configured home position) to the Unity scene.
    ///
    /// The map is deliberately expressed in *task coordinates* rather than raw robot x, because
    /// the sign of the task axis is a CORC configuration parameter (task_direction) while the
    /// direction targets appear on screen is a scene layout choice. Keeping the two separate means
    /// flipping one never silently flips the other.
    ///
    ///     s        = taskDirection * (x_robot - originX)      signed distance along the task axis,
    ///                                                          positive towards the targets
    ///     lateral  = y_robot - originY                        off-axis deviation (held near 0 by
    ///                                                          the CORC virtual channel)
    ///     sceneX   = sceneOrigin.x + sceneAxisSign * gain * s
    ///     sceneY   = sceneOrigin.y + gain * lateral
    ///
    /// gain MUST be 1 for the Fitts design to hold: with gain != 1 the visual amplitude and
    /// visual width are both scaled, so the visual index of difficulty no longer equals the
    /// mechanical one that CORC scores. CORC sends its own display_gain in SESS and this class
    /// adopts it, so the two sides cannot disagree.
    /// </summary>
    [Serializable]
    public class RobotFrame
    {
        [Header("Robot frame (overwritten by the SESS message)")]
        public Vector2 robotOrigin = new Vector2(0.40f, 0.20f);
        [Tooltip("+1: targets at increasing robot x. -1: decreasing. Comes from task_direction.")]
        public float taskDirection = -1f;

        [Header("Scene layout")]
        [Tooltip("Scene position of the home/start marker, in Unity units.")]
        public Vector2 sceneOrigin = Vector2.zero;
        [Tooltip("+1: targets drawn towards +x on screen. -1: towards -x.")]
        public float sceneAxisSign = -1f;
        [Tooltip("Unity units per robot metre. Keep at 1 unless a gain manipulation is intended.")]
        public float displayGain = 1f;

        public bool Configured { get; private set; }

        /// <summary>Adopt the geometry CORC actually used, so the two sides cannot drift apart.</summary>
        public void ApplySession(SessionInfo s)
        {
            if (!s.Valid) return;
            robotOrigin = new Vector2((float)s.OriginX, (float)s.OriginY);
            taskDirection = (float)s.TaskDirection;
            displayGain = (float)s.DisplayGain;
            Configured = true;

            if (Mathf.Abs(displayGain - 1f) > 1e-6f)
            {
                Debug.LogWarning(
                    $"[RobotFrame] display_gain = {displayGain}. Visual amplitude and width are " +
                    "scaled, so the visual index of difficulty differs from the mechanical one " +
                    "scored by CORC. Set display_gain = 1.0 in M2FittsHuman.conf unless this is intended.");
            }
        }

        /// <summary>Signed distance along the task axis, positive towards the targets [m].</summary>
        public float TaskCoord(Vector2 robotXY) => taskDirection * (robotXY.x - robotOrigin.x);

        public Vector2 RobotToScene(Vector2 robotXY)
        {
            float s = TaskCoord(robotXY);
            float lateral = robotXY.y - robotOrigin.y;
            return new Vector2(sceneOrigin.x + sceneAxisSign * displayGain * s,
                               sceneOrigin.y + displayGain * lateral);
        }

        public Vector2 RobotToScene(double x, double y) => RobotToScene(new Vector2((float)x, (float)y));

        /// <summary>Inverse map, for tests and for placing scene objects from scene coordinates.</summary>
        public Vector2 SceneToRobot(Vector2 sceneXY)
        {
            float s = (sceneXY.x - sceneOrigin.x) / (sceneAxisSign * displayGain);
            float lateral = (sceneXY.y - sceneOrigin.y) / displayGain;
            return new Vector2(robotOrigin.x + taskDirection * s, robotOrigin.y + lateral);
        }

        /// <summary>Scale a length (amplitude, width) from robot metres to scene units.</summary>
        public float ScaleLength(double metres) => (float)metres * displayGain;
    }
}
