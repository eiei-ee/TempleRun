using UnityEngine;

public sealed class EchoHudPresenter : MonoBehaviour
{
    private EchoHudView _view;
    private GameManager _gameManager;
    private EchoHudMode _lastMode;
    private bool _hasMode;
    private float _announcementUntil;
    private float _feedbackUntil;
    private int _lastFeedbackSequence = -1;
    private float _singleContractFeedbackStartedAt;
    private string _singleContractFeedbackText = "";
    private Color _singleContractFeedbackColor;
    private int _singleContractFeedbackPriority;
    private bool _hasSingleContractFeedback;
    private bool _discardPendingSingleContractFeedback;
    private bool _presentingSingleContract;
    private bool _hasSingleContractVisualState;
    private SingleContractVisualState _lastSingleContractVisualState;
    private bool _lastSingleContractOpeningMemory;
    private bool _lastSingleContractOpeningReplay;

    public void Initialize(EchoHudView view, GameManager gameManager)
    {
        _view = view;
        _gameManager = gameManager;
        if (_view != null && _view.PauseButton != null)
        {
            _view.PauseButton.onClick.RemoveListener(Pause);
            _view.PauseButton.onClick.AddListener(Pause);
        }
    }

    public void Refresh(bool forceFeedback = false)
    {
        if (_view == null) return;
        if (_gameManager == null) _gameManager = GameManager.Instance;

        AIShadowRunner shadow = AIShadowRunner.Instance;
        string powerUpStatus = PowerUpController.Instance != null
            ? PowerUpController.Instance.GetStatusText() : "";
        if (IsSingleContractPresentation(shadow))
        {
            RefreshSingleContract(shadow, powerUpStatus);
            return;
        }

        ReleaseSingleContractVisualState();
        bool showBuff = !string.IsNullOrEmpty(powerUpStatus)
                        || _gameManager != null
                        && _gameManager.BuffTimeRemaining > 0f;
        string buffText = !string.IsNullOrEmpty(powerUpStatus)
            ? powerUpStatus
            : _gameManager != null && showBuff
                ? string.Format("{0} {1:F1}s", _gameManager.BuffName ?? "Buff",
                    _gameManager.BuffTimeRemaining)
                : "";
        string rewriteStyleSummary = shadow != null
                                     && shadow.DuelPhase
                                     == EchoDuelPhase.Rewrite
            ? shadow.RewriteStyleSummary : "";
        string finaleSegmentSummary = shadow != null
                                      && shadow.DuelPhase
                                      == EchoDuelPhase.Finale
            ? shadow.FinaleSegmentSummary : "";

        EchoHudViewData data = EchoRunPresentation.BuildHud(
            shadow != null && shadow.HasActiveOpponent,
            shadow != null ? shadow.ActiveContract : null,
            shadow != null ? shadow.PlayerLead : 0f,
            shadow != null ? shadow.minimumJumpSamples : 2,
            shadow != null ? shadow.minimumSlideSamples : 2,
            shadow != null ? shadow.JumpTrainingSampleCount : 0,
            shadow != null ? shadow.SlideTrainingSampleCount : 0,
            shadow != null ? shadow.CalibrationProgress : 0f,
            shadow != null ? shadow.DuelPhase : EchoDuelPhase.Calibration,
            shadow != null ? shadow.DuelPhaseProgress : 0f,
            shadow != null ? shadow.PublicPrediction : "",
            _gameManager != null ? _gameManager.SyncRemaining : 2,
            _gameManager != null
                ? _gameManager.CollisionRecoveryTimeRemaining : 0f,
            _gameManager != null
                ? _gameManager.CollisionRecoveryDuration : 1.25f,
            _gameManager != null ? _gameManager.RemainingDistance : 0f,
            _gameManager != null ? _gameManager.ContractMarkerCount : 0,
            showBuff, buffText,
            shadow != null && shadow.DuelTransitionPending,
            shadow != null ? shadow.PendingDuelPhase : EchoDuelPhase.None,
            rewriteStyleSummary, finaleSegmentSummary,
            shadow != null ? shadow.ActiveChallengeStep : default);

        if (!_hasMode || data.mode != _lastMode)
        {
            _lastMode = data.mode;
            _hasMode = true;
            _announcementUntil = Time.unscaledTime + 1f;
        }
        _view.Present(data, Time.unscaledTime < _announcementUntil);
        _view.SetStats(_gameManager != null ? _gameManager.Score : 0,
            _gameManager != null ? _gameManager.Distance : 0f);

        EchoDuelViewData duel = EchoRunPresentation.BuildDuel(
            shadow != null && shadow.HasActiveOpponent,
            shadow != null ? shadow.ActiveContract : null,
            shadow != null ? shadow.PlayerLead : 0f,
            shadow != null ? shadow.minimumJumpSamples : 2,
            shadow != null ? shadow.minimumSlideSamples : 2,
            shadow != null ? shadow.JumpTrainingSampleCount : 0,
            shadow != null ? shadow.SlideTrainingSampleCount : 0,
            shadow != null ? shadow.CalibrationProgress : 0f,
            shadow != null ? shadow.DuelPhase : EchoDuelPhase.Calibration,
            shadow != null ? shadow.DuelPhaseProgress : 0f,
            shadow != null ? shadow.PublicPrediction : "");
        if (!string.IsNullOrEmpty(duel.feedback)
            && (forceFeedback || duel.feedbackSequence != _lastFeedbackSequence))
        {
            _lastFeedbackSequence = duel.feedbackSequence;
            _feedbackUntil = Time.unscaledTime + 1.8f;
        }
        Color feedbackColor = duel.feedback.StartsWith("回声施压")
                              || duel.feedback.StartsWith("命中")
                              || duel.feedback.StartsWith("预判命中")
                              || duel.feedback.StartsWith("重锁")
            ? EchoRunUITheme.HudDangerText
            : duel.feedback.StartsWith("预测失效")
              || duel.feedback.StartsWith("偏离")
              || duel.feedback.StartsWith("裂解")
              || duel.feedback.StartsWith("反制生效")
              || duel.feedback.StartsWith("锁定碎裂")
                ? EchoRunUITheme.HudRewardText
                : EchoRunUITheme.HudSuccessText;
        _view.ShowFeedback(duel.feedback, feedbackColor,
            Time.unscaledTime < _feedbackUntil);
    }

    public static SingleContractHudData BuildSingleContractHudData(
        GameManager gameManager, AIShadowRunner shadow, string powerUpStatus)
    {
        string activePowerUp = (powerUpStatus ?? "").Trim();
        if (string.IsNullOrEmpty(activePowerUp)
            && gameManager != null && gameManager.BuffTimeRemaining > 0f)
        {
            activePowerUp = string.Format("{0} {1:F1}s",
                gameManager.BuffName ?? "Buff", gameManager.BuffTimeRemaining);
        }

        return EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = shadow != null
                    ? shadow.SingleContractVisualState
                    : SingleContractVisualState.Calibration,
                openingMemory = shadow != null
                                && shadow.IsSingleContractOpeningMemory,
                openingReplay = shadow != null
                                && shadow.HasSingleContractOpeningReplay,
                openingReplayAction = shadow != null
                    ? shadow.SingleContractOpeningReplayAction
                    : ShadowAction.Keep,
                openingReplayCount = shadow != null
                    ? shadow.SingleContractOpeningReplayCount : 0,
                generation = shadow != null ? shadow.Generation : 0,
                memory = shadow != null
                    ? shadow.SingleContractMemoryText
                    : "你的选择尚未形成稳定模式",
                showPrediction = shadow != null
                                 && shadow.ShowSingleContractPrediction,
                predictedLane = shadow != null
                    ? shadow.CurrentSingleContractPredictedLane : -1,
                predictionGateNumber = shadow != null
                    ? shadow.CurrentSingleContractPredictionGateNumber : 0,
                predictionGateCount = shadow != null
                    ? shadow.SingleContractPredictionGateCount : 0,
                predictionGateActive = shadow != null
                                       && shadow
                                           .IsCurrentSingleContractPredictionGateActive,
                leadMeters = shadow != null ? shadow.PlayerLead : 0f,
                injuries = gameManager != null
                    ? gameManager.CollisionStrikes : 0,
                maximumCollisionStrikes = gameManager != null
                    ? gameManager.MaximumCollisionStrikes : 2,
                collisionRecoveryTimeRemaining = gameManager != null
                    ? gameManager.CollisionRecoveryTimeRemaining : 0f,
                finishRemaining = gameManager != null
                    ? gameManager.RemainingDistance : 0f,
                powerUp = activePowerUp,
                instantFeedback = shadow != null
                    ? shadow.SingleContractFeedback
                    : SingleContractInstantFeedback.None,
                feedbackLeadDeltaMeters = shadow != null
                    ? shadow.SingleContractFeedbackLeadDeltaMeters : 0f,
                feedbackSequence = shadow != null
                    ? shadow.SingleContractFeedbackSequence : 0,
                feedbackRelearned = shadow != null
                                    && shadow.SingleContractFeedbackRelearned,
                calibrationProgress = shadow != null
                    ? shadow.CurrentSingleContractCalibrationProgress
                    : default,
                result = shadow != null ? shadow.LastResult : ""
            });
    }

    public void ReleaseSingleContractVisualState()
    {
        if (_view != null) _view.StopSingleContractTransition();
        EchoPhaseVisualController visual = EchoPhaseVisualController.Instance;
        if (visual != null && visual.UsesSingleContractVisualState)
            visual.ReleaseSingleContractVisualState();
        if (!_presentingSingleContract) return;

        ResetSingleContractFeedback();
        _presentingSingleContract = false;
        _hasSingleContractVisualState = false;
        _lastSingleContractOpeningMemory = false;
        _lastSingleContractOpeningReplay = false;
        _hasMode = false;
    }

    public void ResetRun()
    {
        if (_view != null) _view.StopSingleContractTransition();
        _hasMode = false;
        _lastFeedbackSequence = -1;
        _discardPendingSingleContractFeedback = false;
        ResetSingleContractFeedback();
        _feedbackUntil = 0f;
        _announcementUntil = 0f;
        _hasSingleContractVisualState = false;
        _lastSingleContractOpeningMemory = false;
        _lastSingleContractOpeningReplay = false;
    }

    private void RefreshSingleContract(AIShadowRunner shadow,
        string powerUpStatus)
    {
        SingleContractHudData data = BuildSingleContractHudData(
            _gameManager, shadow, powerUpStatus);
        bool enteringSingleContract = !_presentingSingleContract;
        bool hadPreviousState = !enteringSingleContract
                                && _hasSingleContractVisualState;
        SingleContractVisualState previousState =
            _lastSingleContractVisualState;
        bool openingChanged = !enteringSingleContract
                              && data.openingMemory
                              != _lastSingleContractOpeningMemory;
        bool endingOpeningReplay = openingChanged
                                   && !data.openingMemory
                                   && _lastSingleContractOpeningReplay;
        bool stateChanged = enteringSingleContract
                            || !_hasSingleContractVisualState
                            || data.visualState
                            != _lastSingleContractVisualState;
        bool emphasizeTransition =
            ShouldEmphasizeSingleContractTransition(
                hadPreviousState, previousState, data.visualState,
                data.openingMemory,
                openingChanged && !endingOpeningReplay,
                data.openingReplay);
        if (stateChanged || openingChanged)
        {
            _presentingSingleContract = true;
            _hasSingleContractVisualState = true;
            _lastSingleContractVisualState = data.visualState;
            _lastSingleContractOpeningMemory = data.openingMemory;
            _lastSingleContractOpeningReplay = data.openingReplay;
            _announcementUntil = emphasizeTransition
                ? Time.unscaledTime + 1f : 0f;
            if (enteringSingleContract) _lastFeedbackSequence = -1;
        }

        EchoPhaseVisualController visual = EchoPhaseVisualController.Instance;
        if (visual != null)
            visual.ApplySingleContractVisualState(data.visualState);

        _view.PresentSingleContract(data, data.openingMemory);
        if (emphasizeTransition && data.openingMemory)
            _view.PlaySingleContractTransition(data.visualState);
        _view.SetStats(_gameManager != null ? _gameManager.Score : 0,
            _gameManager != null ? _gameManager.Distance : 0f);

        PresentSingleContractFeedback(data, Time.unscaledTime);
    }

    // Explicit time keeps event lifetime independent of refresh frequency and
    // allows pause, replacement and expiry to be checked without a running race.
    public void PresentSingleContractFeedback(SingleContractHudData data, float now)
    {
        if (_view == null) return;
        if (_discardPendingSingleContractFeedback)
        {
            // A result can arrive after the final 10 Hz refresh before pause.
            // Discard the latest pending event at resume, including events
            // that were never displayed, rather than replaying stale copy.
            _lastFeedbackSequence = data.feedbackSequence;
            _discardPendingSingleContractFeedback = false;
            ResetSingleContractFeedback();
            return;
        }
        if (data.openingMemory)
        {
            _lastFeedbackSequence = data.feedbackSequence;
            ResetSingleContractFeedback();
            return;
        }
        bool currentVisible = _hasSingleContractFeedback
                              && now - _singleContractFeedbackStartedAt
                              < EchoRunPresentation.SingleContractFeedbackDurationSeconds;
        if (data.feedbackSequence != _lastFeedbackSequence)
        {
            // Consume each event even when a more important current message
            // suppresses it. There is no queue to replay stale feedback later.
            _lastFeedbackSequence = data.feedbackSequence;
            int priority = SingleContractFeedbackPriority(data);
            if (!string.IsNullOrEmpty(data.instantFeedback)
                && (!currentVisible || priority >= _singleContractFeedbackPriority))
            {
                _singleContractFeedbackStartedAt = now;
                _singleContractFeedbackText = data.instantFeedback;
                _singleContractFeedbackColor = SingleContractFeedbackColor(
                    data.instantFeedbackKind);
                _singleContractFeedbackPriority = priority;
                _hasSingleContractFeedback = true;
            }
        }
        RenderSingleContractFeedback(now);
    }

    private void RenderSingleContractFeedback(float now)
    {
        if (now - _singleContractFeedbackStartedAt
            >= EchoRunPresentation.SingleContractFeedbackDurationSeconds)
            _hasSingleContractFeedback = false;
        _view.ShowTimedFeedback(_singleContractFeedbackText,
            _singleContractFeedbackColor, now - _singleContractFeedbackStartedAt,
            _hasSingleContractFeedback, EchoRunAccessibility.ReducedMotion);
    }

    private void Update()
    {
        // Model data refreshes at 10 Hz; the opacity envelope must still run
        // every rendered frame so its short fade does not visibly step.
        if (_hasSingleContractFeedback && _view != null)
            RenderSingleContractFeedback(Time.unscaledTime);
    }

    private static int SingleContractFeedbackPriority(SingleContractHudData data)
    {
        if (data.feedbackRelearned
            || data.instantFeedbackKind == SingleContractInstantFeedback.EchoRelearned)
            return 3;
        if (data.instantFeedbackKind == SingleContractInstantFeedback.ExecutionIncomplete
            || data.instantFeedbackKind == SingleContractInstantFeedback.CounterFailed)
            return 2;
        return 1;
    }

    private void ResetSingleContractFeedback()
    {
        _hasSingleContractFeedback = false;
        _singleContractFeedbackText = "";
        _singleContractFeedbackStartedAt = 0f;
        _singleContractFeedbackPriority = 0;
        if (_view != null) _view.ResetFeedbackPresentation();
    }

    private void OnDisable()
    {
        SuspendSingleContractFeedback();
        if (_view != null) _view.StopSingleContractTransition();
    }

    public void SuspendSingleContractFeedback()
    {
        _discardPendingSingleContractFeedback = true;
        ResetSingleContractFeedback();
    }

    public static bool ShouldEmphasizeSingleContractTransition(
        bool hasPreviousState, SingleContractVisualState previousState,
        SingleContractVisualState currentState, bool openingMemory,
        bool openingChanged, bool openingReplay = false)
    {
        if (openingMemory)
            return openingReplay && (!hasPreviousState || openingChanged);
        if (hasPreviousState
            && previousState == SingleContractVisualState.RelearnPulse
            && currentState == SingleContractVisualState.Challenge)
            return false;
        return !hasPreviousState || previousState != currentState
               || openingChanged;
    }

    public static bool ShouldEmphasizeSingleContractPredictionChange(
        bool hasPreviousPrediction, string previousPredictionKey,
        int previousGateNumber, SingleContractHudData current,
        bool stageTransitionEmphasized, bool returningFromRelearn)
    {
        if (current.openingMemory || stageTransitionEmphasized
            || returningFromRelearn || !hasPreviousPrediction)
            return false;

        string currentKey = SingleContractPredictionSemanticKey(current);
        if (string.IsNullOrEmpty(currentKey)) return false;
        return previousGateNumber != current.predictionGateNumber
               || !string.Equals(previousPredictionKey, currentKey);
    }

    public static string SingleContractPredictionSemanticKey(
        SingleContractHudData data)
    {
        string value = (data.prediction ?? "").Trim();
        if (string.IsNullOrEmpty(value)) return "";
        const string playerToken = "它猜";
        const string legacyToken = "预测：";
        int start = value.IndexOf(playerToken);
        int tokenLength = playerToken.Length;
        if (start < 0)
        {
            start = value.IndexOf(legacyToken);
            tokenLength = legacyToken.Length;
        }
        if (start < 0) return value;
        start += tokenLength;
        int lineEnd = value.IndexOf('\n', start);
        if (lineEnd < 0) lineEnd = value.Length;
        return value.Substring(start, lineEnd - start).Trim();
    }

    private bool IsSingleContractPresentation(AIShadowRunner shadow)
    {
        if (_gameManager != null) return _gameManager.IsSingleContractRun;
        return shadow != null && shadow.ActiveGameplayFlowMode
            == GameplayFlowMode.SingleContract;
    }

    private static Color SingleContractFeedbackColor(
        SingleContractInstantFeedback feedback)
    {
        switch (feedback)
        {
            case SingleContractInstantFeedback.PredictionHit:
            case SingleContractInstantFeedback.CounterFailed:
            case SingleContractInstantFeedback.ExecutionIncomplete:
            case SingleContractInstantFeedback.EchoRelearned:
                return EchoRunUITheme.HudDangerText;
            case SingleContractInstantFeedback.ObservationInconclusive:
                return EchoRunUITheme.HudInkMuted;
            case SingleContractInstantFeedback.RewriteSucceeded:
                return EchoRunUITheme.HudRewardText;
            default:
                return EchoRunUITheme.HudSuccessText;
        }
    }

    private void Pause()
    {
        if (_gameManager != null) _gameManager.Pause();
    }

    private void OnDestroy()
    {
        ReleaseSingleContractVisualState();
        if (_view != null && _view.PauseButton != null)
            _view.PauseButton.onClick.RemoveListener(Pause);
    }
}
