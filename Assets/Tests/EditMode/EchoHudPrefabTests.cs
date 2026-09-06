using NUnit.Framework;
using UnityEngine;

public class EchoHudPrefabTests
{
    private GameObject _instance;

    [TearDown]
    public void TearDown()
    {
        if (_instance != null) Object.DestroyImmediate(_instance);
    }

    [Test]
    public void ResourcePrefabUsesTwoCanvasLayersAndExplicitView()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        Assert.NotNull(prefab);
        _instance = Object.Instantiate(prefab);

        Assert.NotNull(_instance.GetComponent<EchoHudView>());
        Assert.NotNull(_instance.GetComponent<EchoHudPresenter>());
        Assert.NotNull(_instance.transform.Find("HudStaticCanvas"));
        Assert.NotNull(_instance.transform.Find("HudDynamicCanvas"));
        Assert.AreEqual(2, _instance.GetComponentsInChildren<Canvas>(true).Length);
    }

    [Test]
    public void ViewSwitchesBetweenCalibrationAndDuelWithoutAddingPanels()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        _instance = Object.Instantiate(prefab);
        EchoHudView view = _instance.GetComponent<EchoHudView>();

        EchoHudViewData calibration = EchoRunPresentation.BuildHud(false,
            null, 0f, 2, 2, 1, 2, 0.5f,
            EchoDuelPhase.Calibration, 0f);
        view.Present(calibration, true);
        Assert.IsFalse(Find("HudStaticCanvas/StageRail").activeSelf);
        Assert.IsTrue(Find("HudStaticCanvas/CalibrationRail").activeSelf);
        Assert.IsFalse(Find("HudStaticCanvas/LeadGroup").activeSelf);

        EchoContractData contract = new EchoContractData
        {
            type = EchoContractType.BreakLaneHabit,
            targetLane = 0,
            predictionLane = 2,
            targetProgress = 3,
            progress = 1,
            duelPhase = EchoDuelPhase.Resistance
        };
        EchoHudViewData duel = EchoRunPresentation.BuildHud(true, contract,
            2.4f, 2, 2, 2, 2, 1f, EchoDuelPhase.Resistance, 0.4f);
        view.Present(duel, false);

        Assert.IsTrue(Find("HudStaticCanvas/StageRail").activeSelf);
        Assert.IsFalse(Find("HudStaticCanvas/CalibrationRail").activeSelf);
        Assert.IsTrue(Find("HudStaticCanvas/LeadGroup").activeSelf);
        Assert.IsTrue(Find("HudDynamicCanvas/MeterGroup").activeSelf);
        Assert.IsTrue(Find("HudDynamicCanvas/Prediction").activeSelf);
    }

    [Test]
    public void PhaseMeterStaysAboveTheRunnerFocusZone()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        _instance = Object.Instantiate(prefab);

        RectTransform meter = Find("HudDynamicCanvas/MeterGroup")
            .GetComponent<RectTransform>();
        Assert.NotNull(meter);
        Assert.GreaterOrEqual(meter.anchorMin.y, 0.82f,
            "The phase meter must stay above the runner and obstacle sightline.");
        Assert.AreEqual(meter.anchorMin, meter.anchorMax);
    }

    [Test]
    public void SingleContractCalibrationKeepsOnlyProgressAndRaceStatus()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        _instance = Object.Instantiate(prefab);
        EchoHudView view = _instance.GetComponent<EchoHudView>();
        SingleContractHudData data = EchoRunPresentation
            .BuildSingleContractHud(new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Calibration,
                injuries = 1,
                calibrationProgress = new SingleContractCalibrationProgress
                {
                    available = true,
                    totalSamples = 12,
                    minimumTotalSamples = 24,
                    activeSamples = 4,
                    minimumActiveSamples = 6,
                    actionCategories = 1,
                    minimumActionCategories = 2,
                    jumpSamples = 1,
                    minimumJumpSamples = 2,
                    slideSamples = 1,
                    minimumSlideSamples = 2,
                    formalChoices = 2,
                    minimumFormalChoices = 5,
                    successfulChoices = 1,
                    minimumSuccessfulChoices = 3,
                    preferredLane = 2,
                    preferredLaneUnique = true,
                    strongestRouteChoices = 2,
                    minimumStrongestRouteChoices = 3,
                    preferredLaneConfidence = 1f
                }
            });

        view.PresentSingleContract(data, false);

        Assert.IsTrue(Find("HudDynamicCanvas/MeterGroup").activeSelf);
        Assert.AreEqual("观察 33%",
            Find("HudDynamicCanvas/MeterGroup/MeterLabel")
                .GetComponent<UnityEngine.UI.Text>().text);
        Assert.IsFalse(Find("HudDynamicCanvas/Directive").activeSelf);
        Assert.IsFalse(Find("HudDynamicCanvas/Prediction").activeSelf);
        Assert.IsFalse(Find("HudDynamicCanvas/Announcement").activeSelf);
        Assert.AreEqual(data.injuriesText,
            Find("HudStaticCanvas/CalibrationRail/CalibrationObservation")
                .GetComponent<UnityEngine.UI.Text>().text);
    }

    [Test]
    public void ReadyCalibrationMeterLabelFitsWithoutWrapping()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        _instance = Object.Instantiate(prefab);
        EchoHudView view = _instance.GetComponent<EchoHudView>();
        SingleContractHudData data = EchoRunPresentation
            .BuildSingleContractHud(new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Calibration,
                calibrationProgress = new SingleContractCalibrationProgress
                {
                    available = true,
                    evidenceReady = true,
                    totalSamples = 24,
                    minimumTotalSamples = 24,
                    activeSamples = 6,
                    minimumActiveSamples = 6,
                    actionCategories = 2,
                    minimumActionCategories = 2,
                    jumpSamples = 2,
                    minimumJumpSamples = 2,
                    slideSamples = 2,
                    minimumSlideSamples = 2,
                    formalChoices = 5,
                    minimumFormalChoices = 5,
                    successfulChoices = 3,
                    minimumSuccessfulChoices = 3,
                    preferredLane = 1,
                    preferredLaneUnique = true,
                    strongestRouteChoices = 3,
                    minimumStrongestRouteChoices = 3,
                    preferredLaneConfidence = 0.6f
                }
            });

        view.PresentSingleContract(data, false);
        Canvas.ForceUpdateCanvases();

        UnityEngine.UI.Text label = Find(
                "HudDynamicCanvas/MeterGroup/MeterLabel")
            .GetComponent<UnityEngine.UI.Text>();
        Assert.AreEqual("观察完成", label.text);
        Assert.LessOrEqual(label.preferredWidth,
            label.rectTransform.rect.width + 0.5f,
            "The ready state must stay on one line at the reference size.");
        Assert.LessOrEqual(label.preferredHeight,
            label.rectTransform.rect.height + 0.5f,
            "The ready state must not be vertically clipped.");
    }

    [Test]
    public void OpeningReplayTitleAndDetailFitAtNormalAndLargeTextSizes()
    {
        bool oldLargeText = EchoRunAccessibility.LargeText;
        try
        {
            GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
            _instance = Object.Instantiate(prefab);
            EchoHudView view = _instance.GetComponent<EchoHudView>();
            SingleContractHudData data = EchoRunPresentation
                .BuildSingleContractHud(new SingleContractHudInput
                {
                    visualState = SingleContractVisualState.Challenge,
                    openingMemory = true,
                    openingReplay = true,
                    openingReplayAction = ShadowAction.Slide,
                    openingReplayCount = 4095,
                    generation = 9999,
                    memory = "压力出现时，你偏向右侧"
                });

            foreach (bool largeText in new[] { false, true })
            {
                EchoRunAccessibility.SetLargeText(largeText);
                view.PresentSingleContract(data, true);
                EchoRunAccessibility.ApplyToHierarchy(_instance.transform);
                Canvas.ForceUpdateCanvases();

                AssertTextFits("HudDynamicCanvas/Announcement", largeText);
                AssertTextFits("HudDynamicCanvas/Directive", largeText);
            }
        }
        finally
        {
            EchoRunAccessibility.SetLargeText(oldLargeText);
        }
    }

    [Test]
    public void PlayerLanguageFitsAtNormalAndLargeTextSizes()
    {
        bool oldLargeText = EchoRunAccessibility.LargeText;
        try
        {
            GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
            _instance = Object.Instantiate(prefab);
            EchoHudView view = _instance.GetComponent<EchoHudView>();
            SingleContractHudData calibration = EchoRunPresentation
                .BuildSingleContractHud(new SingleContractHudInput
                {
                    visualState = SingleContractVisualState.Calibration,
                    injuries = 9,
                    calibrationProgress =
                        new SingleContractCalibrationProgress
                        {
                            available = true,
                            evidenceReady = true,
                            totalSamples = 24,
                            minimumTotalSamples = 24,
                            activeSamples = 6,
                            minimumActiveSamples = 6,
                            actionCategories = 2,
                            minimumActionCategories = 2,
                            jumpSamples = 2,
                            minimumJumpSamples = 2,
                            slideSamples = 2,
                            minimumSlideSamples = 2,
                            formalChoices = 5,
                            minimumFormalChoices = 5,
                            successfulChoices = 3,
                            minimumSuccessfulChoices = 3,
                            preferredLane = 2,
                            preferredLaneUnique = true,
                            strongestRouteChoices = 3,
                            minimumStrongestRouteChoices = 3,
                            preferredLaneConfidence = 0.6f
                        }
                });
            SingleContractHudData challenge = EchoRunPresentation
                .BuildSingleContractHud(new SingleContractHudInput
                {
                    visualState = SingleContractVisualState.Challenge,
                    generation = 9999,
                    memory = "压力出现时，你偏向右路",
                    showPrediction = true,
                    predictedLane = 2,
                    predictionGateNumber = 6,
                    predictionGateCount = 6,
                    predictionGateActive = true,
                    injuries = 1,
                    instantFeedback =
                        SingleContractInstantFeedback.CounterFailed,
                    feedbackLeadDeltaMeters = -99.9f
                });

            foreach (bool largeText in new[] { false, true })
            {
                EchoRunAccessibility.SetLargeText(largeText);

                view.PresentSingleContract(calibration, true);
                EchoRunAccessibility.ApplyToHierarchy(_instance.transform);
                Canvas.ForceUpdateCanvases();
                Assert.IsFalse(Find("HudDynamicCanvas/Announcement").activeSelf);
                Assert.IsFalse(Find("HudDynamicCanvas/Directive").activeSelf);
                Assert.IsFalse(Find("HudDynamicCanvas/Prediction").activeSelf);
                AssertTextFits(
                    "HudStaticCanvas/CalibrationRail/CalibrationObservation",
                    largeText);
                AssertTextFits(
                    "HudDynamicCanvas/MeterGroup/MeterLabel", largeText);

                view.PresentSingleContract(challenge, true);
                view.ShowFeedback(challenge.instantFeedback, Color.white,
                    true);
                EchoRunAccessibility.ApplyToHierarchy(_instance.transform);
                Canvas.ForceUpdateCanvases();
                Assert.IsFalse(Find("HudDynamicCanvas/Announcement").activeSelf);
                AssertTextFits(
                    "HudStaticCanvas/CalibrationRail/CalibrationObservation",
                    largeText);
                AssertTextFits("HudDynamicCanvas/Prediction", largeText);
                AssertTextFits("HudDynamicCanvas/FeedbackGroup/Feedback", largeText);
            }
        }
        finally
        {
            EchoRunAccessibility.SetLargeText(oldLargeText);
        }
    }

    [Test]
    public void DynamicCopyStaysAtTheLeftEdgeOutsideTheRunnerSightline()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        _instance = Object.Instantiate(prefab);

        string[] paths =
        {
            "HudDynamicCanvas/Announcement",
            "HudDynamicCanvas/Directive",
            "HudDynamicCanvas/Prediction",
            "HudDynamicCanvas/FeedbackGroup/Feedback"
        };
        for (int i = 0; i < paths.Length; i++)
        {
            RectTransform rect = Find(paths[i]).GetComponent<RectTransform>();
            Assert.LessOrEqual(rect.anchorMax.x, 0.05f, paths[i]);
            Assert.AreEqual(0f, rect.pivot.x, 0.0001f, paths[i]);
        }
    }

    [Test]
    public void LeadTextGeneratesVisibleGlyphsAtNormalAndLargeTextSizes()
    {
        bool oldLargeText = EchoRunAccessibility.LargeText;
        try
        {
            CreateFeedbackPresenter();
            EchoHudView view = _instance.GetComponent<EchoHudView>();
            var text = Find("HudStaticCanvas/LeadGroup/LeadText")
                .GetComponent<UnityEngine.UI.Text>();
            foreach (bool large in new[] { false, true })
            {
                EchoRunAccessibility.SetLargeText(large);
                foreach (float lead in new[] { 123.4f, -123.4f, 0f })
                {
                    view.PresentSingleContract(EchoRunPresentation.BuildSingleContractHud(
                        new SingleContractHudInput
                        {
                            visualState = SingleContractVisualState.Challenge,
                            leadMeters = lead
                        }), false);
                    EchoRunAccessibility.ApplyToHierarchy(_instance.transform);
                    Canvas.ForceUpdateCanvases();
                    var generator = new TextGenerator();
                    Assert.IsTrue(generator.Populate(text.text,
                        text.GetGenerationSettings(text.rectTransform.rect.size)));
                    Assert.Greater(generator.vertexCount, 4,
                        "The configured Chinese font must produce glyph geometry, not a truncated empty line.");
                    Vector3 minimum = generator.verts[0].position;
                    Vector3 maximum = minimum;
                    for (int i = 1; i < generator.vertexCount - 4; i++)
                    {
                        minimum = Vector3.Min(minimum, generator.verts[i].position);
                        maximum = Vector3.Max(maximum, generator.verts[i].position);
                    }
                    Assert.Greater(maximum.x - minimum.x, 1f, text.text);
                    Assert.Greater(maximum.y - minimum.y, 1f, text.text);
                }
            }
        }
        finally
        {
            EchoRunAccessibility.SetLargeText(oldLargeText);
        }
    }

    [Test]
    public void SpriteLessProgressBarGeneratesOnlyTheRequestedWidth()
    {
        CreateFeedbackPresenter();
        EchoHudView view = _instance.GetComponent<EchoHudView>();
        var fill = Find("HudDynamicCanvas/MeterGroup/MeterTrack/MeterFill")
            .GetComponent<UnityEngine.UI.Image>();
        var populate = typeof(UnityEngine.UI.Image).GetMethod("OnPopulateMesh",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(UnityEngine.UI.VertexHelper) }, null);
        Assert.NotNull(populate);
        foreach (float progress in new[] { 0f, 0.25f, 1f, 0f })
        {
            view.PresentSingleContract(new SingleContractHudData
            {
                visualState = SingleContractVisualState.Calibration,
                showCalibrationProgress = true,
                calibrationProgress01 = progress,
                calibrationMeterText = "观察"
            }, false);
            Canvas.ForceUpdateCanvases();
            float trackWidth = ((RectTransform)fill.transform.parent).rect.width;
            var mesh = new Mesh();
            try
            {
                using (var vertices = new UnityEngine.UI.VertexHelper())
                {
                    populate.Invoke(fill, new object[] { vertices });
                    vertices.FillMesh(mesh);
                }
                mesh.RecalculateBounds();
                Assert.AreEqual(trackWidth * progress, mesh.bounds.size.x, 1f,
                    "The actual Image mesh must shrink with progress, including zero after a full bar.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }

    [Test]
    public void FeedbackTextAndBackingFadeTogetherWithoutRefreshRenewal()
    {
        bool oldReducedMotion = EchoRunAccessibility.ReducedMotion;
        try
        {
            EchoRunAccessibility.SetReducedMotion(false);
            EchoHudPresenter presenter = CreateFeedbackPresenter();
            var data = Feedback(1, "本次选择符合预测",
                SingleContractInstantFeedback.PredictionHit);
            CanvasGroup group = Find("HudDynamicCanvas/FeedbackGroup")
                .GetComponent<CanvasGroup>();
            Assert.AreSame(group.transform,
                Find("HudDynamicCanvas/FeedbackGroup/Feedback").transform.parent);
            Assert.AreSame(group.transform,
                Find("HudDynamicCanvas/FeedbackGroup/FeedbackPlate").transform.parent);
            Assert.IsFalse(group.blocksRaycasts);
            Assert.IsFalse(group.interactable);

            presenter.PresentSingleContractFeedback(data, 10f);
            Assert.IsTrue(group.gameObject.activeSelf);
            Assert.AreEqual(0f, group.alpha, 0.001f);
            presenter.PresentSingleContractFeedback(data, 10.075f);
            Assert.AreEqual(0.5f, group.alpha, 0.001f);
            presenter.PresentSingleContractFeedback(data, 11f);
            Assert.AreEqual(1f, group.alpha, 0.001f);
            data.instantFeedback = "same sequence must not change the displayed event";
            presenter.PresentSingleContractFeedback(data, 12.025f);
            Assert.AreEqual(0.5f, group.alpha, 0.001f);
            Assert.AreEqual("本次选择符合预测", FeedbackText());
            presenter.PresentSingleContractFeedback(data, 12.21f);
            Assert.IsFalse(group.gameObject.activeSelf,
                "Repeated refreshes must not extend the original event lifetime.");
            Assert.AreEqual(0f, group.alpha);
        }
        finally
        {
            EchoRunAccessibility.SetReducedMotion(oldReducedMotion);
        }
    }

    [Test]
    public void FeedbackReplacementDropsSuppressedEventsAndSuspensionDoesNotReplayThem()
    {
        EchoHudPresenter presenter = CreateFeedbackPresenter();
        var first = Feedback(1, "反制通过",
            SingleContractInstantFeedback.RewriteSucceeded);
        var injury = Feedback(2, "尝试反制 · 通过未完成",
            SingleContractInstantFeedback.ExecutionIncomplete);
        var ordinary = Feedback(3, "安全通过",
            SingleContractInstantFeedback.SafePass);
        presenter.PresentSingleContractFeedback(first, 1f);
        presenter.PresentSingleContractFeedback(injury, 1.5f);
        presenter.PresentSingleContractFeedback(ordinary, 1.6f);
        Assert.AreEqual(injury.instantFeedback, FeedbackText());
        presenter.PresentSingleContractFeedback(ordinary, 4f);
        Assert.IsFalse(Find("HudDynamicCanvas/FeedbackGroup").activeSelf,
            "A suppressed old message must not enter a later playback queue.");

        var next = Feedback(4, "后续预测已调整",
            SingleContractInstantFeedback.EchoRelearned);
        presenter.PresentSingleContractFeedback(next, 5f);
        Assert.IsTrue(Find("HudDynamicCanvas/FeedbackGroup").activeSelf);
        // Ordinary MonoBehaviour lifecycle callbacks are exercised in
        // PlayMode. This EditMode test drives the suspension boundary directly.
        presenter.SuspendSingleContractFeedback();
        Assert.IsFalse(Find("HudDynamicCanvas/FeedbackGroup").activeSelf);
        presenter.PresentSingleContractFeedback(next, 5.2f);
        Assert.IsFalse(Find("HudDynamicCanvas/FeedbackGroup").activeSelf,
            "Resuming the HUD must not replay an already consumed event.");
        presenter.ResetRun();
        presenter.PresentSingleContractFeedback(first, 10f);
        Assert.IsTrue(Find("HudDynamicCanvas/FeedbackGroup").activeSelf,
            "A new run can restart its event sequence.");
        Assert.AreEqual(first.instantFeedback, FeedbackText());
    }

    [Test]
    public void SuspensionDiscardsAnUnobservedEventButNewRunAcceptsItsFirstEvent()
    {
        EchoHudPresenter presenter = CreateFeedbackPresenter();
        presenter.PresentSingleContractFeedback(Feedback(1, "反制通过",
            SingleContractInstantFeedback.RewriteSucceeded), 1f);
        presenter.SuspendSingleContractFeedback();
        var pending = Feedback(2, "通过未完成",
            SingleContractInstantFeedback.ExecutionIncomplete);
        presenter.PresentSingleContractFeedback(pending, 60f);
        Assert.IsFalse(Find("HudDynamicCanvas/FeedbackGroup").activeSelf,
            "The event written before pause but missed by the 10 Hz refresh is stale.");
        presenter.PresentSingleContractFeedback(pending, 60.1f);
        Assert.IsFalse(Find("HudDynamicCanvas/FeedbackGroup").activeSelf);
        presenter.PresentSingleContractFeedback(Feedback(3, "后续预测已调整",
            SingleContractInstantFeedback.EchoRelearned), 60.2f);
        Assert.IsTrue(Find("HudDynamicCanvas/FeedbackGroup").activeSelf);

        presenter.SuspendSingleContractFeedback();
        presenter.ResetRun();
        presenter.PresentSingleContractFeedback(Feedback(1, "首个事件",
            SingleContractInstantFeedback.RewriteSucceeded), 70f);
        Assert.IsTrue(Find("HudDynamicCanvas/FeedbackGroup").activeSelf,
            "ResetRun must clear resume suppression before a new run's first result.");
        Assert.AreEqual("首个事件", FeedbackText());
    }

    [Test]
    public void ReducedMotionFeedbackRemainsReadableAndStillExpires()
    {
        EchoHudPresenter presenter = CreateFeedbackPresenter();
        EchoHudView view = _instance.GetComponent<EchoHudView>();
        CanvasGroup group = Find("HudDynamicCanvas/FeedbackGroup")
            .GetComponent<CanvasGroup>();
        view.ShowTimedFeedback("后续预测已调整", Color.white, 0f, true, true);
        Assert.AreEqual(1f, group.alpha);
        Assert.IsTrue(group.gameObject.activeSelf);
        view.ShowTimedFeedback("后续预测已调整", Color.white,
            EchoRunPresentation.SingleContractFeedbackDurationSeconds,
            true, true);
        Assert.AreEqual(0f, group.alpha);
        Assert.IsFalse(group.gameObject.activeSelf);
        presenter.ResetRun();
        Assert.AreEqual("", FeedbackText());
    }

    [Test]
    public void ActiveGateKeepsItsShortSignalAndOrdinaryRaceHasNoAnnouncement()
    {
        CreateFeedbackPresenter();
        EchoHudView view = _instance.GetComponent<EchoHudView>();
        var data = new SingleContractHudData
        {
            visualState = SingleContractVisualState.Challenge,
            prediction = "本次预测：右路",
            predictionGateActive = false,
            injuriesText = "伤势 1/2 · 再受伤即出局",
            finishRemainingText = "终点还剩 90m",
            lead = "玩家领先：3.2米"
        };
        view.PresentSingleContract(data, true);
        Assert.IsFalse(Find("HudDynamicCanvas/Prediction").activeSelf);
        Assert.IsFalse(Find("HudDynamicCanvas/Announcement").activeSelf);
        data.predictionGateActive = true;
        view.PresentSingleContract(data, true);
        Assert.IsTrue(Find("HudDynamicCanvas/Prediction").activeSelf);
        Assert.AreEqual(data.prediction,
            Find("HudDynamicCanvas/Prediction").GetComponent<UnityEngine.UI.Text>().text);
        data.predictionGateActive = false;
        view.PresentSingleContract(data, false);
        Assert.IsFalse(Find("HudDynamicCanvas/Prediction").activeSelf);
        foreach (string path in new[]
                 {
                     "HudStaticCanvas/LeadGroup", "HudStaticCanvas/DistanceText",
                     "HudStaticCanvas/CalibrationRail"
                 })
        {
            RectTransform rect = Find(path).GetComponent<RectTransform>();
            Assert.IsTrue(rect.gameObject.activeSelf);
            Assert.AreEqual(new Vector2(0f, 1f), rect.anchorMin, path);
            Assert.AreEqual(rect.anchorMin, rect.anchorMax, path);
            Assert.LessOrEqual(rect.sizeDelta.x, 360f, path);
        }
    }

    private EchoHudPresenter CreateFeedbackPresenter()
    {
        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        Assert.NotNull(prefab);
        _instance = Object.Instantiate(prefab);
        EchoHudPresenter presenter = _instance.GetComponent<EchoHudPresenter>();
        presenter.Initialize(_instance.GetComponent<EchoHudView>(), null);
        return presenter;
    }

    private static SingleContractHudData Feedback(int sequence, string text,
        SingleContractInstantFeedback kind)
    {
        return new SingleContractHudData
        {
            visualState = SingleContractVisualState.Challenge,
            feedbackSequence = sequence,
            instantFeedback = text,
            instantFeedbackKind = kind
        };
    }

    private string FeedbackText()
    {
        return Find("HudDynamicCanvas/FeedbackGroup/Feedback")
            .GetComponent<UnityEngine.UI.Text>().text;
    }

    private GameObject Find(string path)
    {
        Transform target = _instance.transform.Find(path);
        Assert.NotNull(target, path);
        return target.gameObject;
    }

    private void AssertTextFits(string path, bool largeText)
    {
        UnityEngine.UI.Text text = Find(path)
            .GetComponent<UnityEngine.UI.Text>();
        Assert.IsTrue(text.gameObject.activeSelf, path);
        Assert.LessOrEqual(text.preferredWidth,
            text.rectTransform.rect.width + 0.5f,
            path + " must not wrap at largeText=" + largeText + ".");
        Assert.LessOrEqual(text.preferredHeight,
            text.rectTransform.rect.height + 0.5f,
            path + " must not clip at largeText=" + largeText + ".");
    }
}
