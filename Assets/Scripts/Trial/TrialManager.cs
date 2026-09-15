using UnityEngine;

public class TrialManager : MonoBehaviour
{
    public enum TrialState
    {
        Idle,
        Prepare,
        Reaching,
        Acquired,
        Returning,
        Complete
    }

    [Header("References")]
    public CursorController cursor;
    public TargetController target;
    public RobotController robot;
    public ExperimentConfig config;
    public SpriteRenderer originSpriteRenderer;

    [Header("Target Dwell")]
    public float targetDwellTime = 1.0f;

    [Header("Origin Dwell")]
    public float originDwellTime = 0.5f;

    public TrialState State { get; private set; }

    private Trial currentTrial;

    private Vector2 startPosition;
    private Vector2 endPosition;
    private Vector2 targetPosition;

    private float movementStartTime = -1f;
    private float movementTime = 0f;

    private float targetInsideTimer = 0f;
    private float originInsideTimer = 0f;

    private bool trialError = false;

    private float GetOriginRadius()
    {
        if (originSpriteRenderer == null)
        {
            Debug.LogError("Origin SpriteRenderer is not assigned.");
            return 0f;
        }

        Bounds bounds = originSpriteRenderer.bounds;

        float diameter = Mathf.Min(
            bounds.size.x,
            bounds.size.y
        );

        return diameter / 2f;
    }

    void Start()
    {
        State = TrialState.Idle;
    }

    void Update()
    {
        switch (State)
        {
            case TrialState.Prepare:
                UpdatePrepare();
                break;

            case TrialState.Reaching:
                UpdateReaching();
                break;

            case TrialState.Returning:
                UpdateReturning();
                break;
        }
    }

    // ============================================================
    // START TRIAL
    // ============================================================

    public void StartTrial(Trial trial)
    {
        currentTrial = trial;

        movementStartTime = -1f;
        movementTime = 0f;

        targetInsideTimer = 0f;
        originInsideTimer = 0f;

        startPosition = Vector2.zero;
        endPosition = Vector2.zero;
        targetPosition = Vector2.zero;

        trialError = false;

        State = TrialState.Prepare;

        PrepareTrial();
    }

    // ============================================================
    // PREPARE
    // ============================================================

    void PrepareTrial()
    {
        startPosition = config.originPosition;

        // Robot automatically returns to origin.
        robot.MoveToOrigin(config.originPosition);

        // CSV is in cm.
        // Unity coordinates are metres.
        float amplitude = currentTrial.A / 100.0f;
        float width = currentTrial.W / 100.0f;

        // 1D experiment: target is along -X.
        targetPosition = new Vector2(
            config.originPosition.x - amplitude,
            config.originPosition.y
        );

        target.SetTarget(
            targetPosition,
            width
        );

        target.Show();

        Debug.Log(
            "Trial " + currentTrial.index +
            " | A = " + currentTrial.A + " cm" +
            " | W = " + currentTrial.W + " cm" +
            " | Target X = " + targetPosition.x
        );

        State = TrialState.Reaching;
    }

    void UpdatePrepare()
    {
        // Nothing required.
    }

    // ============================================================
    // REACHING
    // ============================================================

    void UpdateReaching()
    {
        Vector2 cursorPosition = cursor.GetPosition();

        // --------------------------------------------------------
        // Start movement timing once cursor leaves origin
        // --------------------------------------------------------

        /*
        float distanceFromStart =
            Mathf.Abs(cursorPosition.x - startPosition.x);

        if (movementStartTime < 0f)
        {
            if (distanceFromStart > 0.005f)
            {
                movementStartTime =
                    Time.realtimeSinceStartup;

                Debug.Log("Movement started.");
            }
        }*/
        float distanceFromStart =
            Vector2.Distance(
            cursorPosition,
            startPosition
        );

        float originRadius =
            GetOriginRadius();
        Debug.Log("Origin rad: " + originRadius);

        if (movementStartTime < 0f)
        {
            if (distanceFromStart > originRadius)
            {
                movementStartTime =
                    Time.realtimeSinceStartup;

                Debug.Log(
                    "Movement started. " +
                    "Cursor left origin."
                );
            }
        }

        // --------------------------------------------------------
        // Target dwell detection
        // --------------------------------------------------------

        bool insideTarget =
            target.IsCursorInside(cursorPosition);

        if (insideTarget)
        {
            targetInsideTimer += Time.deltaTime;

            Debug.Log(
                "Inside target: " +
                targetInsideTimer.ToString("F2") +
                " / " +
                targetDwellTime.ToString("F2")
            );

            if (targetInsideTimer >= targetDwellTime)
            {
                AcquireTarget();
            }
        }
        else
        {
            // Cursor left target.
            // Reset dwell timer.
            if (targetInsideTimer > 0f)
            {
                Debug.Log("Left target. Dwell timer reset.");
            }

            targetInsideTimer = 0f;
        }
    }

    // ============================================================
    // TARGET ACQUIRED
    // ============================================================

    void AcquireTarget()
    {
        if (State != TrialState.Reaching)
            return;

        if (movementStartTime >= 0f)
        {
            movementTime =
                Time.realtimeSinceStartup -
                movementStartTime;
        }

        endPosition = cursor.GetPosition();

        Debug.Log(
            "TARGET ACQUIRED. MT = " +
            movementTime.ToString("F3") +
            " s"
        );

        target.Hide();

        State = TrialState.Acquired;

        OnTargetAcquired();
    }

    // ============================================================
    // AFTER TARGET ACQUISITION
    // ============================================================

    void OnTargetAcquired()
    {
        State = TrialState.Returning;

        // Robot automatically moves to position 3.
        robot.MoveToPosition3();
    }

    // ============================================================
    // RETURNING TO ORIGIN
    // ============================================================

    /*void UpdateReturning()
    {
        Vector2 cursorPosition = cursor.GetPosition();

        float distanceFromOrigin =
            Mathf.Abs(
                cursorPosition.x -
                config.originPosition.x
            );

        if (distanceFromOrigin <= 0.005f)
        {
            originInsideTimer += Time.deltaTime;

            if (originInsideTimer >= originDwellTime)
            {
                State = TrialState.Complete;

                Debug.Log("Returned to origin.");
            }
        }
        else
        {
            originInsideTimer = 0f;
        }
    }*/
    void UpdateReturning()
    {
        Vector2 cursorPosition = cursor.GetPosition();

        float originRadius =
            GetOriginRadius();

        float distanceFromOrigin =
            Mathf.Abs(
                cursorPosition.x -
                config.originPosition.x
            );

        if (distanceFromOrigin <= originRadius)
        {
            originInsideTimer += Time.deltaTime;

            if (originInsideTimer >= originDwellTime)
            {
                State = TrialState.Complete;

                Debug.Log(
                    "Returned to origin."
                );
            }
        }
        else
        {
            originInsideTimer = 0f;
        }
    }

    // ============================================================
    // GETTERS
    // ============================================================

    public bool IsComplete()
    {
        return State == TrialState.Complete;
    }

    public float GetMovementTime()
    {
        return movementTime;
    }

    public Vector2 GetStartPosition()
    {
        return startPosition;
    }

    public Vector2 GetEndPosition()
    {
        return endPosition;
    }

    public Vector2 GetTargetPosition()
    {
        return targetPosition;
    }

    public bool GetError()
    {
        return trialError;
    }

    public Trial GetCurrentTrial()
    {
        return currentTrial;
    }
}