using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class AIRunStateSample
{
    public float time;
    public float distance;
    public float speed;
    public int score;
    public int coins;
    public int lane;
    public bool jumping;
    public bool sliding;
    public float playerLead;
}

[Serializable]
public sealed class AIRunEventSample
{
    public float time;
    public float distance;
    public string type;
    public int action;
    public int lane;
    public float value;
    public float value2;
}

public static class AISingleContractEventType
{
    public const string Begin = "single_contract_begin";
    public const string GateScheduled = "prediction_gate_scheduled";
    public const string GatePresented = "prediction_gate_presented";
    public const string GateCommitted = "prediction_gate_committed";
    public const string GateResolved = "prediction_gate_resolved";
    public const string GateApplied = "prediction_gate_applied";
    public const string EchoRelearned = "echo_relearned";
    public const string IdentityDraftDiscarded = "identity_draft_discarded";
    public const string IdentityPromoted = "identity_promoted";
    public const string IdentityCommitFailed = "identity_commit_failed";
    public const string Result = "single_contract_result";
}

[Serializable]
public sealed class AISingleContractEventSample
{
    public float time;
    public float distance;
    public string type = "";

    public int runSequence;
    public int seed;
    public int generation;
    public int gateId;
    public int sequence;
    public int hypothesisVersion;
    public int predictedLane = -1;
    public int committedLane = -1;
    public PredictionGateRole chosenRole;
    public StrategyKey strategyKey;
    public GateExecutionOutcome execution;
    public GateExecutionReason executionReason;
    public bool hasLateralEvidence;
    public float lateralOffset;
    public bool laneChangeInProgress;
    public float reactionTime;
    public float speedAtResolution;
    public float secondsDelta;
    public float metersDelta;
    public float leadBefore;
    public float leadAfter;
    public bool relearned;

    public string oldIdentityId = "";
    public string newIdentityId = "";
    public string transactionId = "";
    public string commitResult = "";
    public string identityHashBefore = "";
    public string identityHashAfter = "";

    public AISingleContractEventSample Clone()
    {
        return (AISingleContractEventSample)MemberwiseClone();
    }
}

[Serializable]
public sealed class AIDirectorDecisionSample
{
    public int id;
    public float time;
    public float distance;
    public int intent;
    public int proposedIntent;
    public float[] context;
    public float policyMean;
    public float policyUncertainty;
    public bool safetyAdjusted;
    public float difficulty;
    public float obstacleChance;
    public float coinChance;
    public int safeLane;
    public int maxBlockedLanes;
    public bool shouldTurn;
    public int echoEncounter;
    public int echoPredictedLane;
    public int echoSafeChoiceLane;
    public int echoRiskChoiceLane;
    public int echoPredictedAction;
    public int echoTargetAction;
    public float segmentStartDistance;
    public float segmentEndDistance;
    public bool activated;
    public float activationDistance;
    public bool trained;
    public float reward;
    public int modelUpdateCount;
}

[Serializable]
public sealed class AIShadowTrainingSample
{
    public float time;
    public float distance;
    public int action;
    public int lane;
    public bool opponentDecision;
    public float confidence;
    public float[] features;
    public int baseAction;
    public int originalPrediction;
    public float sequenceConfidence;
    public float sequenceInfluence;
    public float[] baseScores;
    public float[] styleAdjustedScores;
    public float[] finalScores;
    public bool[] feasibleActions;
    public bool safetyAdjusted;
    public ShadowAIDirective directive;
    public PlayerStyleData playerStyle;
}

[Serializable]
public sealed class AIObstacleContactSample
{
    public float time;
    public float distance;
    public int source;
    public int obstacleId;
    public int obstacleType;
    public int seed;
    public float speed;
    public int lane;
    public bool jumping;
    public bool sliding;
    public float verticalClearance;
    public int outcome;
    public int reason;
}

[Serializable]
public sealed class AIRunInputSample
{
    public int sequence;
    public int direction;
    public int source;
    public float issuedAt;
    public float resolvedAt = -1f;
    public int outcome;
    public int lane = -1;
}

[Serializable]
public sealed class AIRunCapsule
{
    public int schemaVersion = 1;
    public string runId;
    public int seed;
    public string buildVersion;
    public string platform;
    public string balanceFingerprint;
    public string shadowModelFingerprint;
    public string directorModelFingerprint;
    public List<AIRunInputSample> inputs = new List<AIRunInputSample>();
}

[Serializable]
public sealed class AIRunTelemetryData
{
    public int schemaVersion = AIRunTelemetry.SchemaVersion;
    public string runId;
    public int seed;
    public int runSequence;
    public long startedUtcTicks;
    public long endedUtcTicks;
    public string buildVersion;
    public string platform;
    public string finishReason;
    public bool completed;
    public int highScoreBeforeRun;
    public int shadowGenerationAtStart;
    public int directorUpdatesAtStart;
    public float playerSkillAtStart;
    public float skillConfidenceAtStart;
    public PlayerStyleData playerStyleAtStart;
    public float[] shadowWeightsAtStart;
    public string shadowSequenceStateAtStart;
    public float[] directorWeightsAtStart;
    public string directorPolicyStateAtStart;
    public float duration;
    public float distance;
    public int score;
    public int coins;
    public int shadowGenerationAtEnd;
    public int directorUpdatesAtEnd;
    public float playerSkillAtEnd;
    public float skillConfidenceAtEnd;
    public PlayerStyleData playerStyleAtEnd;
    public float[] shadowWeightsAtEnd;
    public string shadowSequenceStateAtEnd;
    public float[] directorWeightsAtEnd;
    public string directorPolicyStateAtEnd;
    public AIRunCapsule runCapsule;
    public List<AIRunStateSample> states = new List<AIRunStateSample>();
    public List<AIRunEventSample> events = new List<AIRunEventSample>();
    public List<AISingleContractEventSample> singleContractEvents =
        new List<AISingleContractEventSample>();
    public List<AIDirectorDecisionSample> directorDecisions =
        new List<AIDirectorDecisionSample>();
    public List<AIShadowTrainingSample> shadowSamples =
        new List<AIShadowTrainingSample>();
    public List<AIObstacleContactSample> obstacleContacts =
        new List<AIObstacleContactSample>();
}

public static class AIRunTelemetry
{
    public const int SchemaVersion = 9;
    public const float StateSampleInterval = 0.25f;
    public const string CompletedTrainingReason = "finish_reached";

    private const int MaxStateSamples = 7200;
    private const int MaxEventSamples = 4096;
    private const int MaxSingleContractEventSamples = 4096;
    private const int MaxShadowSamples = 8192;
    private const int MaxObstacleContactSamples = 2048;
    private const int MaxInputSamples = 4096;

    private static AIRunTelemetryData _active;
    private static float _nextStateSampleTime;
    private static float _runStartTime;
    private static float _runStartUnscaledTime;
    private static int _nextDecisionId;

    [Serializable]
    private sealed class ModelFingerprintPayload
    {
        public int revision;
        public float[] weights;
        public string state;
    }

    public static AIRunTelemetryData ActiveRun => _active;
    public static bool IsRecording => _active != null && !_active.completed;

    public static void ResetTrainingInMemory()
    {
        _active = null;
        _nextStateSampleTime = 0f;
        _runStartTime = 0f;
        _runStartUnscaledTime = 0f;
        _nextDecisionId = 0;
    }

    public static bool IsCompletedTrainingReason(string reason)
    {
        return string.Equals(reason, CompletedTrainingReason,
            StringComparison.Ordinal);
    }

    public static bool IsCompletedTrainingRun(AIRunTelemetryData data)
    {
        return data != null && data.completed
               && IsCompletedTrainingReason(data.finishReason);
    }

    public static void BeginRun(int seed, int sequence, int highScore,
        int shadowGeneration, int directorUpdates, float[] shadowWeights,
        float[] directorWeights, string directorPolicyState,
        string shadowSequenceState = "")
    {
        long now = DateTime.UtcNow.Ticks;
        string runId = seed.ToString("X8") + "-" + sequence.ToString("D6");
        string buildVersion = Application.version;
        string platform = Application.platform.ToString();
        _active = new AIRunTelemetryData
        {
            runId = runId,
            seed = seed,
            runSequence = Mathf.Max(0, sequence),
            startedUtcTicks = now,
            buildVersion = buildVersion,
            platform = platform,
            highScoreBeforeRun = Mathf.Max(0, highScore),
            shadowGenerationAtStart = Mathf.Max(0, shadowGeneration),
            directorUpdatesAtStart = Mathf.Max(0, directorUpdates),
            playerSkillAtStart = AIPlayerSkillEstimator.Skill,
            skillConfidenceAtStart = AIPlayerSkillEstimator.Confidence,
            playerStyleAtStart = StyleTracker.GetSnapshot(),
            shadowWeightsAtStart = Clone(shadowWeights),
            shadowSequenceStateAtStart = shadowSequenceState ?? "",
            directorWeightsAtStart = Clone(directorWeights),
            directorPolicyStateAtStart = directorPolicyState ?? "",
            runCapsule = new AIRunCapsule
            {
                runId = runId,
                seed = seed,
                buildVersion = buildVersion,
                platform = platform,
                balanceFingerprint = StableHash.ComputeHex(
                    JsonUtility.ToJson(GameBalanceConfig.Current)),
                shadowModelFingerprint = FingerprintModel(
                    shadowGeneration, shadowWeights, shadowSequenceState),
                directorModelFingerprint = FingerprintModel(
                    directorUpdates, directorWeights, directorPolicyState)
            }
        };
        _runStartTime = Time.time;
        _runStartUnscaledTime = Time.unscaledTime;
        _nextStateSampleTime = 0f;
        _nextDecisionId = 1;
        RecordEvent("run_start", 0, 1, seed, sequence);
    }

    public static void Tick(GameManager gameManager, PlayerController player)
    {
        if (!IsRecording || gameManager == null) return;
        float elapsed = ElapsedTime();
        if (elapsed + 0.0001f < _nextStateSampleTime) return;
        _nextStateSampleTime = elapsed + StateSampleInterval;
        if (_active.states.Count >= MaxStateSamples) return;

        AIShadowRunner shadow = AIShadowRunner.Instance;
        _active.states.Add(new AIRunStateSample
        {
            time = elapsed,
            distance = gameManager.Distance,
            speed = gameManager.CurrentSpeed,
            score = gameManager.Score,
            coins = gameManager.Coins,
            lane = player != null ? player.CurrentLane : 1,
            jumping = player != null && player.IsJumping,
            sliding = player != null && player.IsSliding,
            playerLead = shadow != null && shadow.HasActiveOpponent
                ? shadow.PlayerLead
                : 0f
        });
    }

    public static void RecordEvent(string type, int action = 0, int lane = -1,
        float value = 0f, float value2 = 0f)
    {
        if (!IsRecording || _active.events.Count >= MaxEventSamples) return;
        _active.events.Add(new AIRunEventSample
        {
            time = ElapsedTime(),
            distance = CurrentDistance(),
            type = type ?? "",
            action = action,
            lane = lane,
            value = value,
            value2 = value2
        });
    }

    public static void RecordSingleContractEvent(
        AISingleContractEventSample sample)
    {
        if (!IsRecording || sample == null
            || _active.singleContractEvents.Count
            >= MaxSingleContractEventSamples)
            return;

        AISingleContractEventSample recorded = sample.Clone();
        recorded.time = ElapsedTime();
        recorded.distance = CurrentDistance();
        recorded.type = recorded.type ?? "";
        recorded.runSequence = _active.runSequence;
        recorded.seed = _active.seed;
        recorded.generation = Mathf.Max(0, recorded.generation);
        recorded.gateId = Mathf.Max(0, recorded.gateId);
        recorded.sequence = Mathf.Max(0, recorded.sequence);
        recorded.hypothesisVersion = Mathf.Max(0,
            recorded.hypothesisVersion);
        recorded.predictedLane = NormalizeTelemetryLane(
            recorded.predictedLane);
        recorded.committedLane = NormalizeTelemetryLane(
            recorded.committedLane);
        recorded.reactionTime = Mathf.Max(0f, recorded.reactionTime);
        recorded.speedAtResolution = Mathf.Max(0f,
            recorded.speedAtResolution);
        recorded.oldIdentityId = recorded.oldIdentityId ?? "";
        recorded.newIdentityId = recorded.newIdentityId ?? "";
        recorded.transactionId = recorded.transactionId ?? "";
        recorded.commitResult = recorded.commitResult ?? "";
        recorded.identityHashBefore = recorded.identityHashBefore ?? "";
        recorded.identityHashAfter = recorded.identityHashAfter ?? "";
        _active.singleContractEvents.Add(recorded);
    }

    public static void RecordInputQueued(BufferedSwipeCommand command)
    {
        if (!IsRecording || _active.runCapsule == null
            || _active.runCapsule.inputs.Count >= MaxInputSamples)
            return;
        _active.runCapsule.inputs.Add(new AIRunInputSample
        {
            sequence = command.sequence,
            direction = (int)command.direction,
            source = (int)command.source,
            issuedAt = RelativeUnscaledTime(command.issuedAt),
            outcome = (int)InputIntentOutcome.Pending
        });
    }

    public static void RecordInputResolved(BufferedSwipeCommand command,
        InputIntentOutcome outcome, int lane, float resolvedAt)
    {
        if (!IsRecording || _active.runCapsule == null) return;
        List<AIRunInputSample> inputs = _active.runCapsule.inputs;
        for (int i = inputs.Count - 1; i >= 0; i--)
        {
            AIRunInputSample sample = inputs[i];
            if (sample.sequence != command.sequence) continue;
            sample.resolvedAt = RelativeUnscaledTime(resolvedAt);
            sample.outcome = (int)outcome;
            sample.lane = lane;
            return;
        }
    }

    public static int RecordDirectorDecision(float[] context, AITrackPlan plan)
    {
        int proposedAction = plan.intent == AIDirectorIntent.Observe
            ? -1
            : (int)plan.intent - 1;
        return RecordDirectorDecision(
            context, plan, proposedAction, 0f, 0f, false);
    }

    public static int RecordDirectorDecision(float[] context,
        AITrackPlan plan, int proposedAction, float policyMean,
        float policyUncertainty, bool safetyAdjusted,
        float segmentStartDistance = 0f, float segmentEndDistance = 0f)
    {
        if (!IsRecording) return 0;
        int id = _nextDecisionId++;
        _active.directorDecisions.Add(new AIDirectorDecisionSample
        {
            id = id,
            time = ElapsedTime(),
            distance = CurrentDistance(),
            intent = (int)plan.intent,
            proposedIntent = proposedAction >= 0
                ? proposedAction + 1
                : (int)AIDirectorIntent.Observe,
            context = Clone(context),
            policyMean = policyMean,
            policyUncertainty = Mathf.Max(0f, policyUncertainty),
            safetyAdjusted = safetyAdjusted,
            difficulty = plan.difficulty,
            obstacleChance = plan.obstacleChance,
            coinChance = plan.coinChance,
            safeLane = plan.safeLane,
            maxBlockedLanes = plan.maxBlockedLanes,
            shouldTurn = plan.shouldTurn,
            echoEncounter = (int)plan.echoEncounterKind,
            echoPredictedLane = plan.echoPredictedLane,
            echoSafeChoiceLane = plan.echoSafeChoiceLane,
            echoRiskChoiceLane = plan.echoRiskChoiceLane,
            echoPredictedAction = (int)plan.echoPredictedAction,
            echoTargetAction = (int)plan.echoTargetAction,
            segmentStartDistance = Mathf.Max(0f, segmentStartDistance),
            segmentEndDistance = Mathf.Max(segmentStartDistance,
                segmentEndDistance)
        });
        return id;
    }

    public static void RecordDirectorActivation(int decisionId,
        float activationDistance)
    {
        if (!IsRecording || decisionId <= 0) return;
        for (int i = _active.directorDecisions.Count - 1; i >= 0; i--)
        {
            AIDirectorDecisionSample sample = _active.directorDecisions[i];
            if (sample.id != decisionId) continue;
            sample.activated = true;
            sample.activationDistance = Mathf.Max(0f, activationDistance);
            return;
        }
    }

    public static void RecordDirectorOutcome(int decisionId, float reward,
        int modelUpdateCount)
    {
        if (!IsRecording || decisionId <= 0) return;
        for (int i = _active.directorDecisions.Count - 1; i >= 0; i--)
        {
            AIDirectorDecisionSample sample = _active.directorDecisions[i];
            if (sample.id != decisionId) continue;
            sample.trained = true;
            sample.reward = reward;
            sample.modelUpdateCount = modelUpdateCount;
            return;
        }
    }

    public static void RecordShadowSample(ShadowAction action, int lane,
        float[] features, bool opponentDecision, float confidence)
    {
        RecordShadowSample(action, lane, features, opponentDecision, confidence,
            (int)action, 0f, 0f);
    }

    public static void RecordObstacleContact(ObstacleContactDiagnostic contact)
    {
        if (!IsRecording || contact == null
            || _active.obstacleContacts.Count >= MaxObstacleContactSamples)
            return;
        _active.obstacleContacts.Add(new AIObstacleContactSample
        {
            time = ElapsedTime(),
            distance = CurrentDistance(),
            source = (int)contact.source,
            obstacleId = contact.obstacleId,
            obstacleType = (int)contact.type,
            seed = contact.seed,
            speed = contact.speed,
            lane = contact.lane,
            jumping = contact.jumping,
            sliding = contact.sliding,
            verticalClearance = contact.verticalClearance,
            outcome = (int)contact.outcome,
            reason = (int)contact.reason
        });
    }

    public static void RecordShadowSample(ShadowAction action, int lane,
        float[] features, bool opponentDecision, float confidence, int baseAction,
        float sequenceConfidence, float sequenceInfluence,
        ShadowDecisionTrace decisionTrace = null,
        PlayerStyleData playerStyle = null)
    {
        if (!IsRecording || _active.shadowSamples.Count >= MaxShadowSamples) return;
        _active.shadowSamples.Add(new AIShadowTrainingSample
        {
            time = ElapsedTime(),
            distance = CurrentDistance(),
            action = (int)action,
            lane = Mathf.Clamp(lane, 0, 2),
            opponentDecision = opponentDecision,
            confidence = Mathf.Clamp01(confidence),
            features = Clone(features),
            baseAction = Mathf.Clamp(baseAction, 0, AIShadowPolicy.ActionCount - 1),
            originalPrediction = decisionTrace != null
                ? (int)decisionTrace.originalPrediction
                : Mathf.Clamp(baseAction, 0, AIShadowPolicy.ActionCount - 1),
            sequenceConfidence = Mathf.Clamp01(sequenceConfidence),
            sequenceInfluence = Mathf.Clamp01(sequenceInfluence),
            baseScores = decisionTrace != null
                ? Clone(decisionTrace.baseScores) : null,
            styleAdjustedScores = decisionTrace != null
                ? Clone(decisionTrace.styleAdjustedScores) : null,
            finalScores = decisionTrace != null
                ? Clone(decisionTrace.finalScores) : null,
            feasibleActions = decisionTrace != null
                ? Clone(decisionTrace.feasibleActions) : null,
            safetyAdjusted = decisionTrace != null
                             && decisionTrace.safetyAdjusted,
            directive = decisionTrace != null
                ? decisionTrace.directive : ShadowAIDirective.Neutral,
            playerStyle = playerStyle != null ? playerStyle.Clone() : null
        });
    }

    public static string FinishRun(GameManager gameManager, string reason,
        int shadowGeneration, int directorUpdates, float[] shadowWeights,
        float[] directorWeights, string directorPolicyState,
        string shadowSequenceState = "")
    {
        if (!IsRecording) return GetLatestRunJson();

        _active.completed = true;
        _active.endedUtcTicks = DateTime.UtcNow.Ticks;
        _active.finishReason = reason ?? "";
        _active.duration = ElapsedTime();
        if (gameManager != null)
        {
            _active.distance = gameManager.Distance;
            _active.score = gameManager.Score;
            _active.coins = gameManager.Coins;
        }
        _active.shadowGenerationAtEnd = Mathf.Max(0, shadowGeneration);
        _active.directorUpdatesAtEnd = Mathf.Max(0, directorUpdates);
        _active.playerSkillAtEnd = AIPlayerSkillEstimator.Skill;
        _active.skillConfidenceAtEnd = AIPlayerSkillEstimator.Confidence;
        _active.playerStyleAtEnd = StyleTracker.GetSnapshot();
        _active.shadowWeightsAtEnd = Clone(shadowWeights);
        _active.shadowSequenceStateAtEnd = shadowSequenceState ?? "";
        _active.directorWeightsAtEnd = Clone(directorWeights);
        _active.directorPolicyStateAtEnd =
            directorPolicyState ?? "";

        string json = JsonUtility.ToJson(_active);
        EchoRunSaveSystem.SaveLastRunTelemetry(json);
        return json;
    }

    public static string GetLatestRunJson()
    {
        if (_active != null) return JsonUtility.ToJson(_active);
        return EchoRunSaveSystem.GetLastRunTelemetryJson();
    }

    public static AIRunTelemetryData FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        AIRunTelemetryData data;
        try
        {
            data = JsonUtility.FromJson<AIRunTelemetryData>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "AI run telemetry could not be loaded: " + exception.Message);
            return null;
        }
        if (data == null || data.schemaVersion <= 0) return null;
        data.states = data.states ?? new List<AIRunStateSample>();
        data.events = data.events ?? new List<AIRunEventSample>();
        data.singleContractEvents = data.singleContractEvents
                                    ?? new List<AISingleContractEventSample>();
        data.directorDecisions = data.directorDecisions
                                 ?? new List<AIDirectorDecisionSample>();
        data.shadowSamples = data.shadowSamples ?? new List<AIShadowTrainingSample>();
        data.obstacleContacts = data.obstacleContacts
                                ?? new List<AIObstacleContactSample>();
        data.runCapsule = data.runCapsule ?? new AIRunCapsule
        {
            runId = data.runId ?? "",
            seed = data.seed,
            buildVersion = data.buildVersion ?? "",
            platform = data.platform ?? ""
        };
        data.runCapsule.inputs = data.runCapsule.inputs
                                 ?? new List<AIRunInputSample>();
        return data;
    }

    public static string ExportLatestRun(string directory = null)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return "";
#else
        string json = GetLatestRunJson();
        if (string.IsNullOrEmpty(json)) return "";
        AIRunTelemetryData data = FromJson(json);
        string runId = data != null && !string.IsNullOrEmpty(data.runId)
            ? data.runId
            : "latest";
        string targetDirectory = string.IsNullOrEmpty(directory)
            ? Path.Combine(Application.persistentDataPath, "TrainingData")
            : directory;
        Directory.CreateDirectory(targetDirectory);
        string path = Path.Combine(targetDirectory,
            "echo-run-" + runId + ".json");
        File.WriteAllText(path, json);
        return path;
#endif
    }

    private static float CurrentDistance()
    {
        return GameManager.Instance != null ? GameManager.Instance.Distance : 0f;
    }

    private static float ElapsedTime()
    {
        return Mathf.Max(0f, Time.time - _runStartTime);
    }

    private static float RelativeUnscaledTime(float timestamp)
    {
        return Mathf.Max(0f, timestamp - _runStartUnscaledTime);
    }

    private static int NormalizeTelemetryLane(int lane)
    {
        return lane >= 0 && lane <= 2 ? lane : -1;
    }

    private static string FingerprintModel(int revision, float[] weights,
        string state)
    {
        var payload = new ModelFingerprintPayload
        {
            revision = Mathf.Max(0, revision),
            weights = Clone(weights),
            state = state ?? ""
        };
        return StableHash.ComputeHex(JsonUtility.ToJson(payload));
    }

    private static float[] Clone(float[] values)
    {
        return values == null ? null : (float[])values.Clone();
    }

    private static bool[] Clone(bool[] values)
    {
        return values == null ? null : (bool[])values.Clone();
    }
}
