using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class AIShadowRunner : MonoBehaviour
{
    public static AIShadowRunner Instance { get; private set; }

    [Header("Behavior Cloning")]
    [Range(0.001f, 0.5f)] public float learningRate = 0.08f;
    public int minimumTrainingSamples = 24;
    public int minimumActiveTrainingSamples = 6;
    public int minimumActionCategories = 2;
    public int minimumJumpSamples = 2;
    public int minimumSlideSamples = 2;
    public float decisionInterval = 0.35f;
    public float keepSampleInterval = 0.7f;
    public float minimumLaneHoldTime = 0.65f;

    [Header("Visual Smoothing")]
    public float laneSmoothTime = 0.14f;
    public float distanceSmoothTime = 0.12f;

    [Header("Duel")]
    public float shadowPaceMultiplier = 1.02f;
    public float maximumVisibleLead = 16f;

    [Header("Diagnostics")]
    public bool enableEmergencyReflex = true;

    public string CurrentStatus { get; private set; } = "AI影子 · 等待校准";
    public string LastResult { get; private set; } = "";
    public float PlayerLead { get; private set; }
    public bool HasActiveOpponent { get; private set; }
    public bool LastRunWasChallenge { get; private set; }
    public bool LastRunWon { get; private set; }
    public bool LastSingleContractCommitSucceeded { get; private set; }
    public bool LastSingleContractIdentityPromoted { get; private set; }
    public bool LastRunWasTransientValidation { get; private set; }
    public event Action<PredictionGateSettlement>
        PredictionGateSettlementConsumed;
    public GameplayFlowMode ActiveGameplayFlowMode => _activeGameplayFlowMode;
    public int Generation => IsSingleContractRuntime()
        ? _activeSingleContractIdentity != null
            ? _activeSingleContractIdentity.generation : 0
        : _activeGeneration != null
            ? _activeGeneration.generation
            : _profile != null ? _profile.generation : 0;
    public int TrainingSampleCount => IsSingleContractRuntime()
        ? _runIdentityDraft != null ? _runIdentityDraft.sampleCount : 0
        : _profile != null ? _profile.sampleCount : 0;
    public int ActiveTrainingSampleCount =>
        IsSingleContractRuntime()
            ? _runIdentityDraft != null
                ? _runIdentityDraft.activeSampleCount : 0
            : _profile != null ? _profile.activeSampleCount : 0;
    public int JumpTrainingSampleCount => IsSingleContractRuntime()
        ? GetDraftActionSampleCount(ShadowAction.Jump)
        : GetActionSampleCount(ShadowAction.Jump);
    public int SlideTrainingSampleCount => IsSingleContractRuntime()
        ? GetDraftActionSampleCount(ShadowAction.Slide)
        : GetActionSampleCount(ShadowAction.Slide);
    public SingleContractCalibrationProgress
        CurrentSingleContractCalibrationProgress
    {
        get
        {
            if (!IsSingleContractRuntime() || HasActiveOpponent)
                return default;
            return _runIdentityDraft != null
                ? _runIdentityDraft.BuildCalibrationProgress(
                    minimumTrainingSamples,
                    minimumActiveTrainingSamples,
                    minimumActionCategories,
                    minimumJumpSamples,
                    minimumSlideSamples)
                : LastSingleContractCalibrationProgress;
        }
    }
    public SingleContractCalibrationProgress
        LastSingleContractCalibrationProgress { get; private set; }
    public float CalibrationProgress
    {
        get
        {
            if (HasActiveOpponent) return 0f;
            if (IsSingleContractRuntime())
            {
                return _runIdentityDraft == null
                    ? 0f
                    : CalculateCalibrationProgress(
                        _runIdentityDraft.sampleCount,
                        _runIdentityDraft.activeSampleCount,
                        _runIdentityDraft.actionCounts,
                        minimumTrainingSamples,
                        minimumActiveTrainingSamples,
                        minimumActionCategories,
                        minimumJumpSamples, minimumSlideSamples);
            }
            return _profile == null
                ? 0f
                : CalculateCalibrationProgress(
                    _profile.sampleCount, _profile.activeSampleCount,
                    _profile.actionCounts, minimumTrainingSamples,
                    minimumActiveTrainingSamples, minimumActionCategories,
                    minimumJumpSamples, minimumSlideSamples);
        }
    }
    public float EchoClarity => IsSingleContractRuntime()
        ? _activeSingleContractIdentity != null
            ? Mathf.Clamp01(_activeSingleContractIdentity.clarity) : 0f
        : _activeGeneration != null
            ? Mathf.Clamp01(_activeGeneration.clarity)
            : _profile != null ? Mathf.Clamp01(_profile.clarity) : 0f;
    public EchoDuelPhase DuelPhase => _duelFlow != null
        ? _duelFlow.Phase
        : HasActiveOpponent ? EchoDuelPhase.Detection : EchoDuelPhase.Calibration;
    public float DuelPhaseProgress => _duelFlow != null
        ? _duelFlow.PhaseProgress01 : 0f;
    public bool DuelTransitionPending => _duelFlow != null
                                         && _duelFlow.TransitionPending;
    public EchoDuelPhase PendingDuelPhase => _duelFlow != null
        ? _duelFlow.PendingPhase : EchoDuelPhase.None;
    public float PendingDuelBoundary => _pendingDuelBoundary;
    public float RewriteWriteStrength => _liveRewriteSnapshot != null
        ? Mathf.Clamp(_liveRewriteSnapshot.writeStrength, 1f, 2f) : 1f;
    public string RewriteStyleSummary => _liveRewriteSnapshot != null
        ? _liveRewriteSnapshot.BuildHudSummary() : "";
    public EchoRewriteSnapshot RewritePreview => _liveRewriteSnapshot != null
        ? _liveRewriteSnapshot.Clone() : null;
    public string FinaleSegmentSummary
    {
        get
        {
            AITrackDirector director = AITrackDirector.Instance;
            return director != null
                ? FinaleLabelFor(director.CurrentPlan.echoEncounterKind) : "";
        }
    }
    public string PublicPrediction => _contractEvaluator != null
        ? _contractEvaluator.BuildPredictionText() : "";
    public EchoContractData ActiveContract =>
        _contractEvaluator != null ? _contractEvaluator.Contract : null;
    public EchoChallengeStep ActiveChallengeStep => _contractEvaluator != null
        ? _contractEvaluator.ActiveChallengeStep : default;
    public EchoEncounterResult LastEncounterResult => _contractEvaluator != null
        ? _contractEvaluator.LastEncounterResult : default;
    public string EncounterDebug => _contractEvaluator != null
        ? _contractEvaluator.Contract.encounterDebug : "";
    public EchoContractData ContractPreview => _activeGeneration != null
        && _activeGeneration.generation > 0
        ? EchoContractPolicy.CreateForRun(_activeGeneration.GetStyle(),
            _activeGeneration.generation,
            EchoRunSaveSystem.GetLastEchoContractJson())
        : null;
    public float DuelPressure => HasActiveOpponent
        ? 1f - Mathf.Clamp01(Mathf.Abs(PlayerLead) / 14f)
        : 0f;
    public ShadowDecisionTrace LastDecisionTrace { get; private set; }
    public int PolicyCorrectDecisionCount { get; private set; }
    public int SafetyOverrideDecisionCount { get; private set; }
    public int EmergencyReflexSaveCount { get; private set; }
    public bool EmergencyReflexEnabled => enableEmergencyReflex;
    public SingleContractFlow SingleContractRuntime => _singleContractFlow;
    public SingleContractInstantFeedback SingleContractFeedback =>
        _singleContractFeedback;
    public int SingleContractFeedbackSequence =>
        _singleContractFeedbackSequence;
    public float SingleContractFeedbackLeadDeltaMeters =>
        _singleContractFeedbackLeadDeltaMeters;
    public bool SingleContractFeedbackRelearned =>
        _singleContractFeedbackRelearned;
    public bool IsSingleContractOpeningMemory =>
        IsSingleContractRuntime() && HasActiveOpponent
        && _singleContractFlow != null
        && _singleContractFlow.IsOpeningMemoryActive;
    public bool HasSingleContractOpeningReplay =>
        _singleContractOpeningReplay != null
        && _singleContractOpeningReplay.available;
    public bool IsSingleContractOpeningReplayActive =>
        HasSingleContractOpeningReplay
        && !_singleContractOpeningReplayCompleted
        && IsSingleContractOpeningMemory;
    public ShadowAction SingleContractOpeningReplayAction =>
        HasSingleContractOpeningReplay
            ? _singleContractOpeningReplay.action : ShadowAction.Keep;
    public int SingleContractOpeningReplayCount =>
        HasSingleContractOpeningReplay
            ? _singleContractOpeningReplay.count : 0;
    public SingleContractVisualState SingleContractVisualState
    {
        get
        {
            if (!IsSingleContractRuntime() || !HasActiveOpponent)
                return SingleContractVisualState.Calibration;
            if (_singleContractRelearnPulseTimer > 0f)
                return SingleContractVisualState.RelearnPulse;
            return _singleContractFlow != null
                   && _singleContractFlow.IsFinaleActive
                ? SingleContractVisualState.Finale
                : SingleContractVisualState.Challenge;
        }
    }
    public string SingleContractMemoryText
    {
        get
        {
            ActiveEchoIdentity identity = _frozenSingleContractIdentity
                                          ?? _activeSingleContractIdentity;
            if (identity == null)
                return "你的选择尚未形成稳定模式";
            return identity.memoryContract != null
                ? identity.memoryContract.BuildMemoryText()
                : "旧回声已保留 · 正在重建路线记忆";
        }
    }
    public bool ShowSingleContractPrediction =>
        HasActiveOpponent
        && (_frozenSingleContractIdentity?.memoryContract
            ?.HasPreciseRouteMemory ?? false)
        && TryGetSingleContractPredictionDisplay(
            out _, out _, out _);
    public int CurrentSingleContractPredictedLane =>
        TryGetSingleContractPredictionDisplay(
            out int lane, out _, out _) ? lane : -1;
    public int CurrentSingleContractPredictionGateNumber =>
        TryGetSingleContractPredictionDisplay(
            out _, out int gateNumber, out _) ? gateNumber : 0;
    public int SingleContractPredictionGateCount =>
        _singleContractFlow != null ? _singleContractFlow.GateCount : 0;
    public bool IsCurrentSingleContractPredictionGateActive =>
        TryGetSingleContractPredictionDisplay(
            out _, out _, out bool active) && active;
    public ActiveEchoIdentity ActiveSingleContractIdentityPreview =>
        _activeSingleContractIdentity != null
            ? _activeSingleContractIdentity.Clone() : null;

    private const int SamplesPerCheckpoint = 4;
    public const float SingleContractOpeningReplayRevealSeconds = 0.18f;
    public const float SingleContractOpeningReplayActionSeconds = 0.58f;
    public const float SingleContractOpeningReplayReturnSeconds = 1.45f;
    public const float SingleContractOpeningReplaySettleSeconds = 2.25f;
    private const float SingleContractOpeningReplayGapMeters = 3.2f;

    [Serializable]
    private sealed class ShadowProfile
    {
        public int version;
        public int generation;
        public int sampleCount;
        public int activeSampleCount;
        public int[] actionCounts = new int[5];
        public float pace;
        public float bestProgress;
        public float[] weights;
        public float[] sequenceTransitions;
        public int sequencePairCount;
        public float clarity;
        public string activeGenerationJson;
    }

    private ShadowProfile _profile;
    private EchoGenerationSnapshot _activeGeneration;
    private GameplayFlowMode _activeGameplayFlowMode =
        GameplayFlowMode.SixPhaseLegacy;
    private ActiveEchoIdentity _activeSingleContractIdentity;
    private ActiveEchoIdentity _frozenSingleContractIdentity;
    private RunIdentityDraft _runIdentityDraft;
    private RunAdaptationState _runAdaptationState;
    private SingleContractFlow _singleContractFlow;
    private AIShadowPolicy _policy;
    private AIShadowPolicy _opponentPolicy;
    private AIShadowSequencePolicy _sequencePolicy;
    private AIShadowSequencePolicy _opponentSequencePolicy;
    private readonly ShadowDecisionMaker _decisionMaker =
        new ShadowDecisionMaker();
    private PlayerStyleData _opponentStyle;
    private EchoContractEvaluator _contractEvaluator;
    private EchoDuelFlow _duelFlow;
    private EchoRewriteTracker _rewriteTracker;
    private EchoRewriteSnapshot _liveRewriteSnapshot;
    private EchoRewriteSnapshot _frozenRewriteSnapshot;
    private IShadowDirectiveSource _directiveSource;
    private System.Random _decisionRandom = new System.Random(1337);
    private GameManager _gameManager;
    private PlayerController _player;
    private GameObject _ghost;
    private Transform _ghostVisual;
    private Vector3 _ghostVisualPosition;
    private CharacterAnimator _ghostAnimator;
    private Vector3 _ghostForward = Vector3.forward;
    private Material _ghostMaterial;
    private int _ghostLane = 1;
    private float _displayedGhostLane = 1f;
    private float _displayedGap;
    private float _ghostGroundY;
    private float _ghostRootToLowestPoint;
    private float _laneSmoothVelocity;
    private float _gapSmoothVelocity;
    private float _laneDecisionCooldown;
    private float _ghostProgress;
    private float _opponentPace;
    private float _playerPhysicalProgress;
    private float _playerProgress;
    private float _appliedContractPlayerBonus;
    private float _appliedContractShadowBonus;
    private float _runTime;
    private float _singleContractRelearnPulseTimer;
    private float _pendingDuelBoundary = -1f;
    private float _decisionTimer;
    private float _keepSampleTimer;
    private float _ghostJumpTimer;
    private float _ghostSlideTimer;
    private float _ghostStumbleTimer;
    private float _ghostRecoveryTimer;
    private EchoSignatureActionResult _singleContractOpeningReplay =
        EchoSignatureActionResult.Unavailable;
    private bool _singleContractOpeningReplayRevealed;
    private bool _singleContractOpeningReplayActionStarted;
    private bool _singleContractOpeningReplayActionFinished;
    private bool _singleContractOpeningReplayCompleted;
    private bool _singleContractOpeningReplayReducedMotion;
    private readonly List<Renderer> _singleContractOpeningReplayHiddenRenderers =
        new List<Renderer>();
    private float _decisionConfidence;
    private float _sequenceInfluence;
    private int _runCoins;
    private int _runDodges;
    private int _ghostMistakes;
    private int _samplesSinceCheckpoint;
    private int _lastTrainingAction = -1;
    private int _lastOpponentAction = -1;
    private ShadowAction _lastStyleDecision = ShadowAction.Keep;
    private bool _runStarted;
    private bool _runFinalized;
    private bool _runUsedTurboStart;
    private bool _usesTransientValidationIdentity;
    private string _persistentIdentityJsonBeforeValidation = "";
    private SingleContractInstantFeedback _singleContractFeedback;
    private float _singleContractFeedbackLeadDeltaMeters;
    private bool _singleContractFeedbackRelearned;
    private GateAttempt _lastSingleContractGateAttempt;
    private int _singleContractFeedbackSequence;
    private int _nextSingleContractSettlementIndex;
    private readonly HashSet<int> _singleContractPresentedTelemetry =
        new HashSet<int>();
    private readonly HashSet<int> _singleContractCommittedTelemetry =
        new HashSet<int>();
    private readonly HashSet<int> _singleContractResolvedTelemetry =
        new HashSet<int>();
    private readonly HashSet<int> _singleContractAppliedTelemetry =
        new HashSet<int>();
    private readonly HashSet<int> _handledGhostObstacles = new HashSet<int>();
    private readonly HashSet<int> _reactedGhostObstacles = new HashSet<int>();
    private readonly HashSet<int> _recordedPlayerDodgeIds = new HashSet<int>();
    private readonly SlideOpportunityTracker _slideOpportunityTracker =
        new SlideOpportunityTracker();
    private readonly ObstacleOpportunityTracker _jumpOpportunityTracker =
        new ObstacleOpportunityTracker(ObstacleType.High);

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        AIBalance balance = GameBalanceConfig.Current.ai;
        learningRate = balance.shadowLearningRate;
        minimumTrainingSamples = balance.minimumTrainingSamples;
        minimumActiveTrainingSamples = balance.minimumActiveSamples;
        minimumJumpSamples = balance.minimumJumpSamples;
        minimumSlideSamples = balance.minimumSlideSamples;
        LoadProfile();
    }

    void Start()
    {
        _gameManager = GameManager.Instance;
        _player = FindObjectOfType<PlayerController>();
        _directiveSource = AITrackDirector.Instance;
        if (_gameManager != null)
            _gameManager.OnStateChanged.AddListener(OnGameStateChanged);
    }

    void Update()
    {
        if (_gameManager == null) _gameManager = GameManager.Instance;
        if (_player == null) _player = FindObjectOfType<PlayerController>();
        if (_gameManager == null || _gameManager.State != GameState.Playing
            || _gameManager.IsDeathSequence || _player == null)
            return;

        if (!_runStarted) BeginRun();
        if (HasActiveOpponent && _ghost == null) CreateGhost();

        TrackPlayerObstacleOpportunities();

        _runTime += Time.deltaTime;
        _runIdentityDraft?.TickStyle(Time.deltaTime);
        bool singleContractRun = IsSingleContractRuntime();
        if (singleContractRun && _singleContractFlow != null)
        {
            _singleContractRelearnPulseTimer = Mathf.Max(0f,
                _singleContractRelearnPulseTimer - Time.deltaTime);
            _singleContractFlow.Tick(new EchoRunFrame
            {
                deltaTime = Time.deltaTime,
                elapsedTime = _gameManager.RunElapsed,
                currentSpeed = _gameManager.CurrentSpeed,
                playerDistance = _gameManager.Distance,
                remainingDistance = _gameManager.RemainingDistance,
                playerLane = _player.CurrentLane,
                hasLateralEvidence = true,
                lateralOffset = _player.LateralOffset,
                laneChangeInProgress = Mathf.Abs(_player.LateralOffset
                    - (_player.CurrentLane - 1) * _player.laneDistance) > 0.05f
            });
            CaptureSingleContractGateTelemetry();
            ConsumeSingleContractSettlements();
        }
        else if (HasActiveOpponent && _contractEvaluator != null)
        {
            CommitPendingDuelTransitionIfReady();
            float remainingSeconds = EchoTimeRules.EstimateRemainingSeconds(
                _gameManager.RemainingDistance, _gameManager.CurrentSpeed);
            TrackManager track = TrackManager.Instance;
            float phaseGateLeadSeconds = track != null
                ? CalculatePhaseGateLeadSeconds(
                    _gameManager.Distance,
                    track.ContentPreparedRouteDistance,
                    _gameManager.CurrentSpeed)
                : 0f;
            if (_duelFlow != null && _duelFlow.Tick(Time.deltaTime,
                    remainingSeconds, _contractEvaluator.Contract,
                    phaseGateLeadSeconds))
            {
                BeginPendingDuelTransition(remainingSeconds);
            }
            _contractEvaluator.TickLane(_player.CurrentLane, Time.deltaTime,
                _gameManager.CurrentSpeed);
            ApplyContractMotionDelta();
        }
        _playerPhysicalProgress = _gameManager.Distance;
        _playerProgress = _playerPhysicalProgress
                          + (singleContractRun
                              ? _appliedContractPlayerBonus : 0f);

        _keepSampleTimer += Time.deltaTime;
        if (_keepSampleTimer >= keepSampleInterval)
        {
            _keepSampleTimer = 0f;
            float[] keepContext = BuildFeatures(_player.CurrentLane, false);
            // Do not teach "keep running" while a nearby obstacle is asking for input.
            if (keepContext[3] < 0.35f)
                Learn(ShadowAction.Keep, keepContext, false);
        }

        if (!HasActiveOpponent)
        {
            int resolvedGates = _runAdaptationState != null
                ? _runAdaptationState.resolvedGateCount : 0;
            int gateCount = _singleContractFlow != null
                ? _singleContractFlow.GateCount : 5;
            CurrentStatus = singleContractRun
                ? "AI影子 · 单契约校准 " + resolvedGates + "/" + gateCount
                : CurrentStatus;
            return;
        }

        _laneDecisionCooldown = Mathf.Max(0f, _laneDecisionCooldown - Time.deltaTime);
        _ghostStumbleTimer = Mathf.Max(0f, _ghostStumbleTimer - Time.deltaTime);
        _ghostRecoveryTimer = Mathf.Max(0f, _ghostRecoveryTimer - Time.deltaTime);
        _ghostJumpTimer = Mathf.Max(0f, _ghostJumpTimer - Time.deltaTime);
        _ghostSlideTimer = Mathf.Max(0f, _ghostSlideTimer - Time.deltaTime);
        float stumbleSpeed = _ghostStumbleTimer > 0f ? 0.25f : 1f;
        float ghostSpeed = singleContractRun
            ? CalculateSingleContractGhostSpeed(
                _gameManager.startSpeed, _gameManager.maxSpeed,
                _gameManager.speedIncreaseRate, _runTime,
                _opponentPace)
            : Mathf.Max(1f, _opponentPace) * shadowPaceMultiplier;
        _ghostProgress += ghostSpeed * stumbleSpeed * Time.deltaTime;
        PlayerLead = CalculatePhysicalLead(_playerProgress,
            _ghostProgress + (singleContractRun
                ? _appliedContractShadowBonus : 0f));

        bool openingReplayActive = TickSingleContractOpeningReplay();
        if (!openingReplayActive)
        {
            _decisionTimer += Time.deltaTime;
            if (_decisionTimer >= decisionInterval)
            {
                _decisionTimer = 0f;
                ApplyShadowDecision();
            }

            ApplyObstacleReaction();
        }

        CurrentStatus = singleContractRun
            ? BuildSingleContractStatus() : BuildDuelStatus();
    }

    void LateUpdate()
    {
        if (!HasActiveOpponent || _gameManager == null
            || _gameManager.State != GameState.Playing
            || _gameManager.IsDeathSequence || _player == null)
            return;

        UpdateGhostPose();
        if (!IsSingleContractOpeningReplayActive)
            EvaluateGhostObstacle();
        CurrentStatus = IsSingleContractRuntime()
            ? BuildSingleContractStatus() : BuildDuelStatus();
    }

    public string GetMenuStatus()
    {
        if (!HasTrainedProfile())
            return "首局校准：AI 将学习你的路线、动作与节奏";

        PlayerStyleData style = _activeGeneration != null
            ? _activeGeneration.GetStyle()
            : StyleTracker.GetSnapshot();
        EchoContractData preview = EchoContractPolicy.Create(
            style, Generation);
        return "第 " + Generation + " 代回声已生成\n"
               + "AI画像：" + EchoContractPolicy.BuildStyleSummary(style) + "\n"
               + preview.title + " · " + preview.learnedTrait + "\n"
               + "规则：" + preview.ruleDescription + "\n目标：" + preview.objective;
    }

    public string GetContractHudText()
    {
        if (_contractEvaluator != null)
            return _contractEvaluator.BuildHudText();
        return HasActiveOpponent
            ? "回声契约正在生成"
            : "校准目标：跳跃 " + minimumJumpSamples
              + " 次、滑铲 " + minimumSlideSamples + " 次";
    }

    public void SetEmergencyReflexEnabled(bool enabled)
    {
        enableEmergencyReflex = enabled;
    }

    private int GetActionSampleCount(ShadowAction action)
    {
        if (_profile == null || _profile.actionCounts == null) return 0;
        int index = (int)action;
        return index >= 0 && index < _profile.actionCounts.Length
            ? _profile.actionCounts[index]
            : 0;
    }

    public void RecordPlayerAction(ShadowAction action, int laneBeforeAction)
    {
        if (_gameManager == null || _gameManager.State != GameState.Playing) return;
        if (!_runStarted) BeginRun();
        AIRunTelemetry.RecordEvent(
            "player_action", (int)action, laneBeforeAction);
        float[] features = BuildFeatures(laneBeforeAction, false);
        float timingOffset = 0f;
        float styleProximity = features[3];
        bool matchedActionObstacle = false;
        if ((action == ShadowAction.Jump || action == ShadowAction.Slide)
            && _gameManager != null
            && _player != null && TrackManager.Instance != null
            && TrackManager.Instance.TryGetUpcomingObstacleInLane(
                _player.transform.position, _player.ForwardDirection,
                laneBeforeAction, null, out float actionObstacleDistance,
                out ObstacleType actionObstacleType, out _))
        {
            ObstacleType expectedType = action == ShadowAction.Jump
                ? ObstacleType.High : ObstacleType.Low;
            if (actionObstacleType == expectedType)
            {
                matchedActionObstacle = true;
                float duration = action == ShadowAction.Jump
                    ? _player.jumpDuration : _player.slideDuration;
                float idealDistance = CalculateReactionDistance(
                    _gameManager.CurrentSpeed, duration);
                float normalizedTiming = CalculateActionTimingOffset(
                    actionObstacleDistance, idealDistance);
                styleProximity = (normalizedTiming + 1f) * 0.5f;
                if (action == ShadowAction.Jump)
                    timingOffset = normalizedTiming;
            }
            else styleProximity = 0f;
        }
        else if (action == ShadowAction.Jump || action == ShadowAction.Slide)
            styleProximity = 0f;
        if (action == ShadowAction.Jump)
            _jumpOpportunityTracker.MarkAction(laneBeforeAction);
        else if (action == ShadowAction.Slide)
            _slideOpportunityTracker.MarkSlide(laneBeforeAction);
        TryRecordCounterattackActionResponse(action, laneBeforeAction);
        bool airLaneChange = _player != null && _player.IsJumping
                             && (action == ShadowAction.Left
                                 || action == ShadowAction.Right);
        StyleTracker.RecordAction(action, styleProximity, timingOffset,
            airLaneChange, matchedActionObstacle);
        if (_rewriteTracker != null)
        {
            _rewriteTracker.RecordVerticalAction(
                action, matchedActionObstacle, _runTime,
                RewriteSampleWeightForPhase(DuelPhase));
            RefreshRewriteSnapshot();
        }
        if (matchedActionObstacle)
        {
            float[] skillFeatures = (float[])features.Clone();
            skillFeatures[3] = styleProximity;
            AIPlayerSkillEstimator.RecordAction(action, skillFeatures);
        }
        LearnWithStyle(action, features, matchedActionObstacle, styleProximity,
            timingOffset, airLaneChange, matchedActionObstacle);
        _keepSampleTimer = 0f;
    }

    public void RecordCoin(bool isEchoContractMarker = false,
        int challengeStepId = 0)
    {
        _runCoins++;
        float routeDistance = _gameManager != null
            ? _gameManager.Distance : _playerPhysicalProgress;
        if (_rewriteTracker != null && _player != null)
        {
            _rewriteTracker.RecordRouteChoice(
                _player.CurrentLane, routeDistance,
                RewriteSampleWeightForPhase(DuelPhase));
            RefreshRewriteSnapshot();
        }
        if (HasActiveOpponent && _contractEvaluator != null && _player != null)
        {
            float speed = _gameManager != null
                ? _gameManager.CurrentSpeed : 10f;
            EchoContractData contract = _contractEvaluator.Contract;
            bool completedBeforeChoice = contract.completed;
            if (ShouldCountContractMarker(
                    contract.type, contract.duelPhase,
                    isEchoContractMarker))
            {
                _contractEvaluator.RecordLaneMarker(
                    _player.CurrentLane, routeDistance, speed,
                    challengeStepId);
            }
            AITrackDirector director = AITrackDirector.Instance;
            AITrackPlan plan = director != null
                ? director.CurrentPlan : default;
            if (contract.duelPhase == EchoDuelPhase.Finale
                && completedBeforeChoice
                && IsFinaleEncounter(plan.echoEncounterKind))
            {
                _contractEvaluator.RecordFinaleRouteChoice(
                    _player.CurrentLane, plan.echoPredictedLane,
                    plan.echoSafeChoiceLane, plan.echoRiskChoiceLane,
                    routeDistance, speed);
            }
            ApplyContractMotionDelta();
        }
        AIRunTelemetry.RecordEvent("coin", 0,
            _player != null ? _player.CurrentLane : -1, _runCoins,
            isEchoContractMarker ? 1f : 0f);
    }

    public static bool ShouldCountContractMarker(EchoContractType contractType,
        bool isEchoContractMarker)
    {
        return ShouldCountContractMarker(contractType, EchoDuelPhase.None,
            isEchoContractMarker);
    }

    public static bool ShouldCountContractMarker(EchoContractType contractType,
        EchoDuelPhase phase, bool isEchoContractMarker)
    {
        return isEchoContractMarker
               && (phase == EchoDuelPhase.Detection
                   || contractType == EchoContractType.BreakLaneHabit);
    }

    public static bool IsFinaleEncounter(EchoEncounterKind kind)
    {
        return kind == EchoEncounterKind.FinaleOldHabit
               || kind == EchoEncounterKind.FinaleCounterHabit
               || kind == EchoEncounterKind.FinaleFreeChoice;
    }

    public static float CalculatePhaseGateLeadSeconds(
        float playerRouteDistance, float contentPreparedRouteDistance,
        float currentSpeed)
    {
        return Mathf.Max(0f,
                   Mathf.Max(0f, contentPreparedRouteDistance)
                   - Mathf.Max(0f, playerRouteDistance))
               / Mathf.Max(1f, currentSpeed);
    }

    public static float ResolveRewriteLearningWeight(bool rewriteWindow,
        bool effectiveSample, float writeStrength)
    {
        return rewriteWindow && effectiveSample
            ? Mathf.Clamp(writeStrength, 1f, 2f) : 1f;
    }

    public static float RewriteSampleWeightForPhase(EchoDuelPhase phase)
    {
        switch (phase)
        {
            case EchoDuelPhase.Detection:
            case EchoDuelPhase.Reveal:
                return 0.35f;
            case EchoDuelPhase.Rewrite:
                return 2f;
            case EchoDuelPhase.Resistance:
            case EchoDuelPhase.Counterattack:
            case EchoDuelPhase.Finale:
                return 1f;
            default:
                return 0.5f;
        }
    }

    public static string FinaleLabelFor(EchoEncounterKind kind)
    {
        switch (kind)
        {
            case EchoEncounterKind.FinaleOldHabit:
                return "第一段 · 旧习惯诱饵";
            case EchoEncounterKind.FinaleCounterHabit:
                return "第二段 · 反制策略锁定";
            case EchoEncounterKind.FinaleFreeChoice:
                return "第三段 · 自由决胜";
            default:
                return "守住领先 · 完成决胜";
        }
    }

    public bool RecordDodge()
    {
        return RecordDodge(ObstacleType.Barrier, 0,
            _player != null ? _player.CurrentLane : -1);
    }

    public GateTransitionResult RecordPredictionGateObstaclePassed(
        PredictionGateObstacleBinding binding, int obstacleId)
    {
        return ResolvePredictionGateObstacle(
            binding, obstacleId, false);
    }

    public GateTransitionResult RecordPredictionGateObstacleHit(
        PredictionGateObstacleBinding binding, int obstacleId)
    {
        return ResolvePredictionGateObstacle(
            binding, obstacleId, true);
    }

    private GateTransitionResult ResolvePredictionGateObstacle(
        PredictionGateObstacleBinding binding, int obstacleId, bool hit)
    {
        if (!IsSingleContractRuntime() || _singleContractFlow == null
            || !binding.IsBound
            || binding.runId != _singleContractFlow.RunSequence)
            return GateTransitionResult.Rejected;
        var obstacleEvent = new GateObstacleEvent
        {
            gateId = binding.gateId,
            obstacleId = obstacleId,
            physicalLane = binding.physicalLane,
            obstacleType = binding.obstacleType,
            routeDistance = _gameManager != null
                ? _gameManager.Distance : _playerPhysicalProgress
        };
        return hit
            ? _singleContractFlow.ResolveObstacleHit(obstacleEvent)
            : _singleContractFlow.ResolveObstaclePassed(obstacleEvent);
    }

    public bool RecordDodge(ObstacleType obstacleType, int obstacleId = 0,
        int playerLane = -1)
    {
        return RecordDodge(obstacleType, obstacleId, playerLane, default);
    }

    public bool RecordDodge(ObstacleType obstacleType, int obstacleId,
        int playerLane, EchoChallengeObstacleBinding binding)
    {
        if (obstacleId != 0 && !_recordedPlayerDodgeIds.Add(obstacleId))
            return false;
        _runDodges++;
        if (_rewriteTracker != null)
        {
            _rewriteTracker.RecordSuccessfulExecution(
                RewriteSampleWeightForPhase(DuelPhase));
            RefreshRewriteSnapshot();
        }
        if (HasActiveOpponent && _contractEvaluator != null)
        {
            _contractEvaluator.RecordDodge(obstacleType, playerLane,
                _gameManager != null ? _gameManager.CurrentSpeed : 10f,
                binding);
            ApplyContractMotionDelta();
        }
        AIRunTelemetry.RecordEvent("dodge", 0,
            playerLane >= 0
                ? playerLane : (_player != null ? _player.CurrentLane : -1),
            _runDodges);
        return true;
    }

    public bool BindChallengeStep(int stepId, int predictedLane,
        int challengeLane, int safeLane, float routeDistance)
    {
        return _contractEvaluator != null
               && _contractEvaluator.BindChallengeStep(stepId, predictedLane,
                   challengeLane, safeLane, routeDistance);
    }

    public void RecordChallengeStepMissed(int stepId)
    {
        if (_contractEvaluator == null) return;
        _contractEvaluator.RecordChallengeMissed(stepId);
        ApplyContractMotionDelta();
    }

    public bool ResolveChallengeStepAtGate(int stepId, int playerLane)
    {
        if (_contractEvaluator == null) return false;
        bool resolved = _contractEvaluator.ResolveChallengeAtGate(
            stepId, playerLane,
            _gameManager != null ? _gameManager.CurrentSpeed : 10f);
        if (resolved) ApplyContractMotionDelta();
        return resolved;
    }

    public void RecordObstacleHit(
        EchoChallengeObstacleBinding binding = default)
    {
        ResolvePlayerObstacleOpportunities();
        _runIdentityDraft?.RecordStyleMistake();
        if (_rewriteTracker != null)
        {
            _rewriteTracker.RecordMistake(
                RewriteSampleWeightForPhase(DuelPhase));
            RefreshRewriteSnapshot();
        }
        if (HasActiveOpponent && _contractEvaluator != null)
        {
            _contractEvaluator.RecordHit(
                _gameManager != null ? _gameManager.CurrentSpeed : 10f,
                binding);
            ApplyContractMotionDelta();
        }
        AIRunTelemetry.RecordEvent("obstacle_hit", 0,
            _player != null ? _player.CurrentLane : -1, PlayerLead);
    }

    public string FinalizeRunIfNeeded()
    {
        if (!_runFinalized && _runStarted) FinishRun();
        return LastResult;
    }

    public float[] GetModelWeightsSnapshot()
    {
        if (IsSingleContractRuntime())
        {
            ActiveEchoIdentity identity = _frozenSingleContractIdentity
                                          ?? _activeSingleContractIdentity;
            return identity != null && identity.policyWeights != null
                ? (float[])identity.policyWeights.Clone() : null;
        }
        if (_activeGeneration != null
            && _activeGeneration.policyWeights != null)
            return (float[])_activeGeneration.policyWeights.Clone();
        return _policy != null ? _policy.ExportWeights() : null;
    }

    public string GetSequenceStateSnapshot()
    {
        if (IsSingleContractRuntime())
        {
            ActiveEchoIdentity identity = _frozenSingleContractIdentity
                                          ?? _activeSingleContractIdentity;
            if (identity == null) return "";
            return JsonUtility.ToJson(new AIShadowSequenceState
            {
                transitions = identity.sequenceTransitions != null
                    ? (float[])identity.sequenceTransitions.Clone() : null,
                pairCount = identity.sequencePairCount
            });
        }
        if (_activeGeneration != null)
        {
            return JsonUtility.ToJson(new AIShadowSequenceState
            {
                transitions = _activeGeneration.sequenceTransitions != null
                    ? (float[])_activeGeneration.sequenceTransitions.Clone()
                    : null,
                pairCount = _activeGeneration.sequencePairCount
            });
        }
        return _sequencePolicy == null
            ? ""
            : JsonUtility.ToJson(_sequencePolicy.ExportState());
    }

    public string GetActiveGenerationSnapshotJson()
    {
        if (IsSingleContractRuntime())
            return _activeSingleContractIdentity != null
                ? _activeSingleContractIdentity.ToJson() : "";
        return _activeGeneration != null ? _activeGeneration.ToJson() : "";
    }

    public string GetActiveSingleContractIdentityJson()
    {
        return _activeSingleContractIdentity != null
            ? _activeSingleContractIdentity.ToJson() : "";
    }

    public bool TryGetSingleContractGate(int index,
        out PredictionGateDefinition definition)
    {
        if (_singleContractFlow == null || index < 0
            || index >= _singleContractFlow.GateCount)
        {
            definition = null;
            return false;
        }
        definition = _singleContractFlow.GetGate(index).Definition;
        return definition != null;
    }

    private bool TryGetSingleContractPredictionDisplay(
        out int physicalLane, out int gateNumber, out bool gateActive)
    {
        physicalLane = -1;
        gateNumber = 0;
        gateActive = false;
        if (_singleContractFlow == null) return false;

        int gateIndex = _singleContractFlow.ActiveGateIndex;
        if (gateIndex >= 0)
        {
            gateActive = true;
        }
        else
        {
            for (int index = 0;
                 index < _singleContractFlow.GateCount; index++)
            {
                if (_singleContractFlow.GetGate(index).State
                    != PredictionGateLifecycle.Scheduled)
                    continue;
                gateIndex = index;
                break;
            }
        }
        if (gateIndex < 0) return false;

        PredictionGateDefinition definition = _singleContractFlow.GetGate(
            gateIndex).Definition;
        if (definition?.lanes == null) return false;
        for (int index = 0; index < definition.lanes.Length; index++)
        {
            if (definition.lanes[index].role
                != PredictionGateRole.Predicted)
                continue;
            physicalLane = definition.lanes[index].physicalLane;
            gateNumber = gateIndex + 1;
            return true;
        }
        return false;
    }

    public void SetDirectiveSource(IShadowDirectiveSource source)
    {
        _directiveSource = source;
    }

    public void ResetTraining()
    {
        ResetTrainingInternal(true);
    }

    public void ResetTrainingInMemory()
    {
        ResetTrainingInternal(false);
    }

    private void ResetTrainingInternal(bool persist)
    {
        SetGhostActive(false);
        _profile = new ShadowProfile { version = 5 };
        _activeGeneration = null;
        _activeSingleContractIdentity = null;
        _frozenSingleContractIdentity = null;
        _runIdentityDraft = null;
        _runAdaptationState = null;
        _singleContractFlow = null;
        _policy = new AIShadowPolicy();
        _sequencePolicy = new AIShadowSequencePolicy();
        _opponentPolicy = null;
        _opponentSequencePolicy = null;
        _opponentStyle = null;
        _opponentPace = 0f;
        _contractEvaluator = null;
        _duelFlow = null;
        _rewriteTracker = null;
        _liveRewriteSnapshot = null;
        _frozenRewriteSnapshot = null;
        _pendingDuelBoundary = -1f;
        _singleContractRelearnPulseTimer = 0f;
        _singleContractFeedback = SingleContractInstantFeedback.None;
        _singleContractFeedbackLeadDeltaMeters = 0f;
        _singleContractFeedbackRelearned = false;
        _lastSingleContractGateAttempt = null;
        _singleContractFeedbackSequence = 0;
        _nextSingleContractSettlementIndex = 0;
        _singleContractPresentedTelemetry.Clear();
        _singleContractCommittedTelemetry.Clear();
        _singleContractResolvedTelemetry.Clear();
        _singleContractAppliedTelemetry.Clear();
        LastDecisionTrace = null;
        _runStarted = false;
        _runFinalized = false;
        _samplesSinceCheckpoint = 0;
        HasActiveOpponent = false;
        PlayerLead = 0f;
        LastResult = "";
        LastRunWasChallenge = false;
        LastRunWon = false;
        LastSingleContractCommitSucceeded = false;
        LastSingleContractIdentityPromoted = false;
        LastRunWasTransientValidation = false;
        LastSingleContractCalibrationProgress = default;
        PolicyCorrectDecisionCount = 0;
        SafetyOverrideDecisionCount = 0;
        EmergencyReflexSaveCount = 0;
        CurrentStatus = "AI影子 · 训练已重置";
        if (persist)
        {
            EchoRunSaveSystem.SaveShadowProfile("");
            EchoRunSaveSystem.SaveLastEchoContract("");
        }
    }

    private void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Playing) BeginRun();
        else if (state == GameState.GameOver) FinishRun();
    }

    private void BeginRun()
    {
        if (_runStarted) return;
        GameManager flowManager = _gameManager != null
            ? _gameManager : GameManager.Instance;
        if (flowManager != null)
            _activeGameplayFlowMode = flowManager.ActiveGameplayFlowMode;
        _runStarted = true;
        _runFinalized = false;
        LastSingleContractCommitSucceeded = false;
        LastSingleContractIdentityPromoted = false;
        LastRunWasTransientValidation = false;
        LastSingleContractCalibrationProgress = default;
        _usesTransientValidationIdentity = false;
        _persistentIdentityJsonBeforeValidation = "";
        _runTime = 0f;
        _pendingDuelBoundary = -1f;
        _singleContractFlow = null;
        _singleContractRelearnPulseTimer = 0f;
        _singleContractFeedback = SingleContractInstantFeedback.None;
        _singleContractFeedbackLeadDeltaMeters = 0f;
        _singleContractFeedbackRelearned = false;
        _lastSingleContractGateAttempt = null;
        _singleContractFeedbackSequence = 0;
        _nextSingleContractSettlementIndex = 0;
        _singleContractPresentedTelemetry.Clear();
        _singleContractCommittedTelemetry.Clear();
        _singleContractResolvedTelemetry.Clear();
        _singleContractAppliedTelemetry.Clear();
        _runUsedTurboStart = PowerUpController.Instance != null
                             && PowerUpController.Instance.ActivePowerUp
                             == PowerUpId.TurboStart;
        _runCoins = 0;
        _runDodges = 0;
        _playerPhysicalProgress = 0f;
        _playerProgress = 0f;
        _ghostProgress = 0f;
        _appliedContractPlayerBonus = 0f;
        _appliedContractShadowBonus = 0f;
        PlayerLead = 0f;
        _ghostLane = 1;
        _displayedGhostLane = 1f;
        _displayedGap = 0f;
        _ghostGroundY = _player != null ? _player.transform.position.y : 0f;
        _laneSmoothVelocity = 0f;
        _gapSmoothVelocity = 0f;
        _laneDecisionCooldown = 0f;
        _ghostForward = _player != null ? _player.ForwardDirection : Vector3.forward;
        _decisionTimer = 0f;
        _keepSampleTimer = 0f;
        _ghostJumpTimer = 0f;
        _ghostSlideTimer = 0f;
        _ghostStumbleTimer = 0f;
        _ghostRecoveryTimer = 0f;
        ResetSingleContractOpeningReplay();
        _sequenceInfluence = 0f;
        _ghostMistakes = 0;
        PolicyCorrectDecisionCount = 0;
        SafetyOverrideDecisionCount = 0;
        EmergencyReflexSaveCount = 0;
        _lastTrainingAction = -1;
        _lastOpponentAction = -1;
        _lastStyleDecision = ShadowAction.Keep;
        LastDecisionTrace = null;
        _slideOpportunityTracker.Reset();
        _jumpOpportunityTracker.Reset();
        _recordedPlayerDodgeIds.Clear();
        _opponentStyle = null;
        _opponentPace = 0f;
        _rewriteTracker = null;
        _liveRewriteSnapshot = null;
        _frozenRewriteSnapshot = null;
        int decisionSeed = _gameManager != null
            ? _gameManager.RunSeed ^ unchecked((int)0x51ED270B)
            : 1337;
        _decisionRandom = new System.Random(decisionSeed);
        _handledGhostObstacles.Clear();
        _reactedGhostObstacles.Clear();
        if (IsSingleContractRuntime())
        {
            BeginSingleContractRun();
            return;
        }
        HasActiveOpponent = HasTrainedProfile();
        _duelFlow = new EchoDuelFlow(HasActiveOpponent);

        if (HasActiveOpponent)
        {
            // Freeze the previous generation for this duel. New player actions train
            // the next generation and cannot make the current shadow mirror inputs.
            _opponentPolicy = new AIShadowPolicy(
                _activeGeneration.policyWeights);
            _opponentSequencePolicy = new AIShadowSequencePolicy(
                _activeGeneration.sequenceTransitions,
                _activeGeneration.sequencePairCount);
            _opponentStyle = _activeGeneration.GetStyle();
            _opponentPace = Mathf.Max(1f, _activeGeneration.pace);
            _contractEvaluator = new EchoContractEvaluator(
                EchoContractPolicy.CreateForRun(_opponentStyle,
                    _activeGeneration.generation,
                    EchoRunSaveSystem.GetLastEchoContractJson()));
            _contractEvaluator.Contract.duelPhase = EchoDuelPhase.None;
            _contractEvaluator.SetPhase(_duelFlow.Phase);
            _rewriteTracker = new EchoRewriteTracker(_opponentStyle);
            RefreshRewriteSnapshot();
            CreateGhost();
            CurrentStatus = _contractEvaluator.BuildHudText();
        }
        else
        {
            _contractEvaluator = null;
            CurrentStatus = "AI影子 · 校准中 0%";
            SetGhostActive(false);
        }
    }

    private void BeginSingleContractRun()
    {
        SingleContractValidationConfig validation = _gameManager != null
            ? _gameManager.ActiveSingleContractValidationConfig
            : new SingleContractValidationConfig();
        ActiveEchoIdentity persistedIdentity =
            EchoRunSaveSystem.GetActiveEchoIdentity();
        _persistentIdentityJsonBeforeValidation = persistedIdentity != null
            ? persistedIdentity.ToJson() : "";
        _usesTransientValidationIdentity =
            SingleContractValidationIdentity.IsEnabled(validation);
        _activeSingleContractIdentity = _usesTransientValidationIdentity
            ? SingleContractValidationIdentity.Create()
            : persistedIdentity;
        _frozenSingleContractIdentity = _activeSingleContractIdentity != null
            ? _activeSingleContractIdentity.Clone() : null;
        int runSequence = ResolveCurrentRunSequence();
        _runIdentityDraft = RunIdentityDraft.Create(
            _frozenSingleContractIdentity, runSequence);
        _runAdaptationState = new RunAdaptationState
        {
            contractId = _frozenSingleContractIdentity != null
                         && _frozenSingleContractIdentity.memoryContract != null
                ? _frozenSingleContractIdentity.memoryContract.contractId : "",
            hypothesisVersion = 1,
            predictedStrategy = (int)StrategyKey.OriginalHabit
        };

        _duelFlow = null;
        _contractEvaluator = null;
        _rewriteTracker = null;
        _liveRewriteSnapshot = null;
        _frozenRewriteSnapshot = null;
        _pendingDuelBoundary = -1f;
        _opponentPolicy = null;
        _opponentSequencePolicy = null;
        bool requiresRouteCalibration = _frozenSingleContractIdentity != null
                                        && _frozenSingleContractIdentity
                                            .RequiresRouteCalibration;
        HasActiveOpponent = _frozenSingleContractIdentity != null
                            && !requiresRouteCalibration;

        if (HasActiveOpponent)
        {
            _opponentPolicy = new AIShadowPolicy(
                _frozenSingleContractIdentity.policyWeights);
            _opponentSequencePolicy = new AIShadowSequencePolicy(
                _frozenSingleContractIdentity.sequenceTransitions,
                _frozenSingleContractIdentity.sequencePairCount);
            _opponentStyle =
                _frozenSingleContractIdentity.GetPlayerStyle();
            float sourceDuration = _frozenSingleContractIdentity
                                       .sourceCourseDuration > 0f
                ? _frozenSingleContractIdentity.sourceCourseDuration
                : string.IsNullOrEmpty(
                    _frozenSingleContractIdentity.parentIdentityId)
                    ? SingleContractFlow.CalibrationDurationSeconds
                    : SingleContractFlow.ChallengeDurationSeconds;
            _opponentPace = CalculateSingleContractGhostPaceScale(
                _frozenSingleContractIdentity.pace, sourceDuration,
                _gameManager != null ? _gameManager.startSpeed : 10f,
                _gameManager != null ? _gameManager.maxSpeed : 40f,
                _gameManager != null
                    ? _gameManager.speedIncreaseRate : 0.5f);
            CreateGhost();
            CurrentStatus = _frozenSingleContractIdentity.memoryContract != null
                ? _frozenSingleContractIdentity.memoryContract.BuildMemoryText()
                : "旧回声已保留 · 正在重建路线记忆";
        }
        else
        {
            _opponentStyle = null;
            _opponentPace = 0f;
            SetGhostActive(false);
            CurrentStatus = requiresRouteCalibration
                ? _frozenSingleContractIdentity.memoryContract == null
                    ? "旧回声已保留 · 正在重建路线记忆"
                    : "回声记忆模糊 · 正在重建路线记忆"
                : "AI影子 · 单契约校准中";
        }

        EchoMemoryContract memory = _frozenSingleContractIdentity != null
            ? _frozenSingleContractIdentity.memoryContract : null;
        int originalHabitLane = memory != null
            ? Mathf.Clamp(memory.preferredLane, 0, 2) : 1;
        float memoryConfidence = memory != null
            ? Mathf.Clamp01(memory.confidence) : 0f;
        float startSpeed = _gameManager != null
            ? Mathf.Max(1f, _gameManager.CurrentSpeed) : 10f;
        float maximumSpeed = _gameManager != null
            ? Mathf.Max(startSpeed, _gameManager.maxSpeed) : 40f;
        float acceleration = _gameManager != null
            ? Mathf.Max(0f, _gameManager.speedIncreaseRate) : 0.5f;
        float courseDuration = HasActiveOpponent
            ? SingleContractFlow.ChallengeDurationSeconds
            : SingleContractFlow.CalibrationDurationSeconds;
        if (_runIdentityDraft != null
            && _frozenSingleContractIdentity == null)
        {
            float unboostedStartSpeed = _gameManager != null
                ? Mathf.Max(1f, _gameManager.startSpeed) : 10f;
            float unboostedDistance =
                EchoTimeRules.DistanceForAcceleratingRun(
                    unboostedStartSpeed, maximumSpeed, acceleration,
                    courseDuration);
            _runIdentityDraft.physicalPace = CalculatePhysicalPace(
                unboostedDistance, courseDuration);
            _runIdentityDraft.sourceCourseDuration = courseDuration;
        }
        float courseDistance = _gameManager != null
                               && _gameManager.CourseDistance > 0f
            ? _gameManager.CourseDistance
            : EchoTimeRules.DistanceForAcceleratingRun(
                startSpeed, maximumSpeed, acceleration, courseDuration);
        _singleContractFlow = new SingleContractFlow(
            new SingleContractAcceleratingGateWindowFactory(
                startSpeed, maximumSpeed, acceleration),
            originalHabitLane, memoryConfidence);
        _singleContractFlow.BeginRun(new EchoRunContext
        {
            mode = GameplayFlowMode.SingleContract,
            runSequence = runSequence,
            runSeed = _gameManager != null ? _gameManager.RunSeed : 1337,
            generation = _frozenSingleContractIdentity != null
                ? _frozenSingleContractIdentity.generation : 0,
            hasOpponent = HasActiveOpponent,
            courseDuration = courseDuration,
            courseDistance = courseDistance,
            validation = validation
        });
        PrepareSingleContractOpeningReplay();
        if (validation.enabled)
            LogSingleContractValidationPlan(validation);
        RecordSingleContractEvent(AISingleContractEventType.Begin);
        for (int gateIndex = 0;
             gateIndex < _singleContractFlow.GateCount; gateIndex++)
        {
            RecordSingleContractGateEvent(
                AISingleContractEventType.GateScheduled,
                _singleContractFlow.GetGate(gateIndex));
        }
    }

    private void LogSingleContractValidationPlan(
        SingleContractValidationConfig validation)
    {
        Debug.Log("Single-contract validation plan: seed="
                  + validation.fixedSeed + ", gates="
                  + _singleContractFlow.GateCount + ", duration="
                  + _singleContractFlow.RunDurationSeconds.ToString("0.0")
                  + ", opponent=" + HasActiveOpponent
                  + ", fixedIdentity="
                  + _usesTransientValidationIdentity
                  + ", identityId="
                  + (_frozenSingleContractIdentity != null
                      ? _frozenSingleContractIdentity.identityId : ""));
        for (int index = 0; index < _singleContractFlow.GateCount; index++)
        {
            PredictionGateDefinition gate =
                _singleContractFlow.GetGate(index).Definition;
            Debug.Log("Single-contract validation gate: sequence="
                      + gate.sequence + ", presentation="
                      + gate.presentationDistance.ToString("0.0")
                      + ", commit=" + gate.commitDistance.ToString("0.0")
                      + ", resolve=" + gate.resolveDistance.ToString("0.0")
                      + ", exit=" + gate.exitDistance.ToString("0.0")
                      + ", final=" + gate.isFinal);
        }
    }

    private void BeginPendingDuelTransition(float remainingSeconds)
    {
        if (_duelFlow == null || !_duelFlow.TransitionPending
            || _contractEvaluator == null)
            return;

        float distance = _gameManager != null
            ? _gameManager.Distance : _playerPhysicalProgress;
        TrackManager track = TrackManager.Instance;
        _pendingDuelBoundary = track != null
            ? track.GetPreparedPhaseBoundary(distance)
            : TrackManager.NextRouteBoundary(distance, 20f);
        if (_duelFlow.Phase == EchoDuelPhase.Detection
            && _duelFlow.PendingPhase == EchoDuelPhase.Reveal
            && _contractEvaluator.LockDetectionContract(
                _opponentStyle, Generation))
        {
            EchoDetectionEvidence evidence =
                _contractEvaluator.DetectionEvidence;
            AIRunTelemetry.RecordEvent("echo_detection_locked",
                (int)_contractEvaluator.Contract.type,
                evidence.ValidChoiceCount,
                evidence.LaneChoiceCount,
                evidence.VerticalChoiceCount);
        }
        _contractEvaluator.SetScoringSuspended(
            ShouldSuspendContractScoringAtGate(_duelFlow.Phase));
        float phaseDuration = _duelFlow.PendingPhase == EchoDuelPhase.Rewrite
            ? _duelFlow.RewriteDuration
            : _duelFlow.PendingPhase == EchoDuelPhase.Finale
                ? _duelFlow.FinaleDuration : 0f;
        float phaseRouteLength = _gameManager != null && phaseDuration > 0f
            ? EchoTimeRules.DistanceForAcceleratingRun(
                _gameManager.CurrentSpeed, _gameManager.maxSpeed,
                _gameManager.speedIncreaseRate, phaseDuration)
            : 0f;
        AITrackDirector.Instance?.ScheduleEchoPhase(
            _duelFlow.PendingPhase, _pendingDuelBoundary, phaseRouteLength);
        AIRunTelemetry.RecordEvent("echo_duel_phase_pending",
            (int)_duelFlow.PendingPhase,
            _player != null ? _player.CurrentLane : -1,
            _pendingDuelBoundary, remainingSeconds);
    }

    public static bool ShouldSuspendContractScoringAtGate(
        EchoDuelPhase currentPhase)
    {
        return currentPhase == EchoDuelPhase.Resistance
               || currentPhase == EchoDuelPhase.Counterattack
               || currentPhase == EchoDuelPhase.Finale;
    }

    private void CommitPendingDuelTransitionIfReady()
    {
        if (_duelFlow == null || !_duelFlow.TransitionPending
            || _contractEvaluator == null || _gameManager == null
            || _gameManager.Distance + 0.01f < _pendingDuelBoundary)
            return;

        EchoDuelPhase next = _duelFlow.PendingPhase;
        EchoDuelPhase failurePhase = _duelFlow.PendingFailurePhase;
        bool failed = _duelFlow.PendingTransitionFailed;
        if (failed) _contractEvaluator.LockForFinale(failurePhase);
        if (!_duelFlow.CommitPendingTransition()) return;

        _contractEvaluator.SetPhase(next);
        _contractEvaluator.SetScoringSuspended(false);
        AITrackDirector.Instance?.CommitScheduledEchoPhase(next);
        _pendingDuelBoundary = -1f;

        if (next == EchoDuelPhase.Rewrite)
        {
            BeginRewriteProfile();
            _gameManager.ScheduleCourseFinishAfter(
                _duelFlow.RewriteDuration + _duelFlow.FinaleDuration);
        }
        else if (next == EchoDuelPhase.Finale)
        {
            FreezeRewriteProfile();
            _gameManager.ScheduleCourseFinishAfter(_duelFlow.FinaleDuration);
        }

        float remainingSeconds = EchoTimeRules.EstimateRemainingSeconds(
            _gameManager.RemainingDistance, _gameManager.CurrentSpeed);
        AIRunTelemetry.RecordEvent("echo_duel_phase",
            (int)next, _player != null ? _player.CurrentLane : -1,
            _runTime, remainingSeconds);
        CurrentStatus = _contractEvaluator.BuildHudText();
    }

    private void BeginRewriteProfile()
    {
        if (_rewriteTracker == null)
            _rewriteTracker = new EchoRewriteTracker(
                _opponentStyle ?? StyleTracker.GetSnapshot());
        _frozenRewriteSnapshot = null;
        RefreshRewriteSnapshot();
        AIRunTelemetry.RecordEvent("echo_rewrite_begin",
            Generation, _player != null ? _player.CurrentLane : -1,
            _runTime, 1f);
    }

    private void RefreshRewriteSnapshot()
    {
        if (_rewriteTracker == null) return;
        _liveRewriteSnapshot = _rewriteTracker.BuildSnapshot(
            StyleTracker.GetSnapshot());
        if (_contractEvaluator != null && _liveRewriteSnapshot != null)
        {
            int effectiveSamples = _liveRewriteSnapshot.effectiveRouteChoices
                                   + _liveRewriteSnapshot.effectiveVerticalActions;
            _contractEvaluator.Contract.rewriteReady = effectiveSamples >= 4
                && _liveRewriteSnapshot.sampleCoverage01 >= 0.6f
                && _liveRewriteSnapshot.execution01 >= 0.55f;
        }
    }

    private void FreezeRewriteProfile()
    {
        if (_rewriteTracker == null) return;
        RefreshRewriteSnapshot();
        _frozenRewriteSnapshot = _liveRewriteSnapshot != null
            ? _liveRewriteSnapshot.Clone() : null;
        _liveRewriteSnapshot = _frozenRewriteSnapshot;
        _rewriteTracker = null;
        if (_frozenRewriteSnapshot == null) return;
        AIRunTelemetry.RecordEvent("echo_rewrite_frozen",
            Mathf.RoundToInt(_frozenRewriteSnapshot.writeStrength * 100f),
            _player != null ? _player.CurrentLane : -1,
            _frozenRewriteSnapshot.execution01,
            _frozenRewriteSnapshot.routeVariation01);
    }

    private void FinishRun()
    {
        RunEndReason endReason = _gameManager != null
                                 && _gameManager.LastEndReason != RunEndReason.None
            ? _gameManager.LastEndReason
            : RunEndReason.Abandoned;
        FinishRunWithReason(endReason);
    }

    private void FinishRunWithReason(RunEndReason endReason)
    {
        if (!_runStarted || _runFinalized) return;
        _runFinalized = true;
        if (IsSingleContractRuntime())
        {
            FinishSingleContractRun(endReason);
            return;
        }
        FreezeRewriteProfile();

        bool challengedOpponent = HasActiveOpponent;
        bool reachedFinish = endReason == RunEndReason.FinishReached;
        bool contractCompleted = _contractEvaluator != null
                                 && _contractEvaluator.Contract.completed;
        bool playerWon = IsContractVictory(
            PlayerLead, challengedOpponent, contractCompleted, endReason);
        LastRunWasChallenge = challengedOpponent;
        LastRunWon = playerWon;
        float physicalDistance = _gameManager != null
            ? _gameManager.Distance
            : _playerPhysicalProgress;
        float runPace = CalculatePhysicalPace(physicalDistance, _runTime);
        if (ShouldRecordPendingPace(endReason, physicalDistance,
                _runTime, _runUsedTurboStart))
        {
            if (_profile.pace <= 0f) _profile.pace = runPace;
            else _profile.pace = Mathf.Lerp(_profile.pace, runPace, 0.35f);
        }
        _profile.bestProgress = Mathf.Max(
            _profile.bestProgress, physicalDistance);
        float calibrationProgress = CalculateCalibrationProgress(
            _profile.sampleCount, _profile.activeSampleCount,
            _profile.actionCounts, minimumTrainingSamples,
            minimumActiveTrainingSamples, minimumActionCategories,
            minimumJumpSamples, minimumSlideSamples);
        bool completedCalibration = reachedFinish
                                    && calibrationProgress >= 0.999f;
        bool formedPartialEcho = !challengedOpponent
                                 && HasPartialEchoSamples(
                                     _profile.sampleCount,
                                     _profile.activeSampleCount,
                                     _profile.actionCounts,
                                     _runTime,
                                     minimumTrainingSamples);
        if (challengedOpponent && reachedFinish && playerWon)
        {
            float nextClarity = Mathf.Max(EchoClarity,
                Mathf.Clamp01(calibrationProgress));
            PromotePendingGeneration(Generation + 1, nextClarity);
        }
        else if (!challengedOpponent && Generation <= 0
                 && (completedCalibration || formedPartialEcho))
        {
            float firstClarity = completedCalibration
                ? 1f
                : Mathf.Clamp(calibrationProgress, 0.25f, 0.85f);
            PromotePendingGeneration(1, firstClarity);
        }
        _profile.weights = _policy.ExportWeights();
        SaveProfile();

        if (!challengedOpponent && formedPartialEcho && !completedCalibration)
        {
            EchoContractData nextContract = EchoContractPolicy.Create(
                _activeGeneration.GetStyle(), Generation);
            LastResult = "校准中断，但回声已经记住了你\n"
                         + "回声清晰度 "
                         + (EchoClarity * 100f).ToString("0") + "% · "
                         + nextContract.learnedTrait + "\n"
                         + "下一局将由模糊回声继续校准："
                         + nextContract.title;
        }
        else if (!reachedFinish)
        {
            LastResult = challengedOpponent
                ? "赛程中断 · 未到达终点\n本代契约未结算，重新挑战才能获胜"
                : "校准中断 · 未到达终点\n本局样本已保留，完成赛程后才会生成回声";
        }
        else if (!challengedOpponent && !completedCalibration)
        {
            int categories = CountTrainedActionCategories(_profile.actionCounts);
            LastResult = "校准未完成 · 有效动作 "
                         + _profile.activeSampleCount + "/"
                         + Mathf.Max(1, minimumActiveTrainingSamples)
                         + " · 动作类型 " + categories + "/"
                         + Mathf.Max(1, minimumActionCategories)
                         + " · 跳/滑 "
                         + _profile.actionCounts[(int)ShadowAction.Jump]
                         + "/" + _profile.actionCounts[(int)ShadowAction.Slide]
                         + "（目标 " + minimumJumpSamples
                         + "/" + minimumSlideSamples + "）"
                         + " · 再跑一局继续训练";
        }
        else if (!challengedOpponent)
        {
            EchoContractData nextContract = EchoContractPolicy.Create(
                _activeGeneration.GetStyle(), Generation);
            LastResult = "校准完成 · 第 1 代 AI 回声已生成\n"
                         + "回声清晰度 100%\n"
                         + nextContract.learnedTrait + "\n"
                         + "下一局规则：" + nextContract.ruleDescription;
        }
        else if (playerWon)
        {
            EchoContractData nextContract = EchoContractPolicy.Create(
                _activeGeneration.GetStyle(), Generation);
            _contractEvaluator.Contract.won = true;
            string rewriteSummary = _frozenRewriteSnapshot != null
                ? _frozenRewriteSnapshot.BuildProfileSummary()
                : "AI已记录你的反制策略";
            LastResult = "契约破解 · 领先回声 "
                         + Mathf.Abs(PlayerLead).ToString("0.0") + "m\n"
                         + "上一代行为：" + _contractEvaluator.Contract.learnedTrait + "\n"
                         + "本代学习：" + rewriteSummary + "\n"
                         + "下一代变化：" + nextContract.title + " · "
                         + nextContract.ruleDescription;
        }
        else
        {
            bool ledButFailedContract = PlayerLead >= 0f && !contractCompleted;
            string cause;
            string learning;
            if (ledButFailedContract)
            {
                bool partialCounter = _contractEvaluator.Contract.progress
                                      > 0.01f;
                cause = partialCounter
                    ? "距离领先，反制已生效但契约未完全破解"
                    : "距离领先，但尚未形成有效反制";
                learning = _contractEvaluator.Contract.counterRelockCount > 0
                    ? "回声已改判，后续需要再次改变选择"
                    : partialCounter
                        ? "反制有效，但回声锁定尚未完全碎裂"
                        : "尚未在有效交锋点打破回声预判";
            }
            else if (contractCompleted)
            {
                cause = "契约已破解，但回声在距离竞速中领先 "
                        + Mathf.Abs(PlayerLead).ToString("0.0") + "m";
                learning = "反制已经有效，重试时需要追回距离";
            }
            else
            {
                cause = "回声在距离竞速中领先 "
                        + Mathf.Abs(PlayerLead).ToString("0.0") + "m";
                learning = "旧习惯仍被回声掌握";
            }
            LastResult = "回声胜出 · " + cause + "\n"
                         + "上一代行为：" + _contractEvaluator.Contract.learnedTrait + "\n"
                         + "反制进度 "
                         + _contractEvaluator.Contract.progress.ToString("0.#")
                         + "/"
                         + _contractEvaluator.Contract.targetProgress.ToString("0.#")
                         + "\n本代结论：" + learning + "\n"
                         + "重试规则保持不变："
                         + _contractEvaluator.Contract.title;
        }

        if (_contractEvaluator != null)
        {
            EchoContractData savedContract = playerWon
                ? null : _contractEvaluator.Contract.ResetForRun();
            EchoRunSaveSystem.SaveLastEchoContract(savedContract != null
                ? JsonUtility.ToJson(savedContract) : "");
        }

        CurrentStatus = LastResult;
        AIRunTelemetry.RecordEvent("shadow_result",
            challengedOpponent ? (playerWon ? 1 : -1) : 0,
            _ghostLane, PlayerLead, _ghostMistakes);
        HasActiveOpponent = false;
        SetGhostActive(false);
    }

    private void FinishSingleContractRun(RunEndReason endReason)
    {
        bool challengedOpponent = HasActiveOpponent;
        bool routeCalibration = !challengedOpponent;
        bool compatibilityCalibration = routeCalibration
                                        && _frozenSingleContractIdentity != null;
        int generationBefore = _frozenSingleContractIdentity != null
            ? _frozenSingleContractIdentity.generation : 0;
        string oldIdentityId = _frozenSingleContractIdentity != null
            ? _frozenSingleContractIdentity.identityId : "";
        string identityHashBefore = _frozenSingleContractIdentity != null
            ? _frozenSingleContractIdentity.ComputeHash() : "";

        _singleContractFlow?.CancelActiveGate();
        CaptureSingleContractGateTelemetry();
        ConsumeSingleContractSettlements();

        float physicalDistance = _gameManager != null
            ? _gameManager.Distance : _playerPhysicalProgress;
        float elapsedTime = _gameManager != null
            ? _gameManager.RunElapsed : _runTime;
        if (_runIdentityDraft != null
            && ShouldRecordPendingPace(endReason, physicalDistance,
                elapsedTime, _runUsedTurboStart))
        {
            float measuredPace = CalculatePhysicalPace(
                physicalDistance, elapsedTime);
            _runIdentityDraft.physicalPace = _frozenSingleContractIdentity
                                                != null
                                            && _frozenSingleContractIdentity
                                                .pace > 0f
                ? BlendSingleContractNormalizedPace(
                    _frozenSingleContractIdentity.pace,
                    _frozenSingleContractIdentity.sourceCourseDuration,
                    measuredPace, elapsedTime,
                    _gameManager != null ? _gameManager.startSpeed : 10f,
                    _gameManager != null ? _gameManager.maxSpeed : 40f,
                    _gameManager != null
                        ? _gameManager.speedIncreaseRate : 0.5f, 0.35f)
                : measuredPace;
            _runIdentityDraft.sourceCourseDuration = elapsedTime;
        }

        _playerPhysicalProgress = Mathf.Max(0f, physicalDistance);
        _playerProgress = _playerPhysicalProgress
                          + _appliedContractPlayerBonus;
        if (challengedOpponent)
        {
            PlayerLead = CalculatePhysicalLead(_playerProgress,
                _ghostProgress + _appliedContractShadowBonus);
        }
        _singleContractFlow?.FinishRun(endReason, PlayerLead);
        _runIdentityDraft?.FinalizeStyle();

        bool reachedFinish = endReason == RunEndReason.FinishReached;
        bool playerWon = IsSingleContractVictory(
            PlayerLead, challengedOpponent, endReason);
        ActiveEchoIdentity promotedIdentity = null;
        bool promotionBuilt = false;
        if (_runIdentityDraft != null && reachedFinish)
        {
            if (challengedOpponent)
            {
                promotionBuilt = _runIdentityDraft.TryBuildChallengePromotion(
                    playerWon, 1f, out promotedIdentity);
            }
            else if (compatibilityCalibration)
            {
                promotionBuilt = _runIdentityDraft
                    .TryBuildCompatibilityCalibrationPromotion(
                        true, 1f, minimumTrainingSamples,
                        minimumActiveTrainingSamples,
                        minimumActionCategories, minimumJumpSamples,
                        minimumSlideSamples, out promotedIdentity);
            }
            else
            {
                promotionBuilt = _runIdentityDraft
                    .TryBuildCalibrationPromotion(
                        true, 1f, minimumTrainingSamples,
                        minimumActiveTrainingSamples,
                        minimumActionCategories, minimumJumpSamples,
                        minimumSlideSamples, out promotedIdentity);
            }
        }

        LastRunWasChallenge = challengedOpponent;
        LastRunWon = playerWon;
        int cognitionGateCount = _singleContractFlow != null
            ? _singleContractFlow.GateCount : 0;
        bool nextLaneHasUniqueEvidence = false;
        if (_runIdentityDraft != null
            && _runIdentityDraft.gateChoices != null
            && promotedIdentity != null
            && promotedIdentity.memoryContract != null
            && _runIdentityDraft.gateChoices.TryGetUniquePreferredLane(
                out int uniquePreferredLane))
        {
            nextLaneHasUniqueEvidence = uniquePreferredLane
                                        == promotedIdentity.memoryContract
                                            .preferredLane;
        }
        EchoCognitionAssessment cognitionAssessment =
            EchoCognitionAssessment.Compare(
                _frozenSingleContractIdentity,
                promotionBuilt ? promotedIdentity : null,
                _runAdaptationState != null
                    ? _runAdaptationState.successfulCounterCount : 0,
                cognitionGateCount,
                _runAdaptationState != null
                    ? _runAdaptationState.relearnStartGateNumber : 0,
                nextLaneHasUniqueEvidence);
        SingleContractCalibrationProgress calibrationProgress =
            !challengedOpponent && _runIdentityDraft != null
                ? _runIdentityDraft.BuildCalibrationProgress(
                    minimumTrainingSamples,
                    minimumActiveTrainingSamples,
                    minimumActionCategories,
                    minimumJumpSamples,
                    minimumSlideSamples,
                    reachedFinish)
                : default;
        if (calibrationProgress.available)
            calibrationProgress.promotionReady = promotionBuilt;
        LastSingleContractCalibrationProgress = calibrationProgress;
        string intendedResult = BuildSingleContractResult(
            endReason, challengedOpponent, playerWon, promotionBuilt,
            promotedIdentity, generationBefore, cognitionAssessment,
            calibrationProgress);
        intendedResult = AppendSingleContractGateReview(intendedResult);
        LastResult = intendedResult;

        int runSequence = _runIdentityDraft != null
            ? _runIdentityDraft.runSequence : ResolveCurrentRunSequence();
        string transactionId = "single-contract-run-" + runSequence;
        var commit = new RunSettlementCommit
        {
            transactionId = transactionId,
            runSequence = runSequence,
            endReason = endReason,
            hasActiveOpponent = challengedOpponent,
            calibrationCompleted = routeCalibration && promotionBuilt,
            playerWon = playerWon,
            playerLead = challengedOpponent ? PlayerLead : 0f,
            resultMessage = intendedResult,
            promotedIdentity = promotionBuilt ? promotedIdentity : null
        };
        if (_usesTransientValidationIdentity)
        {
            FinishTransientValidationRun(endReason, challengedOpponent,
                playerWon, generationBefore, oldIdentityId, transactionId);
            return;
        }
        SaveCommitResult saveResult =
            EchoRunSaveSystem.TryCommitSingleContractSettlement(commit);
        LastSingleContractCommitSucceeded = saveResult.succeeded;
        LastSingleContractIdentityPromoted = saveResult.succeeded
                                               && saveResult.identityPromoted;
        string identityHashAfter = identityHashBefore;
        string newIdentityId = oldIdentityId;
        if (saveResult.succeeded)
        {
            _activeSingleContractIdentity = saveResult.activeIdentity != null
                ? saveResult.activeIdentity.Clone() : null;
            newIdentityId = _activeSingleContractIdentity != null
                ? _activeSingleContractIdentity.identityId : "";
            identityHashAfter = _activeSingleContractIdentity != null
                ? _activeSingleContractIdentity.ComputeHash() : "";
            if (promotionBuilt)
            {
                RecordSingleContractIdentityEvent(
                    AISingleContractEventType.IdentityPromoted,
                    oldIdentityId, newIdentityId, transactionId,
                    saveResult.alreadyCommitted ? "already_committed" : "committed",
                    identityHashBefore, identityHashAfter);
            }
        }
        else
        {
            LastResult = BuildSingleContractSaveFailureResult(
                endReason, challengedOpponent, playerWon,
                generationBefore, calibrationProgress);
            LastResult = AppendSingleContractGateReview(LastResult);
            RecordSingleContractIdentityEvent(
                AISingleContractEventType.IdentityCommitFailed,
                oldIdentityId, newIdentityId, transactionId,
                saveResult.error, identityHashBefore, identityHashAfter);
        }

        if (!promotionBuilt || !saveResult.succeeded)
        {
            RecordSingleContractIdentityEvent(
                AISingleContractEventType.IdentityDraftDiscarded,
                oldIdentityId, newIdentityId, transactionId,
                saveResult.succeeded ? "discarded" : "commit_failed",
                identityHashBefore, identityHashAfter);
        }
        RecordSingleContractIdentityEvent(
            AISingleContractEventType.Result,
            oldIdentityId, newIdentityId, transactionId,
            saveResult.succeeded ? (playerWon ? "won" : "settled") : "failed",
            identityHashBefore, identityHashAfter);

        CurrentStatus = LastResult;
        AIRunTelemetry.RecordEvent("shadow_result",
            challengedOpponent ? (playerWon ? 1 : -1) : 0,
            _ghostLane, PlayerLead,
            _ghostMistakes);
        HasActiveOpponent = false;
        SetGhostActive(false);
        DiscardSingleContractRunState();
    }

    private void FinishTransientValidationRun(RunEndReason endReason,
        bool challengedOpponent, bool playerWon, int generationBefore,
        string validationIdentityId, string transactionId)
    {
        ActiveEchoIdentity persistedIdentity =
            EchoRunSaveSystem.GetActiveEchoIdentity();
        string persistentIdentityJsonAfter = persistedIdentity != null
            ? persistedIdentity.ToJson() : "";
        bool identityUnchanged = string.Equals(
            _persistentIdentityJsonBeforeValidation,
            persistentIdentityJsonAfter, StringComparison.Ordinal);
        string persistentHashBefore = string.IsNullOrEmpty(
            _persistentIdentityJsonBeforeValidation)
            ? "" : StableHash.ComputeHex(
                _persistentIdentityJsonBeforeValidation);
        string persistentHashAfter = string.IsNullOrEmpty(
            persistentIdentityJsonAfter)
            ? "" : StableHash.ComputeHex(persistentIdentityJsonAfter);

        LastRunWasTransientValidation = true;
        LastSingleContractCommitSucceeded = false;
        LastSingleContractIdentityPromoted = false;
        LastResult = identityUnchanged
            ? BuildSingleContractValidationResult(endReason,
                challengedOpponent, playerWon, generationBefore)
            : "固定验收隔离失败\n真实身份档发生意外变化";
        LastResult = AppendSingleContractGateReview(LastResult);
        if (identityUnchanged)
        {
            Debug.Log("Single-contract validation settlement isolated: "
                      + "persistedIdentityUnchanged=true");
        }
        else
        {
            Debug.LogError("Single-contract validation changed the persisted "
                           + "identity unexpectedly.");
        }

        RecordSingleContractIdentityEvent(
            AISingleContractEventType.IdentityDraftDiscarded,
            validationIdentityId, validationIdentityId, transactionId,
            identityUnchanged ? "validation_not_persisted"
                              : "validation_isolation_failed",
            persistentHashBefore, persistentHashAfter);
        RecordSingleContractIdentityEvent(
            AISingleContractEventType.Result,
            validationIdentityId, validationIdentityId, transactionId,
            identityUnchanged ? (playerWon ? "validation_won"
                                           : "validation_settled")
                              : "validation_isolation_failed",
            persistentHashBefore, persistentHashAfter);

        CurrentStatus = LastResult;
        AIRunTelemetry.RecordEvent("shadow_result",
            challengedOpponent ? (playerWon ? 1 : -1) : 0,
            _ghostLane, PlayerLead, _ghostMistakes);
        HasActiveOpponent = false;
        SetGhostActive(false);
        DiscardSingleContractRunState();
    }

    public static string BuildSingleContractValidationResult(
        RunEndReason endReason, bool challengedOpponent, bool playerWon,
        int generationBefore)
    {
        if (endReason != RunEndReason.FinishReached)
            return "固定验收局中断\n真实身份档未修改";
        if (!challengedOpponent)
            return "固定校准局完成\n真实身份档未修改";
        int generation = Mathf.Max(1, generationBefore);
        return playerWon
            ? "你跑赢了第" + generation
              + "代固定回声\n固定验收模式 · 身份档未修改"
            : "第" + generation
              + "代固定回声胜出\n固定验收模式 · 身份档未修改";
    }

    public static string BuildSingleContractSaveFailureResult(
        RunEndReason endReason, bool challengedOpponent, bool playerWon,
        int generationBefore,
        SingleContractCalibrationProgress calibrationProgress = default)
    {
        int generation = Mathf.Max(1, generationBefore);
        if (!challengedOpponent)
        {
            string result = endReason == RunEndReason.FinishReached
                ? "回声保存失败\n本局学习没有写入"
                : "回声保存失败\n这次观察提前结束";
            string evidence = EchoRunPresentation
                .BuildSingleContractCalibrationEvidence(
                    calibrationProgress);
            if (!string.IsNullOrEmpty(evidence))
                result += "\n" + evidence;
            return result + "\n当前回声未改变，请再跑一局";
        }

        if (playerWon && endReason == RunEndReason.FinishReached)
        {
            return "你跑赢了第" + generation
                   + "代回声\n回声保存失败\n下一代未形成，当前回声保持不变";
        }
        return "第" + generation
               + "代回声胜出\n回声保存失败\n当前回声保持不变";
    }

    private static string BuildSingleContractResult(RunEndReason endReason,
        bool challengedOpponent, bool playerWon, bool promotionBuilt,
        ActiveEchoIdentity promotedIdentity, int generationBefore,
        EchoCognitionAssessment cognitionAssessment,
        SingleContractCalibrationProgress calibrationProgress)
    {
        if (endReason != RunEndReason.FinishReached)
        {
            if (challengedOpponent)
            {
                return "第" + Mathf.Max(1, generationBefore)
                       + "代回声胜出\n本局未到终点\n下一局仍使用本代记录";
            }
            return EchoRunPresentation.BuildSingleContractCalibrationResult(
                calibrationProgress);
        }

        if (!challengedOpponent)
        {
            if (!promotionBuilt || promotedIdentity == null)
            {
                return EchoRunPresentation
                    .BuildSingleContractCalibrationResult(
                        calibrationProgress);
            }
            string evidence = EchoRunPresentation
                .BuildSingleContractCalibrationEvidence(
                    calibrationProgress);
            return "第" + promotedIdentity.generation
                   + "代回声已经形成\n它记住了："
                   + promotedIdentity.memoryContract.BuildMemoryText()
                   + (string.IsNullOrEmpty(evidence)
                       ? "" : "\n" + evidence)
                   + "\n下一局，它会带着这些习惯追上你";
        }

        if (!playerWon)
        {
            return "第" + Mathf.Max(1, generationBefore)
                   + "代回声胜出\n本局未能领先到终点\n下一局仍使用本代记录";
        }
        if (!promotionBuilt || promotedIdentity == null)
        {
            return "你跑赢了第" + Mathf.Max(1, generationBefore)
                   + "代回声\n这局还不足以形成下一代，当前回声保持不变";
        }
        string cognitionSummary = EchoRunPresentation
            .BuildSingleContractCognitionSummary(cognitionAssessment);
        if (!string.IsNullOrEmpty(cognitionSummary))
        {
            return "你跑赢了第" + Mathf.Max(1, generationBefore)
                   + "代回声\n" + cognitionSummary;
        }
        return "你跑赢了第" + Mathf.Max(1, generationBefore)
               + "代回声\n第" + promotedIdentity.generation
               + "代回声已经形成\n它记住了："
               + promotedIdentity.memoryContract.BuildMemoryText();
    }

    private string AppendSingleContractGateReview(string result)
    {
        string review = EchoRunPresentation.BuildSingleContractGateReview(
            _lastSingleContractGateAttempt);
        return string.IsNullOrEmpty(review) ? result : result + "\n" + review;
    }

    private void PromotePendingGeneration(int generation, float clarity)
    {
        AIShadowSequenceState sequence = _sequencePolicy != null
            ? _sequencePolicy.ExportState()
            : new AIShadowSequenceState();
        PlayerStyleData style = _frozenRewriteSnapshot != null
            ? _frozenRewriteSnapshot.GetStyle()
            : StyleTracker.GetSnapshot();
        style.Normalize();
        float promotedPace = _profile.pace;
        if (promotedPace <= 0f && _gameManager != null)
        {
            float expectedDistance = EchoTimeRules.DistanceForAcceleratingRun(
                _gameManager.startSpeed, _gameManager.maxSpeed,
                _gameManager.speedIncreaseRate, Mathf.Max(1f, _runTime));
            promotedPace = CalculatePhysicalPace(expectedDistance, _runTime);
        }
        _activeGeneration = new EchoGenerationSnapshot
        {
            generation = Mathf.Max(1, generation),
            policyWeights = _policy != null
                ? _policy.ExportWeights() : _profile.weights,
            sequenceTransitions = sequence.transitions,
            sequencePairCount = sequence.pairCount,
            styleJson = JsonUtility.ToJson(style),
            pace = Mathf.Max(1f, promotedPace),
            clarity = Mathf.Clamp01(clarity)
        };
        _activeGeneration.Normalize();
        _profile.generation = _activeGeneration.generation;
        _profile.clarity = _activeGeneration.clarity;
        _profile.activeGenerationJson = _activeGeneration.ToJson();
    }

    public static bool ShouldRecordPendingPace(RunEndReason endReason,
        float physicalDistance, float runTime, bool usedTurboStart)
    {
        return endReason != RunEndReason.Abandoned
               && !usedTurboStart
               && runTime >= 8f
               && physicalDistance >= 60f;
    }

    private void Learn(ShadowAction action, float[] features,
        bool effectiveRewriteSample)
    {
        LearnWithStyle(action, features, effectiveRewriteSample,
            0f, 0f, false, false);
    }

    private void LearnWithStyle(ShadowAction action, float[] features,
        bool effectiveRewriteSample, float styleProximity,
        float jumpTimingOffset, bool airLaneChange,
        bool matchedActionObstacle)
    {
        int lane = features != null && features.Length > 1
            ? Mathf.RoundToInt(features[1] + 1f)
            : 1;
        AIShadowPolicy trainingPolicy = IsSingleContractRuntime()
            ? _runIdentityDraft != null ? _runIdentityDraft.policy : null
            : _policy;
        float confidence = trainingPolicy != null
            ? trainingPolicy.Confidence(features) : 0f;
        AIRunTelemetry.RecordShadowSample(
            action, lane, features, false, confidence, (int)action,
            0f, 0f);
        float rewriteWeight = ResolveRewriteLearningWeight(
            _duelFlow != null && _duelFlow.IsRewriteLearningWindow,
            effectiveRewriteSample, RewriteWriteStrength);
        float sampleLearningRate = (action == ShadowAction.Keep
            ? learningRate * 0.25f
            : learningRate) * rewriteWeight;
        if (IsSingleContractRuntime())
        {
            if (_runIdentityDraft == null || _runIdentityDraft.policy == null
                || _runIdentityDraft.sequence == null)
                return;
            _runIdentityDraft.policy.Learn(
                (int)action, features, sampleLearningRate);
            if (action != ShadowAction.Keep)
            {
                _runIdentityDraft.sequence.Learn(
                    _lastTrainingAction, (int)action);
                _lastTrainingAction = (int)action;
            }
            _runIdentityDraft.RecordSample(action, lane, styleProximity,
                jumpTimingOffset, airLaneChange, matchedActionObstacle);
            return;
        }
        _policy.Learn((int)action, features, sampleLearningRate);
        if (action != ShadowAction.Keep)
        {
            _sequencePolicy.Learn(_lastTrainingAction, (int)action);
            _lastTrainingAction = (int)action;
        }
        _profile.sampleCount++;
        EnsureActionCounts();
        int actionIndex = Mathf.Clamp((int)action, 0, _profile.actionCounts.Length - 1);
        _profile.actionCounts[actionIndex]++;
        if (action != ShadowAction.Keep)
            _profile.activeSampleCount++;
        _samplesSinceCheckpoint++;

        if (_samplesSinceCheckpoint >= SamplesPerCheckpoint)
        {
            _samplesSinceCheckpoint = 0;
            SaveProfile();
        }

        if (!HasActiveOpponent)
        {
            float progress = CalculateCalibrationProgress(
                _profile.sampleCount, _profile.activeSampleCount,
                _profile.actionCounts, minimumTrainingSamples,
                minimumActiveTrainingSamples, minimumActionCategories,
                minimumJumpSamples, minimumSlideSamples);
            CurrentStatus = "AI影子 · 校准 " + (progress * 100f).ToString("0")
                            + "% · 有效动作 " + _profile.activeSampleCount
                            + "/" + Mathf.Max(1, minimumActiveTrainingSamples)
                            + " · 跳/滑 "
                            + _profile.actionCounts[(int)ShadowAction.Jump]
                            + "/" + _profile.actionCounts[(int)ShadowAction.Slide];
        }
    }

    private float[] BuildFeatures(int lane, bool forShadow)
    {
        float speed = 0f;
        if (_gameManager != null)
        {
            speed = Mathf.InverseLerp(_gameManager.startSpeed,
                _gameManager.maxSpeed, _gameManager.CurrentSpeed);
        }

        float proximity = 0f;
        float relativeLane = 0f;
        float obstacleType = 0f;
        Vector3 samplePosition = forShadow && _ghost != null
            ? _ghost.transform.position
            : (_player != null ? _player.transform.position : Vector3.zero);
        Vector3 sampleForward = forShadow
            ? _ghostForward
            : (_player != null ? _player.ForwardDirection : Vector3.forward);

        if (_player != null && TrackManager.Instance != null
            && TrackManager.Instance.TryGetUpcomingObstacle(
                samplePosition, sampleForward, lane,
                out int threatLane, out float threatDistance,
                out ObstacleType threatType, out int ignoredObstacleId))
        {
            proximity = 1f - Mathf.Clamp01(threatDistance / 24f);
            relativeLane = Mathf.Clamp((threatLane - lane) / 2f, -1f, 1f);
            obstacleType = ((int)threatType + 1) / 3f;
        }

        return new[]
        {
            1f,
            lane - 1f,
            speed,
            proximity,
            relativeLane,
            obstacleType,
            forShadow ? (_ghostJumpTimer > 0f ? 1f : 0f)
                      : (_player != null && _player.IsJumping ? 1f : 0f),
            forShadow ? (_ghostSlideTimer > 0f ? 1f : 0f)
                      : (_player != null && _player.IsSliding ? 1f : 0f)
        };
    }

    private void ApplyShadowDecision()
    {
        if (_opponentPolicy == null) return;
        float[] features = BuildFeatures(_ghostLane, true);
        float[] baseScores = _opponentPolicy.GetProbabilities(features);
        ShadowAction baseAction = (ShadowAction)_opponentPolicy.Predict(features);
        ShadowAction sequenceAction = PredictOpponentAction(features,
            out float baseConfidence,
            out float sequenceConfidence, out float sequenceInfluence);
        baseScores[(int)sequenceAction] += sequenceInfluence * 0.25f;
        ShadowDecisionContext context = BuildDecisionContext(features);
        ShadowAIDirective directive = GetShadowDirective();
        ShadowAction action = _decisionMaker.Select(baseScores,
            _opponentStyle, context, directive,
            (float)_decisionRandom.NextDouble(), out ShadowDecisionTrace trace);
        trace.originalPrediction = sequenceAction;
        LastDecisionTrace = trace;
        CountDecisionOutcome(context, sequenceAction, action, trace);
        _lastStyleDecision = action;
        _decisionConfidence = Mathf.Max(baseConfidence, sequenceConfidence);
        _sequenceInfluence = sequenceInfluence;
        AIRunTelemetry.RecordShadowSample(
            action, _ghostLane, features, true, _decisionConfidence, (int)baseAction,
            sequenceConfidence, sequenceInfluence, trace, _opponentStyle);

        switch (action)
        {
            case ShadowAction.Left:
                if (_laneDecisionCooldown <= 0f && _ghostLane > 0)
                {
                    _ghostLane--;
                    _laneDecisionCooldown = minimumLaneHoldTime;
                    RecordOpponentAction(action);
                }
                else RecordOpponentAction(ShadowAction.Keep);
                break;
            case ShadowAction.Right:
                if (_laneDecisionCooldown <= 0f && _ghostLane < 2)
                {
                    _ghostLane++;
                    _laneDecisionCooldown = minimumLaneHoldTime;
                    RecordOpponentAction(action);
                }
                else RecordOpponentAction(ShadowAction.Keep);
                break;
            case ShadowAction.Jump:
            case ShadowAction.Slide:
                // Vertical actions are scheduled from the obstacle distance below.
                // The policy still owns route selection, but cannot spam jumps or
                // start a slide in mid-air between its regular decision ticks.
                RecordOpponentAction(ShadowAction.Keep);
                break;
            default:
                RecordOpponentAction(ShadowAction.Keep);
                break;
        }
    }

    private void ApplyObstacleReaction()
    {
        if (_ghost == null || _player == null || TrackManager.Instance == null
            || _ghostStumbleTimer > 0f || _ghostJumpTimer > 0f
            || _ghostSlideTimer > 0f)
            return;
        if (TrackManager.Instance.IsInsideTurnTransition(
                _ghost.transform.position))
            return;

        if (!TrackManager.Instance.TryGetUpcomingObstacleInLane(
                _ghost.transform.position, _ghostForward, _ghostLane,
                _reactedGhostObstacles, out float threatDistance,
                out ObstacleType threatType, out int obstacleId))
            return;

        ShadowAction requiredAction = RequiredActionForObstacle(threatType);
        if (requiredAction == ShadowAction.Keep) return;

        float duration = requiredAction == ShadowAction.Jump
            ? GetGhostJumpDuration()
            : GetGhostSlideDuration();
        float speed = _gameManager != null ? _gameManager.CurrentSpeed : 10f;
        float reactionDistance = CalculateReactionDistance(speed, duration)
                                 * ShadowDecisionMaker.ReactionDistanceMultiplier(
                                     _opponentStyle, GetShadowDirective());
        if (threatDistance > reactionDistance) return;

        // A trained clone gets the full reaction window when it predicts the
        // learned move. The close-range reflex is the physical safety layer:
        // it keeps an imperfect model readable without erasing earlier/later
        // reaction timing learned from the player.
        if (_opponentPolicy != null)
        {
            float emergencyDistance = Mathf.Clamp(speed * 0.2f, 2f, 4.5f);
            if (_lastStyleDecision != requiredAction)
            {
                if (!enableEmergencyReflex || threatDistance > emergencyDistance)
                    return;
            }
        }

        _reactedGhostObstacles.Add(obstacleId);
        bool reflexSave = _lastStyleDecision != requiredAction;
        if (StartGhostAction(requiredAction))
        {
            if (reflexSave) EmergencyReflexSaveCount++;
            RecordOpponentAction(requiredAction);
        }
    }

    private void CountDecisionOutcome(ShadowDecisionContext context,
        ShadowAction originalPrediction, ShadowAction selected,
        ShadowDecisionTrace trace)
    {
        if (!context.hasThreat || context.relativeThreatLane != 0) return;
        ShadowAction required = RequiredActionForObstacle(context.threatType);
        if (required == ShadowAction.Keep) return;

        if (originalPrediction == required && selected == required)
            PolicyCorrectDecisionCount++;
        else if (selected == required && trace != null && trace.safetyAdjusted)
            SafetyOverrideDecisionCount++;
    }

    private void PrepareSingleContractOpeningReplay()
    {
        ResetSingleContractOpeningReplay();
        if (!HasActiveOpponent || _usesTransientValidationIdentity
            || _frozenSingleContractIdentity == null || _ghost == null)
            return;

        EchoSignatureActionResult replay = EchoSignatureActionParser.FromJson(
            EchoRunSaveSystem.GetLastRunTelemetryJson(),
            _frozenSingleContractIdentity);
        if (replay == null || !replay.available)
            return;

        _singleContractOpeningReplay = replay;
        bool reducedMotion = EchoRunAccessibility.ReducedMotion;
        _singleContractOpeningReplayReducedMotion = reducedMotion;
        _displayedGhostLane = reducedMotion
            ? _ghostLane : Mathf.Clamp(replay.laneBeforeAction, 0, 2);
        _displayedGap = SingleContractOpeningReplayGapMeters;
        _laneSmoothVelocity = 0f;
        _gapSmoothVelocity = 0f;
        if (!reducedMotion)
            SetGhostRenderersVisible(false);
    }

    private void ResetSingleContractOpeningReplay()
    {
        _singleContractOpeningReplay = EchoSignatureActionResult.Unavailable;
        _singleContractOpeningReplayRevealed = false;
        _singleContractOpeningReplayActionStarted = false;
        _singleContractOpeningReplayActionFinished = false;
        _singleContractOpeningReplayCompleted = false;
        _singleContractOpeningReplayReducedMotion = false;
        SetGhostRenderersVisible(true);
        if (_ghostAnimator != null)
            _ghostAnimator.ClearMotionFeedback();
    }

    private bool TickSingleContractOpeningReplay()
    {
        if (!HasSingleContractOpeningReplay
            || _singleContractOpeningReplayCompleted)
            return false;

        if (!IsSingleContractOpeningMemory)
        {
            CompleteSingleContractOpeningReplay();
            return false;
        }

        float elapsed = GetSingleContractOpeningReplayElapsed();
        bool reducedMotion = ResolveSingleContractOpeningReplayReducedMotion();
        if (!_singleContractOpeningReplayRevealed
            && (reducedMotion
                || elapsed >= SingleContractOpeningReplayRevealSeconds))
        {
            _singleContractOpeningReplayRevealed = true;
            SetGhostRenderersVisible(true);
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayUIConfirm();
            if (!reducedMotion && ParticleManager.Instance != null
                && _ghost != null)
            {
                ParticleManager.Instance.EmitCoin(
                    _ghost.transform.position,
                    _ghost.transform.position + Vector3.up * 1.2f);
            }
        }

        float actionDuration = GetSingleContractOpeningReplayActionDuration();
        if (!_singleContractOpeningReplayActionStarted
            && elapsed >= SingleContractOpeningReplayActionSeconds)
        {
            float remaining = CalculateSingleContractOpeningReplayActionRemaining(
                elapsed, actionDuration);
            if (remaining > 0f)
                BeginSingleContractOpeningReplayAction(
                    reducedMotion, remaining);
            else
            {
                _singleContractOpeningReplayActionStarted = true;
                _singleContractOpeningReplayActionFinished = true;
            }
        }

        if (_singleContractOpeningReplayActionStarted
            && !_singleContractOpeningReplayActionFinished
            && elapsed >= SingleContractOpeningReplayActionSeconds
                          + actionDuration)
            FinishSingleContractOpeningReplayAction(reducedMotion);

        return true;
    }

    private bool ResolveSingleContractOpeningReplayReducedMotion()
    {
        if (_singleContractOpeningReplayReducedMotion)
            return true;
        if (!EchoRunAccessibility.ReducedMotion)
            return false;

        _singleContractOpeningReplayReducedMotion = true;
        SetGhostRenderersVisible(true);
        _ghostJumpTimer = 0f;
        _ghostSlideTimer = 0f;
        if (_singleContractOpeningReplayActionStarted)
            _singleContractOpeningReplayActionFinished = true;
        if (_ghostAnimator != null)
            _ghostAnimator.ClearMotionFeedback();
        return true;
    }

    private void BeginSingleContractOpeningReplayAction(bool reducedMotion,
        float remainingDuration)
    {
        _singleContractOpeningReplayActionStarted = true;
        if (reducedMotion)
        {
            _singleContractOpeningReplayActionFinished = true;
            return;
        }

        ShadowAction action = SingleContractOpeningReplayAction;
        if (action == ShadowAction.Jump || action == ShadowAction.Slide)
        {
            StartGhostAction(action);
            if (action == ShadowAction.Jump)
                _ghostJumpTimer = Mathf.Min(
                    _ghostJumpTimer, remainingDuration);
            else
                _ghostSlideTimer = Mathf.Min(
                    _ghostSlideTimer, remainingDuration);
        }

        AudioManager audio = AudioManager.Instance;
        if (action == ShadowAction.Jump)
        {
            if (audio != null) audio.PlayJump();
            if (!reducedMotion && ParticleManager.Instance != null
                && _ghost != null)
            {
                ParticleManager.Instance.EmitTakeoff(
                    _ghost.transform.position, _ghostForward);
            }
        }
        else if (action == ShadowAction.Slide && audio != null)
        {
            audio.PlaySlide();
        }
    }

    private void FinishSingleContractOpeningReplayAction(bool reducedMotion)
    {
        _singleContractOpeningReplayActionFinished = true;
        ShadowAction action = SingleContractOpeningReplayAction;
        AudioManager audio = AudioManager.Instance;
        if (action == ShadowAction.Jump)
        {
            if (audio != null) audio.PlayLand(0.65f);
            if (!reducedMotion && ParticleManager.Instance != null
                && _ghost != null)
            {
                ParticleManager.Instance.EmitLand(
                    _ghost.transform.position, _ghostForward, 0.65f);
            }
        }
        else if (action == ShadowAction.Slide && audio != null)
        {
            audio.PlaySlideExit();
        }
    }

    private void CompleteSingleContractOpeningReplay()
    {
        if (_singleContractOpeningReplayCompleted) return;
        _singleContractOpeningReplayCompleted = true;
        SetGhostRenderersVisible(true);
        _ghostJumpTimer = 0f;
        _ghostSlideTimer = 0f;
        _decisionTimer = 0f;
        _displayedGhostLane = _ghostLane;
        _displayedGap = Mathf.Clamp(_ghostProgress - _playerProgress,
            -2.5f, maximumVisibleLead);
        _laneSmoothVelocity = 0f;
        _gapSmoothVelocity = 0f;
        if (_ghostAnimator != null)
            _ghostAnimator.ClearMotionFeedback();
    }

    private float GetSingleContractOpeningReplayElapsed()
    {
        return _gameManager != null
            ? Mathf.Max(0f, _gameManager.RunElapsed)
            : Mathf.Max(0f, _runTime);
    }

    private float GetSingleContractOpeningReplayActionDuration()
    {
        switch (SingleContractOpeningReplayAction)
        {
            case ShadowAction.Jump:
                return GetGhostJumpDuration();
            case ShadowAction.Slide:
                return GetGhostSlideDuration();
            default:
                return 0.42f;
        }
    }

    public static float ResolveSingleContractOpeningReplayLane(
        ShadowAction action, int laneBeforeAction, float elapsed,
        int runtimeLane)
    {
        float sourceLane = Mathf.Clamp(laneBeforeAction, 0, 2);
        float targetLane = sourceLane;
        if (action == ShadowAction.Left)
            targetLane = Mathf.Max(0f, sourceLane - 1f);
        else if (action == ShadowAction.Right)
            targetLane = Mathf.Min(2f, sourceLane + 1f);

        float replayLane = sourceLane;
        if (action == ShadowAction.Left || action == ShadowAction.Right)
        {
            float action01 = Mathf.InverseLerp(
                SingleContractOpeningReplayActionSeconds,
                SingleContractOpeningReplayActionSeconds + 0.42f,
                elapsed);
            replayLane = Mathf.Lerp(sourceLane, targetLane,
                Mathf.SmoothStep(0f, 1f, action01));
        }

        float return01 = Mathf.InverseLerp(
            SingleContractOpeningReplayReturnSeconds,
            SingleContractOpeningReplaySettleSeconds, elapsed);
        return Mathf.Lerp(replayLane, Mathf.Clamp(runtimeLane, 0, 2),
            Mathf.SmoothStep(0f, 1f, return01));
    }

    public static float CalculateSingleContractOpeningReplayActionRemaining(
        float elapsed, float actionDuration)
    {
        return Mathf.Max(0f, Mathf.Max(0f, actionDuration)
                              - Mathf.Max(0f, elapsed
                                  - SingleContractOpeningReplayActionSeconds));
    }

    public static float ResolveSingleContractOpeningReplayGap(
        float elapsed, float runtimeGap)
    {
        float return01 = Mathf.InverseLerp(
            SingleContractOpeningReplayReturnSeconds,
            SingleContractOpeningReplaySettleSeconds, elapsed);
        return Mathf.Lerp(SingleContractOpeningReplayGapMeters, runtimeGap,
            Mathf.SmoothStep(0f, 1f, return01));
    }

    private bool StartGhostAction(ShadowAction action)
    {
        if (!CanStartVerticalAction(action, _ghostJumpTimer > 0f,
                _ghostSlideTimer > 0f, _ghostStumbleTimer > 0f))
            return false;

        if (action == ShadowAction.Jump)
            _ghostJumpTimer = GetGhostJumpDuration();
        else if (action == ShadowAction.Slide)
            _ghostSlideTimer = GetGhostSlideDuration();
        else return false;
        return true;
    }

    private ShadowAction PredictOpponentAction(float[] features,
        out float baseConfidence, out float sequenceConfidence,
        out float sequenceInfluence)
    {
        float[] probabilities = _opponentPolicy.GetProbabilities(features);
        int baseAction = _opponentPolicy.Predict(features);
        baseConfidence = probabilities[baseAction];
        if (_opponentSequencePolicy == null)
        {
            sequenceConfidence = 0f;
            sequenceInfluence = 0f;
            return (ShadowAction)baseAction;
        }

        int action = _opponentSequencePolicy.Predict(probabilities,
            _lastOpponentAction, out sequenceConfidence, out sequenceInfluence);
        return (ShadowAction)action;
    }

    private void RecordOpponentAction(ShadowAction action)
    {
        _lastOpponentAction = (int)action;
    }

    private void UpdateGhostPose()
    {
        if (_ghost == null || _player == null) return;

        float targetGap = Mathf.Clamp(_ghostProgress - _playerProgress,
            -2.5f, maximumVisibleLead);
        float previousDisplayedLane = _displayedGhostLane;
        bool openingReplay = IsSingleContractOpeningReplayActive;
        bool openingReplayMotion = openingReplay
                                   && !_singleContractOpeningReplayReducedMotion;
        if (openingReplayMotion)
        {
            float elapsed = GetSingleContractOpeningReplayElapsed();
            _displayedGap = ResolveSingleContractOpeningReplayGap(
                elapsed, targetGap);
            _displayedGhostLane = ResolveSingleContractOpeningReplayLane(
                SingleContractOpeningReplayAction,
                _singleContractOpeningReplay.laneBeforeAction,
                elapsed, _ghostLane);
            _gapSmoothVelocity = 0f;
            _laneSmoothVelocity = 0f;
        }
        else if (openingReplay)
        {
            _displayedGap = SingleContractOpeningReplayGapMeters;
            _displayedGhostLane = _ghostLane;
            _gapSmoothVelocity = 0f;
            _laneSmoothVelocity = 0f;
        }
        else
        {
            _displayedGap = Mathf.SmoothDamp(_displayedGap, targetGap,
                ref _gapSmoothVelocity, Mathf.Max(0.02f, distanceSmoothTime),
                80f, Time.deltaTime);
            _displayedGhostLane = Mathf.SmoothDamp(
                _displayedGhostLane, _ghostLane,
                ref _laneSmoothVelocity, Mathf.Max(0.02f, laneSmoothTime),
                12f, Time.deltaTime);
        }
        float unclampedLane = _displayedGhostLane;
        _displayedGhostLane = Mathf.Clamp(_displayedGhostLane, 0f, 2f);
        if (!Mathf.Approximately(unclampedLane, _displayedGhostLane))
            _laneSmoothVelocity = 0f;
        float jumpDuration = GetGhostJumpDuration();
        float jumpProgress = _ghostJumpTimer > 0f
            ? 1f - _ghostJumpTimer / jumpDuration
            : 0f;
        float jumpHeight = _ghostJumpTimer > 0f
            ? EvaluateJumpArc(jumpProgress) * _player.jumpHeight
            : 0f;

        Vector3 target;
        Vector3 targetForward;
        if (TrackManager.Instance != null)
        {
            TrackManager.Instance.GetTrackPoseAhead(
                _player.transform.position, _player.ForwardDirection,
                _player.RenderedLateralOffset,
                _displayedGhostLane, _displayedGap,
                out target, out targetForward);
        }
        else
        {
            targetForward = _player.ForwardDirection.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, targetForward).normalized;
            target = _player.transform.position
                     + targetForward * _displayedGap
                     + right * ((_displayedGhostLane - 1f)
                                * _player.laneDistance
                                - _player.RenderedLateralOffset);
        }

        if (TryGetGhostGroundHeight(target, out float groundHeight))
            // Authored run clips can extend the foot slightly below the bind-pose
            // bounds used by CacheGhostGroundOffset.
            _ghostGroundY = groundHeight + _ghostRootToLowestPoint + 0.04f;
        else if (!_player.IsJumping)
            _ghostGroundY = _player.transform.position.y;
        target.y = _ghostGroundY + jumpHeight;
        _ghostForward = targetForward;
        _ghost.transform.position = target;
        Quaternion targetRotation = Quaternion.LookRotation(targetForward, Vector3.up);
        float rotationBlend = 1f - Mathf.Exp(-18f * Time.deltaTime);
        _ghost.transform.rotation = Quaternion.Slerp(
            _ghost.transform.rotation, targetRotation, rotationBlend);

        if (_ghostVisual != null)
        {
            _ghostVisual.localPosition = _ghostVisualPosition;
            if (_ghostAnimator != null)
            {
                float animationSpeed = _gameManager != null
                    ? _gameManager.CurrentSpeed * shadowPaceMultiplier
                    : 10f;
                if (openingReplayMotion)
                {
                    float jump01 = _ghostJumpTimer > 0f
                        ? 1f - _ghostJumpTimer / GetGhostJumpDuration() : 0f;
                    float slide01 = _ghostSlideTimer > 0f
                        ? 1f - _ghostSlideTimer / GetGhostSlideDuration() : 0f;
                    float lateralVelocity =
                        (_displayedGhostLane - previousDisplayedLane)
                        * _player.laneDistance
                        / Mathf.Max(0.001f, Time.deltaTime);
                    _ghostAnimator.ApplyExternalMotion(
                        _ghostJumpTimer > 0f, _ghostSlideTimer > 0f,
                        _ghostForward, animationSpeed, Time.deltaTime,
                        jump01, slide01, lateralVelocity);
                }
                else
                {
                    _ghostAnimator.ApplyExternalMotion(
                        _ghostJumpTimer > 0f, _ghostSlideTimer > 0f,
                        _ghostForward, animationSpeed, Time.deltaTime);
                }
            }
        }

        if (_ghostMaterial != null)
        {
            bool reducedMotion = EchoRunAccessibility.ReducedMotion
                                 || (openingReplay
                                     && _singleContractOpeningReplayReducedMotion);
            bool stumbling = _ghostStumbleTimer > 0f;
            _ghostMaterial.color = ResolveGhostBodyColor(
                stumbling, reducedMotion, Time.time);
            if (_ghostMaterial.HasProperty("_RimColor"))
                _ghostMaterial.SetColor(
                    "_RimColor", ResolveGhostRimColor(stumbling));
            if (_ghostMaterial.HasProperty("_ScanStrength"))
                _ghostMaterial.SetFloat(
                    "_ScanStrength", reducedMotion ? 0f : 0.16f);
            if (_ghostMaterial.HasProperty("_GlitchStrength"))
                _ghostMaterial.SetFloat(
                    "_GlitchStrength", reducedMotion ? 0f : 0.014f);
        }
    }

    private void EvaluateGhostObstacle()
    {
        if (_ghost == null || TrackManager.Instance == null) return;
        if (TrackManager.Instance.IsInsideTurnTransition(
                _ghost.transform.position))
            return;
        if (!TrackManager.Instance.TryGetUpcomingObstacleInLane(
                _ghost.transform.position, _ghostForward, _ghostLane,
                _handledGhostObstacles, out float threatDistance,
                out ObstacleType threatType, out int obstacleId))
            return;
        if (threatDistance > 1.5f) return;

        _handledGhostObstacles.Add(obstacleId);

        bool avoided = CanAvoidObstacle(
            threatType, _ghostJumpTimer > 0f, _ghostSlideTimer > 0f);
        if (avoided) return;

        _ghostMistakes++;
        _ghostProgress = Mathf.Max(0f, _ghostProgress - 6f);
        _ghostStumbleTimer = 0.85f;
        _ghostRecoveryTimer = 10f;
        PlayerLead = CalculatePhysicalLead(_playerProgress, _ghostProgress);
    }

    private ShadowDecisionContext BuildDecisionContext(float[] features)
    {
        int obstacleType = Mathf.Clamp(
            Mathf.RoundToInt(features[5] * 3f) - 1, 0, 2);
        return new ShadowDecisionContext
        {
            lane = _ghostLane,
            threatProximity = Mathf.Clamp01(features[3]),
            relativeThreatLane = Mathf.RoundToInt(features[4] * 2f),
            threatType = (ObstacleType)obstacleType,
            hasThreat = features[3] > 0f,
            isJumping = _ghostJumpTimer > 0f,
            isSliding = _ghostSlideTimer > 0f,
            isStumbling = _ghostStumbleTimer > 0f,
            isRecovering = _ghostRecoveryTimer > 0f
        };
    }

    private ShadowAIDirective GetShadowDirective()
    {
        if (_directiveSource == null)
            _directiveSource = AITrackDirector.Instance;
        return _directiveSource != null
            ? _directiveSource.CurrentShadowDirective.Normalized()
            : ShadowAIDirective.Neutral;
    }

    private void TrackPlayerObstacleOpportunities()
    {
        TrackPlayerObstacleOpportunity(_jumpOpportunityTracker,
            ObstacleType.High, _player != null && _player.IsJumping,
            _player != null ? _player.jumpDuration : 0.9f);
        TrackPlayerObstacleOpportunity(_slideOpportunityTracker,
            ObstacleType.Low, _player != null && _player.IsSliding,
            _player != null ? _player.slideDuration : 0.8f);
    }

    private void TryRecordCounterattackActionResponse(ShadowAction action,
        int playerLane)
    {
        if ((action != ShadowAction.Jump && action != ShadowAction.Slide)
            || _contractEvaluator == null || _gameManager == null
            || _player == null || TrackManager.Instance == null)
            return;

        EchoChallengeStep step = _contractEvaluator.ActiveChallengeStep;
        if (!step.IsActive
            || (step.contractType != EchoContractType.ChangeVerticalHabit
                && step.contractType != EchoContractType.DisruptRhythm)
            || !TrackManager.Instance.TryGetUpcomingChallengeObstacle(
                _player.transform.position, _player.ForwardDirection,
                step.stepId, out float obstacleDistance))
            return;

        float actionDuration = action == ShadowAction.Jump
            ? _player.jumpDuration : _player.slideDuration;
        float responseDistance = CalculateReactionDistance(
            _gameManager.CurrentSpeed, Mathf.Max(0.2f, actionDuration)) * 1.25f;
        if (obstacleDistance > responseDistance)
            return;

        if (!_contractEvaluator.RecordEncounterInput(step.stepId, action,
                playerLane, _gameManager.Distance))
            return;

        AIRunTelemetry.RecordEvent("echo_challenge_input",
            (int)action, playerLane, step.stepId, obstacleDistance);
    }

    private void TrackPlayerObstacleOpportunity(
        ObstacleOpportunityTracker tracker, ObstacleType obstacleType,
        bool isUsingRequiredAction, float actionDuration)
    {
        if (_player == null || _gameManager == null
            || TrackManager.Instance == null)
            return;

        bool found = TrackManager.Instance.TryGetUpcomingObstacleInLane(
            _player.transform.position, _player.ForwardDirection,
            _player.CurrentLane, tracker.ResolvedIds,
            out float distance, out ObstacleType type, out int obstacleId);
        float detectionDistance = CalculateReactionDistance(
            _gameManager.CurrentSpeed,
            Mathf.Max(0.2f, actionDuration)) * 1.25f;
        if (tracker.Update(
                _player.CurrentLane, isUsingRequiredAction, found, distance,
                type, obstacleId, detectionDistance, out bool usedAction))
        {
            StyleTracker.RecordObstacleOpportunity(obstacleType, usedAction);
            _runIdentityDraft?.RecordStyleObstacleOpportunity(
                obstacleType, usedAction);
            if (tracker.LastResolvedByPass && usedAction)
            {
                PredictionGateObstacleBinding gateBinding =
                    TrackManager.Instance.GetPredictionGateBinding(
                        tracker.LastResolvedId);
                if (gateBinding.IsBound)
                {
                    RecordPredictionGateObstaclePassed(
                        gateBinding, tracker.LastResolvedId);
                }
                if (RecordDodge(obstacleType,
                        tracker.LastResolvedId,
                        tracker.LastResolvedLane,
                        TrackManager.Instance.GetChallengeBinding(
                            tracker.LastResolvedId)))
                {
                    AIPlayerSkillEstimator.RecordObstacleOutcome(
                        obstacleType, true);
                    AITrackDirector.Instance?.RecordDodge();
                    AudioManager.Instance?.PlayDodgeObstacle();
                }
            }
        }
    }

    private void ResolvePlayerObstacleOpportunities()
    {
        _jumpOpportunityTracker.Resolve(out _);
        if (_slideOpportunityTracker.Resolve(out bool usedSlide))
        {
            StyleTracker.RecordObstacleOpportunity(ObstacleType.Low, usedSlide);
            _runIdentityDraft?.RecordStyleObstacleOpportunity(
                ObstacleType.Low, usedSlide);
        }
    }

    private string BuildDuelStatus()
    {
        if (_ghostStumbleTimer > 0f)
            return "AI恢复窗口 · 回声撞击失速 · 立即完成反制";

        string lead = PlayerLead >= 0f
            ? "领先 " + PlayerLead.ToString("0.0") + "m"
            : "落后 " + Mathf.Abs(PlayerLead).ToString("0.0") + "m";
        string sequence = _sequenceInfluence > 0.01f
            ? " · 序列 " + (_sequenceInfluence * 100f).ToString("0") + "%"
            : "";
        string contract = _contractEvaluator != null
            ? _contractEvaluator.BuildHudText()
            : "回声契约未载入";
        string phase = _duelFlow != null
            ? EchoDuelFlow.PhaseName(_duelFlow.Phase) : "回声决斗";
        string encounter = _contractEvaluator != null
                           && !string.IsNullOrEmpty(
                               _contractEvaluator.Contract.encounterDebug)
            ? "\n" + _contractEvaluator.Contract.encounterDebug : "";
        return phase + " · " + contract + "\n第 " + _profile.generation
                + " 代 · 回声清晰度 " + (EchoClarity * 100f).ToString("0")
                + "% · " + lead
                + " · AI置信 " + (_decisionConfidence * 100f).ToString("0")
                + "%" + sequence + encounter;
    }

    public static bool IsContractVictory(float playerLead,
        bool challengedOpponent, bool contractCompleted,
        RunEndReason endReason)
    {
        return endReason == RunEndReason.FinishReached
               && challengedOpponent && contractCompleted && playerLead >= 0f;
    }

    public static bool IsSingleContractVictory(float playerLead,
        bool hasActiveOpponent, RunEndReason endReason)
    {
        return endReason == RunEndReason.FinishReached
               && hasActiveOpponent && playerLead >= 0f;
    }

    public static bool ShouldAdvanceGeneration(bool challengedOpponent,
        bool reachedFinish, bool playerWon, bool calibrationCompleted)
    {
        return reachedFinish
               && (challengedOpponent ? playerWon : calibrationCompleted);
    }

    public static float CalculatePhysicalLead(float playerRouteDistance,
        float ghostRouteDistance)
    {
        return Mathf.Max(0f, playerRouteDistance)
               - Mathf.Max(0f, ghostRouteDistance);
    }

    private void ApplyContractMotionDelta()
    {
        if (_contractEvaluator == null) return;
        EchoContractData contract = _contractEvaluator.Contract;
        float playerDelta = Mathf.Max(0f,
            contract.playerProgressBonus - _appliedContractPlayerBonus);
        float shadowDelta = Mathf.Max(0f,
            contract.shadowProgressBonus - _appliedContractShadowBonus);
        _appliedContractPlayerBonus = contract.playerProgressBonus;
        _appliedContractShadowBonus = contract.shadowProgressBonus;
        if (playerDelta <= 0f && shadowDelta <= 0f) return;

        _ghostProgress = Mathf.Max(0f,
            _ghostProgress + shadowDelta - playerDelta);
        PlayerLead = CalculatePhysicalLead(_playerProgress, _ghostProgress);
    }

    private void ConsumeSingleContractSettlements()
    {
        if (_singleContractFlow == null) return;
        while (_nextSingleContractSettlementIndex
               < _singleContractFlow.SettlementCount)
        {
            int settlementIndex = _nextSingleContractSettlementIndex++;
            if (!_singleContractFlow.TryConsumeSettlement(
                    settlementIndex,
                    out PredictionGateSettlement settlement))
                continue;

            PredictionGateController gate = FindSingleContractGate(
                settlement.gateId);
            GateAttempt attempt = gate != null
                ? gate.BuildAttempt() : null;
            if (attempt != null && attempt.committedLane >= 0)
            {
                _lastSingleContractGateAttempt = attempt;
                if (settlement.execution != GateExecutionOutcome.Cancelled)
                {
                    _runIdentityDraft?.RecordFormalGateChoice(
                        settlement.gateId, attempt.committedLane,
                        settlement.execution == GateExecutionOutcome.Success);
                }
            }

            if (_runAdaptationState != null)
            {
                _runAdaptationState.resolvedGateCount++;
                if (settlement.IsCounterSuccess)
                    _runAdaptationState.successfulCounterCount++;
                _runAdaptationState.consecutiveSuccessfulCounters =
                    settlement.IsCounterSuccess
                        ? _runAdaptationState
                              .consecutiveSuccessfulCounters + 1
                        : 0;
            }

            ApplyPredictionGateSettlement(settlement, gate);
            bool settlementTriggeredRelearn =
                _singleContractFlow.RelearnTriggerGateId > 0
                    ? settlement.gateId
                      == _singleContractFlow.RelearnTriggerGateId
                    : _singleContractFlow.LastRelearnResult.triggered;
            if (settlementTriggeredRelearn && _runAdaptationState != null
                && !_runAdaptationState.relearnUsed)
            {
                _runAdaptationState.relearnUsed = true;
                int gateCount = _singleContractFlow.GateCount;
                int nextGateNumber =
                    _singleContractFlow.RelearnStartGateNumber > 0
                        ? _singleContractFlow.RelearnStartGateNumber
                        : gate != null
                            ? gate.Definition.sequence + 1
                            : _runAdaptationState.resolvedGateCount + 1;
                _runAdaptationState.relearnStartGateNumber = gateCount > 0
                    ? Mathf.Clamp(nextGateNumber, 1, gateCount)
                    : Mathf.Max(1, nextGateNumber);
                _runAdaptationState.hypothesisVersion =
                    _singleContractFlow.HypothesisVersion;
                _runAdaptationState.predictedStrategy =
                    (int)StrategyKey.AvoidOriginal;
                _singleContractRelearnPulseTimer = 1.25f;
                // Keep this gate's choice/execution feedback and add its
                // adaptation consequence to the same message and sequence.
                _singleContractFeedbackRelearned = true;
                RecordSingleContractGateEvent(
                    AISingleContractEventType.EchoRelearned, gate,
                    settlement, PlayerLead, PlayerLead, true);
            }
            PredictionGateSettlementConsumed?.Invoke(settlement);
        }
    }

    private void ApplyPredictionGateSettlement(
        PredictionGateSettlement settlement,
        PredictionGateController gate)
    {
        float leadBefore = PlayerLead;
        if (HasActiveOpponent)
        {
            _appliedContractPlayerBonus +=
                Mathf.Max(0f, settlement.playerLeadMeters);
            _appliedContractShadowBonus +=
                Mathf.Max(0f, settlement.echoLeadMeters);
            float playerDistance = _gameManager != null
                ? _gameManager.Distance : _playerPhysicalProgress;
            _playerPhysicalProgress = Mathf.Max(0f, playerDistance);
            _playerProgress = _playerPhysicalProgress
                              + _appliedContractPlayerBonus;
            PlayerLead = CalculatePhysicalLead(_playerProgress,
                _ghostProgress + _appliedContractShadowBonus);
        }

        SetSingleContractFeedback(FeedbackForSingleContractSettlement(
            HasActiveOpponent, settlement), settlement.signedLeadMeters);

        RecordSingleContractGateEvent(
            AISingleContractEventType.GateApplied, gate, settlement,
            leadBefore, PlayerLead);
    }

    public static SingleContractInstantFeedback
        FeedbackForSingleContractSettlement(bool hasActiveOpponent,
            PredictionGateSettlement settlement)
    {
        if (settlement.execution == GateExecutionOutcome.Cancelled
            || settlement.execution == GateExecutionOutcome.None)
            return SingleContractInstantFeedback.None;
        if (!hasActiveOpponent)
        {
            return settlement.execution == GateExecutionOutcome.Success
                ? SingleContractInstantFeedback.SafePass
                : SingleContractInstantFeedback.None;
        }
        if (settlement.executionReason == GateExecutionReason.Unresolved)
            return SingleContractInstantFeedback.ObservationInconclusive;
        if (settlement.execution == GateExecutionOutcome.Hit)
            return settlement.chosenRole == PredictionGateRole.Counter
                ? SingleContractInstantFeedback.CounterFailed
                : SingleContractInstantFeedback.ExecutionIncomplete;
        if (settlement.chosenRole == PredictionGateRole.Counter)
            return SingleContractInstantFeedback.RewriteSucceeded;
        if (settlement.chosenRole == PredictionGateRole.Predicted)
            return SingleContractInstantFeedback.PredictionHit;
        return SingleContractInstantFeedback.SafePass;
    }

    private void CaptureSingleContractGateTelemetry()
    {
        if (_singleContractFlow == null) return;
        for (int index = 0; index < _singleContractFlow.GateCount; index++)
        {
            PredictionGateController gate = _singleContractFlow.GetGate(index);
            int gateId = gate.Definition.gateId;
            if (gate.State != PredictionGateLifecycle.Scheduled
                && _singleContractPresentedTelemetry.Add(gateId))
            {
                RecordSingleContractGateEvent(
                    AISingleContractEventType.GatePresented, gate);
            }
            if (gate.HasChoice
                && _singleContractCommittedTelemetry.Add(gateId))
            {
                RecordSingleContractGateEvent(
                    AISingleContractEventType.GateCommitted, gate);
            }
            if (gate.ExecutionOutcome != GateExecutionOutcome.None
                && _singleContractResolvedTelemetry.Add(gateId))
            {
                gate.TryGetSettlement(
                    out PredictionGateSettlement settlement);
                RecordSingleContractGateEvent(
                    AISingleContractEventType.GateResolved, gate,
                    settlement);
            }
        }
    }

    private PredictionGateController FindSingleContractGate(int gateId)
    {
        if (_singleContractFlow == null) return null;
        for (int index = 0; index < _singleContractFlow.GateCount; index++)
        {
            PredictionGateController gate = _singleContractFlow.GetGate(index);
            if (gate.Definition.gateId == gateId) return gate;
        }
        return null;
    }

    private void SetSingleContractFeedback(
        SingleContractInstantFeedback feedback,
        float leadDeltaMeters = 0f)
    {
        _singleContractFeedback = feedback;
        _singleContractFeedbackLeadDeltaMeters = leadDeltaMeters;
        _singleContractFeedbackRelearned = false;
        _singleContractFeedbackSequence++;
    }

    private string BuildSingleContractStatus()
    {
        string lead = PlayerLead >= 0f
            ? "玩家领先 " + PlayerLead.ToString("0.0") + "m"
            : "回声领先 " + Mathf.Abs(PlayerLead).ToString("0.0") + "m";
        return SingleContractMemoryText + " · " + lead;
    }

    private void RecordSingleContractEvent(string type)
    {
        AIRunTelemetry.RecordSingleContractEvent(
            new AISingleContractEventSample
            {
                type = type,
                generation = _frozenSingleContractIdentity != null
                    ? _frozenSingleContractIdentity.generation : 0
            });
    }

    private void RecordSingleContractIdentityEvent(string type,
        string oldIdentityId, string newIdentityId, string transactionId,
        string commitResult, string identityHashBefore,
        string identityHashAfter)
    {
        AIRunTelemetry.RecordSingleContractEvent(
            new AISingleContractEventSample
            {
                type = type,
                generation = _frozenSingleContractIdentity != null
                    ? _frozenSingleContractIdentity.generation : 0,
                oldIdentityId = oldIdentityId ?? "",
                newIdentityId = newIdentityId ?? "",
                transactionId = transactionId ?? "",
                commitResult = commitResult ?? "",
                identityHashBefore = identityHashBefore ?? "",
                identityHashAfter = identityHashAfter ?? ""
            });
    }

    private void RecordSingleContractGateEvent(string type,
        PredictionGateController gate,
        PredictionGateSettlement settlement = default,
        float leadBefore = 0f, float leadAfter = 0f,
        bool relearned = false)
    {
        if (gate == null)
        {
            RecordSingleContractEvent(type);
            return;
        }
        PredictionGateDefinition definition = gate.Definition;
        GateAttempt attempt = gate.BuildAttempt();
        int predictedLane = -1;
        if (definition.lanes != null)
        {
            for (int index = 0; index < definition.lanes.Length; index++)
            {
                if (definition.lanes[index].role
                    != PredictionGateRole.Predicted)
                    continue;
                predictedLane = definition.lanes[index].physicalLane;
                break;
            }
        }
        AIRunTelemetry.RecordSingleContractEvent(
            new AISingleContractEventSample
            {
                type = type,
                generation = _frozenSingleContractIdentity != null
                    ? _frozenSingleContractIdentity.generation : 0,
                gateId = definition.gateId,
                sequence = definition.sequence,
                hypothesisVersion = definition.hypothesisVersion,
                predictedLane = predictedLane,
                committedLane = attempt.committedLane,
                chosenRole = attempt.chosenRole,
                strategyKey = attempt.strategyKey,
                execution = attempt.execution,
                executionReason = attempt.executionReason,
                hasLateralEvidence = attempt.hasLateralEvidence,
                lateralOffset = attempt.lateralOffset,
                laneChangeInProgress = attempt.laneChangeInProgress,
                reactionTime = attempt.reactionTime,
                speedAtResolution = settlement.speedAtResolution,
                secondsDelta = settlement.signedLeadSeconds,
                metersDelta = settlement.signedLeadMeters,
                leadBefore = leadBefore,
                leadAfter = leadAfter,
                relearned = relearned
            });
    }

    public static bool CanAvoidObstacle(ObstacleType obstacleType,
        bool isJumping, bool isSliding)
    {
        return AIShadowRules.CanAvoidObstacle(
            obstacleType, isJumping, isSliding);
    }

    public static ShadowAction RequiredActionForObstacle(ObstacleType obstacleType)
    {
        return AIShadowRules.RequiredActionForObstacle(obstacleType);
    }

    public static bool CanStartVerticalAction(ShadowAction action,
        bool isJumping, bool isSliding, bool isStumbling)
    {
        return AIShadowRules.CanStartVerticalAction(
            action, isJumping, isSliding, isStumbling);
    }

    public static float CalculateReactionDistance(float speed, float actionDuration)
    {
        return AIShadowRules.CalculateReactionDistance(speed, actionDuration);
    }

    public static float EvaluateJumpArc(float normalizedProgress)
    {
        return AIShadowRules.EvaluateJumpArc(normalizedProgress);
    }

    public static float EvaluateSlideAmount(float remainingTime, float duration)
    {
        return AIShadowRules.EvaluateSlideAmount(remainingTime, duration);
    }

    public static float CalculatePhysicalPace(float physicalDistance,
        float elapsedTime)
    {
        return Mathf.Max(0f, physicalDistance) / Mathf.Max(1f, elapsedTime);
    }

    public static float CalculateSingleContractGhostPaceScale(
        float recordedPace, float sourceCourseDuration, float startSpeed,
        float maximumSpeed, float acceleration)
    {
        float duration = Mathf.Max(1f, sourceCourseDuration);
        float referencePace = EchoTimeRules.DistanceForAcceleratingRun(
                                  Mathf.Max(1f, startSpeed),
                                  Mathf.Max(startSpeed, maximumSpeed),
                                  Mathf.Max(0f, acceleration), duration)
                              / duration;
        return Mathf.Clamp(Mathf.Max(0f, recordedPace)
                           / Mathf.Max(1f, referencePace), 0.75f, 1.25f);
    }

    public static float CalculateSingleContractGhostSpeed(float startSpeed,
        float maximumSpeed, float acceleration, float elapsedTime,
        float learnedPaceScale)
    {
        float baseline = Mathf.Min(Mathf.Max(startSpeed, maximumSpeed),
            Mathf.Max(1f, startSpeed)
            + Mathf.Max(0f, acceleration) * Mathf.Max(0f, elapsedTime));
        return baseline * Mathf.Clamp(learnedPaceScale, 0.75f, 1.25f);
    }

    public static float BlendSingleContractNormalizedPace(float oldPace,
        float oldCourseDuration, float measuredPace,
        float measuredCourseDuration, float startSpeed,
        float maximumSpeed, float acceleration, float blendWeight)
    {
        float duration = Mathf.Max(1f, measuredCourseDuration);
        float referencePace = EchoTimeRules.DistanceForAcceleratingRun(
                                  Mathf.Max(1f, startSpeed),
                                  Mathf.Max(startSpeed, maximumSpeed),
                                  Mathf.Max(0f, acceleration), duration)
                              / duration;
        if (oldCourseDuration <= 0f)
            return Mathf.Max(0f, measuredPace);
        float oldScale = CalculateSingleContractGhostPaceScale(
            oldPace, oldCourseDuration, startSpeed, maximumSpeed,
            acceleration);
        float measuredScale = Mathf.Clamp(
            Mathf.Max(0f, measuredPace) / Mathf.Max(1f, referencePace),
            0.75f, 1.25f);
        return referencePace * Mathf.Lerp(oldScale, measuredScale,
            Mathf.Clamp01(blendWeight));
    }

    public static float CalculateActionTimingOffset(float obstacleDistance,
        float idealDistance)
    {
        return Mathf.Clamp(
            (Mathf.Max(0f, idealDistance) - Mathf.Max(0f, obstacleDistance))
            / Mathf.Max(1f, idealDistance), -1f, 1f);
    }

    private float GetGhostJumpDuration()
    {
        return Mathf.Max(0.2f, _player != null ? _player.jumpDuration : 0.6f);
    }

    private float GetGhostSlideDuration()
    {
        return Mathf.Max(0.2f, _player != null ? _player.slideDuration : 0.8f);
    }

    private bool HasTrainedProfile()
    {
        return _activeGeneration != null
               && _activeGeneration.generation > 0
               && _activeGeneration.pace > 0f
               && _activeGeneration.clarity >= 0.2f;
    }

    private bool IsSingleContractRuntime()
    {
        if (_activeGameplayFlowMode == GameplayFlowMode.SingleContract)
            return true;
        GameManager manager = _gameManager != null
            ? _gameManager : GameManager.Instance;
        if (manager == null) return false;
        return manager.State == GameState.Menu
            ? manager.ConfiguredGameplayFlowMode
              == GameplayFlowMode.SingleContract
            : manager.ActiveGameplayFlowMode
              == GameplayFlowMode.SingleContract;
    }

    private int GetDraftActionSampleCount(ShadowAction action)
    {
        int index = (int)action;
        return _runIdentityDraft != null
               && _runIdentityDraft.actionCounts != null
               && index >= 0
               && index < _runIdentityDraft.actionCounts.Length
            ? _runIdentityDraft.actionCounts[index] : 0;
    }

    private static int ResolveCurrentRunSequence()
    {
        AIRunTelemetryData activeRun = AIRunTelemetry.ActiveRun;
        if (activeRun != null && !string.IsNullOrEmpty(activeRun.runId))
        {
            int separator = activeRun.runId.LastIndexOf('-');
            if (separator >= 0 && separator + 1 < activeRun.runId.Length
                && int.TryParse(activeRun.runId.Substring(separator + 1),
                    out int sequence))
                return Mathf.Max(1, sequence);
        }
        EchoSingleContractSaveData archive =
            EchoRunSaveSystem.GetSingleContractSaveData();
        return Mathf.Max(1, archive.lastCommittedRunSequence + 1);
    }

    private void DiscardSingleContractRunState()
    {
        ResetSingleContractOpeningReplay();
        _runIdentityDraft?.Discard();
        _runIdentityDraft = null;
        _runAdaptationState = null;
        _frozenSingleContractIdentity = null;
        _singleContractFlow = null;
        _opponentPolicy = null;
        _opponentSequencePolicy = null;
        _usesTransientValidationIdentity = false;
        _persistentIdentityJsonBeforeValidation = "";
    }

    public static bool HasCalibrationSamples(int totalSamples, int activeSamples,
        int[] actionCounts, int minimumTotal, int minimumActive,
        int minimumCategories, int minimumJumpSamples = 0,
        int minimumSlideSamples = 0)
    {
        return AIShadowRules.HasCalibrationSamples(totalSamples, activeSamples,
            actionCounts, minimumTotal, minimumActive, minimumCategories,
            minimumJumpSamples, minimumSlideSamples);
    }

    public static float CalculateCalibrationProgress(int totalSamples,
        int activeSamples, int[] actionCounts, int minimumTotal,
        int minimumActive, int minimumCategories, int minimumJumpSamples = 0,
        int minimumSlideSamples = 0)
    {
        return AIShadowRules.CalculateCalibrationProgress(totalSamples,
            activeSamples, actionCounts, minimumTotal, minimumActive,
            minimumCategories, minimumJumpSamples, minimumSlideSamples);
    }

    public static int CountTrainedActionCategories(int[] actionCounts)
    {
        return AIShadowRules.CountTrainedActionCategories(actionCounts);
    }

    public static bool HasPartialEchoSamples(int totalSamples,
        int activeSamples, int[] actionCounts, float runTime,
        int minimumTotalSamples)
    {
        int minimumSeedSamples = Mathf.Max(4, minimumTotalSamples / 4);
        return runTime >= 8f
               && totalSamples >= minimumSeedSamples
               && activeSamples >= 1
               && CountTrainedActionCategories(actionCounts) >= 1;
    }

    private void CreateGhost()
    {
        if (_ghost != null)
        {
            _ghost.SetActive(true);
            return;
        }

        if (_player == null) _player = FindObjectOfType<PlayerController>();
        if (_player == null || _player.characterModel == null) return;

        _ghost = new GameObject("AI Shadow Runner");

        GameObject visual = Instantiate(_player.characterModel.gameObject, _ghost.transform);
        visual.name = "ShadowVisual";
        _ghostVisual = visual.transform;
        _ghostVisualPosition = _ghostVisual.localPosition;
        _ghostAnimator = visual.GetComponent<CharacterAnimator>();
        if (_ghostAnimator == null)
            _ghostAnimator = visual.GetComponentInChildren<CharacterAnimator>(true);
        if (_ghostAnimator != null) _ghostAnimator.SetExternalDriver();

        foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
            Destroy(collider);
        ApplyGhostMaterial(visual);

        _ghost.transform.position = _player.transform.position + Vector3.forward * 2f;
        CacheGhostGroundOffset();
    }

    private void CacheGhostGroundOffset()
    {
        if (_ghost == null) return;
        Renderer[] renderers = _ghost.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        _ghostRootToLowestPoint = Mathf.Max(0f,
            _ghost.transform.position.y - bounds.min.y);
    }

    private bool TryGetGhostGroundHeight(Vector3 target, out float groundHeight)
    {
        int groundMask = _player != null ? _player.groundLayer.value : 0;
        if (groundMask == 0) groundMask = Physics.DefaultRaycastLayers;
        Vector3 origin = new Vector3(target.x, target.y + 5f, target.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 10f,
                groundMask, QueryTriggerInteraction.Ignore))
        {
            groundHeight = hit.point.y;
            return true;
        }

        groundHeight = 0f;
        return false;
    }

    public static Color ResolveGhostBodyColor(
        bool stumbling, bool reducedMotion, float time)
    {
        if (stumbling)
            return new Color(0.11f, 0.022f, 0.030f, 0.18f);

        float alpha = reducedMotion
            ? 0.14f
            : 0.14f + Mathf.Sin(time * 2.2f) * 0.012f;
        return new Color(0.018f, 0.045f, 0.075f, alpha);
    }

    public static Color ResolveGhostRimColor(bool stumbling)
    {
        return stumbling
            ? new Color(0.95f, 0.20f, 0.18f, 1f)
            : new Color(0.18f, 0.72f, 0.92f, 1f);
    }

    private void ApplyGhostMaterial(GameObject visual)
    {
        Shader shader = Resources.Load<Shader>("Shaders/EchoGhost");
        if (shader == null) shader = Shader.Find("EchoRun/GhostRunner");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return;

        _ghostMaterial = new Material(shader)
        {
            color = ResolveGhostBodyColor(false, false, 0f),
            renderQueue = 3000
        };

        if (_ghostMaterial.HasProperty("_RimColor"))
            _ghostMaterial.SetColor("_RimColor", ResolveGhostRimColor(false));
        if (_ghostMaterial.HasProperty("_RimPower"))
            _ghostMaterial.SetFloat("_RimPower", 3.4f);
        if (_ghostMaterial.HasProperty("_EmissionStrength"))
            _ghostMaterial.SetFloat("_EmissionStrength", 0.38f);
        if (_ghostMaterial.HasProperty("_ScanStrength"))
            _ghostMaterial.SetFloat("_ScanStrength", 0.16f);
        if (_ghostMaterial.HasProperty("_GlitchStrength"))
            _ghostMaterial.SetFloat("_GlitchStrength", 0.014f);

        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            Material[] sourceMaterials = renderer.sharedMaterials;
            Material[] ghostMaterials = new Material[sourceMaterials.Length];
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            for (int slot = 0; slot < ghostMaterials.Length; slot++)
                ghostMaterials[slot] = _ghostMaterial;
            renderer.sharedMaterials = ghostMaterials;

            for (int slot = 0; slot < sourceMaterials.Length; slot++)
            {
                Material sourceMaterial = sourceMaterials[slot];
                if (sourceMaterial == null || !sourceMaterial.HasProperty("_MainTex")
                    || !_ghostMaterial.HasProperty("_MainTex"))
                    continue;

                Texture sourceTexture = sourceMaterial.GetTexture("_MainTex");
                if (sourceTexture == null) continue;
                block.Clear();
                renderer.GetPropertyBlock(block, slot);
                block.SetTexture("_MainTex", sourceTexture);
                renderer.SetPropertyBlock(block, slot);
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private void SetGhostActive(bool active)
    {
        if (_ghost != null) _ghost.SetActive(active);
    }

    private void SetGhostRenderersVisible(bool visible)
    {
        if (_ghost == null) return;
        if (!visible)
        {
            if (_singleContractOpeningReplayHiddenRenderers.Count > 0)
                return;
            Renderer[] renderers =
                _ghost.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                _singleContractOpeningReplayHiddenRenderers.Add(renderer);
                renderer.enabled = false;
            }
            return;
        }

        for (int i = 0;
             i < _singleContractOpeningReplayHiddenRenderers.Count; i++)
        {
            Renderer renderer =
                _singleContractOpeningReplayHiddenRenderers[i];
            if (renderer != null) renderer.enabled = true;
        }
        _singleContractOpeningReplayHiddenRenderers.Clear();
    }

    private void LoadProfile()
    {
        _profile = null;
        EchoRunSaveSystem.EnsureInitialized();
        string json = EchoRunSaveSystem.GetShadowProfileJson();
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                _profile = JsonUtility.FromJson<ShadowProfile>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("AI shadow profile could not be loaded: " + exception.Message);
            }
        }

        if (_profile == null) _profile = new ShadowProfile { version = 5 };
        NormalizeProfile();
        _activeGeneration = EchoGenerationSnapshot.FromJson(
            _profile.activeGenerationJson);
        if (_activeGeneration == null && _profile.generation > 0
            && _profile.pace > 0f)
        {
            PlayerStyleData legacyStyle = StyleTracker.GetSnapshot();
            legacyStyle.Normalize();
            _activeGeneration = new EchoGenerationSnapshot
            {
                generation = _profile.generation,
                policyWeights = _profile.weights,
                sequenceTransitions = _profile.sequenceTransitions,
                sequencePairCount = _profile.sequencePairCount,
                styleJson = JsonUtility.ToJson(legacyStyle),
                pace = _profile.pace,
                clarity = Mathf.Max(0.2f, _profile.clarity)
            };
            _profile.activeGenerationJson = _activeGeneration.ToJson();
        }
        if (_activeGeneration != null)
        {
            _profile.generation = _activeGeneration.generation;
            _profile.clarity = _activeGeneration.clarity;
        }
        _policy = new AIShadowPolicy(_profile.weights);
        _sequencePolicy = new AIShadowSequencePolicy(_profile.sequenceTransitions,
            _profile.sequencePairCount);
        _activeSingleContractIdentity =
            EchoRunSaveSystem.GetActiveEchoIdentity();
        _frozenSingleContractIdentity = null;
        _runIdentityDraft = null;
        _runAdaptationState = null;
    }

    private void SaveProfile()
    {
        if (_profile == null || _policy == null) return;
        _profile.weights = _policy.ExportWeights();
        if (_sequencePolicy != null)
        {
            AIShadowSequenceState state = _sequencePolicy.ExportState();
            _profile.sequenceTransitions = state.transitions;
            _profile.sequencePairCount = state.pairCount;
        }
        _profile.activeGenerationJson = _activeGeneration != null
            ? _activeGeneration.ToJson() : "";
        EchoRunSaveSystem.SaveShadowProfile(JsonUtility.ToJson(_profile));
    }

    private void NormalizeProfile()
    {
        if (_profile.version < 2)
        {
            if (_profile.generation > 0)
            {
                _profile.sampleCount = Mathf.Max(
                    _profile.sampleCount, minimumTrainingSamples);
                _profile.activeSampleCount = Mathf.Max(
                    _profile.activeSampleCount, minimumActiveTrainingSamples);
                _profile.actionCounts = new int[5];
                _profile.actionCounts[(int)ShadowAction.Left] = 1;
                _profile.actionCounts[(int)ShadowAction.Jump] = 1;
            }
        }

        if (_profile.version < 3)
        {
            // Older sequence data mixed passive Keep samples into action habits.
            _profile.sequenceTransitions = null;
            _profile.sequencePairCount = 0;
        }
        if (_profile.version < 4 && _profile.generation > 0)
            _profile.clarity = 1f;
        _profile.version = 5;
        _profile.clarity = Mathf.Clamp01(_profile.clarity);
        _profile.activeGenerationJson = _profile.activeGenerationJson ?? "";

        _profile.sampleCount = Mathf.Max(0, _profile.sampleCount);
        _profile.activeSampleCount = Mathf.Clamp(
            _profile.activeSampleCount, 0, _profile.sampleCount);
        EnsureActionCounts();
    }

    private void EnsureActionCounts()
    {
        if (_profile.actionCounts != null && _profile.actionCounts.Length == 5)
            return;

        int[] normalized = new int[5];
        if (_profile.actionCounts != null)
            Array.Copy(_profile.actionCounts, normalized,
                Mathf.Min(_profile.actionCounts.Length, normalized.Length));
        _profile.actionCounts = normalized;
    }

    void OnDestroy()
    {
        if (IsSingleContractRuntime())
            DiscardSingleContractRunState();
        else
            SaveProfile();
        if (_ghostMaterial != null) Destroy(_ghostMaterial);
        if (_gameManager != null)
            _gameManager.OnStateChanged.RemoveListener(OnGameStateChanged);
        if (Instance == this) Instance = null;
    }
}
