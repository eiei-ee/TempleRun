using System;
using System.Collections.Generic;

public interface ISingleContractGateWindowFactory
{
    PredictionGateDistanceWindow[] CreateWindows(float courseDistance,
        float runDurationSeconds, float[] presentationTimesSeconds);
}

public sealed class SingleContractLinearGateWindowFactory
    : ISingleContractGateWindowFactory
{
    private const float CommitOffsetSeconds = 1f;
    private const float ResolveOffsetSeconds = 2f;
    private const float ExitOffsetSeconds = 3f;

    public PredictionGateDistanceWindow[] CreateWindows(
        float courseDistance, float runDurationSeconds,
        float[] presentationTimesSeconds)
    {
        if (presentationTimesSeconds == null
            || presentationTimesSeconds.Length == 0)
        {
            throw new ArgumentException(
                "At least one gate presentation time is required.",
                nameof(presentationTimesSeconds));
        }
        if (!IsFinite(courseDistance) || courseDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(courseDistance));
        if (!IsFinite(runDurationSeconds) || runDurationSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(runDurationSeconds));

        var windows = new PredictionGateDistanceWindow[
            presentationTimesSeconds.Length];
        for (int i = 0; i < windows.Length; i++)
        {
            float presentationTime = presentationTimesSeconds[i];
            if (!IsFinite(presentationTime) || presentationTime < 0f
                || presentationTime > runDurationSeconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(presentationTimesSeconds));
            }

            windows[i] = new PredictionGateDistanceWindow
            {
                presentationDistance = ToDistance(presentationTime,
                    courseDistance, runDurationSeconds),
                commitDistance = ToDistance(
                    presentationTime + CommitOffsetSeconds,
                    courseDistance, runDurationSeconds),
                resolveDistance = ToDistance(
                    presentationTime + ResolveOffsetSeconds,
                    courseDistance, runDurationSeconds),
                exitDistance = ToDistance(
                    presentationTime + ExitOffsetSeconds,
                    courseDistance, runDurationSeconds)
            };
        }
        return windows;
    }

    private static float ToDistance(float timeSeconds,
        float courseDistance, float runDurationSeconds)
    {
        float clampedTime = Math.Max(0f,
            Math.Min(timeSeconds, runDurationSeconds));
        return courseDistance * clampedTime / runDurationSeconds;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

public sealed class SingleContractFixedGateWindowFactory
    : ISingleContractGateWindowFactory
{
    private readonly PredictionGateDistanceWindow[] _windows;

    public SingleContractFixedGateWindowFactory(
        PredictionGateDistanceWindow[] windows)
    {
        if (windows == null || windows.Length == 0)
        {
            throw new ArgumentException(
                "At least one precomputed gate window is required.",
                nameof(windows));
        }
        _windows = (PredictionGateDistanceWindow[])windows.Clone();
    }

    public PredictionGateDistanceWindow[] CreateWindows(
        float courseDistance, float runDurationSeconds,
        float[] presentationTimesSeconds)
    {
        if (presentationTimesSeconds == null
            || presentationTimesSeconds.Length != _windows.Length)
        {
            throw new InvalidOperationException(
                "The fixed gate window count does not match this run mode.");
        }
        return (PredictionGateDistanceWindow[])_windows.Clone();
    }
}

public sealed class SingleContractFlow : IEchoGameplayFlowRuntime
{
    public const float ChallengeDurationSeconds = 95f;
    public const float CalibrationDurationSeconds = 55f;
    public const float OpeningMemoryDurationSeconds = 2.5f;
    public const float FinaleStartSeconds = 70f;

    private static readonly float[] ChallengeGatePresentationTimesSeconds =
    {
        12f, 26f, 40f, 54f, 68f, 82f
    };

    private static readonly float[] CalibrationGatePresentationTimesSeconds =
    {
        8f, 18f, 28f, 38f, 48f
    };

    private readonly ISingleContractGateWindowFactory _windowFactory;
    private readonly int _originalHabitLane;
    private readonly float _memoryConfidence;
    private readonly List<PredictionGateSettlement> _settlements
        = new List<PredictionGateSettlement>();
    private readonly HashSet<int> _collectedGateIds = new HashSet<int>();
    private readonly HashSet<int> _consumedSettlementIndices
        = new HashSet<int>();

    private SingleContractGatePlan _gatePlan;
    private int _activeGateIndex = -1;
    private int _nextGateIndex;
    private bool _hasBegun;
    private bool _hasFinished;
    private float _lastSpeed;
    private float _lastElapsedTime;
    private float _activeGatePresentedElapsedTime;
    private float[] _gatePresentedElapsedTimes;
    private RunSettlement _finishedSettlement;

    public GameplayFlowMode Mode => GameplayFlowMode.SingleContract;
    public bool OwnsSpecialEncounters => true;
    public bool OwnsLeadSettlement => true;
    public bool OwnsFinishSchedule => true;

    public int RunSequence { get; private set; }
    public int RunSeed { get; private set; }
    public int IdentityGeneration { get; private set; }
    public bool HasOpponent { get; private set; }
    public bool IsCalibration => _hasBegun && !HasOpponent;
    public float RunDurationSeconds { get; private set; }
    public bool IsOpeningMemoryActive => _hasBegun && !_hasFinished
                                         && HasOpponent
                                         && _lastElapsedTime
                                         < OpeningMemoryDurationSeconds;
    public bool IsFinaleActive => _hasBegun && !_hasFinished
                                  && HasOpponent
                                  && _lastElapsedTime
                                  >= FinaleStartSeconds;
    public int GateCount => _gatePlan != null ? _gatePlan.GateCount : 0;
    public int ActiveGateIndex => _activeGateIndex;
    public int ActiveGateId => _activeGateIndex >= 0
        ? _gatePlan.GetGate(_activeGateIndex).Definition.gateId : -1;
    public int SettlementCount => _settlements.Count;
    public float AccumulatedSignedLeadMeters { get; private set; }
    public EchoRelearnResult LastRelearnResult { get; private set; }
    public bool RelearnTriggered => _gatePlan != null
                                    && _gatePlan.RelearnTriggered;
    public int RelearnTriggerGateId { get; private set; } = -1;
    public int RelearnStartGateNumber { get; private set; }
    public int HypothesisVersion => _gatePlan != null
        ? _gatePlan.HypothesisVersion : 0;
    public StrategyKey PredictedStrategy => _gatePlan != null
        ? _gatePlan.PredictedStrategy : StrategyKey.Neutral;

    public SingleContractFlow(int originalHabitLane = 1,
        float memoryConfidence = 1f)
        : this(new SingleContractLinearGateWindowFactory(),
            originalHabitLane, memoryConfidence)
    {
    }

    public SingleContractFlow(
        ISingleContractGateWindowFactory windowFactory,
        int originalHabitLane = 1, float memoryConfidence = 1f)
    {
        _windowFactory = windowFactory
            ?? throw new ArgumentNullException(nameof(windowFactory));
        if (originalHabitLane < 0 || originalHabitLane > 2)
            throw new ArgumentOutOfRangeException(nameof(originalHabitLane));
        if (float.IsNaN(memoryConfidence)
            || float.IsInfinity(memoryConfidence))
            throw new ArgumentOutOfRangeException(nameof(memoryConfidence));
        _originalHabitLane = originalHabitLane;
        _memoryConfidence = Math.Max(0f, Math.Min(1f, memoryConfidence));
    }

    public static float GetGatePresentationTimeSeconds(int gateIndex)
    {
        if (gateIndex < 0
            || gateIndex >= ChallengeGatePresentationTimesSeconds.Length)
            throw new ArgumentOutOfRangeException(nameof(gateIndex));
        return ChallengeGatePresentationTimesSeconds[gateIndex];
    }

    public static float GetCalibrationGatePresentationTimeSeconds(
        int gateIndex)
    {
        if (gateIndex < 0
            || gateIndex >= CalibrationGatePresentationTimesSeconds.Length)
            throw new ArgumentOutOfRangeException(nameof(gateIndex));
        return CalibrationGatePresentationTimesSeconds[gateIndex];
    }

    public PredictionGateController GetGate(int index)
    {
        if (_gatePlan == null)
            throw new InvalidOperationException(
                "BeginRun must create the gate plan before it is queried.");
        return _gatePlan.GetGate(index);
    }

    public PredictionGateSettlement GetSettlement(int index)
    {
        if (index < 0 || index >= _settlements.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _settlements[index];
    }

    public float GetGatePresentedElapsedTime(int index)
    {
        if (_gatePresentedElapsedTimes == null
            || index < 0 || index >= _gatePresentedElapsedTimes.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _gatePresentedElapsedTimes[index];
    }

    public bool TryConsumeSettlement(int index,
        out PredictionGateSettlement settlement)
    {
        if (index < 0 || index >= _settlements.Count
            || !_consumedSettlementIndices.Add(index))
        {
            settlement = default;
            return false;
        }
        settlement = _settlements[index];
        return true;
    }

    public void BeginRun(EchoRunContext context)
    {
        RunSequence = context.runSequence;
        RunSeed = context.runSeed;
        IdentityGeneration = context.generation;
        HasOpponent = context.hasOpponent;
        RunDurationSeconds = HasOpponent
            ? ChallengeDurationSeconds : CalibrationDurationSeconds;
        _settlements.Clear();
        _collectedGateIds.Clear();
        _consumedSettlementIndices.Clear();
        AccumulatedSignedLeadMeters = 0f;
        LastRelearnResult = default;
        RelearnTriggerGateId = -1;
        RelearnStartGateNumber = 0;
        _activeGateIndex = -1;
        _nextGateIndex = 0;
        _lastSpeed = 0f;
        _lastElapsedTime = 0f;
        _activeGatePresentedElapsedTime = 0f;
        _hasFinished = false;
        _finishedSettlement = default;

        float[] schedule = HasOpponent
            ? (float[])ChallengeGatePresentationTimesSeconds.Clone()
            : (float[])CalibrationGatePresentationTimesSeconds.Clone();
        PredictionGateDistanceWindow[] windows =
            _windowFactory.CreateWindows(context.courseDistance,
                RunDurationSeconds, schedule);
        ValidateWindowOrder(windows, schedule.Length);
        PredictionGateDefinition[] definitions = HasOpponent
            ? PredictionGateTemplates.Create(
                RunSequence, RunSeed, _originalHabitLane, windows)
            : PredictionGateTemplates.CreateCalibration(
                RunSequence, RunSeed, _originalHabitLane, windows);
        _gatePlan = new SingleContractGatePlan(definitions);
        _gatePresentedElapsedTimes = new float[_gatePlan.GateCount];
        for (int i = 0; i < _gatePresentedElapsedTimes.Length; i++)
            _gatePresentedElapsedTimes[i] = -1f;

        _hasBegun = true;
    }

    public void Tick(EchoRunFrame frame)
    {
        if (!_hasBegun || _hasFinished)
            return;

        _lastSpeed = frame.currentSpeed;
        _lastElapsedTime = Math.Max(0f, frame.elapsedTime);
        if (_gatePlan == null)
            return;

        if (_activeGateIndex < 0
            && (!HasOpponent
                || _lastElapsedTime >= OpeningMemoryDurationSeconds))
            PresentNextEligibleGate(frame.playerDistance);

        if (_activeGateIndex < 0)
            return;

        PredictionGateController gate =
            _gatePlan.GetGate(_activeGateIndex);
        PredictionGateDefinition definition = gate.Definition;
        if (gate.State == PredictionGateLifecycle.Presented
            && frame.playerDistance >= definition.commitDistance)
        {
            TryCommitChoice(new GateChoice
            {
                gateId = definition.gateId,
                physicalLane = frame.playerLane,
                hasLateralEvidence = frame.hasLateralEvidence,
                lateralOffset = frame.lateralOffset,
                laneChangeInProgress = frame.laneChangeInProgress,
                routeDistance = frame.playerDistance,
                reactionTime = Math.Max(0f,
                    _lastElapsedTime - _activeGatePresentedElapsedTime)
            });
        }

        if (gate.State != PredictionGateLifecycle.ChoiceCommitted
            || frame.playerDistance < definition.exitDistance)
            return;

        bool requiresObstacle = CommittedLaneRequiresObstacle(gate);
        GateExecutionReason reason = !requiresObstacle
            ? GateExecutionReason.Completed
            : frame.playerLane != gate.CommittedChoice.physicalLane
                ? GateExecutionReason.RouteAbandoned
                : GateExecutionReason.Unresolved;
        ResolveActiveGate(requiresObstacle
            ? GateExecutionOutcome.Hit
            : GateExecutionOutcome.Success, _lastSpeed, reason);
    }

    public void OnGateChoiceCommitted(GateChoice choice)
    {
        TryCommitChoice(choice);
    }

    public GateTransitionResult TryCommitChoice(GateChoice choice)
    {
        if (!_hasBegun || _hasFinished || _activeGateIndex < 0)
            return GateTransitionResult.Rejected;
        PredictionGateController gate =
            _gatePlan.GetGate(_activeGateIndex);
        if (gate.State == PredictionGateLifecycle.Presented
            && choice.reactionTime <= 0f)
        {
            choice.reactionTime = Math.Max(0f,
                _lastElapsedTime - _activeGatePresentedElapsedTime);
        }
        return gate.CommitChoice(choice);
    }

    public void OnObstaclePassed(GateObstacleEvent obstacleEvent)
    {
        ResolveObstacle(obstacleEvent, GateExecutionOutcome.Success);
    }

    public void OnObstacleHit(GateObstacleEvent obstacleEvent)
    {
        ResolveObstacle(obstacleEvent, GateExecutionOutcome.Hit);
    }

    public GateTransitionResult ResolveObstaclePassed(
        GateObstacleEvent obstacleEvent)
    {
        return ResolveObstacle(
            obstacleEvent, GateExecutionOutcome.Success);
    }

    public GateTransitionResult ResolveObstacleHit(
        GateObstacleEvent obstacleEvent)
    {
        return ResolveObstacle(obstacleEvent, GateExecutionOutcome.Hit);
    }

    public GateTransitionResult CancelActiveGate()
    {
        if (_activeGateIndex < 0)
            return GateTransitionResult.Rejected;
        return CancelGate(ActiveGateId);
    }

    public GateTransitionResult CancelGate(int gateId)
    {
        if (!_hasBegun || _gatePlan == null
            || !_gatePlan.TryGetGate(gateId,
                out PredictionGateController gate))
            return GateTransitionResult.Rejected;

        if (gate.State != PredictionGateLifecycle.Cancelled
            && (_activeGateIndex < 0
                || _gatePlan.GetGate(_activeGateIndex) != gate))
            return GateTransitionResult.Rejected;

        GateTransitionResult result = gate.Cancel(
            out PredictionGateSettlement settlement);
        if (result == GateTransitionResult.Applied
            || result == GateTransitionResult.AlreadyApplied)
        {
            CollectSettlement(gate, settlement);
            if (_activeGateIndex >= 0
                && _gatePlan.GetGate(_activeGateIndex) == gate)
                _activeGateIndex = -1;
        }
        return result;
    }

    public GateTransitionResult RecycleGate(int gateId)
    {
        return CancelGate(gateId);
    }

    public RunSettlement FinishRun(RunEndReason reason)
    {
        return FinishRun(reason, AccumulatedSignedLeadMeters);
    }

    public RunSettlement FinishRun(RunEndReason reason,
        float playerLeadMeters)
    {
        if (_hasFinished)
            return _finishedSettlement;

        if (_activeGateIndex >= 0)
            CancelActiveGate();

        bool reachedFinish = reason == RunEndReason.FinishReached;
        float authoritativeLead = float.IsNaN(playerLeadMeters)
                                  || float.IsInfinity(playerLeadMeters)
            ? 0f : playerLeadMeters;
        bool playerWon = HasOpponent && reachedFinish
                         && authoritativeLead >= 0f;
        _finishedSettlement = new RunSettlement
        {
            reason = reason,
            reachedFinish = reachedFinish,
            playerWon = playerWon,
            playerLeadMeters = HasOpponent
                ? authoritativeLead : 0f
        };
        _hasFinished = true;
        return _finishedSettlement;
    }

    private void PresentNextEligibleGate(float playerDistance)
    {
        while (_nextGateIndex < _gatePlan.GateCount)
        {
            PredictionGateController candidate =
                _gatePlan.GetGate(_nextGateIndex);
            if (candidate.State != PredictionGateLifecycle.Scheduled)
            {
                _nextGateIndex++;
                continue;
            }
            if (playerDistance < candidate.Definition.presentationDistance)
                return;

            if (candidate.Present() == GateTransitionResult.Applied)
            {
                _activeGateIndex = _nextGateIndex;
                _nextGateIndex++;
                _activeGatePresentedElapsedTime = _lastElapsedTime;
                _gatePresentedElapsedTimes[_activeGateIndex]
                    = _activeGatePresentedElapsedTime;
            }
            return;
        }
    }

    private GateTransitionResult ResolveObstacle(
        GateObstacleEvent obstacleEvent, GateExecutionOutcome outcome)
    {
        if (!_hasBegun || _hasFinished || _activeGateIndex < 0)
            return GateTransitionResult.Rejected;

        PredictionGateController gate =
            _gatePlan.GetGate(_activeGateIndex);
        if (gate.State != PredictionGateLifecycle.ChoiceCommitted
            || obstacleEvent.gateId != gate.Definition.gateId
            || obstacleEvent.physicalLane
            != gate.CommittedChoice.physicalLane
            || !CommittedLaneRequiresObstacle(gate))
            return GateTransitionResult.Rejected;

        return ResolveActiveGate(outcome, _lastSpeed,
            outcome == GateExecutionOutcome.Hit
                ? GateExecutionReason.Collision
                : GateExecutionReason.Completed);
    }

    private GateTransitionResult ResolveActiveGate(
        GateExecutionOutcome outcome, float speedAtResolution,
        GateExecutionReason executionReason = GateExecutionReason.None)
    {
        PredictionGateController gate =
            _gatePlan.GetGate(_activeGateIndex);
        GateTransitionResult result = gate.ResolveExecution(outcome,
            speedAtResolution, _memoryConfidence,
            out PredictionGateSettlement settlement, executionReason);
        if (result != GateTransitionResult.Applied
            && result != GateTransitionResult.AlreadyApplied)
            return result;

        GateTransitionResult applyResult =
            gate.ApplyDistance(out settlement);
        if (applyResult == GateTransitionResult.Applied
            || applyResult == GateTransitionResult.AlreadyApplied)
            CollectSettlement(gate, settlement);
        gate.Close();
        _activeGateIndex = -1;
        return result;
    }

    private void CollectSettlement(PredictionGateController gate,
        PredictionGateSettlement settlement)
    {
        if (!_collectedGateIds.Add(settlement.gateId))
            return;

        _settlements.Add(settlement);
        if (HasOpponent)
        {
            AccumulatedSignedLeadMeters += settlement.signedLeadMeters;
            LastRelearnResult = _gatePlan.RecordSettlement(settlement);
            if (LastRelearnResult.triggered)
            {
                RelearnTriggerGateId = settlement.gateId;
                int firstRemappedSequence = 0;
                for (int index = 0; index < _gatePlan.GateCount; index++)
                {
                    PredictionGateDefinition definition =
                        _gatePlan.GetGate(index).Definition;
                    if (definition.hypothesisVersion
                        != LastRelearnResult.hypothesisVersion)
                        continue;
                    if (firstRemappedSequence == 0
                        || definition.sequence < firstRemappedSequence)
                        firstRemappedSequence = definition.sequence;
                }
                RelearnStartGateNumber = firstRemappedSequence;
            }
        }
    }

    private static bool CommittedLaneRequiresObstacle(
        PredictionGateController gate)
    {
        return gate.Definition.TryGetLane(
                   gate.CommittedChoice.physicalLane,
                   out PredictionGateLane lane)
               && lane.obstacle.isRequired;
    }

    private static void ValidateWindowOrder(
        PredictionGateDistanceWindow[] windows, int expectedCount)
    {
        if (windows == null
            || windows.Length != expectedCount)
            throw new InvalidOperationException(
                "The gate window factory returned the wrong gate count.");

        float previousPresentationDistance = -1f;
        for (int i = 0; i < windows.Length; i++)
        {
            if (!windows[i].IsValid
                || windows[i].presentationDistance
                <= previousPresentationDistance)
            {
                throw new InvalidOperationException(
                    "Gate windows must be valid and strictly distance ordered.");
            }
            previousPresentationDistance = windows[i].presentationDistance;
        }
    }
}
