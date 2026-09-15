using UnityEngine;

/// <summary>
/// Which side owns the protocol.
/// </summary>
public enum ControlMode
{
    /// <summary>
    /// Mouse simulation. Unity runs its own state machine (ExperimentManager / TrialManager) and
    /// measures its own movement times at frame rate. Suitable for piloting the display and the
    /// trial schedule; NOT a source of analysable data.
    /// </summary>
    MouseSimulation = 0,

    /// <summary>
    /// M2 over FLNL. CORC owns the trial sequence, the target geometry, entry and dwell detection,
    /// all timing and all logging; Unity renders and reports display time stamps only.
    /// </summary>
    RobotM2 = 1
}

public class ExperimentConfig : MonoBehaviour
{
    [Header("Control mode")]
    [Tooltip("RobotM2 hands the protocol to CORC. MouseSimulation keeps it in Unity (piloting only).")]
    public ControlMode mode = ControlMode.RobotM2;

    [Header("Experiment (simulation mode only - CORC is authoritative in robot mode)")]
    public int warmupTrials = 10;

    public int trialsPerRound = 45;
    public int roundsPerBlock = 4;

    [Header("Timing (simulation mode only)")]
    public float maxTrialRestTime = 5.0f;
    public float maxRoundRestTime = 10.0f;
    public float maxBlockRestTime = 15.0f;

    [Header("Workspace (simulation mode only)")]
    public float workspaceWidth = 0.25f;
    public float workspaceHeight = 0.20f;

    [Header("Origin (simulation mode only; robot mode uses the origin from SESS)")]
    public Vector2 originPosition = Vector2.zero;

    [Header("Target (simulation mode only)")]
    public float targetAngleDegrees = 180.0f;

    [Header("Input")]
    public KeyCode startKey = KeyCode.Space;
    public KeyCode continueKey = KeyCode.Space;

    /// <summary>Convenience accessor used by the scripts that must not run in robot mode.</summary>
    public bool IsSimulation => mode == ControlMode.MouseSimulation;

    public Vector2 GetTargetPosition(float amplitude)
    {
        float angleRad = targetAngleDegrees * Mathf.Deg2Rad;

        return originPosition + new Vector2(amplitude * Mathf.Cos(angleRad),
                                            amplitude * Mathf.Sin(angleRad));
    }
}
