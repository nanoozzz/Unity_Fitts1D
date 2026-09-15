using UnityEngine;

public class ExperimentConfig : MonoBehaviour
{
    [Header("Experiment")]
    public int warmupTrials = 10;

    public int trialsPerRound = 45;
    public int roundsPerBlock = 4;

    [Header("Timing")]
    public float maxTrialRestTime = 5.0f;
    public float maxRoundRestTime = 10.0f;
    public float maxBlockRestTime = 15.0f;

    [Header("Workapce")]
    public float workspaceWidth = 0.25f;
    public float workspaceHeight = 0.20f;

    [Header("Origin")]
    public Vector2 originPosition = Vector2.zero;

    [Header("Simulation")]
    public bool useMouse = true;

    [Header("Target")]
    public float targetAngleDegrees = 0.0f;

    [Header("Input")]
    public KeyCode startKey = KeyCode.Space;
    public KeyCode continueKey = KeyCode.Space;

    public Vector2 GetTargetPosition(float amplitude)
    {
        float angleRad = targetAngleDegrees * Mathf.Deg2Rad;

        return originPosition + new Vector2(amplitude * Mathf.Cos(angleRad), amplitude * Mathf.Sin(angleRad));
    }
}
