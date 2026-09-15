using UnityEngine;

public class ExperimentManager : MonoBehaviour
{
    // ============================================================
    // REFERENCES
    // ============================================================

    [Header("References")]
    public ExperimentConfig config;
    public TrialSequence trialSequence;
    public TrialManager trialManager;
    public UIController ui;
    public CSVLogger logger;
    public RobotController robot;

    [Header("Schedules")]
    public TextAsset warmupCSV;
    public TextAsset block1CSV;
    public TextAsset block2CSV;


    // ============================================================
    // EXPERIMENT STATE
    // ============================================================

    public ExperimentState State { get; private set; }

    private ExperimentState stateAfterRest;


    // ============================================================
    // TRIAL COUNTERS
    // ============================================================

    /*
     * Index of the trial within the CURRENT BLOCK.
     *
     * 0  → first trial
     * 44 → trial 45
     * 45 → trial 46
     * ...
     * 179 → trial 180
     */
    private int currentTrialIndex = 0;


    /*
     * Current round.
     *
     * 0 → Round 1
     * 1 → Round 2
     * 2 → Round 3
     * 3 → Round 4
     */
    private int currentRound = 0;


    /*
     * Number of trials completed in the current block.
     */
    private int completedTrials = 0;


    // ============================================================
    // REST
    // ============================================================

    private float restStartTime;
    private float restDuration;


    // ============================================================
    // START
    // ============================================================

    void Start()
    {
        /*
         * AUTHORITY.
         *
         * In RobotM2 mode CORC owns the protocol: trial order, target onset, entry and dwell
         * detection, every timing that enters the analysis, and the trial-by-trial log. This
         * manager must not run at all, or two state machines would sequence trials against the
         * same participant. RemoteExperimentController renders instead.
         *
         * Nothing measured in MouseSimulation mode is comparable to robot data: the mouse is
         * sampled at frame rate, has no force feedback, and the movement time measured below is
         * quantised to the frame period. Simulation mode is for piloting the display and the
         * trial schedule only.
         */
        if (config == null)
        {
            Debug.LogError("ExperimentManager: no ExperimentConfig assigned.");
            enabled = false;
            return;
        }

        if (config.mode != ControlMode.MouseSimulation)
        {
            Debug.Log(
                "ExperimentManager disabled: control mode is " + config.mode +
                ". CORC sequences the protocol; RemoteExperimentController renders it."
            );
            enabled = false;
            return;
        }

        State = ExperimentState.Idle;

        currentTrialIndex = 0;
        currentRound = 0;
        completedTrials = 0;

        /*
         * The logger was previously declared but never opened, so simulation runs recorded
         * nothing at all.
         */
        if (logger != null)
            logger.StartNewFile();

        if (ui != null)
        {
            ui.HideRest();

            ui.ShowState("READY (SIMULATION)");
            ui.ShowInstruction(
                "Mouse simulation. Press SPACE to start."
            );
        }

        Debug.Log("Experiment Manager started in mouse simulation mode.");
    }


    // ============================================================
    // UPDATE
    // ============================================================

    void Update()
    {
        /*
         * Belt and braces: Start() disables the component in robot mode, but a scene that
         * re-enables it by hand must still not drive the protocol.
         */
        if (config == null || config.mode != ControlMode.MouseSimulation)
            return;

        switch (State)
        {
            case ExperimentState.Idle:
                UpdateIdle();
                break;

            case ExperimentState.Warmup:
                UpdateWarmup();
                break;

            case ExperimentState.RoundRest:
                UpdateRoundRest();
                break;

            case ExperimentState.TransitionToBlock1:
                UpdateTransitionToBlock1();
                break;

            case ExperimentState.Block1:
                UpdateBlock1();
                break;

            case ExperimentState.BlockRest:
                UpdateBlockRest();
                break;

            case ExperimentState.Block2:
                UpdateBlock2();
                break;

            case ExperimentState.Finished:
                UpdateFinished();
                break;
        }
    }


    // ============================================================
    // IDLE
    // ============================================================

    void UpdateIdle()
    {
        if (Input.GetKeyDown(config.startKey))
        {
            StartWarmup();
        }
    }


    // ============================================================
    // WARMUP
    // ============================================================

    void StartWarmup()
    {
        trialSequence.LoadCSV(warmupCSV);
        State = ExperimentState.Warmup;

        Debug.Log("Starting warmup.");

        /*
         * Warmup consists of the first warmupTrials
         * in the trial sequence.
         */
        currentTrialIndex = 0;
        currentRound = 0;
        completedTrials = 0;

        ui.HideRest();

        ui.ShowState("WARMUP");

        ui.ShowInstruction(
            "Warmup: move the cursor to the target."
        );

        Debug.Log(
            "Warmup started. Trials loaded: " +
            trialSequence.Count()
        );

        StartNextTrial();
    }


    void UpdateWarmup()
    {
        /*
         * TrialManager handles the actual trial.
         */

        if (trialManager.IsComplete())
        {
            CompleteCurrentTrial();

            /*
             * Warmup finished?
             */
            if (completedTrials >= config.warmupTrials)
            {
                Debug.Log("Warmup complete.");

                StartRest(
                    "Warmup complete.\nPrepare for Block 1.",
                    60f,
                    ExperimentState.TransitionToBlock1
                );
            }
            else
            {
                StartNextTrial();
            }
        }
    }


    // ============================================================
    // TRANSITION TO BLOCK 1
    // ============================================================

    void UpdateTransitionToBlock1()
    {
        /*
         * This state exists only to make the transition explicit.
         */

        StartBlock1();
    }


    void StartBlock1()
    {
        trialSequence.LoadCSV(block1CSV);

        State = ExperimentState.Block1;

        /*
         * IMPORTANT:
         *
         * Reset the block counter here.
         *
         * This happens ONCE when Block 1 starts,
         * NOT every time a new round starts.
         */
        currentTrialIndex = 0;
        currentRound = 0;
        completedTrials = 0;

        ui.HideRest();

        ui.ShowState("BLOCK 1");

        ui.ShowInstruction(
            "Block 1: move the cursor to the target."
        );

        Debug.Log(
            "Starting Block 1: 180 trials."
        );

        StartNextTrial();
    }


    void UpdateBlock1()
    {
        if (trialManager.IsComplete())
        {
            CompleteCurrentTrial();

            /*
             * Check whether the entire block is finished.
             */
            if (completedTrials >=
                config.trialsPerRound *
                config.roundsPerBlock)
            {
                Debug.Log(
                    "Block 1 complete."
                );

                StartRest(
                    "Block 1 complete.\nPrepare for Block 2.",
                    config.maxBlockRestTime,
                    ExperimentState.Block2
                );

                return;
            }


            /*
             * Check whether the current ROUND is finished.
             */
            if (completedTrials %
                config.trialsPerRound == 0)
            {
                currentRound++;

                Debug.Log(
                    "Block 1 Round " +
                    currentRound +
                    " complete."
                );

                /*
                 * IMPORTANT:
                 *
                 * DO NOT RESET currentTrialIndex HERE.
                 *
                 * It must continue from 46, 91, 136, etc.
                 */

                StartRest(
                    "Round " +
                    currentRound +
                    " complete.",
                    config.maxRoundRestTime,
                    ExperimentState.Block1
                );

                return;
            }


            /*
             * Otherwise simply start the next trial.
             */
            StartNextTrial();
        }
    }


    // ============================================================
    // BLOCK REST
    // ============================================================

    void UpdateBlockRest()
    {
        float elapsed =
            Time.realtimeSinceStartup -
            restStartTime;

        float remaining =
            Mathf.Max(
                0f,
                restDuration - elapsed
            );

        ui.ShowRest(
            "Rest",
            remaining
        );

        /*
         * User can continue before the maximum
         * rest duration.
         */
        if (Input.GetKeyDown(config.continueKey))
        {
            EndRest();
            return;
        }

        /*
         * Automatically continue if maximum
         * rest time expires.
         */
        if (elapsed >= restDuration)
        {
            EndRest();
        }
    }


    // ============================================================
    // ROUND REST
    // ============================================================

    void UpdateRoundRest()
    {
        float elapsed =
            Time.realtimeSinceStartup -
            restStartTime;

        float remaining =
            Mathf.Max(
                0f,
                restDuration - elapsed
            );

        ui.ShowRest(
            "Round complete.",
            remaining
        );

        if (Input.GetKeyDown(config.continueKey))
        {
            EndRest();
            return;
        }

        if (elapsed >= restDuration)
        {
            EndRest();
        }
    }


    // ============================================================
    // START REST
    // ============================================================

    void StartRest(
        string message,
        float duration,
        ExperimentState nextState)
    {
        stateAfterRest = nextState;

        restStartTime =
            Time.realtimeSinceStartup;

        restDuration = duration;

        /*
         * Determine whether this is a round rest
         * or block rest.
         */
        if (duration <= config.maxRoundRestTime)
        {
            State = ExperimentState.RoundRest;
        }
        else
        {
            State = ExperimentState.BlockRest;
        }

        ui.ShowRest(
            message,
            duration
        );

        Debug.Log(
            "Rest started. Duration = " +
            duration +
            " s"
        );
    }


    // ============================================================
    // END REST
    // ============================================================

    void EndRest()
    {
        ui.HideRest();

        State = stateAfterRest;

        Debug.Log(
            "Rest finished. Continuing with state: " +
            State
        );

        /*
         * Start the appropriate next stage.
         */

        if (State == ExperimentState.TransitionToBlock1)
        {
            StartBlock1();
        }
        else if (State == ExperimentState.Block1)
        {
            StartNextTrial();
        }
        else if (State == ExperimentState.Block2)
        {
            StartBlock2();
        }
    }


    // ============================================================
    // BLOCK 2
    // ============================================================

    void StartBlock2()
    {
        trialSequence.LoadCSV(block2CSV);

        State = ExperimentState.Block2;

        /*
         * Block 2 starts a NEW block.
         *
         * Therefore the trial counter resets here.
         */
        currentTrialIndex = 0;
        currentRound = 0;
        completedTrials = 0;

        ui.HideRest();

        ui.ShowState("BLOCK 2");

        ui.ShowInstruction(
            "Block 2: move the cursor to the target."
        );

        Debug.Log(
            "Starting Block 2: 180 trials."
        );

        StartNextTrial();
    }


    void UpdateBlock2()
    {
        if (trialManager.IsComplete())
        {
            CompleteCurrentTrial();

            /*
             * Entire Block 2 finished.
             */
            if (completedTrials >=
                config.trialsPerRound *
                config.roundsPerBlock)
            {
                Debug.Log(
                    "Block 2 complete."
                );

                State = ExperimentState.Finished;

                ui.HideRest();

                ui.ShowState("FINISHED");

                ui.ShowInstruction(
                    "Experiment complete. Thank you."
                );

                return;
            }


            /*
             * End of round.
             */
            if (completedTrials %
                config.trialsPerRound == 0)
            {
                currentRound++;

                Debug.Log(
                    "Block 2 Round " +
                    currentRound +
                    " complete."
                );

                StartRest(
                    "Round " +
                    currentRound +
                    " complete.",
                    config.maxRoundRestTime,
                    ExperimentState.Block2
                );

                return;
            }


            StartNextTrial();
        }
    }


    // ============================================================
    // START NEXT TRIAL
    // ============================================================

    void StartNextTrial()
    {
        /*
         * Safety check.
         */
        if (trialSequence.Count() <= 0)
        {
            Debug.LogError(
                "No trials in TrialSequence."
            );

            return;
        }

        /*
         * Warmup.csv holds one condition per ID (5 rows) while config.warmupTrials asks for 10
         * reps, so the list has to be cycled: rep 6 re-presents condition 1. Previously the index
         * ran off the end of the list and warm-up aborted after 5 trials with "No more trials".
         *
         * This mirrors warmup_repeats in CORC (warmupRepeats rounds, each covering every warm-up
         * condition once, in increasing ID order).
         */
        int sequenceIndex = currentTrialIndex;

        if (State == ExperimentState.Warmup)
        {
            sequenceIndex = currentTrialIndex % trialSequence.Count();
        }
        else if (currentTrialIndex >= trialSequence.Count())
        {
            Debug.LogError(
                "No more trials in TrialSequence."
            );

            return;
        }

        Trial trial =
            trialSequence.GetTrial(
                sequenceIndex
            );

        if (trial == null)
        {
            Debug.LogError(
                "Trial is null at index " +
                currentTrialIndex
            );

            return;
        }

        /*
         * Display the CONTINUOUS block trial number.
         *
         * currentTrialIndex is zero-based,
         * so add 1 for display.
         */
        int displayTrialNumber =
            currentTrialIndex + 1;

        int totalBlockTrials =
            config.trialsPerRound *
            config.roundsPerBlock;

        ui.ShowTrial(
            displayTrialNumber,
            totalBlockTrials
        );

        ui.ShowTimer(0f);

        Debug.Log(
            "StartNextTrial." +
            "\nIndex = " +
            currentTrialIndex +
            "\nDisplay Trial = " +
            displayTrialNumber +
            " / " +
            totalBlockTrials +
            "\nRound = " +
            (currentRound + 1) +
            "\nA = " +
            trial.A +
            "\nW = " +
            trial.W +
            "\nID = " +
            trial.ID
        );

        /*
         * Start the actual trial.
         */
        trialManager.StartTrial(trial);
    }


    // ============================================================
    // COMPLETE TRIAL
    // ============================================================

    void CompleteCurrentTrial()
    {
        Trial trial =
            trialManager.GetCurrentTrial();

        if (trial == null)
            return;

        /*
         * Record the trial. Simulation only: these movement times are sampled at frame rate and
         * must not be pooled with, or compared to, the CORC measurements.
         */
        if (logger != null)
        {
            logger.LogTrial(
                State.ToString(),
                currentRound + 1,
                trial,
                trialManager.GetTargetPosition(),
                trialManager.GetStartPosition(),
                trialManager.GetEndPosition(),
                trialManager.GetMovementTime(),
                trialManager.GetError()
            );
        }

        completedTrials++;

        /*
         * Move to the next trial INDEX.
         *
         * This is the critical part.
         */
        currentTrialIndex++;

        Debug.Log(
            "Trial complete." +
            "\nCompleted in block = " +
            completedTrials +
            "\nNext trial index = " +
            currentTrialIndex
        );
    }


    // ============================================================
    // FINISHED
    // ============================================================

    void UpdateFinished()
    {
        /*
         * Nothing to do.
         */
    }
}