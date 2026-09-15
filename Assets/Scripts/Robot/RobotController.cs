using UnityEngine;
using Fitts.Net;
using Fitts.Experiment;

/// <summary>
/// Reports where the handle is, and refuses to move it.
///
/// In MouseSimulation mode this behaves as before: the "robot" is the mouse cursor and the
/// MoveTo* calls reposition it, which is what TrialManager expects when piloting without hardware.
///
/// In RobotM2 mode every MoveTo* call becomes a logged no-op. CORC owns all robot motion: the
/// move off the origin, the participant-driven drag back, the final snap onto the origin and the
/// hold are sequenced by M2FittsReturnState against measured interaction forces, with a force
/// limit that aborts a driven move if the participant resists. A second commanded motion arriving
/// from the display would at best be ignored and at worst fight the control loop, so it is not
/// sent at all. CurrentPosition reads the streamed end-effector position instead.
/// </summary>
public class RobotController : MonoBehaviour
{
    [Header("References")]
    public CursorController cursor;
    public ExperimentConfig config;

    [Tooltip("Left empty, the link is found in the scene at Awake.")]
    public M2Link link;

    [Tooltip("Transform used to map robot metres to scene units. Assign the one owned by " +
             "RemoteExperimentController so both use the geometry CORC reported in SESS.")]
    public RemoteExperimentController remote;

    private bool RobotMode => config != null && config.mode == ControlMode.RobotM2;

    void Awake()
    {
        if (link == null) link = FindFirstObjectByType<M2Link>();
        if (remote == null) remote = FindFirstObjectByType<RemoteExperimentController>();
    }

    /// <summary>
    /// Handle position in scene units. In robot mode this is the measured M2 end-effector
    /// position mapped through RobotFrame, not the drawn cursor, so it stays correct even on a
    /// frame where the cursor has not yet been updated.
    /// </summary>
    public Vector2 CurrentPosition
    {
        get
        {
            if (RobotMode)
            {
                if (link != null && link.State.Valid && remote != null)
                    return remote.frame.RobotToScene(link.State.X, link.State.Y);

                return cursor != null ? cursor.GetPosition() : Vector2.zero;
            }

            if (cursor != null)
                return cursor.GetPosition();

            return Vector2.zero;
        }
    }

    /// <summary>Raw end-effector position in the robot frame [m]. NaN if nothing is streaming.</summary>
    public Vector2 CurrentRobotPosition
    {
        get
        {
            if (link != null && link.State.Valid)
                return new Vector2((float)link.State.X, (float)link.State.Y);

            return new Vector2(float.NaN, float.NaN);
        }
    }

    public void MoveToOrigin(Vector2 origin)
    {
        if (RobotMode)
        {
            WarnIgnored("MoveToOrigin");
            return;
        }

        if (cursor != null)
            cursor.SetPosition(origin);
    }

    public void MoveToPosition(Vector2 position)
    {
        if (RobotMode)
        {
            WarnIgnored("MoveToPosition");
            return;
        }

        if (cursor != null)
            cursor.SetPosition(position);
    }

    /// <summary>
    /// Was a placeholder for "move automatically to position 3", i.e. the post-trial move off the
    /// origin. That manoeuvre is now M2FittsReturnState's MOVE_AWAY phase (minimum-jerk to
    /// return_offset, aborted above force_limit), announced to the display as RETN.
    /// </summary>
    public void MoveToPosition3()
    {
        if (RobotMode)
        {
            WarnIgnored("MoveToPosition3");
            return;
        }

        Debug.Log("Robot (simulation): move automatically to position 3.");
    }

    private void WarnIgnored(string call)
    {
        Debug.LogWarning(
            call + " ignored: CORC owns robot motion in RobotM2 mode. " +
            "If the display needs to request a change of state, send a command over M2Link instead."
        );
    }
}
