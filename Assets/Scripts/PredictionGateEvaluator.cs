using System;
using UnityEngine;

public readonly struct PredictionGateSettlement
{
    public readonly int gateId;
    public readonly PredictionGateRole chosenRole;
    public readonly GateExecutionOutcome execution;
    public readonly GateExecutionReason executionReason;
    public readonly float playerLeadSeconds;
    public readonly float echoLeadSeconds;
    public readonly float signedLeadSeconds;
    public readonly float playerLeadMeters;
    public readonly float echoLeadMeters;
    public readonly float signedLeadMeters;
    public readonly float speedAtResolution;
    public readonly float memoryConfidence;
    public readonly float effectScale;

    public PredictionGateSettlement(int gateId,
        PredictionGateRole chosenRole, GateExecutionOutcome execution,
        float playerLeadSeconds, float echoLeadSeconds,
        float speedAtResolution, float memoryConfidence, float effectScale,
        GateExecutionReason executionReason = GateExecutionReason.None)
    {
        this.gateId = gateId;
        this.chosenRole = chosenRole;
        this.execution = execution;
        this.executionReason = executionReason;
        this.playerLeadSeconds = playerLeadSeconds;
        this.echoLeadSeconds = echoLeadSeconds;
        signedLeadSeconds = playerLeadSeconds - echoLeadSeconds;
        this.speedAtResolution = speedAtResolution;
        this.memoryConfidence = memoryConfidence;
        this.effectScale = effectScale;
        playerLeadMeters = playerLeadSeconds * speedAtResolution * effectScale;
        echoLeadMeters = echoLeadSeconds * speedAtResolution * effectScale;
        signedLeadMeters = signedLeadSeconds * speedAtResolution * effectScale;
    }

    public bool IsCounterSuccess => chosenRole == PredictionGateRole.Counter
                                    && execution
                                    == GateExecutionOutcome.Success;
}

public static class PredictionGateEvaluator
{
    public const float PredictedSuccessEchoSeconds = 0.225f;
    public const float CounterSuccessPlayerSeconds = 0.375f;
    public const float HitEchoSeconds = 0.45f;
    public const float MinimumConfidenceScale = 0.4f;

    public static PredictionGateSettlement Evaluate(int gateId,
        PredictionGateRole chosenRole, GateExecutionOutcome execution,
        float speedAtResolution, float memoryConfidence,
        GateExecutionReason executionReason = GateExecutionReason.None)
    {
        if (!Enum.IsDefined(typeof(PredictionGateRole), chosenRole))
            throw new ArgumentOutOfRangeException(nameof(chosenRole));
        if (execution != GateExecutionOutcome.Success
            && execution != GateExecutionOutcome.Hit
            && execution != GateExecutionOutcome.Cancelled)
            throw new ArgumentOutOfRangeException(nameof(execution));

        executionReason = ResolveReason(execution, executionReason);

        float playerSeconds = 0f;
        float echoSeconds = 0f;
        if (execution == GateExecutionOutcome.Hit)
        {
            echoSeconds = HitEchoSeconds;
        }
        else if (execution == GateExecutionOutcome.Success)
        {
            if (chosenRole == PredictionGateRole.Predicted)
                echoSeconds = PredictedSuccessEchoSeconds;
            else if (chosenRole == PredictionGateRole.Counter)
                playerSeconds = CounterSuccessPlayerSeconds;
        }

        float speed = IsFinite(speedAtResolution)
            ? Mathf.Max(0f, speedAtResolution) : 0f;
        float confidence = IsFinite(memoryConfidence)
            ? Mathf.Clamp01(memoryConfidence) : 0f;
        float scale = Mathf.Lerp(
            MinimumConfidenceScale, 1f, confidence);
        return new PredictionGateSettlement(
            Mathf.Max(0, gateId), chosenRole, execution,
            playerSeconds, echoSeconds, speed, confidence, scale,
            executionReason);
    }

    private static GateExecutionReason ResolveReason(
        GateExecutionOutcome execution, GateExecutionReason reason)
    {
        if (execution == GateExecutionOutcome.Success)
            return GateExecutionReason.Completed;
        if (execution == GateExecutionOutcome.Cancelled)
            return GateExecutionReason.Cancelled;

        // A legacy Hit carries a scoring result, not proof of a collision.
        if (reason == GateExecutionReason.None)
            return GateExecutionReason.Unresolved;
        if (reason == GateExecutionReason.Collision
            || reason == GateExecutionReason.RouteAbandoned
            || reason == GateExecutionReason.Unresolved)
            return reason;
        throw new ArgumentOutOfRangeException(nameof(reason));
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
