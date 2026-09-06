using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class SingleContractRuntimeTests
{
    private readonly List<GameObject> _createdObjects =
        new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = _createdObjects.Count - 1; i >= 0; i--)
        {
            if (_createdObjects[i] != null)
                Object.DestroyImmediate(_createdObjects[i]);
        }
        _createdObjects.Clear();
    }

    [UnityTest]
    public IEnumerator SingleContractDefaultAndFrozenConfigurationSurvivesFrames()
    {
        GameManager gameManager = CreateInactiveComponent<GameManager>(
            "SingleContractGameManager");
        Assert.AreEqual(GameplayFlowMode.SingleContract,
            gameManager.ConfiguredGameplayFlowMode,
            "SingleContract must be the shipping default after the migration gate passes.");

        var configured = new SingleContractValidationConfig
        {
            enabled = true,
            fixedSeed = 424242,
            freezeDirector = true,
            disablePowerUps = true,
            forceStandardDifficulty = true,
            useFixedIdentity = true
        };
        Assert.IsTrue(gameManager.TryConfigureGameplayFlow(
            GameplayFlowMode.SingleContract, configured));
        InvokePrivate(gameManager, "FreezeGameplayFlowConfiguration");
        SetAutoProperty(gameManager, "State", GameState.Playing);

        configured.fixedSeed = -1;
        Assert.IsFalse(gameManager.TryConfigureGameplayFlow(
            GameplayFlowMode.SixPhaseLegacy),
            "A run configuration cannot be changed after play starts.");

        yield return null;
        yield return null;

        SingleContractValidationConfig frozen =
            gameManager.ActiveSingleContractValidationConfig;
        Assert.AreEqual(GameplayFlowMode.SingleContract,
            gameManager.ActiveGameplayFlowMode);
        Assert.IsTrue(gameManager.IsSingleContractRun);
        Assert.IsTrue(frozen.enabled);
        Assert.AreEqual(424242, frozen.fixedSeed);
        Assert.IsTrue(frozen.freezeDirector);
        Assert.IsTrue(frozen.disablePowerUps);
        Assert.IsTrue(frozen.forceStandardDifficulty);
        Assert.IsTrue(frozen.useFixedIdentity);
        Assert.AreEqual(RunDifficultyLevel.Standard,
            gameManager.ActiveRunDifficulty);
    }

    [UnityTest]
    public IEnumerator LaneChangeAfterLockLineCannotRewriteCommittedChoice()
    {
        SingleContractFlow flow = CreateFixedFlow(222);
        PredictionGateDefinition gate = flow.GetGate(0).Definition;
        int counterLane = FindLaneForRole(gate,
            PredictionGateRole.Counter);
        int lateLane = (counterLane + 1) % 3;

        flow.Tick(Frame(12f, gate.presentationDistance, 18f,
            counterLane));
        yield return null;
        flow.Tick(Frame(13f, gate.commitDistance, 18f, counterLane));
        yield return null;
        flow.Tick(Frame(14f, gate.resolveDistance, 18f, lateLane));
        yield return null;

        PredictionGateController runtimeGate = flow.GetGate(0);
        Assert.AreEqual(counterLane,
            runtimeGate.CommittedChoice.physicalLane);
        Assert.AreEqual(PredictionGateRole.Counter,
            runtimeGate.CommittedRole);
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.TryCommitChoice(new GateChoice
            {
                gateId = gate.gateId,
                physicalLane = lateLane,
                routeDistance = gate.resolveDistance
            }));
        Assert.AreEqual(counterLane,
            runtimeGate.CommittedChoice.physicalLane);

        flow.Tick(Frame(15f, gate.exitDistance, 18f, lateLane));
        yield return null;

        Assert.AreEqual(1, flow.SettlementCount);
        PredictionGateSettlement settlement = flow.GetSettlement(0);
        Assert.AreEqual(PredictionGateRole.Counter,
            settlement.chosenRole);
        Assert.AreEqual(GateExecutionOutcome.Hit,
            settlement.execution,
            "Leaving the locked obstacle route is an execution failure, not a cancellation.");
        Assert.AreEqual(0f, settlement.playerLeadMeters, 0.0001f);
        Assert.Greater(settlement.echoLeadMeters, 0f);
    }

    [UnityTest]
    public IEnumerator CounterSuccessTriggerSweepAndExitRewardOnlyOnce()
    {
        SingleContractFlow flow = CreateFixedFlow(431);
        PredictionGateDefinition gate = flow.GetGate(0).Definition;
        int counterLane = FindLaneForRole(gate,
            PredictionGateRole.Counter);

        flow.Tick(Frame(12f, gate.presentationDistance, 20f,
            counterLane));
        yield return null;
        flow.Tick(Frame(13f, gate.commitDistance, 20f, counterLane));
        yield return null;

        PredictionGateObstacleTag tag = CreateGateObstacleTag(
            flow.RunSequence, gate, counterLane);
        GateObstacleEvent triggerReport = EventFor(
            tag.Binding, 101, gate.resolveDistance);
        Assert.AreEqual(GateTransitionResult.Applied,
            flow.ResolveObstaclePassed(triggerReport),
            "The first trigger report must settle the gate.");
        float leadAfterTrigger = flow.AccumulatedSignedLeadMeters;

        yield return null;

        GateObstacleEvent sweepReport = EventFor(
            tag.Binding, 102, gate.resolveDistance + 0.1f);
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstaclePassed(sweepReport),
            "A sweep duplicate must not settle the same gate again.");
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstacleHit(sweepReport),
            "A conflicting duplicate cannot overwrite the first result.");
        flow.Tick(Frame(14f, gate.exitDistance, 20f,
            (counterLane + 1) % 3));
        yield return null;

        Assert.AreEqual(1, flow.SettlementCount);
        Assert.Greater(leadAfterTrigger, 0f);
        Assert.AreEqual(leadAfterTrigger,
            flow.AccumulatedSignedLeadMeters, 0.0001f);
        Assert.IsTrue(flow.TryConsumeSettlement(0,
            out PredictionGateSettlement settlement));
        Assert.IsFalse(flow.TryConsumeSettlement(0, out _));
        Assert.AreEqual(PredictionGateRole.Counter,
            settlement.chosenRole);
        Assert.AreEqual(GateExecutionOutcome.Success,
            settlement.execution);
    }

    [UnityTest]
    public IEnumerator CounterCollisionRecordsCounterChoiceAndFailedExecution()
    {
        SingleContractFlow flow = CreateFixedFlow(432);
        PredictionGateDefinition gate = flow.GetGate(0).Definition;
        int counterLane = FindLaneForRole(gate,
            PredictionGateRole.Counter);
        flow.Tick(Frame(12f, gate.presentationDistance, 20f,
            counterLane));
        yield return null;
        flow.Tick(Frame(13f, gate.commitDistance, 20f, counterLane));
        yield return null;

        GateObstacleEvent collision = EventFor(
            CreateGateObstacleTag(flow.RunSequence, gate, counterLane)
                .Binding,
            201, gate.resolveDistance);
        Assert.AreEqual(GateTransitionResult.Applied,
            flow.ResolveObstacleHit(collision));
        yield return null;

        GateAttempt attempt = flow.GetGate(0).BuildAttempt();
        PredictionGateSettlement settlement = flow.GetSettlement(0);
        Assert.AreEqual(PredictionGateRole.Counter,
            attempt.chosenRole,
            "The chosen counter route remains a counter after collision.");
        Assert.AreEqual(StrategyKey.AvoidOriginal,
            attempt.strategyKey);
        Assert.AreEqual(GateExecutionOutcome.Hit,
            attempt.execution,
            "Collision is an execution failure, not a prediction match.");
        Assert.Less(settlement.signedLeadMeters, 0f);
        Assert.IsFalse(settlement.IsCounterSuccess);
    }

    [UnityTest]
    public IEnumerator MagnetCoinCollectionCannotCommitPresentedGate()
    {
        SingleContractFlow flow = CreateFixedFlow(511);
        PredictionGateDefinition gate = flow.GetGate(0).Definition;
        flow.Tick(Frame(12f, gate.presentationDistance, 18f, 1));
        Assert.AreEqual(PredictionGateLifecycle.Presented,
            flow.GetGate(0).State);

        PowerUpController powerUp =
            CreateInactiveComponent<PowerUpController>("MagnetPowerUp");
        SetAutoProperty(powerUp, "ActivePowerUp", PowerUpId.Magnet);
        SetAutoProperty(powerUp, "TimeRemaining", 10f);
        Assert.IsTrue(powerUp.HasMagnet);

        Coin coin = CreateGameObject("MagnetCoin").AddComponent<Coin>();
        coin.ConfigureEchoContractMarker(false);
        AIShadowRunner runner =
            CreateInactiveComponent<AIShadowRunner>("SingleContractRunner");
        SetField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        SetField(runner, "_singleContractFlow", flow);
        SetAutoProperty(runner, "HasActiveOpponent", true);

        runner.RecordCoin(coin.IsEchoContractMarker,
            coin.EchoChallengeStepId);
        yield return null;

        PredictionGateController runtimeGate = flow.GetGate(0);
        Assert.AreEqual(PredictionGateLifecycle.Presented,
            runtimeGate.State);
        Assert.IsFalse(runtimeGate.HasChoice,
            "Coin collection, including magnet collection, cannot lock a gate lane.");
        Assert.AreEqual(0, flow.SettlementCount);
    }

    [UnityTest]
    public IEnumerator ShieldAbsorbsDamageWithoutChangingGateHitToSuccess()
    {
        SingleContractFlow flow = CreateFixedFlow(612);
        PredictionGateDefinition gate = flow.GetGate(0).Definition;
        int counterLane = FindLaneForRole(gate,
            PredictionGateRole.Counter);
        flow.Tick(Frame(12f, gate.presentationDistance, 20f,
            counterLane));
        yield return null;
        flow.Tick(Frame(13f, gate.commitDistance, 20f, counterLane));

        PredictionGateObstacleTag tag = CreateGateObstacleTag(
            flow.RunSequence, gate, counterLane);
        Assert.AreEqual(GateTransitionResult.Applied,
            flow.ResolveObstacleHit(EventFor(tag.Binding, 301,
                gate.resolveDistance)));

        PowerUpController shield =
            CreateInactiveComponent<PowerUpController>("ShieldPowerUp");
        SetAutoProperty(shield, "ActivePowerUp", PowerUpId.Shield);
        SetField(shield, "_shieldCharges", 1);
        Assert.IsTrue(shield.TryAbsorbCollision(),
            "The shield should absorb the physical damage after contact is recorded.");
        yield return null;

        GateAttempt attempt = flow.GetGate(0).BuildAttempt();
        PredictionGateSettlement settlement = flow.GetSettlement(0);
        Assert.AreEqual(PredictionGateRole.Counter,
            attempt.chosenRole);
        Assert.AreEqual(GateExecutionOutcome.Hit,
            attempt.execution,
            "Shield survival must not turn a collision into counter success.");
        Assert.IsFalse(settlement.IsCounterSuccess);
        Assert.Less(settlement.signedLeadMeters, 0f);
    }

    [UnityTest]
    public IEnumerator SingleContractFramesKeepLegacyPhaseMeterAndBonusDormant()
    {
        SingleContractFlow flow = CreateFixedFlow(713);
        AIShadowRunner runner =
            CreateInactiveComponent<AIShadowRunner>("DormantLegacyRunner");
        SetField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        SetField(runner, "_singleContractFlow", flow);
        SetAutoProperty(runner, "HasActiveOpponent", true);

        Assert.IsNull(GetField(runner, "_duelFlow"));
        Assert.IsNull(GetField(runner, "_contractEvaluator"));
        Assert.AreEqual(0f, runner.DuelPhaseProgress);
        Assert.IsNull(runner.ActiveContract);

        RectTransform viewport;
        GameObject hud = CreateHud(new Vector2(1920f, 1080f),
            out viewport);
        EchoHudView view = hud.GetComponent<EchoHudView>();
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Challenge,
                memory = "回声记忆：压力下偏向右侧",
                showPrediction = true,
                predictedLane = 2,
                leadMeters = 0f,
                injuries = 1,
                finishRemaining = 700f,
                powerUp = "",
                instantFeedback = SingleContractInstantFeedback.None
            });

        for (int frame = 0; frame < 3; frame++)
        {
            flow.Tick(Frame(frame + 1f, 0f, 18f, 1));
            view.PresentSingleContract(data, frame == 0);
            yield return null;
        }

        Assert.IsFalse(Find(hud, "HudStaticCanvas/StageRail")
            .activeInHierarchy);
        Assert.IsFalse(Find(hud, "HudDynamicCanvas/MeterGroup")
            .activeInHierarchy);
        Assert.IsFalse(Find(hud, "HudStaticCanvas/MarkerGroup")
            .activeInHierarchy);
        Assert.IsFalse(Find(hud, "HudStaticCanvas/SyncGroup")
            .activeInHierarchy);
        Component injuriesText = (Component)GetField(view,
            "calibrationObservationText");
        Assert.IsTrue(injuriesText.gameObject.activeInHierarchy,
            "The single-contract injury counter must remain visible without the old calibration/phase UI.");
        Assert.IsNull(typeof(SingleContractHudData).GetField("phaseIndex"));
        Assert.IsNull(typeof(SingleContractHudData).GetField("stability"));
        Assert.IsNull(typeof(SingleContractHudData).GetField(
            "contractMarkerCount"));
        Assert.AreEqual(0, flow.SettlementCount);
        Assert.AreEqual(0f, flow.AccumulatedSignedLeadMeters);
    }

    [UnityTest]
    public IEnumerator PredictionStaysVisibleBetweenGatesAndAdvancesToNextGate()
    {
        SingleContractFlow flow = CreateFixedFlow(714);
        AIShadowRunner runner =
            CreateInactiveComponent<AIShadowRunner>("PredictionHudRunner");
        SetField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        SetField(runner, "_singleContractFlow", flow);
        SetField(runner, "_frozenSingleContractIdentity",
            SingleContractValidationIdentity.Create());
        SetAutoProperty(runner, "HasActiveOpponent", true);

        Assert.IsTrue(runner.ShowSingleContractPrediction);
        Assert.AreEqual(1,
            runner.CurrentSingleContractPredictionGateNumber);
        Assert.IsFalse(runner.IsCurrentSingleContractPredictionGateActive,
            "Before the gate window the HUD should label it as the next gate.");

        PredictionGateDefinition first = flow.GetGate(0).Definition;
        int predictedLane = FindLaneForRole(first,
            PredictionGateRole.Predicted);
        flow.Tick(Frame(12f, first.presentationDistance, 20f,
            predictedLane));
        yield return null;

        Assert.IsTrue(runner.ShowSingleContractPrediction);
        Assert.AreEqual(1,
            runner.CurrentSingleContractPredictionGateNumber);
        Assert.IsTrue(runner.IsCurrentSingleContractPredictionGateActive);

        flow.Tick(Frame(13f, first.commitDistance, 20f,
            predictedLane));
        flow.Tick(Frame(14f, first.exitDistance, 20f,
            predictedLane));
        yield return null;

        Assert.AreEqual(PredictionGateLifecycle.Closed,
            flow.GetGate(0).State);
        Assert.IsTrue(runner.ShowSingleContractPrediction,
            "Closing one gate must not make the red prediction disappear.");
        Assert.AreEqual(2,
            runner.CurrentSingleContractPredictionGateNumber);
        Assert.IsFalse(runner.IsCurrentSingleContractPredictionGateActive,
            "Between windows the HUD should immediately preview gate two.");
    }

    [UnityTest]
    public IEnumerator OpeningMemoryKeepsRunShellVisibleThenRestoresSignals()
    {
        RectTransform viewport;
        GameObject hud = CreateHud(new Vector2(1920f, 1080f),
            out viewport);
        EchoHudView view = hud.GetComponent<EchoHudView>();
        SingleContractHudData opening =
            EchoRunPresentation.BuildSingleContractHud(
                new SingleContractHudInput
                {
                    visualState = SingleContractVisualState.Challenge,
                    openingMemory = true,
                    openingReplay = true,
                    openingReplayAction = ShadowAction.Slide,
                    openingReplayCount = 4,
                    generation = 4,
                    memory = "压力出现时，你偏向右侧",
                    showPrediction = true,
                    predictedLane = 2,
                    injuries = 1,
                    finishRemaining = 900f,
                    powerUp = "护盾",
                    instantFeedback =
                        SingleContractInstantFeedback.PredictionHit
                });

        view.PresentSingleContract(opening, true);
        yield return null;

        Assert.IsTrue(Find(hud, "HudDynamicCanvas/Directive")
            .activeInHierarchy);
        var directive = (UnityEngine.UI.Text)GetField(view,
            "directiveText");
        var announcement = (UnityEngine.UI.Text)GetField(view,
            "announcementText");
        Assert.AreEqual("第4代回声现身", announcement.text);
        Assert.AreEqual("正在重演：上一局滑铲×4", directive.text);
        Assert.IsTrue(Find(hud, "HudDynamicCanvas/Announcement")
            .activeInHierarchy);
        foreach (string visiblePath in new[]
                 {
                     "HudStaticCanvas/StatsPlate",
                     "HudStaticCanvas/DistancePlate",
                     "HudStaticCanvas/CalibrationRail",
                     "HudStaticCanvas/LeadGroup",
                     "HudDynamicCanvas/PauseButton"
                 })
            Assert.IsTrue(Find(hud, visiblePath).activeInHierarchy,
                visiblePath + " must be visible on the first run frame.");
        foreach (string hiddenPath in new[]
                 {
                     "HudDynamicCanvas/Prediction",
                     "HudDynamicCanvas/FeedbackGroup/Feedback",
                     "HudDynamicCanvas/BuffGroup"
                 })
            Assert.IsFalse(Find(hud, hiddenPath).activeInHierarchy,
                hiddenPath + " must stay hidden behind opening memory.");

        SingleContractHudData challenge =
            EchoRunPresentation.BuildSingleContractHud(
                new SingleContractHudInput
                {
                    visualState = SingleContractVisualState.Challenge,
                    generation = 4,
                    memory = "压力出现时，你偏向右侧",
                    showPrediction = true,
                    predictionGateActive = true,
                    predictedLane = 2,
                    injuries = 1,
                    finishRemaining = 900f,
                    powerUp = "护盾",
                    instantFeedback =
                        SingleContractInstantFeedback.PredictionHit
                });
        view.PresentSingleContract(challenge, true);
        view.ShowFeedback(challenge.instantFeedback, Color.white, true);
        yield return null;

        foreach (string restoredPath in new[]
                 {
                     "HudStaticCanvas/StatsPlate",
                     "HudStaticCanvas/DistancePlate",
                     "HudStaticCanvas/CalibrationRail",
                     "HudStaticCanvas/LeadGroup",
                     "HudDynamicCanvas/Prediction",
                     "HudDynamicCanvas/FeedbackGroup/Feedback",
                     "HudDynamicCanvas/BuffGroup",
                     "HudDynamicCanvas/PauseButton"
                 })
            Assert.IsTrue(Find(hud, restoredPath).activeInHierarchy,
                restoredPath + " must be restored after opening memory.");
        Assert.IsFalse(Find(hud, "HudDynamicCanvas/Directive")
            .activeInHierarchy,
            "Frozen memory must not remain as a misleading live instruction.");
        Assert.IsFalse(Find(hud, "HudDynamicCanvas/Announcement").activeInHierarchy,
            "The race uses one event message instead of concurrent stage announcements.");
    }

    [UnityTest]
    public IEnumerator FeedbackHideDropsPendingEventsAndNewRunRestartsSequence()
    {
        RectTransform viewport;
        GameObject hud = CreateHud(new Vector2(1920f, 1080f), out viewport);
        EchoHudView view = hud.GetComponent<EchoHudView>();
        EchoHudPresenter presenter = hud.GetComponent<EchoHudPresenter>();
        presenter.Initialize(view, null);
        yield return null;

        var data = new SingleContractHudData
        {
            visualState = SingleContractVisualState.Challenge,
            feedbackSequence = 1,
            instantFeedback = "反制通过",
            instantFeedbackKind = SingleContractInstantFeedback.RewriteSucceeded
        };
        presenter.PresentSingleContractFeedback(data, Time.unscaledTime);
        GameObject feedback = Find(hud, "HudDynamicCanvas/FeedbackGroup");
        Assert.IsTrue(feedback.activeSelf);

        // UIManager disables this hierarchy at pause, then restores it.
        // Let real MonoBehaviour callbacks perform the reset in PlayMode.
        hud.SetActive(false);
        Assert.IsFalse(feedback.activeSelf,
            "OnDisable must clear the message and its backing together.");
        yield return null;
        hud.SetActive(true);
        yield return null;
        data.feedbackSequence = 2;
        data.instantFeedback = "暂停前尚未显示的结果";
        presenter.PresentSingleContractFeedback(data, Time.unscaledTime);
        Assert.IsFalse(feedback.activeSelf,
            "The latest event missed before suspension must be consumed silently.");

        data.feedbackSequence = 3;
        data.instantFeedback = "恢复后的新结果";
        presenter.PresentSingleContractFeedback(data, Time.unscaledTime);
        Assert.IsTrue(feedback.activeSelf);
        hud.SetActive(false);
        hud.SetActive(true);
        presenter.ResetRun();
        data.feedbackSequence = 1;
        data.instantFeedback = "新局首个结果";
        presenter.PresentSingleContractFeedback(data, Time.unscaledTime);
        Assert.IsTrue(feedback.activeSelf,
            "The explicit new-run boundary must allow sequence one again.");
        Assert.AreEqual(data.instantFeedback,
            Find(hud, "HudDynamicCanvas/FeedbackGroup/Feedback")
                .GetComponent<UnityEngine.UI.Text>().text);
    }

    [UnityTest]
    public IEnumerator OpeningMemoryFallbackUsesSeparateTitleAndBodyRows()
    {
        RectTransform viewport;
        GameObject hud = CreateHud(new Vector2(1920f, 1080f),
            out viewport);
        EchoHudView view = hud.GetComponent<EchoHudView>();
        SingleContractHudData opening =
            EchoRunPresentation.BuildSingleContractHud(
                new SingleContractHudInput
                {
                    visualState = SingleContractVisualState.Challenge,
                    openingMemory = true,
                    generation = 7,
                    memory = "压力出现时，你偏向左侧",
                    injuries = 0,
                    finishRemaining = 900f
                });

        view.PresentSingleContract(opening, false);
        Canvas.ForceUpdateCanvases();
        yield return null;

        var announcement = (UnityEngine.UI.Text)GetField(view,
            "announcementText");
        var directive = (UnityEngine.UI.Text)GetField(view,
            "directiveText");
        Assert.AreEqual("第7代回声现身", announcement.text);
        Assert.AreEqual("领先它到终点", directive.text);
        Assert.IsTrue(announcement.gameObject.activeInHierarchy);
        Assert.IsTrue(directive.gameObject.activeInHierarchy);
        Assert.LessOrEqual(announcement.preferredHeight,
            announcement.rectTransform.rect.height + 0.5f);
        Assert.LessOrEqual(directive.preferredHeight,
            directive.rectTransform.rect.height + 0.5f);
    }

    [UnityTest]
    public IEnumerator SingleContractSchedulesFinishExactlyOnceForWholeRun()
    {
        GameManager gameManager = CreateInactiveComponent<GameManager>(
            "FinishOwnerGameManager");
        SetAutoProperty(gameManager, "State", GameState.Playing);
        SetAutoProperty(gameManager, "ActiveGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        SetAutoProperty(gameManager, "CourseDistance", 950f);
        SetAutoProperty(gameManager, "CurrentSpeed", 20f);
        SetAutoProperty(gameManager, "Distance", 120f);
        SetAutoProperty(gameManager, "RunElapsed", 12f);
        SetAutoProperty(gameManager, "FinishScheduleCount", 1);

        float originalDistance = gameManager.CourseDistance;
        for (int frame = 0; frame < 3; frame++)
        {
            Assert.AreEqual(originalDistance,
                gameManager.ScheduleCourseFinishAfter(5f + frame),
                0.0001f);
            yield return null;
        }

        Assert.AreEqual(originalDistance,
            gameManager.CourseDistance, 0.0001f);
        Assert.AreEqual(1, gameManager.FinishScheduleCount,
            "Legacy phase boundaries cannot reschedule a single-contract finish.");
    }

    [UnityTest]
    public IEnumerator FinishReachedWithPhysicalLeadWinsImmediately()
    {
        SingleContractFlow flow = CreateFixedFlow(814);
        yield return null;

        RunSettlement result = flow.FinishRun(
            RunEndReason.FinishReached, 0.25f);

        Assert.IsTrue(result.reachedFinish);
        Assert.IsTrue(result.playerWon);
        Assert.AreEqual(0.25f, result.playerLeadMeters, 0.0001f);
    }

    [UnityTest]
    public IEnumerator FinishReachedWhilePhysicallyBehindLosesImmediately()
    {
        SingleContractFlow flow = CreateFixedFlow(815);
        yield return null;

        RunSettlement result = flow.FinishRun(
            RunEndReason.FinishReached, -0.01f);

        Assert.IsTrue(result.reachedFinish);
        Assert.IsFalse(result.playerWon);
        Assert.AreEqual(-0.01f, result.playerLeadMeters, 0.0001f);
    }

    [UnityTest]
    public IEnumerator FailedRetryKeepsContractButClearsRelearnAndGateState()
    {
        const int seed = 916;
        var frozenIdentity = new ActiveEchoIdentity
        {
            generation = 3,
            identityId = "echo-retry-fixed",
            parentIdentityId = "echo-retry-parent",
            pace = 20f,
            memoryContract = new EchoMemoryContract
            {
                contractId = "route-retry-fixed",
                identityId = "echo-retry-fixed",
                preferredLane = 2,
                confidence = 1f,
                evidenceCount = 5
            }
        };
        var retryReference = new EchoRetryState
        {
            identityId = frozenIdentity.identityId,
            contractId = frozenIdentity.memoryContract.contractId,
            attemptCount = 1
        };
        retryReference.Normalize();
        SingleContractFlow failedRun = CreateFixedFlow(seed);
        PredictionGateDefinition[] originalLayout =
            SnapshotDefinitions(failedRun);

        SettleCounterGate(failedRun, 0, 12f, 20f);
        yield return null;
        SettleCounterGate(failedRun, 1, 26f, 20f);
        Assert.IsTrue(failedRun.RelearnTriggered);
        Assert.AreEqual(2, failedRun.HypothesisVersion);
        failedRun.FinishRun(RunEndReason.Collision, -3f);
        yield return null;

        SingleContractFlow retry = CreateFixedFlow(seed);
        ActiveEchoIdentity reloadedIdentity = frozenIdentity.Clone();
        var retryAdaptation = new RunAdaptationState
        {
            contractId = retryReference.contractId
        };
        Assert.AreEqual(frozenIdentity.identityId,
            reloadedIdentity.identityId);
        Assert.AreEqual(frozenIdentity.memoryContract.contractId,
            reloadedIdentity.memoryContract.contractId);
        Assert.AreEqual(retryReference.contractId,
            retryAdaptation.contractId);
        Assert.IsFalse(retryAdaptation.relearnUsed);
        Assert.AreEqual(0,
            retryAdaptation.consecutiveSuccessfulCounters);
        Assert.AreEqual(0, retryAdaptation.resolvedGateCount);
        AssertDefinitionsEqual(originalLayout,
            SnapshotDefinitions(retry));
        Assert.IsFalse(retry.RelearnTriggered);
        Assert.AreEqual(1, retry.HypothesisVersion);
        Assert.AreEqual(StrategyKey.OriginalHabit,
            retry.PredictedStrategy);
        Assert.AreEqual(0, retry.SettlementCount);
        Assert.AreEqual(0f, retry.AccumulatedSignedLeadMeters);
        for (int i = 0; i < retry.GateCount; i++)
        {
            Assert.AreEqual(PredictionGateLifecycle.Scheduled,
                retry.GetGate(i).State, "Gate " + i);
            Assert.IsFalse(retry.GetGate(i).HasChoice, "Gate " + i);
        }
    }

    [UnityTest]
    public IEnumerator FixedSeedConsecutiveRunsReproduceEveryGateDefinition()
    {
        SingleContractFlow first = CreateFixedFlow(1017);
        SingleContractFlow second = CreateFixedFlow(1017);

        for (int frame = 0; frame < 3; frame++)
        {
            EchoRunFrame sample = Frame(frame + 1f,
                frame * 10f, 18f, frame % 3);
            first.Tick(sample);
            second.Tick(sample);
            yield return null;
        }

        AssertDefinitionsEqual(SnapshotDefinitions(first),
            SnapshotDefinitions(second));
    }

    [UnityTest]
    public IEnumerator AuthoredGateLanesAllHaveAValidRuntimePassPath()
    {
        SingleContractFlow flow = CreateFixedFlow(1118);
        GameObject player = CreateGameObject("GatePassPlayer");
        BoxCollider playerCollider = player.AddComponent<BoxCollider>();

        for (int gateIndex = 0; gateIndex < flow.GateCount; gateIndex++)
        {
            PredictionGateDefinition gate =
                flow.GetGate(gateIndex).Definition;
            var physicalLanes = new HashSet<int>();
            for (int laneIndex = 0; laneIndex < gate.lanes.Length;
                 laneIndex++)
            {
                PredictionGateLane lane = gate.lanes[laneIndex];
                Assert.IsTrue(physicalLanes.Add(lane.physicalLane),
                    "Each physical lane must be authored exactly once.");
                GameObject laneObject = CreateGameObject(
                    "Gate" + gateIndex + "Lane" + lane.physicalLane);
                laneObject.transform.position =
                    new Vector3((lane.physicalLane - 1) * 3.5f, 0f, 0f);

                if (!lane.obstacle.isRequired)
                {
                    Assert.IsNull(laneObject.GetComponent<Obstacle>());
                    continue;
                }

                Assert.AreNotEqual(ObstacleType.Barrier,
                    lane.obstacle.obstacleType,
                    "A required counter route must remain executable by jump or slide.");
                Obstacle obstacle = laneObject.AddComponent<Obstacle>();
                obstacle.type = lane.obstacle.obstacleType;
                BoxCollider obstacleCollider =
                    laneObject.AddComponent<BoxCollider>();
                obstacleCollider.size = ObstacleGeometryRules.ColliderSize(
                    obstacle.type);
                obstacleCollider.center = ObstacleGeometryRules.ColliderCenter(
                    obstacle.type);

                player.transform.position = laneObject.transform.position
                    + (obstacle.type == ObstacleType.High
                        ? Vector3.up * 0.8f
                        : Vector3.up * 0.25f);
                playerCollider.center = Vector3.zero;
                playerCollider.size = obstacle.type == ObstacleType.High
                    ? Vector3.one
                    : new Vector3(1f, 0.5f, 1f);
                Physics.SyncTransforms();
                ObstacleContactEvaluation pass =
                    ObstacleContactRules.Evaluate(obstacle.type,
                        playerCollider.bounds, obstacleCollider.bounds,
                        obstacle.type == ObstacleType.High,
                        obstacle.type == ObstacleType.Low,
                        Vector3.forward);
                Assert.IsTrue(pass.Passed,
                    "Gate " + gate.sequence + " lane "
                    + lane.physicalLane + " has no valid authored pass.");
            }
            Assert.AreEqual(3, physicalLanes.Count);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator PooledGateObstacleClearsOldBindingOnEveryDisable()
    {
        GameObject pooled = CreateGameObject("PooledGateObstacle");
        PredictionGateObstacleTag tag =
            pooled.AddComponent<PredictionGateObstacleTag>();
        tag.Configure(new PredictionGateObstacleBinding
        {
            runId = 7,
            gateId = 3,
            physicalLane = 2,
            obstacleType = ObstacleType.High
        });
        Assert.IsTrue(tag.Binding.IsBound);

        pooled.SetActive(false);
        yield return null;
        Assert.IsFalse(tag.Binding.IsBound);
        Assert.AreEqual(0, tag.Binding.gateId);

        pooled.SetActive(true);
        yield return null;
        tag.Configure(new PredictionGateObstacleBinding
        {
            runId = 8,
            gateId = 4,
            physicalLane = 0,
            obstacleType = ObstacleType.Low
        });
        Assert.AreEqual(4, tag.Binding.gateId);
        pooled.SetActive(false);
        yield return null;

        Assert.IsFalse(tag.Binding.IsBound);
        Assert.AreEqual(0, tag.Binding.runId);
        Assert.AreEqual(0, tag.Binding.gateId,
            "A pooled obstacle cannot retain the prior gate identity.");
    }

    [UnityTest]
    public IEnumerator SingleContractHudSignalsDoNotOverlapAcrossViewports()
    {
        RectTransform viewport;
        GameObject hud = CreateHud(new Vector2(1920f, 1080f),
            out viewport);
        EchoHudView view = hud.GetComponent<EchoHudView>();
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(
            new SingleContractHudInput
            {
                visualState = SingleContractVisualState.Finale,
                memory = "回声记忆：压力下偏向右侧",
                showPrediction = true,
                predictionGateActive = true,
                predictedLane = 2,
                leadMeters = 3.25f,
                injuries = 1,
                finishRemaining = 90f,
                powerUp = "护盾 · 1 次",
                instantFeedback =
                    SingleContractInstantFeedback.EchoRelearned,
                feedbackSequence = 1
            });
        view.PresentSingleContract(data, true);
        view.ShowFeedback(data.instantFeedback, Color.white, true);

        Vector2[] viewports =
        {
            new Vector2(1920f, 1080f),
            new Vector2(1080f, 1920f),
            new Vector2(2560f, 1080f)
        };
        RectTransform pauseButton = Find(hud,
            "HudDynamicCanvas/PauseButton").GetComponent<RectTransform>();
        pauseButton.sizeDelta = new Vector2(104f, 104f);
        string[] signalPaths =
        {
            "HudStaticCanvas/StatsPlate",
            "HudStaticCanvas/DistancePlate",
            "HudStaticCanvas/CalibrationRail",
            "HudStaticCanvas/LeadGroup",
            "HudDynamicCanvas/Prediction",
            "HudDynamicCanvas/FeedbackGroup/Feedback",
            "HudDynamicCanvas/BuffGroup",
            "HudDynamicCanvas/PauseButton"
        };
        Assert.IsFalse(Find(hud, "HudDynamicCanvas/Directive")
            .activeInHierarchy);

        for (int sizeIndex = 0; sizeIndex < viewports.Length; sizeIndex++)
        {
            viewport.sizeDelta = viewports[sizeIndex];
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();

            var visibleSignals = new List<GameObject>();
            for (int i = 0; i < signalPaths.Length; i++)
            {
                GameObject signal = Find(hud, signalPaths[i]);
                Assert.IsTrue(signal.activeInHierarchy,
                    signalPaths[i] + " must be visible in the test state.");
                AssertContained(viewport,
                    signal.GetComponent<RectTransform>(),
                    viewports[sizeIndex], signalPaths[i]);
                visibleSignals.Add(signal);
            }

            for (int left = 0; left < visibleSignals.Count; left++)
            {
                Rect leftRect = RectInViewport(viewport,
                    visibleSignals[left].GetComponent<RectTransform>());
                for (int right = left + 1;
                     right < visibleSignals.Count; right++)
                {
                    Rect rightRect = RectInViewport(viewport,
                        visibleSignals[right]
                            .GetComponent<RectTransform>());
                    Assert.IsFalse(Overlaps(leftRect, rightRect, 0.5f),
                        viewports[sizeIndex] + ": "
                        + visibleSignals[left].name + " overlaps "
                        + visibleSignals[right].name);
                }
            }
        }
    }

    private SingleContractFlow CreateFixedFlow(int seed,
        int originalHabitLane = 2)
    {
        var flow = new SingleContractFlow(
            new SingleContractFixedGateWindowFactory(CreateWindows()),
            originalHabitLane, 1f);
        flow.BeginRun(new EchoRunContext
        {
            mode = GameplayFlowMode.SingleContract,
            hasOpponent = true,
            courseDuration = SingleContractFlow.ChallengeDurationSeconds,
            courseDistance = 950f,
            runSequence = 5,
            runSeed = seed,
            generation = 3,
            validation = new SingleContractValidationConfig
            {
                enabled = true,
                fixedSeed = seed,
                freezeDirector = true,
                disablePowerUps = true,
                forceStandardDifficulty = true
            }
        });
        return flow;
    }

    private static PredictionGateDistanceWindow[] CreateWindows()
    {
        var windows = new PredictionGateDistanceWindow[6];
        for (int i = 0; i < windows.Length; i++)
        {
            float start = 100f * (i + 1);
            windows[i] = new PredictionGateDistanceWindow
            {
                presentationDistance = start,
                commitDistance = start + 10f,
                resolveDistance = start + 20f,
                exitDistance = start + 30f
            };
        }
        return windows;
    }

    private static EchoRunFrame Frame(float elapsedTime,
        float distance, float speed, int lane)
    {
        return new EchoRunFrame
        {
            deltaTime = 1f / 60f,
            elapsedTime = elapsedTime,
            currentSpeed = speed,
            playerDistance = distance,
            remainingDistance = Mathf.Max(0f, 950f - distance),
            playerLane = lane
        };
    }

    private static int FindLaneForRole(
        PredictionGateDefinition definition, PredictionGateRole role)
    {
        for (int i = 0; i < definition.lanes.Length; i++)
        {
            if (definition.lanes[i].role == role)
                return definition.lanes[i].physicalLane;
        }
        Assert.Fail("Requested gate role is missing: " + role);
        return -1;
    }

    private PredictionGateObstacleTag CreateGateObstacleTag(
        int runId, PredictionGateDefinition gate, int lane)
    {
        Assert.IsTrue(gate.TryGetLane(lane,
            out PredictionGateLane laneDefinition));
        GameObject obstacle = CreateGameObject(
            "GateObstacle_" + gate.gateId + "_" + lane);
        PredictionGateObstacleTag tag =
            obstacle.AddComponent<PredictionGateObstacleTag>();
        tag.Configure(new PredictionGateObstacleBinding
        {
            runId = runId,
            gateId = gate.gateId,
            physicalLane = lane,
            obstacleType = laneDefinition.obstacle.obstacleType
        });
        return tag;
    }

    private static GateObstacleEvent EventFor(
        PredictionGateObstacleBinding binding, int obstacleId,
        float routeDistance)
    {
        return new GateObstacleEvent
        {
            gateId = binding.gateId,
            obstacleId = obstacleId,
            physicalLane = binding.physicalLane,
            obstacleType = binding.obstacleType,
            routeDistance = routeDistance
        };
    }

    private static void SettleCounterGate(SingleContractFlow flow,
        int gateIndex, float elapsedTime, float speed)
    {
        PredictionGateDefinition gate =
            flow.GetGate(gateIndex).Definition;
        int lane = FindLaneForRole(gate, PredictionGateRole.Counter);
        Assert.IsTrue(gate.TryGetLane(lane,
            out PredictionGateLane laneDefinition));
        flow.Tick(Frame(elapsedTime, gate.presentationDistance,
            speed, lane));
        flow.Tick(Frame(elapsedTime + 0.5f, gate.commitDistance,
            speed, lane));
        Assert.AreEqual(GateTransitionResult.Applied,
            flow.ResolveObstaclePassed(new GateObstacleEvent
            {
                gateId = gate.gateId,
                obstacleId = 1000 + gateIndex,
                physicalLane = lane,
                obstacleType = laneDefinition.obstacle.obstacleType,
                routeDistance = gate.resolveDistance
            }));
    }

    private static PredictionGateDefinition[] SnapshotDefinitions(
        SingleContractFlow flow)
    {
        var definitions = new PredictionGateDefinition[flow.GateCount];
        for (int i = 0; i < definitions.Length; i++)
            definitions[i] = flow.GetGate(i).Definition;
        return definitions;
    }

    private static void AssertDefinitionsEqual(
        PredictionGateDefinition[] expected,
        PredictionGateDefinition[] actual)
    {
        Assert.AreEqual(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i].runId, actual[i].runId, "runId " + i);
            Assert.AreEqual(expected[i].gateId, actual[i].gateId, "gateId " + i);
            Assert.AreEqual(expected[i].sequence, actual[i].sequence,
                "sequence " + i);
            Assert.AreEqual(expected[i].hypothesisVersion,
                actual[i].hypothesisVersion, "hypothesis " + i);
            Assert.AreEqual(expected[i].predictedStrategy,
                actual[i].predictedStrategy, "strategy " + i);
            Assert.AreEqual(expected[i].isFinal, actual[i].isFinal,
                "final " + i);
            Assert.AreEqual(expected[i].templateKind,
                actual[i].templateKind, "template " + i);
            Assert.AreEqual(expected[i].presentationDistance,
                actual[i].presentationDistance, 0.0001f,
                "presentation " + i);
            Assert.AreEqual(expected[i].commitDistance,
                actual[i].commitDistance, 0.0001f, "commit " + i);
            Assert.AreEqual(expected[i].resolveDistance,
                actual[i].resolveDistance, 0.0001f, "resolve " + i);
            Assert.AreEqual(expected[i].exitDistance,
                actual[i].exitDistance, 0.0001f, "exit " + i);
            Assert.AreEqual(expected[i].lanes.Length,
                actual[i].lanes.Length);
            for (int lane = 0; lane < expected[i].lanes.Length; lane++)
            {
                PredictionGateLane expectedLane = expected[i].lanes[lane];
                PredictionGateLane actualLane = actual[i].lanes[lane];
                Assert.AreEqual(expectedLane.physicalLane,
                    actualLane.physicalLane, "physical lane " + i);
                Assert.AreEqual(expectedLane.role, actualLane.role,
                    "role " + i);
                Assert.AreEqual(expectedLane.strategyKey,
                    actualLane.strategyKey, "lane strategy " + i);
                Assert.AreEqual(expectedLane.attribute,
                    actualLane.attribute, "attribute " + i);
                Assert.AreEqual(expectedLane.obstacle.isRequired,
                    actualLane.obstacle.isRequired, "required " + i);
                Assert.AreEqual(expectedLane.obstacle.obstacleType,
                    actualLane.obstacle.obstacleType, "obstacle " + i);
                Assert.AreEqual(expectedLane.obstacle.prefabIndex,
                    actualLane.obstacle.prefabIndex, "prefab " + i);
                Assert.AreEqual(expectedLane.coinCount,
                    actualLane.coinCount, "coins " + i);
            }
        }
    }

    private GameObject CreateHud(Vector2 size,
        out RectTransform viewport)
    {
        var viewportObject = new GameObject(
            "HudViewport", typeof(RectTransform));
        _createdObjects.Add(viewportObject);
        viewport = viewportObject.GetComponent<RectTransform>();
        viewport.anchorMin = new Vector2(0.5f, 0.5f);
        viewport.anchorMax = new Vector2(0.5f, 0.5f);
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.sizeDelta = size;

        GameObject prefab = Resources.Load<GameObject>("UI/EchoHud");
        Assert.IsNotNull(prefab);
        GameObject instance = Object.Instantiate(
            prefab, viewport, false);
        _createdObjects.Add(instance);
        RectTransform root = instance.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();
        return instance;
    }

    private static GameObject Find(GameObject root, string path)
    {
        Transform target = root.transform.Find(path);
        Assert.IsNotNull(target, path);
        return target.gameObject;
    }

    private static void AssertContained(RectTransform viewport,
        RectTransform child, Vector2 size, string label)
    {
        Rect bounds = RectInViewport(viewport, child);
        Rect viewportRect = viewport.rect;
        const float tolerance = 0.5f;
        Assert.GreaterOrEqual(bounds.xMin,
            viewportRect.xMin - tolerance,
            size + ": " + label + " extends left of the viewport.");
        Assert.LessOrEqual(bounds.xMax,
            viewportRect.xMax + tolerance,
            size + ": " + label + " extends right of the viewport.");
        Assert.GreaterOrEqual(bounds.yMin,
            viewportRect.yMin - tolerance,
            size + ": " + label + " extends below the viewport.");
        Assert.LessOrEqual(bounds.yMax,
            viewportRect.yMax + tolerance,
            size + ": " + label + " extends above the viewport.");
    }

    private static Rect RectInViewport(RectTransform viewport,
        RectTransform child)
    {
        Bounds bounds =
            RectTransformUtility.CalculateRelativeRectTransformBounds(
                viewport, child);
        return Rect.MinMaxRect(bounds.min.x, bounds.min.y,
            bounds.max.x, bounds.max.y);
    }

    private static bool Overlaps(Rect left, Rect right,
        float tolerance)
    {
        return left.xMin < right.xMax - tolerance
               && left.xMax > right.xMin + tolerance
               && left.yMin < right.yMax - tolerance
               && left.yMax > right.yMin + tolerance;
    }

    private T CreateInactiveComponent<T>(string name)
        where T : Component
    {
        GameObject host = CreateGameObject(name);
        host.SetActive(false);
        return host.AddComponent<T>();
    }

    private GameObject CreateGameObject(string name)
    {
        var gameObject = new GameObject(name);
        _createdObjects.Add(gameObject);
        return gameObject;
    }

    private static object GetField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, fieldName);
        return field.GetValue(target);
    }

    private static void SetField(object target, string fieldName,
        object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, fieldName);
        field.SetValue(target, value);
    }

    private static void SetAutoProperty(object target,
        string propertyName, object value)
    {
        SetField(target, "<" + propertyName + ">k__BackingField", value);
    }

    private static object InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, methodName);
        return method.Invoke(target, null);
    }
}
