using UnityEngine;
using UnityEngine.UI;

public sealed class EchoHudView : MonoBehaviour
{
    [Header("Layers")]
    [SerializeField] private GameObject staticLayer;
    [SerializeField] private GameObject dynamicLayer;

    [Header("Primary")]
    [SerializeField] private Text statsText;
    [SerializeField] private Text announcementText;
    [SerializeField] private Text directiveText;
    [SerializeField] private Text predictionText;
    [SerializeField] private Text calibrationObservationText;
    [SerializeField] private Text distanceText;
    [SerializeField] private GameObject stageRail;
    [SerializeField] private Text[] stageNodes;
    [SerializeField] private GameObject calibrationRail;

    [Header("Meter")]
    [SerializeField] private GameObject meterGroup;
    [SerializeField] private Text meterLabel;
    [SerializeField] private Image meterFill;

    [Header("Lead")]
    [SerializeField] private GameObject leadGroup;
    [SerializeField] private Text leadText;
    [SerializeField] private RectTransform leadMarker;

    [Header("Sync")]
    [SerializeField] private Image[] syncCells;
    [SerializeField] private Text recoveryText;

    [Header("Edges")]
    [SerializeField] private GameObject markerGroup;
    [SerializeField] private Text markerText;
    [SerializeField] private GameObject buffGroup;
    [SerializeField] private Text buffText;
    [SerializeField] private Text feedbackText;
    [SerializeField] private CanvasGroup feedbackGroup;
    [SerializeField] private Button pauseButton;

    [Header("Cold White Fortress Skin")]
    [SerializeField] private Image[] skinPanels;
    [SerializeField] private Image[] skinRules;
    [SerializeField] private Image[] phaseAccentRules;
    [SerializeField] private GameObject announcementPlate;
    [SerializeField] private GameObject directivePlate;
    [SerializeField] private GameObject predictionPlate;
    [SerializeField] private GameObject feedbackPlate;
    [SerializeField] private GameObject stateAccentBar;

    [Header("State Transition")]
    [SerializeField] private CanvasGroup stateTransitionFx;
    [SerializeField] private RectTransform transitionScanLine;
    [SerializeField] private RectTransform fractureSliceA;
    [SerializeField] private RectTransform fractureSliceB;

    private static readonly Color Coral = EchoRunUITheme.HudDangerText;
    private static readonly Color Gold = EchoRunUITheme.HudRewardText;
    private static readonly Color Muted = EchoRunUITheme.HudInkMuted;
    private static readonly Color EmptyCell = EchoRunUITheme.HudRule;

    private Color _phaseAccent = EchoRunUITheme.HudCalibrationAccent;
    private EchoHudTransitionKind _transitionKind;
    private float _transitionElapsed;
    private float _transitionDuration;
    private bool _transitionActive;
    private bool _transitionBasesCached;
    private Vector2 _scanLineBase;
    private Vector2 _fractureSliceABase;
    private Vector2 _fractureSliceBBase;
    private bool _layoutInitialized;
    private bool _compactLayout;

    public Button PauseButton => pauseButton;

    public EchoHudTransitionKind ActiveTransitionKind => _transitionActive
        ? _transitionKind : EchoHudTransitionKind.None;

    public void Present(EchoHudViewData data, bool showAnnouncement)
    {
        ApplyModeLayout(false);
        SetActiveIfChanged(stateAccentBar, true);
        ApplySkin(EchoRunUITheme.HudSkinFor(data.mode));
        bool calibrating = data.mode == EchoHudMode.Calibration;
        SetActiveIfChanged(stageRail, !calibrating);
        SetActiveIfChanged(calibrationRail, calibrating);
        SetActiveIfChanged(leadGroup, !calibrating);

        SetTextIfChanged(announcementText, data.announcement);
        SetActiveIfChanged(announcementText != null
            ? announcementText.gameObject : null, showAnnouncement);
        SetActiveIfChanged(announcementPlate, showAnnouncement);
        SetTextIfChanged(directiveText, data.directiveShort);
        bool showDirective = !string.IsNullOrEmpty(data.directiveShort);
        SetActiveIfChanged(directiveText != null
            ? directiveText.gameObject : null, showDirective);
        SetActiveIfChanged(directivePlate, showDirective);
        SetTextIfChanged(predictionText, data.predictionShort);
        bool showPrediction = data.showPrediction
                              && !string.IsNullOrEmpty(data.predictionShort);
        SetActiveIfChanged(predictionText != null
            ? predictionText.gameObject : null, showPrediction);
        SetActiveIfChanged(predictionPlate, showPrediction);

        SetTextIfChanged(calibrationObservationText,
            calibrating ? "路线  记录中    节奏  采集中" : "");
        SetActiveIfChanged(calibrationObservationText != null
            ? calibrationObservationText.gameObject : null, calibrating);
        SetTextIfChanged(distanceText,
            data.remainingDistance > 0f
                ? "终点 " + Mathf.CeilToInt(data.remainingDistance) + "m"
                : "终点已定位");

        PresentStage(data.phaseIndex);
        PresentMeter(data);
        PresentLead(data);
        PresentSync(data);

        SetActiveIfChanged(markerGroup, data.showContractMarkers);
        SetTextIfChanged(markerText, "契约标记 " + data.contractMarkerCount);

        bool showBuff = data.showBuff && !string.IsNullOrEmpty(data.buffText);
        SetActiveIfChanged(buffGroup, showBuff);
        SetTextIfChanged(buffText, data.buffText);
    }

    public void PresentSingleContract(SingleContractHudData data,
        bool showAnnouncement)
    {
        ApplyModeLayout(true);
        SetActiveIfChanged(stateAccentBar, data.openingMemory);
        ApplySingleContractSkin(data.visualState, data.openingMemory);
        // Keep the persistent run shell visible from the first gameplay frame.
        // Opening memory is a foreground message, not a loading state.
        SetActiveIfChanged(staticLayer, true);
        SetActiveIfChanged(dynamicLayer, true);
        SetActiveIfChanged(stageRail, false);
        SetActiveIfChanged(calibrationRail, true);
        bool showCalibrationProgress = data.showCalibrationProgress
                                       && data.visualState
                                       == SingleContractVisualState.Calibration
                                       && !data.openingMemory;
        SetActiveIfChanged(meterGroup, showCalibrationProgress);
        if (showCalibrationProgress)
            PresentSingleContractCalibrationMeter(data);
        SetActiveIfChanged(markerGroup, false);
        SetActiveIfChanged(pauseButton != null
            ? pauseButton.gameObject : null, true);

        SetTextIfChanged(directiveText, data.openingMemory ? data.memory : "");
        SetActiveIfChanged(directiveText != null
            ? directiveText.gameObject : null, data.openingMemory);
        SetActiveIfChanged(directivePlate, data.openingMemory);
        if (data.openingMemory)
        {
            bool showOpeningTitle = !string.IsNullOrEmpty(data.openingTitle);
            SetTextIfChanged(announcementText, data.openingTitle);
            SetActiveIfChanged(announcementText != null
                ? announcementText.gameObject : null, showOpeningTitle);
            SetActiveIfChanged(announcementPlate, showOpeningTitle);
            SetTextIfChanged(predictionText, "");
            SetActiveIfChanged(predictionText != null
                ? predictionText.gameObject : null, false);
            SetActiveIfChanged(predictionPlate, false);
            SetTextIfChanged(calibrationObservationText, data.injuriesText);
            SetActiveIfChanged(calibrationObservationText != null
                ? calibrationObservationText.gameObject : null, true);
            SetTextIfChanged(distanceText, data.finishRemainingText);
            SetActiveIfChanged(distanceText != null
                ? distanceText.gameObject : null, true);
            PresentSingleContractLead(data);
            SetSyncCellsVisible(false);
            SetActiveIfChanged(buffGroup, false);
            ResetFeedbackPresentation();
            return;
        }

        // The opening owns the announcement slot. During the race all event
        // copy goes through the single timed feedback slot below.
        SetTextIfChanged(announcementText, "");
        SetActiveIfChanged(announcementText != null
            ? announcementText.gameObject : null, false);
        SetActiveIfChanged(announcementPlate, false);
        string prediction = !showCalibrationProgress && data.predictionGateActive
            ? data.prediction : "";
        SetTextIfChanged(predictionText, prediction);
        SetColorIfChanged(predictionText, showCalibrationProgress
            ? _phaseAccent : EchoRunUITheme.HudDangerText);
        bool showPrediction = !string.IsNullOrEmpty(prediction);
        SetActiveIfChanged(predictionText != null
            ? predictionText.gameObject : null, showPrediction);
        SetActiveIfChanged(predictionPlate, showPrediction);

        SetTextIfChanged(calibrationObservationText,
            data.injuriesText);
        SetActiveIfChanged(calibrationObservationText != null
            ? calibrationObservationText.gameObject : null, true);
        SetTextIfChanged(distanceText, data.finishRemainingText);
        SetActiveIfChanged(distanceText != null
            ? distanceText.gameObject : null, true);

        PresentSingleContractLead(data);
        SetSyncCellsVisible(false);
        SetTextIfChanged(recoveryText, "");
        SetActiveIfChanged(recoveryText != null
            ? recoveryText.gameObject : null, false);

        bool showBuff = data.showPowerUp
                        && !string.IsNullOrEmpty(data.powerUp);
        SetActiveIfChanged(buffGroup, showBuff);
        SetTextIfChanged(buffText, data.powerUp);
    }

    public void SetStats(int score, float distance)
    {
        SetTextIfChanged(statsText, _compactLayout
            ? "分数 " + Mathf.Max(0, score) : string.Format(
            "SCORE {0:D5}   RANGE {1:000}m", Mathf.Max(0, score),
            Mathf.Max(0, Mathf.FloorToInt(distance))));
    }

    private void ApplyModeLayout(bool compact)
    {
        if (_layoutInitialized && _compactLayout == compact) return;
        _layoutInitialized = true;
        _compactLayout = compact;
        if (meterGroup != null)
            SetLayout(meterGroup.GetComponent<RectTransform>(),
                compact ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.855f),
                compact ? new Vector2(332f, 40f) : new Vector2(520f, 34f),
                compact ? new Vector2(26f, -22f) : Vector2.zero,
                compact ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f));
        if (predictionText != null)
            SetLayout(predictionText.rectTransform, new Vector2(0f, 1f),
                compact ? new Vector2(312f, 38f) : new Vector2(420f, 66f),
                new Vector2(30f, compact ? -176f : -255f), new Vector2(0f, 1f));
        if (predictionPlate != null)
            SetLayout(predictionPlate.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                compact ? new Vector2(330f, 40f) : new Vector2(450f, 68f),
                new Vector2(22f, compact ? -175f : -254f), new Vector2(0f, 1f));
        if (feedbackText != null)
            SetLayout(feedbackText.rectTransform, new Vector2(0f, 1f),
                new Vector2(540f, 40f),
                new Vector2(30f, compact ? -223f : -333f), new Vector2(0f, 1f));
        if (feedbackPlate != null)
            SetLayout(feedbackPlate.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(560f, 42f),
                new Vector2(22f, compact ? -222f : -332f), new Vector2(0f, 1f));
    }

    private static void SetLayout(RectTransform rect, Vector2 anchor,
        Vector2 size, Vector2 position, Vector2 pivot)
    {
        if (rect == null) return;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    public void ShowFeedback(string text, Color color, bool visible)
    {
        bool show = visible && !string.IsNullOrEmpty(text);
        PresentFeedback(text, color, show, show ? 1f : 0f);
    }

    private void PresentFeedback(string text, Color color, bool show, float alpha)
    {
        SetTextIfChanged(feedbackText, text);
        if (feedbackText != null && feedbackText.color != color)
            feedbackText.color = color;
        SetFeedbackGroup(show, alpha);
        SetActiveIfChanged(feedbackText != null
            ? feedbackText.gameObject : null, show);
        SetActiveIfChanged(feedbackPlate, show);
    }

    public void ShowTimedFeedback(string text, Color color, float elapsed,
        bool visible, bool reducedMotion)
    {
        bool show = visible && !string.IsNullOrEmpty(text)
                    && elapsed >= 0f
                    && elapsed < EchoRunPresentation.SingleContractFeedbackDurationSeconds;
        PresentFeedback(text, color, show,
            show ? FeedbackAlpha(elapsed, reducedMotion) : 0f);
    }

    public static float FeedbackAlpha(float elapsed, bool reducedMotion)
    {
        if (elapsed < 0f
            || elapsed >= EchoRunPresentation.SingleContractFeedbackDurationSeconds)
            return 0f;
        if (reducedMotion) return 1f;
        float fadeIn = EchoRunPresentation.SingleContractFeedbackFadeInSeconds;
        float fadeOutStart = fadeIn
                             + EchoRunPresentation.SingleContractFeedbackHoldSeconds;
        if (elapsed < fadeIn) return Mathf.Clamp01(elapsed / fadeIn);
        if (elapsed <= fadeOutStart) return 1f;
        return Mathf.Clamp01((EchoRunPresentation.SingleContractFeedbackDurationSeconds
                              - elapsed)
                             / EchoRunPresentation.SingleContractFeedbackFadeOutSeconds);
    }

    public void ResetFeedbackPresentation()
    {
        ShowFeedback("", Color.white, false);
    }

    private void SetFeedbackGroup(bool visible, float alpha)
    {
        if (feedbackGroup == null) return;
        feedbackGroup.alpha = alpha;
        feedbackGroup.interactable = false;
        feedbackGroup.blocksRaycasts = false;
        SetActiveIfChanged(feedbackGroup.gameObject, visible);
    }

    private void OnDisable()
    {
        ResetFeedbackPresentation();
        StopSingleContractTransition();
    }

    public void ApplySingleContractSkin(SingleContractVisualState state,
        bool openingMemory = false)
    {
        // Opening memory is part of the persistent shell, so it receives the
        // neutral foundation without starting a second visual state.
        EchoHudSkin skin = EchoRunUITheme.HudSkinFor(state);
        if (openingMemory)
            skin.accent = Color.Lerp(skin.rule, skin.accent, 0.45f);
        ApplySkin(skin);
    }

    public void PlaySingleContractTransition(SingleContractVisualState state)
    {
        EchoHudSkin skin = EchoRunUITheme.HudSkinFor(state);
        ApplySkin(skin);
        StartTransition(skin.transition);
    }

    public void PlayPredictionChangeTransition()
    {
        // Prediction changes fracture the current HUD accent; they are not a
        // phase change and therefore must not apply the red relearn skin.
        StartTransition(EchoHudTransitionKind.Fracture);
    }

    private void StartTransition(EchoHudTransitionKind kind)
    {
        if (stateTransitionFx == null || EchoRunAccessibility.ReducedMotion
            || kind == EchoHudTransitionKind.None)
        {
            StopSingleContractTransition();
            return;
        }

        CacheTransitionBases();
        _transitionKind = kind;
        _transitionDuration = TransitionDuration(_transitionKind);
        _transitionElapsed = 0f;
        _transitionActive = true;
        stateTransitionFx.alpha = 0f;
        stateTransitionFx.blocksRaycasts = false;
        stateTransitionFx.interactable = false;
        SetActiveIfChanged(stateTransitionFx.gameObject, true);
        SetTransitionPieceVisibility(_transitionKind);
    }

    public void StopSingleContractTransition()
    {
        _transitionActive = false;
        _transitionKind = EchoHudTransitionKind.None;
        _transitionElapsed = 0f;
        ResetTransitionGeometry();
        SetTransitionPieceVisibility(EchoHudTransitionKind.None);
        if (stateTransitionFx == null) return;
        stateTransitionFx.alpha = 0f;
        stateTransitionFx.blocksRaycasts = false;
        stateTransitionFx.interactable = false;
        SetActiveIfChanged(stateTransitionFx.gameObject, false);
    }

    private void SetTransitionPieceVisibility(EchoHudTransitionKind kind)
    {
        bool fracture = kind == EchoHudTransitionKind.Fracture;
        bool scan = kind == EchoHudTransitionKind.Scan
                    || kind == EchoHudTransitionKind.Activate
                    || kind == EchoHudTransitionKind.Release;
        SetActiveIfChanged(transitionScanLine != null
            ? transitionScanLine.gameObject : null, scan);
        SetActiveIfChanged(fractureSliceA != null
            ? fractureSliceA.gameObject : null, fracture);
        SetActiveIfChanged(fractureSliceB != null
            ? fractureSliceB.gameObject : null, fracture);
    }

    private void Update()
    {
        if (!_transitionActive || stateTransitionFx == null) return;
        if (EchoRunAccessibility.ReducedMotion)
        {
            StopSingleContractTransition();
            return;
        }

        _transitionElapsed += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.01f, _transitionDuration);
        float t = Mathf.Clamp01(_transitionElapsed / duration);
        float pulse = Mathf.Sin(t * Mathf.PI);
        stateTransitionFx.alpha = pulse * TransitionAlpha(_transitionKind);
        AnimateTransitionGeometry(_transitionKind, t, pulse);
        if (t >= 1f) StopSingleContractTransition();
    }

    private void ApplySkin(EchoHudSkin skin)
    {
        _phaseAccent = skin.accent;
        SetColors(skinPanels, skin.panel, false);
        SetColors(skinRules, skin.rule, false);
        SetColors(phaseAccentRules, skin.accent, true);

        SetColorIfChanged(statsText, _compactLayout ? skin.mutedInk : skin.ink);
        SetColorIfChanged(announcementText, skin.ink);
        SetColorIfChanged(directiveText, skin.ink);
        SetColorIfChanged(predictionText, EchoRunUITheme.HudDangerText);
        SetColorIfChanged(calibrationObservationText, skin.mutedInk);
        SetColorIfChanged(distanceText, skin.mutedInk);
        SetColorIfChanged(meterLabel, skin.mutedInk);
        SetColorIfChanged(recoveryText, EchoRunUITheme.HudDangerText);
        SetColorIfChanged(markerText, EchoRunUITheme.HudDangerText);
        SetColorIfChanged(buffText, skin.ink);

        if (pauseButton != null)
        {
            Graphic target = pauseButton.targetGraphic;
            if (target != null && target.color != skin.panelRaised)
                target.color = skin.panelRaised;
            Text label = pauseButton.GetComponentInChildren<Text>(true);
            SetColorIfChanged(label, skin.ink);
        }
    }

    private void CacheTransitionBases()
    {
        if (_transitionBasesCached) return;
        if (transitionScanLine != null)
            _scanLineBase = transitionScanLine.anchoredPosition;
        if (fractureSliceA != null)
            _fractureSliceABase = fractureSliceA.anchoredPosition;
        if (fractureSliceB != null)
            _fractureSliceBBase = fractureSliceB.anchoredPosition;
        _transitionBasesCached = true;
    }

    private void AnimateTransitionGeometry(EchoHudTransitionKind kind,
        float t, float pulse)
    {
        CacheTransitionBases();
        ResetTransitionGeometry();
        if (kind == EchoHudTransitionKind.Scan && transitionScanLine != null)
        {
            transitionScanLine.anchoredPosition = _scanLineBase
                + new Vector2(Mathf.Lerp(-250f, 250f, t), 0f);
        }
        else if (kind == EchoHudTransitionKind.Activate
                 && transitionScanLine != null)
        {
            transitionScanLine.anchoredPosition = _scanLineBase
                + new Vector2(Mathf.Lerp(-90f, 90f, t), 0f);
        }
        else if (kind == EchoHudTransitionKind.Fracture)
        {
            if (fractureSliceA != null)
                fractureSliceA.anchoredPosition = _fractureSliceABase
                    + new Vector2(7f * pulse, 0f);
            if (fractureSliceB != null)
                fractureSliceB.anchoredPosition = _fractureSliceBBase
                    - new Vector2(5f * pulse, 0f);
        }
        else if (kind == EchoHudTransitionKind.Release
                 && transitionScanLine != null)
        {
            transitionScanLine.anchoredPosition = _scanLineBase
                + new Vector2(Mathf.Lerp(-160f, 160f, t), 0f);
        }
    }

    private void ResetTransitionGeometry()
    {
        if (!_transitionBasesCached) return;
        if (transitionScanLine != null)
            transitionScanLine.anchoredPosition = _scanLineBase;
        if (fractureSliceA != null)
            fractureSliceA.anchoredPosition = _fractureSliceABase;
        if (fractureSliceB != null)
            fractureSliceB.anchoredPosition = _fractureSliceBBase;
    }

    private static float TransitionDuration(EchoHudTransitionKind kind)
    {
        switch (kind)
        {
            case EchoHudTransitionKind.Scan: return 0.28f;
            case EchoHudTransitionKind.Activate: return 0.24f;
            case EchoHudTransitionKind.Fracture: return 0.36f;
            case EchoHudTransitionKind.Release: return 0.45f;
            default: return 0f;
        }
    }

    private static float TransitionAlpha(EchoHudTransitionKind kind)
    {
        return kind == EchoHudTransitionKind.Fracture ? 0.48f : 0.34f;
    }

    private void PresentStage(int phaseIndex)
    {
        if (stageNodes == null) return;
        for (int i = 0; i < stageNodes.Length; i++)
        {
            Text node = stageNodes[i];
            if (node == null) continue;
            Color target = i < phaseIndex ? Muted
                : i == phaseIndex ? EchoRunUITheme.HudInk
                : new Color(Muted.r, Muted.g, Muted.b, 0.45f);
            if (node.color != target) node.color = target;
            FontStyle style = i == phaseIndex ? FontStyle.Bold : FontStyle.Normal;
            if (node.fontStyle != style) node.fontStyle = style;
        }
    }

    private void PresentMeter(EchoHudViewData data)
    {
        bool visible = data.meterKind != EchoHudMeterKind.None;
        SetActiveIfChanged(meterGroup, visible);
        if (!visible) return;

        string label = data.meterKind == EchoHudMeterKind.Calibration
            ? "校准" : data.meterKind == EchoHudMeterKind.Phase
                ? "阶段" : "回声锁定";
        SetTextIfChanged(meterLabel, label);
        SetMeterWidthIfChanged(meterFill, data.displayedMeter01);
        if (meterFill != null)
        {
            Color target = data.meterKind == EchoHudMeterKind.EchoLock
                ? Coral : _phaseAccent;
            if (data.meterKind == EchoHudMeterKind.EchoLock
                && data.displayedMeter01 <= 0.01f)
                target = Gold;
            else if (data.meterKind != EchoHudMeterKind.EchoLock
                     && data.displayedMeter01 >= 1f)
                target = Gold;
            if (meterFill.color != target) meterFill.color = target;
        }
    }

    private void PresentSingleContractCalibrationMeter(
        SingleContractHudData data)
    {
        SetTextIfChanged(meterLabel, data.calibrationMeterText);
        SetMeterWidthIfChanged(meterFill, data.calibrationProgress01);
        if (meterFill != null)
        {
            Color target = data.calibrationProgress01 >= 1f
                ? Gold : _phaseAccent;
            if (meterFill.color != target) meterFill.color = target;
        }
    }

    private void PresentLead(EchoHudViewData data)
    {
        if (data.mode == EchoHudMode.Calibration) return;
        string sign = data.leadMeters > 0.05f ? "+" : "";
        SetTextIfChanged(leadText, sign + data.leadMeters.ToString("0.0") + "m");
        if (leadText != null)
        {
            Color target = data.leadMeters > 0.25f ? Gold
                : data.leadMeters < -0.25f ? Coral : Muted;
            if (leadText.color != target) leadText.color = target;
        }
        if (leadMarker != null)
        {
            Vector2 anchor = leadMarker.anchorMin;
            float x = Mathf.Clamp01(data.leadPosition01);
            if (!Mathf.Approximately(anchor.x, x))
            {
                leadMarker.anchorMin = new Vector2(x, 0.03f);
                leadMarker.anchorMax = new Vector2(x, 0.03f);
            }
        }
    }

    private void PresentSync(EchoHudViewData data)
    {
        SetSyncCellsVisible(true);
        if (syncCells != null)
        {
            for (int i = 0; i < syncCells.Length; i++)
            {
                Image cell = syncCells[i];
                if (cell == null) continue;
                Color target = i < data.syncRemaining
                    ? _phaseAccent : EmptyCell;
                if (cell.color != target) cell.color = target;
            }
        }

        bool recovering = data.recoverySeconds > 0.01f;
        SetTextIfChanged(recoveryText, recovering
            ? "失步 · 重同步 " + data.recoverySeconds.ToString("0.0") + "s"
            : "");
        SetActiveIfChanged(recoveryText != null ? recoveryText.gameObject : null,
            recovering);
    }

    private void PresentSingleContractLead(SingleContractHudData data)
    {
        bool visible = data.visualState != SingleContractVisualState.Calibration;
        SetActiveIfChanged(leadGroup, visible);
        if (!visible) return;

        SetTextIfChanged(leadText, data.lead);
        if (leadText != null)
        {
            Color target = data.leadState
                == SingleContractLeadState.PlayerLeading
                ? Gold
                : data.leadState == SingleContractLeadState.EchoLeading
                    ? Coral : Muted;
            if (leadText.color != target) leadText.color = target;
        }
        if (leadMarker != null)
        {
            float x = Mathf.InverseLerp(-12f, 12f, data.leadMeters);
            Vector2 anchor = leadMarker.anchorMin;
            if (!Mathf.Approximately(anchor.x, x))
            {
                leadMarker.anchorMin = new Vector2(x, 0.03f);
                leadMarker.anchorMax = new Vector2(x, 0.03f);
            }
        }
    }

    private void SetSyncCellsVisible(bool visible)
    {
        GameObject syncGroup = null;
        if (recoveryText != null && recoveryText.transform.parent != null)
            syncGroup = recoveryText.transform.parent.gameObject;
        else if (syncCells != null)
        {
            for (int i = 0; i < syncCells.Length; i++)
            {
                Image cell = syncCells[i];
                if (cell == null || cell.transform.parent == null) continue;
                syncGroup = cell.transform.parent.gameObject;
                break;
            }
        }
        if (syncGroup != null)
        {
            SetActiveIfChanged(syncGroup, visible);
            return;
        }

        if (syncCells == null) return;
        for (int i = 0; i < syncCells.Length; i++)
        {
            Image cell = syncCells[i];
            SetActiveIfChanged(cell != null ? cell.gameObject : null, visible);
        }
    }

    private static string SingleContractAnnouncement(
        SingleContractVisualState state)
    {
        switch (state)
        {
            case SingleContractVisualState.Challenge:
                return "回声正在追你";
            case SingleContractVisualState.RelearnPulse:
                return "回声改猜了";
            case SingleContractVisualState.Finale:
                return "最后冲刺";
            default:
                return "AI 正在学你的跑法";
        }
    }

    private static void SetTextIfChanged(Text target, string value)
    {
        if (target == null) return;
        string safe = value ?? "";
        if (target.text != safe) target.text = safe;
    }

    private static void SetMeterWidthIfChanged(Image target, float value)
    {
        if (target == null) return;
        float safe = Mathf.Clamp01(value);
        RectTransform rect = target.rectTransform;
        Vector2 maximum = new Vector2(safe, 1f);
        if (rect.anchorMax != maximum) rect.anchorMax = maximum;
    }

    private static void SetColors(Image[] targets, Color color,
        bool preserveAlpha)
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            Image target = targets[i];
            if (target == null) continue;
            Color value = color;
            if (preserveAlpha) value.a = target.color.a;
            if (target.color != value) target.color = value;
            target.raycastTarget = false;
        }
    }

    private static void SetColorIfChanged(Graphic target, Color color)
    {
        if (target != null && target.color != color) target.color = color;
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

}
