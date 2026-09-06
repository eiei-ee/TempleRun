using System;
using NUnit.Framework;

public sealed class SingleContractHudTests
{
    [Test]
    public void VisualStatesAreIndependentFromLegacyDuelPhases()
    {
        Array values = Enum.GetValues(typeof(SingleContractVisualState));

        Assert.AreEqual(4, values.Length);
        CollectionAssert.AreEqual(new[]
        {
            SingleContractVisualState.Calibration,
            SingleContractVisualState.Challenge,
            SingleContractVisualState.RelearnPulse,
            SingleContractVisualState.Finale
        }, values);

        foreach (var field in typeof(SingleContractHudInput).GetFields())
            Assert.AreNotEqual(typeof(EchoDuelPhase), field.FieldType);
        foreach (var field in typeof(SingleContractHudData).GetFields())
            Assert.AreNotEqual(typeof(EchoDuelPhase), field.FieldType);
    }

    [Test]
    public void ChallengeHudContainsOnlySingleContractSignals()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Challenge,
                memory = "压力下偏向右侧",
                showPrediction = true,
                predictedLane = 2,
                predictionGateNumber = 2,
                predictionGateCount = 6,
                predictionGateActive = true,
                leadMeters = 3.26f,
                injuries = 2,
                finishRemaining = 42.2f,
                powerUp = "护盾",
                instantFeedback = SingleContractInstantFeedback.CounterFailed,
                feedbackLeadDeltaMeters = -4.5f,
                feedbackSequence = 7,
                result = "它还记得同样的你"
            });

        Assert.AreEqual(SingleContractVisualState.Challenge, data.visualState);
        Assert.IsFalse(data.showMemory,
            "Frozen identity memory is an opening card, not a live route instruction.");
        Assert.AreEqual("它记住了：压力下偏向右侧", data.memory);
        Assert.AreEqual("本次预测：右路", data.prediction);
        Assert.AreEqual(SingleContractLeadState.PlayerLeading, data.leadState);
        Assert.AreEqual("玩家领先：3.3米", data.lead);
        Assert.AreEqual(2, data.injuries);
        Assert.AreEqual("伤势 2/2 · 已出局", data.injuriesText);
        Assert.AreEqual(42.2f, data.finishRemaining, 0.0001f);
        Assert.AreEqual("终点距离：43米", data.finishRemainingText);
        Assert.IsTrue(data.showPowerUp);
        Assert.AreEqual("当前补给：护盾", data.powerUp);
        Assert.AreEqual("尝试反制 · 通过未完成", data.instantFeedback);
        Assert.AreEqual(7, data.feedbackSequence);
        Assert.AreEqual("它还记得同样的你", data.result);

        string visible = string.Join("|", new[]
        {
            data.memory, data.prediction, data.lead, data.injuriesText,
            data.finishRemainingText, data.powerUp, data.instantFeedback,
            data.result
        });
        foreach (string forbidden in new[]
                 {
                     "侦测", "暴露", "反抗", "反扑", "阶段轨道",
                     "稳定度", "0/100", "重写覆盖", "契约锁死", "未交锋",
                     "校准", "契约", "正式选择", "草稿", "身份",
                     "采样", "追学", "置信度", "路线认知"
                 })
            StringAssert.DoesNotContain(forbidden, visible);
    }

    [TestCase(0, 0, 0f, "伤势 0/2")]
    [TestCase(1, 2, 1.25f, "伤势 1/2 · 恢复中")]
    [TestCase(1, 2, 0f, "伤势 1/2 · 再受伤即出局")]
    [TestCase(2, 2, 0f, "伤势 2/2 · 已出局")]
    [TestCase(1, 3, 0f, "伤势 1/3")]
    [TestCase(2, 3, 0f, "伤势 2/3 · 再受伤即出局")]
    public void InjuryCopyUsesTheRealLimitAndDistinguishesRecovery(
        int injuries, int maximumCollisionStrikes, float recoveryRemaining,
        string expected)
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Challenge,
                injuries = injuries,
                maximumCollisionStrikes = maximumCollisionStrikes,
                collisionRecoveryTimeRemaining = recoveryRemaining,
                powerUp = "护盾 · 1 次"
            });

        Assert.AreEqual(expected, data.injuriesText);
        Assert.IsTrue(data.showPowerUp,
            "Shield protection stays visible independently of injury capacity.");
        StringAssert.DoesNotContain("再撞", data.injuriesText,
            "Contact during recovery or with a shield need not cause an injury.");
    }

    [Test]
    public void PresenterFollowsRealCollisionRecoveryWithoutCountingProtectedContacts()
    {
        var host = new UnityEngine.GameObject("InjuryHud_Test");
        host.SetActive(false);
        try
        {
            GameManager manager = host.AddComponent<GameManager>();
            typeof(GameManager).GetProperty("State").SetValue(
                manager, GameState.Playing);
            Assert.AreEqual("伤势 0/2",
                EchoHudPresenter.BuildSingleContractHudData(manager, null, "")
                    .injuriesText);

            Assert.IsTrue(manager.TryRecoverFromCollision());
            Assert.AreEqual("伤势 1/2 · 恢复中",
                EchoHudPresenter.BuildSingleContractHudData(manager, null, "")
                    .injuriesText);
            Assert.IsTrue(manager.TryRecoverFromCollision());
            Assert.AreEqual(1, manager.CollisionStrikes,
                "A contact during recovery must not consume another injury.");

            typeof(GameManager).GetMethod("AdvanceRunSpeed",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .Invoke(manager, new object[] { manager.CollisionRecoveryDuration });
            Assert.AreEqual("伤势 1/2 · 再受伤即出局",
                EchoHudPresenter.BuildSingleContractHudData(
                    manager, null, "护盾 · 1 次").injuriesText);
            Assert.IsFalse(manager.TryRecoverFromCollision());
            Assert.AreEqual("伤势 2/2 · 已出局",
                EchoHudPresenter.BuildSingleContractHudData(manager, null, "")
                    .injuriesText);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void OpeningMemoryShowsOnlyGenerationAndFrozenMemoryCopy()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Challenge,
                openingMemory = true,
                generation = 3,
                memory = "压力出现时，你偏向右侧",
                showPrediction = true,
                predictedLane = 2,
                powerUp = "护盾",
                instantFeedback =
                    SingleContractInstantFeedback.RewriteSucceeded
            });

        Assert.IsTrue(data.openingMemory);
        Assert.IsTrue(data.showMemory);
        Assert.AreEqual(3, data.generation);
        Assert.AreEqual("第3代回声现身", data.openingTitle);
        Assert.AreEqual("领先它到终点", data.memory);
        Assert.IsEmpty(data.prediction);
        Assert.IsFalse(data.showPowerUp);
        Assert.AreEqual(SingleContractInstantFeedback.None,
            data.instantFeedbackKind);
        Assert.IsEmpty(data.instantFeedback);
    }

    [Test]
    public void ReliableOpeningReplayNamesTheLearnedActionInTwoShortLines()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Challenge,
                openingMemory = true,
                openingReplay = true,
                openingReplayAction = ShadowAction.Slide,
                openingReplayCount = 4,
                generation = 3,
                memory = "压力出现时，你偏向右侧"
            });

        Assert.IsTrue(data.openingReplay);
        Assert.AreEqual("第3代回声现身", data.openingTitle);
        Assert.AreEqual("正在重演：上一局滑铲×4", data.memory);
        StringAssert.DoesNotContain("\n", data.memory);
    }

    [Test]
    public void WeakOpeningActionFallsBackToFrozenMemoryCopy()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Challenge,
                openingMemory = true,
                openingReplay = true,
                openingReplayAction = ShadowAction.Jump,
                openingReplayCount = 1,
                generation = 2,
                memory = "压力出现时，你偏向左侧"
            });

        Assert.IsFalse(data.openingReplay);
        Assert.AreEqual("第2代回声现身", data.openingTitle);
        Assert.AreEqual("领先它到终点", data.memory);
    }

    [TestCase(SingleContractInstantFeedback.PredictionHit, -3.2f,
        "本次选择符合预测 · 回声 +3.2米")]
    [TestCase(SingleContractInstantFeedback.RewriteSucceeded, 5.4f,
        "反制通过 · 玩家 +5.4米")]
    [TestCase(SingleContractInstantFeedback.SafePass,
        0f, "安全通过 · 距离不变")]
    [TestCase(SingleContractInstantFeedback.CounterFailed, -6.8f,
        "尝试反制 · 通过未完成")]
    [TestCase(SingleContractInstantFeedback.EchoRelearned,
        0f, "后续预测已调整")]
    [TestCase(SingleContractInstantFeedback.ExecutionIncomplete,
        -3f, "通过未完成")]
    [TestCase(SingleContractInstantFeedback.ObservationInconclusive,
        -3f, "通过结果未确认")]
    public void InstantFeedbackSeparatesChoiceFromExecution(
        SingleContractInstantFeedback feedback, float leadDeltaMeters,
        string expected)
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.RelearnPulse,
                instantFeedback = feedback,
                feedbackLeadDeltaMeters = leadDeltaMeters
            });

        Assert.AreEqual(feedback, data.instantFeedbackKind);
        Assert.AreEqual(expected, data.instantFeedback);
    }

    [Test]
    public void CalibrationHidesPredictionAndClampsRunCounters()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Calibration,
                memory = "",
                showPrediction = true,
                predictedLane = 2,
                leadMeters = -0.02f,
                injuries = -3,
                finishRemaining = -12f,
                powerUp = "",
                instantFeedback = SingleContractInstantFeedback.None,
                feedbackSequence = -4
            });

        Assert.AreEqual("AI 正在观察你的跑法", data.memory);
        Assert.IsTrue(data.showMemory,
            "Calibration still needs the memory-building explanation.");
        Assert.IsEmpty(data.prediction);
        Assert.AreEqual(SingleContractLeadState.Tied, data.leadState);
        Assert.AreEqual("并驾齐驱：0.0米", data.lead);
        Assert.AreEqual(0, data.injuries);
        Assert.AreEqual(0f, data.finishRemaining);
        Assert.AreEqual("终点已到达", data.finishRemainingText);
        Assert.IsFalse(data.showPowerUp);
        Assert.AreEqual("当前补给：无", data.powerUp);
        Assert.IsEmpty(data.instantFeedback);
        Assert.AreEqual(0, data.feedbackSequence);
    }

    [Test]
    public void CalibrationHudShowsEveryRealPromotionRequirement()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
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
                    slideSamples = 0,
                    minimumSlideSamples = 2,
                    formalChoices = 2,
                    minimumFormalChoices = 5,
                    successfulChoices = 1,
                    minimumSuccessfulChoices = 3,
                    preferredLane = 2,
                    preferredLaneUnique = true,
                    strongestRouteChoices = 2,
                    minimumStrongestRouteChoices = 3
                }
            });

        Assert.IsTrue(data.showCalibrationProgress);
        Assert.AreEqual("观察 0%", data.calibrationMeterText);
        Assert.AreEqual("AI 学习 12/24 · 主动 4/6 · 种类 1/2",
            data.memory);
        Assert.AreEqual("跳 1/2 · 滑 0/2 · 受伤 1",
            data.calibrationActionProgress);
        Assert.AreEqual("选路 2/5 · 通过 1/3 · 右路2/3",
            data.calibrationRouteProgress);
        Assert.IsEmpty(data.prediction,
            "Calibration progress must not masquerade as an echo prediction.");
    }

    [Test]
    public void CalibrationHudExplainsLowRouteConfidenceAfterCountIsMet()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Calibration,
                calibrationProgress = new SingleContractCalibrationProgress
                {
                    available = true,
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
                    formalChoices = 6,
                    minimumFormalChoices = 5,
                    successfulChoices = 3,
                    minimumSuccessfulChoices = 3,
                    preferredLane = 0,
                    preferredLaneUnique = false,
                    strongestRouteChoices = 3,
                    minimumStrongestRouteChoices = 3,
                    preferredLaneConfidence = 0.5f
                }
            });

        Assert.AreEqual("选路 6/5 · 通过 3/3 · 同路50%/60%",
            data.calibrationRouteProgress);
        Assert.AreEqual(5f / 6f, data.calibrationProgress01, 0.0001f);
    }

    [Test]
    public void ImpreciseMemoryUsesOneShortNonDuplicatedLabel()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Calibration,
                memory = "回声记忆模糊\n你的选择尚未形成稳定模式"
            });

        Assert.AreEqual("AI 正在观察你的跑法", data.memory);
        StringAssert.DoesNotContain("回声记忆：回声记忆", data.memory);
        StringAssert.DoesNotContain("\n", data.memory);
    }

    [Test]
    public void FinaleKeepsEchoLeadAndResultCopyExplicit()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Finale,
                memory = "回声记忆：压力下偏向中间",
                showPrediction = true,
                predictedLane = 1,
                predictionGateNumber = 6,
                predictionGateCount = 6,
                predictionGateActive = true,
                leadMeters = -4.44f,
                finishRemaining = 8f,
                result = "第3代回声胜出"
            });

        Assert.AreEqual("它记住了：压力下偏向中间", data.memory);
        Assert.IsFalse(data.showMemory);
        Assert.AreEqual("本次预测：中路", data.prediction);
        Assert.AreEqual(SingleContractLeadState.EchoLeading, data.leadState);
        Assert.AreEqual("回声领先：4.4米", data.lead);
        Assert.AreEqual("第3代回声胜出", data.result);
    }

    [Test]
    public void UpcomingGateDoesNotKeepAPredictionOnScreen()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Challenge,
                showPrediction = true,
                predictedLane = 2,
                predictionGateActive = false
            });

        Assert.IsEmpty(data.prediction);
    }

    [Test]
    public void SameGateRelearnKeepsTheExecutionResultInOneMessage()
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.RelearnPulse,
                instantFeedback = SingleContractInstantFeedback.RewriteSucceeded,
                feedbackLeadDeltaMeters = 5f,
                feedbackRelearned = true,
                feedbackSequence = 4
            });

        Assert.AreEqual(SingleContractInstantFeedback.RewriteSucceeded,
            data.instantFeedbackKind);
        Assert.IsTrue(data.feedbackRelearned);
        Assert.AreEqual("反制通过 · 玩家 +5.0米 · 后续预测已调整",
            data.instantFeedback);
        StringAssert.DoesNotContain("\n", data.instantFeedback);
        Assert.AreEqual(4, data.feedbackSequence);
    }
}
