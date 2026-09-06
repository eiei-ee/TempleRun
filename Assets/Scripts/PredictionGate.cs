using System;
using UnityEngine;

public enum PredictionGateRole
{
    Predicted,
    Counter,
    Neutral
}

public enum StrategyKey
{
    OriginalHabit,
    AvoidOriginal,
    Neutral
}

public enum RouteAttribute
{
    Safe,
    Reward,
    Risk
}

public enum PredictionGateTemplateKind
{
    CounterJump,
    CounterSlide,
    FinalChoice
}

public enum GateExecutionOutcome
{
    None,
    Success,
    Hit,
    Cancelled
}

public enum GateExecutionReason
{
    None,
    Completed,
    Collision,
    RouteAbandoned,
    Unresolved,
    Cancelled
}

public enum PredictionGateLifecycle
{
    Scheduled,
    Presented,
    ChoiceCommitted,
    ExecutionResolved,
    DistanceApplied,
    Closed,
    Cancelled
}

public enum GateTransitionResult
{
    Applied,
    AlreadyApplied,
    Rejected
}

[Serializable]
public sealed class GateAttempt
{
    public int gateId;
    public int hypothesisVersion;
    public int committedLane;
    public PredictionGateRole chosenRole;
    public StrategyKey strategyKey;
    public GateExecutionOutcome execution;
    public GateExecutionReason executionReason;
    public bool hasLateralEvidence;
    public float lateralOffset;
    public bool laneChangeInProgress;
    public bool predictionMatched;
    public float reactionTime;
    public float leadDeltaSeconds;
    public float leadDeltaMeters;
}

[Serializable]
public struct PredictionGateObstacle
{
    public bool isRequired;
    public ObstacleType obstacleType;
    public int prefabIndex;

    public static PredictionGateObstacle None => new PredictionGateObstacle
    {
        isRequired = false,
        obstacleType = ObstacleType.Barrier,
        prefabIndex = -1
    };
}

[Serializable]
public struct PredictionGateLane
{
    public int physicalLane;
    public PredictionGateRole role;
    public StrategyKey strategyKey;
    public RouteAttribute attribute;
    public PredictionGateObstacle obstacle;
    public int coinCount;
}

[Serializable]
public struct PredictionGateDistanceWindow
{
    public float presentationDistance;
    public float commitDistance;
    public float resolveDistance;
    public float exitDistance;

    public bool IsValid => presentationDistance >= 0f
                           && commitDistance >= presentationDistance
                           && resolveDistance >= commitDistance
                           && exitDistance >= resolveDistance;
}

[Serializable]
public sealed class PredictionGateDefinition
{
    public int runId;
    public int gateId;
    public int sequence;
    public int hypothesisVersion = 1;
    public StrategyKey predictedStrategy = StrategyKey.OriginalHabit;
    public bool isFinal;
    public PredictionGateTemplateKind templateKind;
    public float presentationDistance;
    public float commitDistance;
    public float resolveDistance;
    public float exitDistance;
    public PredictionGateLane[] lanes;

    public PredictionGateDefinition Clone()
    {
        return new PredictionGateDefinition
        {
            runId = runId,
            gateId = gateId,
            sequence = sequence,
            hypothesisVersion = hypothesisVersion,
            predictedStrategy = predictedStrategy,
            isFinal = isFinal,
            templateKind = templateKind,
            presentationDistance = presentationDistance,
            commitDistance = commitDistance,
            resolveDistance = resolveDistance,
            exitDistance = exitDistance,
            lanes = lanes != null
                ? (PredictionGateLane[])lanes.Clone()
                : null
        };
    }

    public bool IsValid()
    {
        if (gateId <= 0 || sequence <= 0 || hypothesisVersion <= 0
            || lanes == null || lanes.Length != 3
            || (predictedStrategy != StrategyKey.OriginalHabit
                && predictedStrategy != StrategyKey.AvoidOriginal))
            return false;

        var physicalLanes = new bool[3];
        var roles = new bool[3];
        var strategies = new bool[3];
        for (int i = 0; i < lanes.Length; i++)
        {
            PredictionGateLane lane = lanes[i];
            int role = (int)lane.role;
            int strategy = (int)lane.strategyKey;
            if (lane.physicalLane < 0 || lane.physicalLane > 2
                || role < 0 || role >= roles.Length
                || strategy < 0 || strategy >= strategies.Length
                || physicalLanes[lane.physicalLane]
                || roles[role] || strategies[strategy])
                return false;
            if (!Enum.IsDefined(typeof(RouteAttribute), lane.attribute)
                || lane.coinCount < 0
                || lane.obstacle.isRequired
                && (lane.obstacle.prefabIndex < 0
                    || !Enum.IsDefined(typeof(ObstacleType),
                        lane.obstacle.obstacleType)))
                return false;
            physicalLanes[lane.physicalLane] = true;
            roles[role] = true;
            strategies[strategy] = true;
        }

        return presentationDistance >= 0f
               && commitDistance >= presentationDistance
               && resolveDistance >= commitDistance
               && exitDistance >= resolveDistance;
    }

    public bool TryGetLane(int physicalLane, out PredictionGateLane lane)
    {
        if (lanes != null)
        {
            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i].physicalLane != physicalLane) continue;
                lane = lanes[i];
                return true;
            }
        }

        lane = default;
        return false;
    }

    public PredictionGateDefinition RemapPrediction(
        StrategyKey strategy, int version)
    {
        if (strategy != StrategyKey.OriginalHabit
            && strategy != StrategyKey.AvoidOriginal)
            throw new ArgumentOutOfRangeException(nameof(strategy));
        if (version < hypothesisVersion)
            throw new ArgumentOutOfRangeException(nameof(version));

        PredictionGateDefinition remapped = Clone();
        remapped.predictedStrategy = strategy;
        remapped.hypothesisVersion = Mathf.Max(1, version);
        for (int i = 0; i < remapped.lanes.Length; i++)
        {
            PredictionGateLane lane = remapped.lanes[i];
            if (lane.strategyKey == StrategyKey.Neutral)
                lane.role = PredictionGateRole.Neutral;
            else
                lane.role = lane.strategyKey == strategy
                    ? PredictionGateRole.Predicted
                    : PredictionGateRole.Counter;
            remapped.lanes[i] = lane;
        }
        return remapped;
    }
}

public sealed class PredictionGateController
{
    public PredictionGateLifecycle State { get; private set; }
        = PredictionGateLifecycle.Scheduled;
    public bool HasChoice { get; private set; }
    public GateChoice CommittedChoice { get; private set; }
    public PredictionGateRole CommittedRole { get; private set; }
    public StrategyKey CommittedStrategy { get; private set; }
    public GateExecutionOutcome ExecutionOutcome { get; private set; }
        = GateExecutionOutcome.None;
    public GateExecutionReason ExecutionReason { get; private set; }
        = GateExecutionReason.None;
    public float ReactionTime { get; private set; }
    public PredictionGateDefinition Definition => _definition.Clone();

    private PredictionGateDefinition _definition;
    private PredictionGateSettlement _settlement;
    private bool _hasSettlement;

    public PredictionGateController(PredictionGateDefinition definition)
    {
        if (definition == null || !definition.IsValid())
            throw new ArgumentException(
                "Prediction gate definition is invalid.", nameof(definition));
        _definition = definition.Clone();
    }

    public GateTransitionResult Present()
    {
        if (State == PredictionGateLifecycle.Presented)
            return GateTransitionResult.AlreadyApplied;
        if (State != PredictionGateLifecycle.Scheduled)
            return GateTransitionResult.Rejected;
        State = PredictionGateLifecycle.Presented;
        return GateTransitionResult.Applied;
    }

    public GateTransitionResult CommitChoice(GateChoice choice)
    {
        if (choice.gateId != _definition.gateId)
            return GateTransitionResult.Rejected;
        if (State == PredictionGateLifecycle.ChoiceCommitted)
        {
            return HasChoice
                   && CommittedChoice.physicalLane == choice.physicalLane
                ? GateTransitionResult.AlreadyApplied
                : GateTransitionResult.Rejected;
        }
        if (State != PredictionGateLifecycle.Presented
            || !_definition.TryGetLane(choice.physicalLane,
                out PredictionGateLane lane))
            return GateTransitionResult.Rejected;

        choice.routeDistance = Mathf.Max(0f, choice.routeDistance);
        choice.reactionTime = Mathf.Max(0f, choice.reactionTime);
        HasChoice = true;
        CommittedChoice = choice;
        CommittedRole = lane.role;
        CommittedStrategy = lane.strategyKey;
        ReactionTime = choice.reactionTime;
        State = PredictionGateLifecycle.ChoiceCommitted;
        return GateTransitionResult.Applied;
    }

    public GateTransitionResult ResolveExecution(
        GateExecutionOutcome outcome, float speedAtResolution,
        float memoryConfidence, out PredictionGateSettlement settlement,
        GateExecutionReason executionReason = GateExecutionReason.None)
    {
        if (outcome == GateExecutionOutcome.Cancelled)
            return Cancel(out settlement);

        if (State == PredictionGateLifecycle.ExecutionResolved)
        {
            settlement = _settlement;
            return outcome == ExecutionOutcome
                ? GateTransitionResult.AlreadyApplied
                : GateTransitionResult.Rejected;
        }
        if (State != PredictionGateLifecycle.ChoiceCommitted
            || (outcome != GateExecutionOutcome.Success
                && outcome != GateExecutionOutcome.Hit))
        {
            settlement = default;
            return GateTransitionResult.Rejected;
        }

        _settlement = PredictionGateEvaluator.Evaluate(
            _definition.gateId, CommittedRole, outcome,
            speedAtResolution, memoryConfidence, executionReason);
        ExecutionOutcome = outcome;
        ExecutionReason = _settlement.executionReason;
        _hasSettlement = true;
        State = PredictionGateLifecycle.ExecutionResolved;
        settlement = _settlement;
        return GateTransitionResult.Applied;
    }

    public GateTransitionResult ApplyDistance(
        out PredictionGateSettlement settlement)
    {
        if (State == PredictionGateLifecycle.DistanceApplied)
        {
            settlement = _settlement;
            return GateTransitionResult.AlreadyApplied;
        }
        if (State != PredictionGateLifecycle.ExecutionResolved
            || !_hasSettlement)
        {
            settlement = default;
            return GateTransitionResult.Rejected;
        }

        State = PredictionGateLifecycle.DistanceApplied;
        settlement = _settlement;
        return GateTransitionResult.Applied;
    }

    public GateTransitionResult Close()
    {
        if (State == PredictionGateLifecycle.Closed)
            return GateTransitionResult.AlreadyApplied;
        if (State != PredictionGateLifecycle.DistanceApplied)
            return GateTransitionResult.Rejected;
        State = PredictionGateLifecycle.Closed;
        return GateTransitionResult.Applied;
    }

    public GateTransitionResult Cancel(
        out PredictionGateSettlement settlement)
    {
        if (State == PredictionGateLifecycle.Cancelled)
        {
            settlement = _settlement;
            return GateTransitionResult.AlreadyApplied;
        }
        if (State == PredictionGateLifecycle.ExecutionResolved
            || State == PredictionGateLifecycle.DistanceApplied
            || State == PredictionGateLifecycle.Closed)
        {
            settlement = default;
            return GateTransitionResult.Rejected;
        }

        PredictionGateRole role = HasChoice
            ? CommittedRole : PredictionGateRole.Neutral;
        ExecutionOutcome = GateExecutionOutcome.Cancelled;
        _settlement = PredictionGateEvaluator.Evaluate(
            _definition.gateId, role, GateExecutionOutcome.Cancelled, 0f, 0f);
        ExecutionReason = _settlement.executionReason;
        _hasSettlement = true;
        State = PredictionGateLifecycle.Cancelled;
        settlement = _settlement;
        return GateTransitionResult.Applied;
    }

    public bool TryGetSettlement(out PredictionGateSettlement settlement)
    {
        settlement = _settlement;
        return _hasSettlement;
    }

    public GateAttempt BuildAttempt()
    {
        return new GateAttempt
        {
            gateId = _definition.gateId,
            hypothesisVersion = _definition.hypothesisVersion,
            committedLane = HasChoice
                ? CommittedChoice.physicalLane : -1,
            chosenRole = HasChoice
                ? CommittedRole : PredictionGateRole.Neutral,
            strategyKey = HasChoice
                ? CommittedStrategy : StrategyKey.Neutral,
            execution = ExecutionOutcome,
            executionReason = ExecutionReason,
            hasLateralEvidence = HasChoice
                                 && CommittedChoice.hasLateralEvidence,
            lateralOffset = HasChoice ? CommittedChoice.lateralOffset : 0f,
            laneChangeInProgress = HasChoice
                                   && CommittedChoice.laneChangeInProgress,
            predictionMatched = HasChoice
                                && CommittedRole
                                == PredictionGateRole.Predicted,
            reactionTime = ReactionTime,
            leadDeltaSeconds = _hasSettlement
                ? _settlement.signedLeadSeconds : 0f,
            leadDeltaMeters = _hasSettlement
                ? _settlement.signedLeadMeters : 0f
        };
    }

    public GateTransitionResult RemapScheduledPrediction(
        StrategyKey strategy, int hypothesisVersion)
    {
        if (State != PredictionGateLifecycle.Scheduled)
            return GateTransitionResult.Rejected;
        if (_definition.predictedStrategy == strategy
            && _definition.hypothesisVersion == hypothesisVersion)
            return GateTransitionResult.AlreadyApplied;
        if (hypothesisVersion < _definition.hypothesisVersion)
            return GateTransitionResult.Rejected;

        _definition = _definition.RemapPrediction(
            strategy, hypothesisVersion);
        return GateTransitionResult.Applied;
    }
}
