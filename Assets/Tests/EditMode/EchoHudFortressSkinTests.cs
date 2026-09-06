using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class EchoHudFortressSkinTests
{
    private GameObject _instance;

    [TearDown]
    public void TearDown()
    {
        if (_instance != null) Object.DestroyImmediate(_instance);
    }

    [Test]
    public void FourStatesShareOneNeutralFoundationAndUseDistinctAccents()
    {
        EchoHudSkin calibration = EchoRunUITheme.HudSkinFor(
            SingleContractVisualState.Calibration);
        EchoHudSkin challenge = EchoRunUITheme.HudSkinFor(
            SingleContractVisualState.Challenge);
        EchoHudSkin relearn = EchoRunUITheme.HudSkinFor(
            SingleContractVisualState.RelearnPulse);
        EchoHudSkin finale = EchoRunUITheme.HudSkinFor(
            SingleContractVisualState.Finale);

        foreach (EchoHudSkin skin in new[]
                 {
                     challenge, relearn, finale
                 })
        {
            AssertColor(calibration.panel, skin.panel);
            AssertColor(calibration.panelRaised, skin.panelRaised);
            AssertColor(calibration.ink, skin.ink);
            AssertColor(calibration.mutedInk, skin.mutedInk);
            AssertColor(calibration.rule, skin.rule);
        }

        Assert.AreNotEqual(calibration.accent, challenge.accent);
        Assert.AreNotEqual(challenge.accent, relearn.accent);
        Assert.AreNotEqual(relearn.accent, finale.accent);
        Assert.AreEqual(EchoHudTransitionKind.Scan, calibration.transition);
        Assert.AreEqual(EchoHudTransitionKind.Activate, challenge.transition);
        Assert.AreEqual(EchoHudTransitionKind.Fracture, relearn.transition);
        Assert.AreEqual(EchoHudTransitionKind.Release, finale.transition);
        Assert.Less(calibration.panel.grayscale, 0.15f);
        Assert.Greater(calibration.panel.a, 0.55f);
        Assert.Less(calibration.panel.a, 0.9f);
        Assert.Greater(calibration.ink.grayscale, 0.8f);
    }

    [Test]
    public void RelearnReturnToChallengeSettlesSilently()
    {
        Assert.IsTrue(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            true, SingleContractVisualState.Challenge,
            SingleContractVisualState.RelearnPulse, false, false));
        Assert.IsFalse(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            true, SingleContractVisualState.RelearnPulse,
            SingleContractVisualState.Challenge, false, false));
        Assert.IsFalse(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            false, SingleContractVisualState.Calibration,
            SingleContractVisualState.Challenge, true, false));
        Assert.IsTrue(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            false, SingleContractVisualState.Calibration,
            SingleContractVisualState.Challenge, true, false, true));
        Assert.IsTrue(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            true, SingleContractVisualState.Challenge,
            SingleContractVisualState.Challenge, true, true, true));
        Assert.IsFalse(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            true, SingleContractVisualState.Challenge,
            SingleContractVisualState.Challenge, true, false, true));
        Assert.IsFalse(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            true, SingleContractVisualState.Challenge,
            SingleContractVisualState.Challenge, false, false));
        Assert.IsTrue(EchoHudPresenter.ShouldEmphasizeSingleContractTransition(
            true, SingleContractVisualState.Challenge,
            SingleContractVisualState.Challenge, false, true));
    }

    [Test]
    public void TransitionKindsOnlyShowTheirOwnGeometryAndStopHidesAll()
    {
        bool oldReducedMotion = EchoRunAccessibility.ReducedMotion;
        EchoRunAccessibility.SetReducedMotion(false);
        try
        {
            GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
            Assert.NotNull(prefab);
            _instance = Object.Instantiate(prefab);
            EchoHudView view = _instance.GetComponent<EchoHudView>();
            Assert.NotNull(view);

            AssertTransitionGeometry(view,
                SingleContractVisualState.Calibration, true, false);
            AssertTransitionGeometry(view,
                SingleContractVisualState.Challenge, true, false);
            AssertTransitionGeometry(view,
                SingleContractVisualState.RelearnPulse, false, true);
            AssertTransitionGeometry(view,
                SingleContractVisualState.Finale, true, false);
        }
        finally
        {
            EchoRunAccessibility.SetReducedMotion(oldReducedMotion);
        }
    }

    [Test]
    public void PredictionChangeUsesCurrentAccentAndFractureGeometry()
    {
        bool oldReducedMotion = EchoRunAccessibility.ReducedMotion;
        EchoRunAccessibility.SetReducedMotion(false);
        try
        {
            GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
            Assert.NotNull(prefab);
            _instance = Object.Instantiate(prefab);
            EchoHudView view = _instance.GetComponent<EchoHudView>();
            Image accent = Find("HudDynamicCanvas/StateAccentBar")
                .GetComponent<Image>();

            view.ApplySingleContractSkin(
                SingleContractVisualState.Challenge);
            Color challengeAccent = accent.color;
            view.PlayPredictionChangeTransition();

            Assert.AreEqual(EchoHudTransitionKind.Fracture,
                view.ActiveTransitionKind);
            AssertColor(challengeAccent, accent.color);
            AssertTransitionPieces(false, true);
        }
        finally
        {
            EchoRunAccessibility.SetReducedMotion(oldReducedMotion);
        }
    }

    [Test]
    public void PredictionSemanticChangePulsesOnceWithoutBecomingAStageEvent()
    {
        var current = new SingleContractHudData
        {
            visualState = SingleContractVisualState.Challenge,
            predictionGateNumber = 2,
            prediction =
                "第2/6次选路 · 下一次它猜右路\n"
                + "红=它猜  青=骗它  白=安全"
        };
        string key = EchoHudPresenter.SingleContractPredictionSemanticKey(
            current);
        Assert.AreEqual("右路", key);

        Assert.IsFalse(ShouldPulse(false, "", 0, current));
        Assert.IsFalse(ShouldPulse(true, key, 2, current));
        Assert.IsTrue(ShouldPulse(true, "左路", 2, current));
        Assert.IsTrue(ShouldPulse(true, key, 1, current));

        SingleContractHudData activeWindow = current;
        activeWindow.prediction =
            "第2/6次选路 · 这次它猜右路\n"
            + "红=它猜  青=骗它  白=安全";
        Assert.AreEqual(key,
            EchoHudPresenter.SingleContractPredictionSemanticKey(activeWindow));
        Assert.IsFalse(ShouldPulse(true, key, 2, activeWindow),
            "Opening the same gate window is timing, not a new prediction.");

        SingleContractHudData opening = current;
        opening.openingMemory = true;
        Assert.IsFalse(ShouldPulse(true, "左路", 1, opening));
        Assert.IsFalse(EchoHudPresenter
            .ShouldEmphasizeSingleContractPredictionChange(
                true, "左路", 1, current, true, false),
            "A phase transition already owns this frame's emphasis.");
        Assert.IsFalse(EchoHudPresenter
            .ShouldEmphasizeSingleContractPredictionChange(
                true, "左路", 1, current, false, true),
            "Relearn-to-challenge must stay silent.");
    }

    [Test]
    public void ResourcePrefabBuildsNonBlockingFortressDecoration()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        Assert.NotNull(prefab);
        _instance = Object.Instantiate(prefab);

        Transform dynamicLayer = _instance.transform.Find("HudDynamicCanvas");
        Assert.NotNull(dynamicLayer);
        foreach (string name in new[]
                 {
                     "AnnouncementPlate", "DirectivePlate", "PredictionPlate",
                     "FeedbackGroup/FeedbackPlate", "StateAccentBar", "StateTransitionFx"
                 })
        {
            Transform decoration = dynamicLayer.Find(name);
            Assert.NotNull(decoration, name);
            Image image = decoration.GetComponent<Image>();
            if (image != null)
                Assert.IsFalse(image.raycastTarget, name);
        }

        CanvasGroup transition = dynamicLayer.Find("StateTransitionFx")
            .GetComponent<CanvasGroup>();
        Assert.NotNull(transition);
        Assert.IsFalse(transition.blocksRaycasts);
        Assert.IsFalse(transition.interactable);
        foreach (Image decoration in transition.GetComponentsInChildren<Image>(true))
            Assert.IsFalse(decoration.raycastTarget, decoration.name);
    }

    [Test]
    public void ResourcePrefabUsesOneTopRailAndReadablePredictionVeil()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        Assert.NotNull(prefab);
        _instance = Object.Instantiate(prefab);

        Transform staticLayer = _instance.transform.Find("HudStaticCanvas");
        Transform dynamicLayer = _instance.transform.Find("HudDynamicCanvas");
        Assert.NotNull(staticLayer);
        Assert.NotNull(dynamicLayer);

        Image topRail = staticLayer.Find("TopInformationRail")
            .GetComponent<Image>();
        Assert.NotNull(topRail);
        Assert.IsFalse(topRail.raycastTarget);
        AssertColor(EchoRunUITheme.HudPanel, topRail.color);

        foreach (string name in new[]
                 {
                     "StatsPlate", "StageRail", "CalibrationRail",
                     "DistancePlate"
                 })
        {
            Image plate = staticLayer.Find(name).GetComponent<Image>();
            Assert.LessOrEqual(plate.color.a, 0.001f, name);
        }

        foreach (string name in new[]
                 {
                     "AnnouncementPlate", "DirectivePlate"
                 })
        {
            Image veil = dynamicLayer.Find(name).GetComponent<Image>();
            Assert.LessOrEqual(veil.color.a, 0.001f, name);
            Assert.IsFalse(veil.raycastTarget, name);
        }

        Image predictionVeil = dynamicLayer.Find("PredictionPlate")
            .GetComponent<Image>();
        AssertColor(EchoRunUITheme.HudPredictionVeil,
            predictionVeil.color);
        Assert.GreaterOrEqual(predictionVeil.color.a, 0.8f,
            "The prediction must remain readable over bright track scenery.");
        Assert.Less(predictionVeil.color.grayscale, 0.15f,
            "PredictionPlate must stay dark.");
        Assert.IsFalse(predictionVeil.raycastTarget, "PredictionPlate");
        Image feedbackVeil = dynamicLayer.Find("FeedbackGroup/FeedbackPlate")
            .GetComponent<Image>();
        Assert.GreaterOrEqual(feedbackVeil.color.a, 0.8f,
            "Event text needs its own readable backing, faded with the text.");
        Assert.IsFalse(feedbackVeil.raycastTarget);

        RectTransform accent = dynamicLayer.Find("StateAccentBar")
            as RectTransform;
        Assert.LessOrEqual(accent.sizeDelta.x, 4f);
        Assert.GreaterOrEqual(accent.sizeDelta.y, 100f);

        foreach (string path in new[]
                 {
                     "HudStaticCanvas/StatsText",
                     "HudStaticCanvas/DistanceText",
                     "HudDynamicCanvas/Announcement",
                     "HudDynamicCanvas/Directive",
                     "HudDynamicCanvas/Prediction"
                 })
        {
            Assert.NotNull(Find(path).GetComponent<Shadow>(), path);
        }
    }

    [Test]
    public void BuffUsesTheRightEdgeWhileDynamicCopyKeepsTheLeftEdge()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        Assert.NotNull(prefab);
        _instance = Object.Instantiate(prefab);

        RectTransform buff = _instance.transform
            .Find("HudDynamicCanvas/BuffGroup") as RectTransform;
        RectTransform announcement = _instance.transform
            .Find("HudDynamicCanvas/Announcement") as RectTransform;
        Assert.NotNull(buff);
        Assert.NotNull(announcement);
        Assert.AreEqual(1f, buff.anchorMin.x, 0.0001f);
        Assert.AreEqual(1f, buff.anchorMax.x, 0.0001f);
        Assert.AreEqual(1f, buff.pivot.x, 0.0001f);
        Assert.LessOrEqual(announcement.anchorMax.x, 0.05f);
        Assert.AreEqual(0f, announcement.pivot.x, 0.0001f);
    }

    private void AssertTransitionGeometry(EchoHudView view,
        SingleContractVisualState state, bool scan, bool fracture)
    {
        GameObject transitionFx = Find(
            "HudDynamicCanvas/StateTransitionFx");
        RectTransform scanLine = Find(
            "HudDynamicCanvas/StateTransitionFx/TransitionScanLine")
            .GetComponent<RectTransform>();
        RectTransform sliceA = Find(
            "HudDynamicCanvas/StateTransitionFx/FractureSliceA")
            .GetComponent<RectTransform>();
        RectTransform sliceB = Find(
            "HudDynamicCanvas/StateTransitionFx/FractureSliceB")
            .GetComponent<RectTransform>();
        Vector2 scanBase = scanLine.anchoredPosition;
        Vector2 sliceABase = sliceA.anchoredPosition;
        Vector2 sliceBBase = sliceB.anchoredPosition;

        view.PlaySingleContractTransition(state);
        Assert.IsTrue(transitionFx.activeSelf);
        AssertTransitionPieces(scan, fracture);
        scanLine.anchoredPosition += new Vector2(20f, 0f);
        sliceA.anchoredPosition += new Vector2(7f, 0f);
        sliceB.anchoredPosition -= new Vector2(5f, 0f);

        view.StopSingleContractTransition();
        Assert.IsFalse(transitionFx.activeSelf);
        AssertTransitionPieces(false, false);
        Assert.AreEqual(scanBase, scanLine.anchoredPosition);
        Assert.AreEqual(sliceABase, sliceA.anchoredPosition);
        Assert.AreEqual(sliceBBase, sliceB.anchoredPosition);
        Assert.AreEqual(EchoHudTransitionKind.None,
            view.ActiveTransitionKind);
    }

    private void AssertTransitionPieces(bool scan, bool fracture)
    {
        Assert.AreEqual(scan,
            Find("HudDynamicCanvas/StateTransitionFx/TransitionScanLine")
                .activeSelf);
        Assert.AreEqual(fracture,
            Find("HudDynamicCanvas/StateTransitionFx/FractureSliceA")
                .activeSelf);
        Assert.AreEqual(fracture,
            Find("HudDynamicCanvas/StateTransitionFx/FractureSliceB")
                .activeSelf);
    }

    private static bool ShouldPulse(bool hasPrevious, string previousKey,
        int previousGate, SingleContractHudData current)
    {
        return EchoHudPresenter.ShouldEmphasizeSingleContractPredictionChange(
            hasPrevious, previousKey, previousGate, current, false, false);
    }

    private GameObject Find(string path)
    {
        Transform target = _instance.transform.Find(path);
        Assert.NotNull(target, path);
        return target.gameObject;
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.AreEqual(expected.r, actual.r, 0.001f);
        Assert.AreEqual(expected.g, actual.g, 0.001f);
        Assert.AreEqual(expected.b, actual.b, 0.001f);
        Assert.AreEqual(expected.a, actual.a, 0.001f);
    }
}
