using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

public class GameStateTests
{
    [System.Serializable]
    private sealed class ShadowGenerationProbe
    {
        public int generation;
    }

    private readonly List<GameObject> _objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        foreach (GameObject go in _objects)
            if (go != null)
                Object.DestroyImmediate(go);
        _objects.Clear();
    }

    [Test]
    public void StartGameResetsSessionValues()
    {
        GameManager manager = Create<GameManager>("GameManager");
        manager.BuffName = "Shield";
        manager.BuffTimeRemaining = 5f;
        manager.AddCoins(3);
        manager.AddContractMarker();
        GameManager.SetNextRunSeed(424242);

        manager.StartGame();

        Assert.AreEqual(GameState.Playing, manager.State);
        Assert.AreEqual(manager.startSpeed, manager.CurrentSpeed);
        Assert.AreEqual(0, manager.Score);
        Assert.AreEqual(0, manager.Coins);
        Assert.AreEqual(0, manager.ContractMarkerCount);
        Assert.AreEqual(0f, manager.Distance);
        Assert.IsNull(manager.BuffName);
        Assert.AreEqual(0f, manager.BuffTimeRemaining);
        Assert.AreEqual(1f, Time.timeScale);
        Assert.AreEqual(424242, manager.RunSeed);
        Assert.Greater(manager.CourseDistance, 0f);
        Assert.AreEqual(manager.CourseDistance, manager.RemainingDistance);
        Assert.AreEqual(RunEndReason.None, manager.LastEndReason);
        Assert.IsTrue(AIRunTelemetry.IsRecording);
    }

    [Test]
    public void ScheduledFinishDistanceUsesCurrentPaceAndRequestedWindow()
    {
        float distance = GameManager.CalculateScheduledCourseDistance(
            100f, 10f, 20f, 2f, 6f);

        Assert.AreEqual(195f, distance, 0.001f);
    }

    [Test]
    public void ContractMarkersAreCountedSeparatelyFromOrdinaryCoins()
    {
        GameManager manager = Create<GameManager>("GameManager");
        int observedMarkerCount = -1;
        manager.OnContractMarkerChanged.AddListener(
            value => observedMarkerCount = value);

        manager.AddCoins(1);
        manager.AddContractMarker();

        Assert.AreEqual(1, manager.Coins);
        Assert.AreEqual(1, manager.ContractMarkerCount);
        Assert.AreEqual(1, observedMarkerCount);
    }

    [Test]
    public void ReachingCourseDistanceCompletesRunWithFinishReason()
    {
        GameManager manager = Create<GameManager>("GameManager");
        manager.StartGame();

        InvokePrivate(manager, "CompleteCourse");

        Assert.AreEqual(GameState.GameOver, manager.State);
        Assert.AreEqual(RunEndReason.FinishReached, manager.LastEndReason);
        Assert.AreEqual(manager.CourseDistance, manager.Distance);
        Assert.AreEqual(0f, manager.RemainingDistance);
    }

    [Test]
    public void CollisionRecordsFailureBeforeDeathSequenceCompletes()
    {
        GameManager manager = Create<GameManager>("GameManager");
        manager.StartGame();

        manager.GameOver();

        Assert.AreEqual(RunEndReason.Collision, manager.LastEndReason);
        Assert.AreEqual(GameState.Playing, manager.State);
        Assert.IsTrue(manager.IsDeathSequence);
        Assert.IsFalse(AIShadowRunner.IsContractVictory(
            10f, true, true, manager.LastEndReason));
    }

    [Test]
    public void RecoveryWindowProtectsBeforeSecondCollisionEndsRun()
    {
        GameManager manager = Create<GameManager>("GameManager");
        manager.StartGame();

        Assert.IsTrue(manager.TryRecoverFromCollision());
        Assert.AreEqual(1, manager.CollisionStrikes);
        Assert.Greater(manager.CollisionRecoveryTimeRemaining, 0f);

        Assert.IsTrue(manager.TryRecoverFromCollision(),
            "Contacts during resynchronization must not consume another strike.");
        Assert.AreEqual(1, manager.CollisionStrikes);

        InvokePrivate(manager, "AdvanceRunSpeed",
            manager.CollisionRecoveryDuration);
        Assert.IsFalse(manager.TryRecoverFromCollision());
        Assert.AreEqual(2, manager.CollisionStrikes);
    }

    [TestCase(11.68f, 25)]
    [TestCase(16.72f, 1)]
    [TestCase(16.72f, 75)]
    [TestCase(16.72f, 150)]
    [TestCase(20.08f, 25)]
    [TestCase(24f, 25)]
    public void CollisionRecoveryReturnsToNoHitSpeedCurve(
        float preImpactSpeed, int steps)
    {
        GameManager manager = Create<GameManager>("GameManager");
        manager.StartGame();
        SetPrivateField(manager, "<CurrentSpeed>k__BackingField",
            preImpactSpeed);
        float expectedRecoveredSpeed = Mathf.Min(manager.maxSpeed,
            preImpactSpeed + manager.speedIncreaseRate
            * manager.CollisionRecoveryDuration);

        Assert.IsTrue(manager.TryRecoverFromCollision());
        float speedAfterImpact = manager.CurrentSpeed;
        float baselineDistance = 0f;
        float recoveredDistance = 0f;
        float stepDuration = manager.CollisionRecoveryDuration / steps;
        for (int step = 0; step < steps; step++)
        {
            float baselineSpeed = Mathf.Min(manager.maxSpeed,
                preImpactSpeed + manager.speedIncreaseRate
                * stepDuration * (step + 1));
            float recoveryDistanceAdjustment = (float)InvokePrivate(
                manager, "AdvanceRunSpeed", stepDuration);
            baselineDistance += baselineSpeed * stepDuration;
            recoveredDistance += manager.CurrentSpeed * stepDuration
                                 - recoveryDistanceAdjustment;
        }

        Assert.AreEqual(0f, manager.CollisionRecoveryTimeRemaining, 0.0001f);
        Assert.AreEqual(expectedRecoveredSpeed, manager.CurrentSpeed, 0.0001f,
            "Recovery must rejoin the speed curve that existed before impact.");
        float expectedDistanceLoss = 0.5f
            * (preImpactSpeed - speedAfterImpact)
            * manager.CollisionRecoveryDuration;
        Assert.AreEqual(expectedDistanceLoss,
            baselineDistance - recoveredDistance, 0.001f,
            "Recovery distance loss must not depend on frame rate.");
        float minimumConfidenceTwoCounterReward = 2f
            * PredictionGateEvaluator.CounterSuccessPlayerSeconds
            * preImpactSpeed
            * PredictionGateEvaluator.MinimumConfidenceScale;
        Assert.LessOrEqual(baselineDistance - recoveredDistance,
            minimumConfidenceTwoCounterReward,
            "One recoverable collision must remain repayable by two counter successes.");
    }

    [Test]
    public void CollisionRecoveryExposesSyncCountAndDuration()
    {
        GameManager manager = Create<GameManager>("GameManager");
        manager.StartGame();

        Assert.AreEqual(2, manager.SyncRemaining);
        Assert.AreEqual(1.25f, manager.CollisionRecoveryDuration);
        Assert.IsTrue(manager.TryRecoverFromCollision());
        Assert.AreEqual(1, manager.SyncRemaining);
        Assert.AreEqual(manager.CollisionRecoveryDuration,
            manager.CollisionRecoveryTimeRemaining);
    }

    [TestCase(RunEndReason.Collision, true, false, 3, "重新挑战")]
    [TestCase(RunEndReason.FinishReached, true, false, 3, "重新挑战")]
    [TestCase(RunEndReason.FinishReached, true, true, 4, "挑战下一代")]
    [TestCase(RunEndReason.FinishReached, false, false, 0, "继续校准")]
    [TestCase(RunEndReason.FinishReached, false, false, 1, "挑战下一代")]
    public void GameOverActionLabelMatchesSettlementOutcome(
        RunEndReason endReason, bool wasChallenge, bool won,
        int generation, string expected)
    {
        Assert.AreEqual(expected, UIManager.GetGameOverActionLabel(
            endReason, wasChallenge, won, generation));
    }

    [Test]
    public void StartGameIgnoresDuplicateSubmitAfterRunBegins()
    {
        GameManager manager = Create<GameManager>("GameManager");
        GameManager.SetNextRunSeed(424242);

        manager.StartGame();
        manager.AddCoins(3);
        int firstRunSeed = manager.RunSeed;

        manager.StartGame();

        Assert.AreEqual(GameState.Playing, manager.State);
        Assert.AreEqual(firstRunSeed, manager.RunSeed);
        Assert.AreEqual(3, manager.Coins,
            "A duplicate UI submit must not reset an active run.");
    }

    [Test]
    public void RunRandomRepeatsTheSameSequenceForTheSameSeed()
    {
        AIRunRandom.BeginRun(9137);
        float firstValue = AIRunRandom.Value;
        int firstLane = AIRunRandom.Range(0, 3);
        float firstOffset = AIRunRandom.Range(-0.8f, 0.8f);

        AIRunRandom.BeginRun(9137);

        Assert.AreEqual(firstValue, AIRunRandom.Value);
        Assert.AreEqual(firstLane, AIRunRandom.Range(0, 3));
        Assert.AreEqual(firstOffset, AIRunRandom.Range(-0.8f, 0.8f));
    }

    [Test]
    public void BayesianAbilityGainsSkillAndConfidenceFromEvidence()
    {
        var ability = new BayesianAbilityEstimate();
        float initialMean = ability.Mean;
        float initialConfidence = ability.Confidence;

        for (int i = 0; i < 12; i++)
            ability.Observe(true);

        Assert.Greater(ability.Mean, initialMean);
        Assert.Greater(ability.Confidence, initialConfidence);
    }

    [Test]
    public void PlayerSkillProfileSeparatesJumpAndSlideEvidence()
    {
        var profile = new AIPlayerSkillProfile();
        float initialJump = profile.jumping.Mean;
        float initialSlide = profile.sliding.Mean;

        profile.RecordObstacle(ObstacleType.High, true, 0.7f);
        profile.RecordObstacle(ObstacleType.Low, false, 0.4f);

        Assert.Greater(profile.jumping.Mean, initialJump);
        Assert.Less(profile.sliding.Mean, initialSlide);
        Assert.AreEqual(1, profile.reactionSamples);
        Assert.AreEqual(0.7f, profile.reactionProximityMean, 0.0001f);
    }

    [Test]
    public void TrainingSimulatorIsDeterministicAndAccountsForEverySegment()
    {
        var config = new AITrainingSimulationConfig
        {
            seed = 4815,
            episodes = 8,
            segmentsPerEpisode = 25,
            initialPlayerSkill = 0.58f
        };

        AITrainingSimulationResult first =
            AITrainingSimulator.Run(config);
        AITrainingSimulationResult second =
            AITrainingSimulator.Run(config);

        Assert.AreEqual(200, first.totalSegments);
        Assert.AreEqual(first.meanReward, second.meanReward, 0.000001f);
        Assert.AreEqual(first.survivalRate, second.survivalRate, 0.000001f);
        CollectionAssert.AreEqual(first.actionCounts, second.actionCounts);
        CollectionAssert.AreEqual(first.finalWeights, second.finalWeights);

        int decisionCount = 0;
        foreach (int count in first.actionCounts) decisionCount += count;
        Assert.AreEqual(first.totalSegments, decisionCount);
        Assert.That(first.survivalRate, Is.InRange(0f, 1f));
    }

    [Test]
    public void TrainingComparisonUsesTheSameEpisodeBudget()
    {
        var config = new AITrainingSimulationConfig
        {
            seed = 1338,
            episodes = 4,
            segmentsPerEpisode = 30
        };

        AITrainingComparisonResult result =
            AITrainingSimulator.Compare(config);

        Assert.AreEqual("EpsilonGreedy", result.baseline.policyType);
        Assert.AreEqual("LinUCB", result.linUcb.policyType);
        Assert.AreEqual(120, result.baseline.totalSegments);
        Assert.AreEqual(120, result.linUcb.totalSegments);
        Assert.Greater(result.linUcb.meanPolicyUncertainty, 0f);
    }

    [Test]
    public void LinUcbOffersHarderRunsToHigherSkillCohorts()
    {
        var novice = new AITrainingSimulationConfig
        {
            seed = 909,
            episodes = 30,
            segmentsPerEpisode = 50,
            initialPlayerSkill = 0.3f,
            useLinUcb = true
        };
        var expert = new AITrainingSimulationConfig
        {
            seed = 909,
            episodes = 30,
            segmentsPerEpisode = 50,
            initialPlayerSkill = 0.82f,
            useLinUcb = true
        };

        AITrainingSimulationResult noviceResult =
            AITrainingSimulator.Run(novice);
        AITrainingSimulationResult expertResult =
            AITrainingSimulator.Run(expert);

        Assert.Greater(expertResult.meanDifficulty,
            noviceResult.meanDifficulty + 0.05f);
    }

    [Test]
    public void TelemetryRoundTripPreservesDecisionInputsAndReward()
    {
        float[] shadowWeights = { 0.1f, 0.2f };
        float[] directorWeights = { 0.3f, 0.4f, 0.5f };
        const string directorState =
            "{\"version\":1,\"actionPulls\":[1,2,3,4]}";
        const string shadowSequenceState =
            "{\"pairCount\":8,\"transitions\":[1,2,3]}";
        AIRunTelemetry.BeginRun(
            77, 12, 7904, 22, 48, shadowWeights,
            directorWeights, directorState, shadowSequenceState);
        AITrackPlan plan = new AITrackPlan
        {
            intent = AIDirectorIntent.Pressure,
            difficulty = 0.72f,
            obstacleChance = 0.8f,
            coinChance = 0.45f,
            safeLane = 2,
            maxBlockedLanes = 2,
            shouldTurn = true
        };
        float[] context = { 1f, 0.7f, 0.2f, 0.8f, 0.6f };

        int decisionId = AIRunTelemetry.RecordDirectorDecision(
            context, plan, 1, 0.42f, 0.18f, true, 40f, 60f);
        AIRunTelemetry.RecordDirectorActivation(decisionId, 40.2f);
        AIRunTelemetry.RecordDirectorOutcome(decisionId, 0.65f, 49);
        AIRunTelemetry.RecordShadowSample(
            ShadowAction.Jump, 1,
            new[] { 1f, 0f, 0.3f, 0.8f, 0f, 0.66f, 0f, 0f },
            false, 0.72f, (int)ShadowAction.Keep, 0.85f, 0.36f);

        AIRunTelemetryData restored = AIRunTelemetry.FromJson(
            AIRunTelemetry.GetLatestRunJson());

        Assert.AreEqual(AIRunTelemetry.SchemaVersion, restored.schemaVersion);
        Assert.AreEqual(77, restored.seed);
        Assert.AreEqual("0000004D-000012", restored.runId);
        CollectionAssert.AreEqual(
            shadowWeights, restored.shadowWeightsAtStart);
        CollectionAssert.AreEqual(
            directorWeights, restored.directorWeightsAtStart);
        Assert.AreEqual(
            directorState, restored.directorPolicyStateAtStart);
        Assert.AreEqual(
            shadowSequenceState, restored.shadowSequenceStateAtStart);
        Assert.AreEqual(1, restored.directorDecisions.Count);
        Assert.IsTrue(restored.directorDecisions[0].trained);
        Assert.AreEqual((int)AIDirectorIntent.Flow,
            restored.directorDecisions[0].proposedIntent);
        Assert.IsTrue(restored.directorDecisions[0].safetyAdjusted);
        Assert.AreEqual(0.18f,
            restored.directorDecisions[0].policyUncertainty, 0.0001f);
        Assert.IsTrue(restored.directorDecisions[0].activated);
        Assert.AreEqual(40f,
            restored.directorDecisions[0].segmentStartDistance, 0.0001f);
        Assert.AreEqual(60f,
            restored.directorDecisions[0].segmentEndDistance, 0.0001f);
        Assert.AreEqual(40.2f,
            restored.directorDecisions[0].activationDistance, 0.0001f);
        Assert.AreEqual(0.65f, restored.directorDecisions[0].reward, 0.0001f);
        CollectionAssert.AreEqual(context,
            restored.directorDecisions[0].context);
        Assert.AreEqual(1, restored.shadowSamples.Count);
        Assert.AreEqual((int)ShadowAction.Jump,
            restored.shadowSamples[0].action);
        Assert.AreEqual((int)ShadowAction.Keep,
            restored.shadowSamples[0].baseAction);
        Assert.AreEqual(0.36f,
            restored.shadowSamples[0].sequenceInfluence, 0.0001f);
    }

    [Test]
    public void ClearInputEmptiesQueuedSwipes()
    {
        InputManager input = Create<InputManager>("InputManager");
        input.QueueSwipe(SwipeDirection.Left, InputIntentSource.Replay, 1f);
        input.QueueSwipe(SwipeDirection.Up, InputIntentSource.Replay, 1.01f);
        Assert.AreEqual(2, input.PendingInputCount);

        input.ClearInput();

        Assert.AreEqual(0, input.PendingInputCount);
        Assert.IsFalse(input.TryPeekSwipe(1.02f, out _));
    }

    [Test]
    public void ClearInputSuppressesTheStartButtonRelease()
    {
        InputManager input = Create<InputManager>("InputStartReleaseGuard");

        input.ClearInput();

        Assert.IsTrue(GetPrivateField<bool>(
            input, "_suppressUntilPointersReleased"),
            "Starting a run must not reinterpret the UI pointer-up as Jump.");
    }

    [Test]
    public void SwipeThresholdAdaptsToScreenSizeAndDensity()
    {
        Assert.AreEqual(30f,
            InputManager.ResolveSwipeThreshold(30f, 390f, 0f), 0.0001f);
        Assert.AreEqual(54f,
            InputManager.ResolveSwipeThreshold(30f, 1200f, 0f), 0.0001f);
        Assert.AreEqual(56f,
            InputManager.ResolveSwipeThreshold(30f, 800f, 400f), 0.0001f);
    }

    [Test]
    public void IntendedJumpGesturesExceedNinetyPercentClassificationTarget()
    {
        const int samples = 101;
        int jumps = 0;
        for (int i = 0; i < samples; i++)
        {
            float horizontalNoise = Mathf.Lerp(-0.9f, 0.9f,
                i / (float)(samples - 1));
            Vector2 intendedJump = new Vector2(horizontalNoise * 120f, 120f);
            if (InputManager.ClassifySwipe(intendedJump, 30f)
                == SwipeDirection.Up)
                jumps++;
        }

        Assert.Greater((float)jumps / samples, 0.90f,
            "Representative diagonal upward gestures must classify as Jump.");
        Assert.AreEqual(SwipeDirection.Right,
            InputManager.ClassifySwipe(new Vector2(120f, 60f), 30f),
            "A clearly horizontal lane change must remain horizontal.");
    }

    [Test]
    public void SwipeResultReportsAcceptedAndBlockedActions()
    {
        InputManager input = Create<InputManager>("InputManagerFeedback");
        SwipeDirection reportedDirection = SwipeDirection.None;
        bool reportedAccepted = true;
        input.SwipeResolved += (direction, accepted) =>
        {
            reportedDirection = direction;
            reportedAccepted = accepted;
        };

        input.ReportSwipeResult(SwipeDirection.Up, false);

        Assert.AreEqual(SwipeDirection.Up, reportedDirection);
        Assert.IsFalse(reportedAccepted);
    }

    [Test]
    public void ConstrainedPlatformsCapSavedHighFrameRates()
    {
        Assert.AreEqual(30, GameManager.NormalizeFrameRate(30, true));
        Assert.AreEqual(60, GameManager.NormalizeFrameRate(120, true));
        Assert.AreEqual(120, GameManager.NormalizeFrameRate(120, false));
        Assert.AreEqual(60, GameManager.NormalizeFrameRate(75, false));
    }

    [Test]
    public void NativeAndroidAndDesktopWebGlKeepThe120FrameRateOption()
    {
        Assert.IsFalse(GameManager.ShouldConstrainHighFrameRate(
            false, true, false));
        Assert.IsTrue(GameManager.ShouldConstrainHighFrameRate(
            false, true, true));
        Assert.IsFalse(GameManager.ShouldConstrainHighFrameRate(
            true, false, false));
        Assert.IsFalse(GameManager.ShouldConstrainHighFrameRate(
            true, false, true));
    }

    [Test]
    public void TrackBufferUsesRouteProgressInsteadOfWorldDisplacement()
    {
        Assert.IsTrue(TrackSpawnRules.NeedsSegment(120f, 100f, 20f, 10));
        Assert.IsFalse(TrackSpawnRules.NeedsSegment(200f, 100f, 20f, 10));
        Assert.IsTrue(TrackSpawnRules.CanRecycleSegment(80f, 200f, 20f, 5f));
        Assert.IsFalse(TrackSpawnRules.CanRecycleSegment(100f, 200f, 20f, 5f));
    }

    [Test]
    public void TouchLayoutSupportsPortraitWithoutBlockingTheGame()
    {
        Assert.IsFalse(UIManager.ShouldShowLandscapeGuard(720, 1280, true));
        Assert.IsFalse(UIManager.ShouldShowLandscapeGuard(1280, 720, true));
        Assert.IsFalse(UIManager.ShouldShowLandscapeGuard(720, 1280, false));
    }

    [Test]
    public void WeixinPortraitLayoutDoesNotRequestLandscapeGuard()
    {
        Assert.IsFalse(UILayoutRules.ShouldShowLandscapeGuard(
            720, 1280, true, true));
        Assert.IsTrue(UILayoutRules.IsCompactPortrait(720, 1280));
        Assert.IsFalse(UILayoutRules.IsCompactPortrait(1280, 720));
        Assert.AreEqual(new Vector2(1080f, 1920f),
            UILayoutRules.GetReferenceResolution(720, 1280));
        Assert.AreEqual(new Vector2(1920f, 1080f),
            UILayoutRules.GetReferenceResolution(1280, 720));
    }

    [Test]
    public void ShadowCalibrationRejectsPassiveKeepSamples()
    {
        int[] actionCounts = { 24, 0, 0, 0, 0 };

        Assert.IsFalse(AIShadowRunner.HasCalibrationSamples(
            24, 0, actionCounts, 24, 6, 2));
        Assert.AreEqual(0f, AIShadowRunner.CalculateCalibrationProgress(
            24, 0, actionCounts, 24, 6, 2), 0.0001f);
    }

    [Test]
    public void ShadowCalibrationRequiresDiverseActiveActions()
    {
        int[] laneOnlyCounts = { 18, 3, 3, 0, 0 };
        int[] diverseCounts = { 18, 3, 0, 3, 0 };

        Assert.IsFalse(AIShadowRunner.HasCalibrationSamples(
            24, 6, laneOnlyCounts, 24, 6, 2),
            "Repeated lane changes are only one action category.");
        Assert.IsTrue(AIShadowRunner.HasCalibrationSamples(
            24, 6, diverseCounts, 24, 6, 2),
            "Lane changes plus a vertical action provide enough behavioral variety.");
    }

    [Test]
    public void ShadowCalibrationProgressUsesTheWeakestRequirement()
    {
        int[] actionCounts = { 18, 3, 0, 3, 0 };

        Assert.AreEqual(0.5f, AIShadowRunner.CalculateCalibrationProgress(
            24, 3, actionCounts, 24, 6, 2), 0.0001f);
    }

    [Test]
    public void ShadowCanReachCalibrationContractWithinThreeRounds()
    {
        int[] actionCounts = { 0, 0, 0, 0, 0 };
        int totalSamples = 0;
        int activeSamples = 0;

        for (int round = 0; round < 3; round++)
        {
            totalSamples += 8;
            actionCounts[(int)ShadowAction.Keep] += 6;
            actionCounts[(int)(round % 2 == 0
                ? ShadowAction.Jump : ShadowAction.Left)] += 2;
            activeSamples += 2;
        }

        Assert.IsTrue(AIShadowRunner.HasCalibrationSamples(
            totalSamples, activeSamples, actionCounts, 24, 6, 2),
            "Eight representative samples per completed round must calibrate by round three.");
    }

    [Test]
    public void ShadowCalibrationRequiresMinimumJumpAndSlideEvidence()
    {
        int[] missingSlide = { 18, 2, 0, 4, 0 };
        int[] balanced = { 18, 1, 1, 2, 2 };

        Assert.IsFalse(AIShadowRunner.HasCalibrationSamples(
            24, 6, missingSlide, 24, 6, 2, 2, 2));
        Assert.IsTrue(AIShadowRunner.HasCalibrationSamples(
            24, 6, balanced, 24, 6, 2, 2, 2));
        Assert.AreEqual(0f, AIShadowRunner.CalculateCalibrationProgress(
            24, 6, missingSlide, 24, 6, 2, 2, 2));
    }

    [Test]
    public void UiFontIsBundledForRuntime()
    {
        Font font = Resources.Load<Font>("Fonts/EchoRunSansSC-Regular");

        Assert.IsNotNull(font, "The bundled Noto Sans CJK font must be included in runtime builds.");

        const string requiredCharacters =
            "开始游戏设置角色选择主音量一键静音已帧率返回默认红色蓝色绿色金色暗黑距离已暂停继续主页得分最高金币重新新纪录总计校准影子挑战领先落后模仿进化▶";
        foreach (char character in requiredCharacters)
            Assert.IsTrue(font.HasCharacter(character), "UI font is missing: " + character);
    }

    [Test]
    public void AITrackPolicySelectsFlowForNewPlayer()
    {
        AITrackPolicy policy = new AITrackPolicy(1);
        float[] context = { 1f, 0f, 0f, 0f, 0f };

        int action = policy.Select(context, false, 0f);

        Assert.AreEqual(1, action, "The initial model should favor a readable flow pattern.");
    }

    [Test]
    public void AITrackPolicyLearnsFromReward()
    {
        AITrackPolicy policy = new AITrackPolicy(1);
        float[] context = { 1f, 0f, 0f, 0f, 0f };
        float before = policy.Score(3, context);

        policy.Update(3, context, 1f, 0.2f);

        Assert.Greater(policy.Score(3, context), before,
            "A positive play reward must increase the selected strategy score.");
    }

    [Test]
    public void AITrackPolicyWeightsSurviveRoundTrip()
    {
        float[] context = { 1f, 0.8f, 0.1f, 0.9f, 0.6f };
        AITrackPolicy trained = new AITrackPolicy(1);
        for (int i = 0; i < 12; i++)
            trained.Update(3, context, 1f, 0.1f);

        AITrackPolicy restored = new AITrackPolicy(2, trained.ExportWeights());

        Assert.AreEqual(trained.Score(3, context),
            restored.Score(3, context), 0.0001f);
        Assert.AreEqual(trained.Select(context, false, 0f),
            restored.Select(context, false, 0f));
    }

    [Test]
    public void AITrackPolicyRejectsNonFiniteWeightsAndClampsFiniteWeights()
    {
        float[] invalid = new float[AITrackPolicy.ActionCount
                                    * AITrackPolicy.FeatureCount];
        invalid[3] = float.NaN;
        LogAssert.Expect(LogType.Warning,
            "AI director weights were invalid and were reset to defaults.");
        AITrackPolicy reset = new AITrackPolicy(1, invalid);
        Assert.IsFalse(float.IsNaN(reset.Score(0,
            new[] { 1f, 0f, 0f, 0f, 0f })));

        float[] oversized = new float[invalid.Length];
        for (int i = 0; i < oversized.Length; i++) oversized[i] = 99f;
        AITrackPolicy clamped = new AITrackPolicy(1, oversized);
        foreach (float weight in clamped.ExportWeights())
            Assert.AreEqual(3f, weight);
    }

    [Test]
    public void LinUcbStartsFromLegacyFlowPrior()
    {
        var policy = new AILinUcbPolicy(
            new AITrackPolicy(1).ExportWeights());
        float[] context = { 1f, 0f, 0f, 0f, 0f };

        int action = policy.Select(context, 0f);

        Assert.AreEqual(1, action);
        Assert.Greater(policy.LastSelectedUncertainty, 0f);
    }

    [Test]
    public void LinUcbPositiveEvidenceRaisesMeanAndReducesUncertainty()
    {
        var policy = new AILinUcbPolicy();
        float[] context = { 1f, 0.7f, 0.2f, 0.6f, 0.8f };
        float meanBefore = policy.MeanScore(2, context);
        float uncertaintyBefore = policy.Uncertainty(2, context);

        for (int i = 0; i < 20; i++)
            policy.Update(2, context, 1f);

        Assert.Greater(policy.MeanScore(2, context), meanBefore);
        Assert.Less(policy.Uncertainty(2, context), uncertaintyBefore);
    }

    [Test]
    public void LinUcbStateSurvivesJsonRoundTrip()
    {
        float[] context = { 1f, 0.4f, 0.3f, 0.2f, 0.7f };
        var trained = new AILinUcbPolicy();
        for (int i = 0; i < 8; i++)
            trained.Update(1, context, 0.75f);

        var restored = new AILinUcbPolicy(
            null, trained.ExportStateJson());

        Assert.AreEqual(trained.MeanScore(1, context),
            restored.MeanScore(1, context), 0.0001f);
        Assert.AreEqual(trained.Uncertainty(1, context),
            restored.Uncertainty(1, context), 0.0001f);
        CollectionAssert.AreEqual(
            trained.ExportWeights(), restored.ExportWeights());
    }

    [Test]
    public void LinUcbMalformedJsonFallsBackToFiniteDefaults()
    {
        LogAssert.Expect(LogType.Warning,
            new System.Text.RegularExpressions.Regex(
                "LinUCB director state could not be loaded:"));
        var restored = new AILinUcbPolicy(null, "{not-json");
        float[] weights = restored.ExportWeights();

        Assert.AreEqual(AILinUcbPolicy.ActionCount
                        * AILinUcbPolicy.FeatureCount, weights.Length);
        foreach (float weight in weights)
            Assert.IsFalse(float.IsNaN(weight) || float.IsInfinity(weight));
    }

    [Test]
    public void DirectorSafetyLayerCapsRiskWithoutChangingSafeChoices()
    {
        Assert.AreEqual(0, AITrackDirector.ConstrainAction(
            3, 0.2f, 0.1f, true));
        Assert.AreEqual(0, AITrackDirector.ConstrainAction(
            2, 0.8f, 0.1f, false));
        Assert.AreEqual(1, AITrackDirector.ConstrainAction(
            3, 0.2f, 0.9f, false));
        Assert.AreEqual(2, AITrackDirector.ConstrainAction(
            2, 0.2f, 0.1f, false));
    }

    [Test]
    public void DirectorHitStrainDecaysInsteadOfLastingForTheWholeRun()
    {
        Assert.AreEqual(0f, AITrackDirector.CalculateRecentHitStrain(
            100f, float.NegativeInfinity), 0.0001f);
        Assert.AreEqual(0.8f, AITrackDirector.CalculateRecentHitStrain(
            100f, 100f), 0.0001f);
        Assert.AreEqual(0.4f, AITrackDirector.CalculateRecentHitStrain(
            130f, 100f), 0.0001f);
        Assert.AreEqual(0f, AITrackDirector.CalculateRecentHitStrain(
            160f, 100f), 0.0001f);
    }

    [Test]
    public void ArchiveJsonPreservesExistingProgressAndModels()
    {
        EchoRunSaveData original = new EchoRunSaveData
        {
            highScore = 7904,
            totalCoins = 321,
            targetFrameRate = 60,
            shadowProfileJson = "{\"generation\":22,\"sampleCount\":90}",
            directorWeights = new[] { 0.1f, 0.2f, 0.3f },
            directorModelUpdateCount = 48,
            directorPolicyJson = "{\"version\":1,\"actionPulls\":[1,2,3,4]}",
            runSequence = 12,
            lastRunTelemetryJson = "{\"schemaVersion\":1,\"seed\":77}",
            skillProfileJson = "{\"version\":1,\"completedRuns\":4}",
            savedAtUtcTicks = 123456789L
        };

        EchoRunSaveData restored = JsonUtility.FromJson<EchoRunSaveData>(
            JsonUtility.ToJson(original));

        Assert.AreEqual(7904, restored.highScore);
        Assert.AreEqual(22,
            JsonUtility.FromJson<ShadowGenerationProbe>(
                restored.shadowProfileJson).generation);
        CollectionAssert.AreEqual(original.directorWeights, restored.directorWeights);
        Assert.AreEqual(48, restored.directorModelUpdateCount);
        StringAssert.Contains("\"actionPulls\"",
            restored.directorPolicyJson);
        Assert.AreEqual(12, restored.runSequence);
        StringAssert.Contains("\"seed\":77", restored.lastRunTelemetryJson);
        StringAssert.Contains("\"completedRuns\":4", restored.skillProfileJson);
    }

    [Test]
    public void AIShadowPolicyLearnsPlayerActionFromContext()
    {
        AIShadowPolicy policy = new AIShadowPolicy();
        float[] obstacleAhead = { 1f, 0f, 0.4f, 1f, 0f, 0.33f, 0f, 0f };

        for (int i = 0; i < 30; i++)
            policy.Learn((int)ShadowAction.Jump, obstacleAhead, 0.12f);

        Assert.AreEqual((int)ShadowAction.Jump, policy.Predict(obstacleAhead),
            "The behavior clone should reproduce a repeatedly observed jump response.");
        Assert.Greater(policy.Confidence(obstacleAhead), 0.5f);
    }

    [Test]
    public void AIShadowPolicyWeightsSurviveRoundTrip()
    {
        float[] context = { 1f, -1f, 0.2f, 0.8f, 0.5f, 0.66f, 0f, 0f };
        AIShadowPolicy trained = new AIShadowPolicy();
        for (int i = 0; i < 20; i++)
            trained.Learn((int)ShadowAction.Right, context, 0.1f);

        AIShadowPolicy restored = new AIShadowPolicy(trained.ExportWeights());

        Assert.AreEqual(trained.Predict(context), restored.Predict(context));
        Assert.AreEqual(trained.Score((int)ShadowAction.Right, context),
            restored.Score((int)ShadowAction.Right, context), 0.0001f);
    }

    [Test]
    public void AIShadowPolicyRejectsInvalidWeightsAndClampsFiniteWeights()
    {
        int count = AIShadowPolicy.ActionCount * AIShadowPolicy.FeatureCount;
        float[] wrongLength = new float[count - 1];
        float[] nan = new float[count];
        nan[5] = float.NaN;
        float[] infinity = new float[count];
        infinity[7] = float.PositiveInfinity;

        LogAssert.Expect(LogType.Warning,
            "AI shadow weights were invalid and were reset to defaults.");
        CollectionAssert.AreEqual(new AIShadowPolicy().ExportWeights(),
            new AIShadowPolicy(wrongLength).ExportWeights());
        LogAssert.Expect(LogType.Warning,
            "AI shadow weights were invalid and were reset to defaults.");
        CollectionAssert.AreEqual(new AIShadowPolicy().ExportWeights(),
            new AIShadowPolicy(nan).ExportWeights());
        LogAssert.Expect(LogType.Warning,
            "AI shadow weights were invalid and were reset to defaults.");
        CollectionAssert.AreEqual(new AIShadowPolicy().ExportWeights(),
            new AIShadowPolicy(infinity).ExportWeights());

        float[] oversized = new float[count];
        for (int i = 0; i < oversized.Length; i++)
            oversized[i] = i % 2 == 0 ? 10f : -10f;
        foreach (float weight in new AIShadowPolicy(oversized).ExportWeights())
            Assert.That(weight, Is.InRange(-4f, 4f));
    }

    [Test]
    public void ShadowSequencePolicyResolvesAnAmbiguousImmediateDecision()
    {
        AIShadowSequencePolicy policy = new AIShadowSequencePolicy();
        for (int i = 0; i < 24; i++)
            policy.Learn((int)ShadowAction.Jump, (int)ShadowAction.Slide);

        float[] ambiguous = { 0.36f, 0.1f, 0.1f, 0.1f, 0.34f };
        int selected = policy.Predict(ambiguous, (int)ShadowAction.Jump,
            out float sequenceConfidence, out float sequenceInfluence);

        Assert.AreEqual((int)ShadowAction.Slide, selected);
        Assert.Greater(sequenceConfidence, 0.9f);
        Assert.Greater(sequenceInfluence, 0.5f);
    }

    [Test]
    public void ShadowSequencePolicyDefersToAConfidentImmediateDecision()
    {
        AIShadowSequencePolicy policy = new AIShadowSequencePolicy();
        for (int i = 0; i < 24; i++)
            policy.Learn((int)ShadowAction.Jump, (int)ShadowAction.Slide);

        float[] clearContext = { 0.95f, 0.01f, 0.01f, 0.01f, 0.02f };
        int selected = policy.Predict(clearContext, (int)ShadowAction.Jump,
            out _, out float sequenceInfluence);

        Assert.AreEqual((int)ShadowAction.Keep, selected);
        Assert.AreEqual(0f, sequenceInfluence, 0.0001f);
    }

    [Test]
    public void ShadowSequencePolicyStateSurvivesRoundTrip()
    {
        AIShadowSequencePolicy trained = new AIShadowSequencePolicy();
        for (int i = 0; i < 10; i++)
            trained.Learn((int)ShadowAction.Left, (int)ShadowAction.Jump);

        AIShadowSequenceState state = trained.ExportState();
        AIShadowSequencePolicy restored = new AIShadowSequencePolicy(
            state.transitions, state.pairCount);
        float[] ambiguous = { 0.3f, 0.18f, 0.18f, 0.16f, 0.18f };

        Assert.AreEqual(trained.Predict(ambiguous, (int)ShadowAction.Left,
                out _, out _),
            restored.Predict(ambiguous, (int)ShadowAction.Left, out _, out _));
        Assert.AreEqual(trained.PairCount, restored.PairCount);
    }

    [Test]
    public void AIShadowObstacleOutcomeRequiresTheCorrectIndependentAction()
    {
        Assert.IsTrue(AIShadowRunner.CanAvoidObstacle(
            ObstacleType.Low, false, true));
        Assert.IsFalse(AIShadowRunner.CanAvoidObstacle(
            ObstacleType.Low, true, false));
        Assert.IsTrue(AIShadowRunner.CanAvoidObstacle(
            ObstacleType.High, true, false));
        Assert.IsFalse(AIShadowRunner.CanAvoidObstacle(
            ObstacleType.High, false, true));
        Assert.IsFalse(AIShadowRunner.CanAvoidObstacle(
            ObstacleType.Barrier, true, true));
    }

    [Test]
    public void ShadowObstacleReflexUsesOneMutuallyExclusiveVerticalAction()
    {
        Assert.AreEqual(ShadowAction.Slide,
            AIShadowRunner.RequiredActionForObstacle(ObstacleType.Low));
        Assert.AreEqual(ShadowAction.Jump,
            AIShadowRunner.RequiredActionForObstacle(ObstacleType.High));
        Assert.AreEqual(ShadowAction.Keep,
            AIShadowRunner.RequiredActionForObstacle(ObstacleType.Barrier));

        Assert.IsTrue(AIShadowRunner.CanStartVerticalAction(
            ShadowAction.Jump, false, false, false));
        Assert.IsFalse(AIShadowRunner.CanStartVerticalAction(
            ShadowAction.Slide, true, false, false));
        Assert.IsFalse(AIShadowRunner.CanStartVerticalAction(
            ShadowAction.Jump, false, true, false));
    }

    [Test]
    public void ShadowJumpAndSlideCurvesHaveSmoothGroundedEndpoints()
    {
        Assert.AreEqual(0f, AIShadowRunner.EvaluateJumpArc(0f), 0.0001f);
        Assert.AreEqual(1f, AIShadowRunner.EvaluateJumpArc(0.5f), 0.0001f);
        Assert.AreEqual(0f, AIShadowRunner.EvaluateJumpArc(1f), 0.0001f);
        Assert.Less(AIShadowRunner.EvaluateJumpArc(0.01f), 0.002f,
            "The shadow should ease off the ground instead of popping upward.");

        Assert.AreEqual(0f, AIShadowRunner.EvaluateSlideAmount(0f, 0.8f), 0.0001f);
        Assert.Greater(AIShadowRunner.EvaluateSlideAmount(0.4f, 0.8f), 0.99f);
        Assert.Less(AIShadowRunner.EvaluateSlideAmount(0.01f, 0.8f), 0.1f,
            "The shadow should smoothly stand up at the end of a slide.");
    }

    [Test]
    public void SlideColliderLowersTopWithoutMovingBottom()
    {
        GameObject playerObject = new GameObject("player");
        _objects.Add(playerObject);
        CapsuleCollider capsule = playerObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1.1f, 0f);
        capsule.height = 2.2f;
        capsule.radius = 0.4f;
        PlayerController player = playerObject.AddComponent<PlayerController>();
        player.slideColliderHeight = 0.9f;
        SetPrivateField(player, "_capsuleCollider", capsule);
        SetPrivateField(player, "_originalColliderHeight", capsule.height);
        SetPrivateField(player, "_originalColliderCenter", capsule.center);

        float originalBottom = capsule.center.y - capsule.height * 0.5f;
        InvokePrivate(player, "ApplySlideCollider");

        Assert.AreEqual(0.9f, capsule.height, 0.0001f);
        Assert.AreEqual(originalBottom,
            capsule.center.y - capsule.height * 0.5f, 0.0001f,
            "Sliding must not lift the player capsule away from the track.");
    }

    [Test]
    public void ShadowPhysicalPaceExcludesProgressBonuses()
    {
        const float physicalDistance = 120f;
        const float elapsedTime = 12f;
        const float coinAndContractBonuses = 48f;

        float pace = AIShadowRunner.CalculatePhysicalPace(
            physicalDistance, elapsedTime);

        Assert.AreEqual(10f, pace, 0.0001f);
        float bonusInflatedPace =
            (physicalDistance + coinAndContractBonuses) / elapsedTime;
        Assert.Greater(Mathf.Abs(pace - bonusInflatedPace), 0.0001f);
    }

    [Test]
    public void ShadowActionTimingIsRelativeToTheObstacleWindow()
    {
        float slowWindow = AIShadowRunner.CalculateReactionDistance(10f, 1f);
        float fastWindow = AIShadowRunner.CalculateReactionDistance(16f, 1f);

        Assert.AreEqual(0f, AIShadowRunner.CalculateActionTimingOffset(
            slowWindow, slowWindow), 0.0001f);
        Assert.AreEqual(0f, AIShadowRunner.CalculateActionTimingOffset(
            fastWindow, fastWindow), 0.0001f);
        Assert.Greater(AIShadowRunner.CalculateActionTimingOffset(
            slowWindow * 0.5f, slowWindow), 0f);
        Assert.Less(AIShadowRunner.CalculateActionTimingOffset(
            fastWindow * 1.5f, fastWindow), 0f);
    }

    [Test]
    public void LowObstacleUsesGeometryAndReportsStateMismatch()
    {
        Bounds obstacle = new Bounds(new Vector3(0f, 1.95f, 0f),
            new Vector3(3.1f, 0.82f, 1.2f));
        Bounds slidingPlayer = new Bounds(new Vector3(0f, 0.4f, 0f),
            new Vector3(0.8f, 1f, 0.8f));

        ObstacleContactEvaluation result = ObstacleContactRules.Evaluate(
            ObstacleType.Low, slidingPlayer, obstacle, false, false,
            Vector3.forward);

        Assert.AreEqual(ObstacleContactOutcome.Pass, result.outcome);
        Assert.AreEqual(ObstacleContactReason.LowClearanceWithoutSlideState,
            result.reason);
    }

    [Test]
    public void HighObstaclePassesOnClearanceOrJumpingPastFrontOnly()
    {
        Bounds obstacle = new Bounds(new Vector3(0f, 0.55f, 5f),
            new Vector3(3.2f, 0.9f, 0.7f));
        Bounds clearPlayer = new Bounds(new Vector3(0f, 1.8f, 4.4f),
            new Vector3(0.8f, 1.4f, 0.8f));
        Bounds lowPastFront = new Bounds(new Vector3(0f, 0.9f, 4.8f),
            new Vector3(0.8f, 2f, 0.8f));

        Assert.AreEqual(ObstacleContactReason.HighClearance,
            ObstacleContactRules.Evaluate(ObstacleType.High, clearPlayer,
                obstacle, true, false, Vector3.forward).reason);
        Assert.AreEqual(ObstacleContactReason.HighPastFrontDuringJump,
            ObstacleContactRules.Evaluate(ObstacleType.High, lowPastFront,
                obstacle, true, false, Vector3.forward).reason);
        Assert.AreEqual(ObstacleContactOutcome.Hit,
            ObstacleContactRules.Evaluate(ObstacleType.High, lowPastFront,
                obstacle, false, false, Vector3.forward).outcome,
            "Passing the front plane without an active jump must still hit.");
    }

    [Test]
    public void ObstacleContactSettlesOnceUntilThePooledObjectIsReleased()
    {
        GameManager manager = Create<GameManager>("GameManager");
        manager.StartGame();

        GameObject playerObject = new GameObject("player");
        _objects.Add(playerObject);
        Rigidbody body = playerObject.AddComponent<Rigidbody>();
        CapsuleCollider capsule = playerObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.5f, 0f);
        capsule.height = 1f;
        capsule.radius = 0.4f;
        PlayerController player = playerObject.AddComponent<PlayerController>();
        SetPrivateField(player, "_gm", manager);
        SetPrivateField(player, "_rb", body);
        SetPrivateField(player, "_capsuleCollider", capsule);

        GameObject obstacleObject = new GameObject("LowObstacle");
        _objects.Add(obstacleObject);
        Obstacle obstacle = obstacleObject.AddComponent<Obstacle>();
        obstacle.type = ObstacleType.Low;
        BoxCollider trigger = obstacleObject.AddComponent<BoxCollider>();
        trigger.center = new Vector3(0f, 1.95f, 0f);
        trigger.size = new Vector3(3.1f, 0.82f, 1.2f);

        InvokePrivate(player, "HandleObstacleContact", trigger, obstacle,
            ObstacleContactSource.Trigger);
        Assert.AreEqual(ObstacleContactOutcome.Pass,
            player.LastObstacleContact.outcome);
        Assert.AreEqual(1, player.ResolvedObstacleCount);

        InvokePrivate(player, "HandleObstacleContact", trigger, obstacle,
            ObstacleContactSource.Overlap);
        Assert.AreEqual(ObstacleContactOutcome.Pass,
            player.LastObstacleContact.outcome);
        Assert.AreEqual(1, player.DuplicateObstacleContactCount,
            "Repeated physics sources must be counted without overwriting " +
            "the first-contact snapshot.");
        Assert.AreEqual(1, player.ResolvedObstacleCount);

        player.ForgetResolvedObstacle(obstacleObject);
        Assert.AreEqual(0, player.ResolvedObstacleCount,
            "Pool recycle must clear the stable instance id before reuse.");
    }

    [Test]
    public void CharacterAnimatorFindsPlayerAfterLateParenting()
    {
        GameObject visual = new GameObject("CharacterModel");
        _objects.Add(visual);
        GameObject upperArm = new GameObject("Arm_Upper_L");
        _objects.Add(upperArm);
        upperArm.transform.SetParent(visual.transform, false);
        GameObject lowerArm = new GameObject("Arm_Lower_L");
        _objects.Add(lowerArm);
        lowerArm.transform.SetParent(upperArm.transform, false);
        CharacterAnimator characterAnimator = visual.AddComponent<CharacterAnimator>();
        InvokePrivate(characterAnimator, "Initialize");
        Assert.IsNull(GetPrivateField<PlayerController>(characterAnimator, "_player"));

        GameObject playerObject = new GameObject("player");
        _objects.Add(playerObject);
        PlayerController player = playerObject.AddComponent<PlayerController>();
        visual.transform.SetParent(playerObject.transform, false);
        GameManager manager = Create<GameManager>("GameManager");
        manager.StartGame();
        SetPrivateField(characterAnimator, "_gm", manager);

        InvokePrivate(characterAnimator, "LateUpdate");

        Assert.AreSame(player,
            GetPrivateField<PlayerController>(characterAnimator, "_player"),
            "The animator must recover when prefab parenting happens after Awake.");
        Assert.Greater(Quaternion.Angle(Quaternion.identity,
            upperArm.transform.localRotation), 5f,
            "Recovered animators must write a running pose to the model bones.");
    }

    [Test]
    public void CharacterAnimatorForcesLateProceduralPosesIntoSkinning()
    {
        GameObject visual = new GameObject("CharacterModel");
        _objects.Add(visual);
        GameObject meshObject = new GameObject("SkinnedMesh");
        _objects.Add(meshObject);
        meshObject.transform.SetParent(visual.transform, false);
        SkinnedMeshRenderer renderer =
            meshObject.AddComponent<SkinnedMeshRenderer>();
        renderer.updateWhenOffscreen = false;
        renderer.forceMatrixRecalculationPerRender = false;

        CharacterAnimator characterAnimator =
            visual.AddComponent<CharacterAnimator>();
        InvokePrivate(characterAnimator, "Initialize");

        Assert.IsFalse(renderer.updateWhenOffscreen,
            "Offscreen skinning should stay disabled for the WeChat budget.");
        Assert.IsTrue(renderer.forceMatrixRecalculationPerRender,
            "LateUpdate bone changes must reach WebGL/WeChat skinning.");
    }

    [Test]
    public void CharacterAnimatorRunUsesHumanOpposedLimbMechanics()
    {
        GameObject visual = new GameObject("CharacterModel");
        _objects.Add(visual);
        Transform leftUpperArm = CreateBone("LeftUpperArm", visual.transform);
        Transform rightUpperArm = CreateBone("RightUpperArm", visual.transform);
        Transform leftLowerArm = CreateBone("LeftLowerArm", leftUpperArm);
        Transform rightLowerArm = CreateBone("RightLowerArm", rightUpperArm);
        Transform leftUpperLeg = CreateBone("LeftUpperLeg", visual.transform);
        Transform rightUpperLeg = CreateBone("RightUpperLeg", visual.transform);
        Transform leftLowerLeg = CreateBone("LeftLowerLeg", leftUpperLeg);
        Transform rightLowerLeg = CreateBone("RightLowerLeg", rightUpperLeg);
        Transform leftFoot = CreateBone("LeftFoot", leftLowerLeg);
        Transform rightFoot = CreateBone("RightFoot", rightLowerLeg);
        Transform spine = CreateBone("Spine", visual.transform);

        CharacterAnimator characterAnimator =
            visual.AddComponent<CharacterAnimator>();
        characterAnimator.runSwingSpeed = 0f;
        characterAnimator.SetExternalDriver();
        SetPrivateField(characterAnimator, "_runPhase", Mathf.PI * 0.25f);
        characterAnimator.ApplyExternalMotion(
            false, false, Vector3.forward, 10f, 1f);

        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, leftUpperArm.localRotation), 55f,
            "The shoulders must drop out of the horizontal T pose.");
        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, rightUpperArm.localRotation), 55f,
            "Both arms must stay close to the torso while running.");
        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, leftLowerArm.localRotation), 45f,
            "The left elbow must remain bent like a runner's arm.");
        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, rightLowerArm.localRotation), 45f,
            "The right elbow must remain bent like a runner's arm.");
        Assert.Greater(Quaternion.Angle(
            leftUpperArm.localRotation, rightUpperArm.localRotation), 35f,
            "The arms must counter-swing instead of moving as one bar.");
        Assert.Greater(Quaternion.Angle(
            leftUpperLeg.localRotation, rightUpperLeg.localRotation), 35f,
            "The thighs must stride in opposite directions.");
        Assert.Greater(Quaternion.Angle(
            leftLowerLeg.localRotation, rightLowerLeg.localRotation), 35f,
            "The recovery leg must bend while the support leg extends.");
        Assert.Greater(Quaternion.Angle(
            leftFoot.localRotation, rightFoot.localRotation), 12f,
            "The feet must alternate between recovery and push-off angles.");
        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, spine.localRotation), 4f,
            "A running torso needs a small forward lean.");
    }

    [Test]
    public void HumanAnimationControllerContainsRetargetedMotionClips()
    {
        const string controllerPath =
            "Assets/Animations/HumanMotion/EchoRunHuman.controller";
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(
                controllerPath);
        Assert.IsNotNull(controller,
            "The ExoGray model must use an authored Animator Controller.");

        AnimationClip run = System.Array.Find(
            controller.animationClips, clip => clip.name == "HumanRun");
        AnimationClip idle = System.Array.Find(
            controller.animationClips, clip => clip.name == "HumanIdle");
        AnimationClip falling = System.Array.Find(
            controller.animationClips, clip => clip.name == "HumanFalling");
        AnimationClip slide = System.Array.Find(
            controller.animationClips,
            clip => clip.name == "EchoRunSlideLow_Candidate1");
        Assert.IsNotNull(run);
        Assert.IsNotNull(idle);
        Assert.IsNotNull(falling);
        Assert.IsNotNull(slide);
        Assert.IsTrue(run.isHumanMotion);
        Assert.IsTrue(slide.isHumanMotion);
        Assert.IsFalse(slide.isLooping,
            "The authored slide must play once per slide action.");
        Assert.IsTrue(run.isLooping,
            "The authored running clip must loop without procedural resets.");

        AnimatorState idleState = null;
        AnimatorState runState = null;
        AnimatorState jumpState = null;
        AnimatorState slideState = null;
        foreach (ChildAnimatorState child in
                 controller.layers[0].stateMachine.states)
        {
            if (child.state.name == "Idle") idleState = child.state;
            if (child.state.name == "Run") runState = child.state;
            if (child.state.name == "Jump") jumpState = child.state;
            if (child.state.name == "Slide") slideState = child.state;
        }
        Assert.IsNotNull(idleState);
        Assert.IsNotNull(runState);
        Assert.IsNotNull(jumpState);
        Assert.IsNotNull(slideState);
        Assert.AreEqual(
            "Assets/Animations/HumanMotion/HumanRunForwards.fbx",
            AssetDatabase.GetAssetPath(runState.motion),
            "The release controller must keep the accepted Run clip.");
        Assert.AreEqual(1f, runState.speed, 0.0001f,
            "The accepted Run clip must not receive a second speed multiplier.");
        Assert.AreEqual(slide, slideState.motion);
        Assert.IsTrue(idleState.iKOnFeet,
            "Idle Foot IK must keep the player's feet planted on the track.");
        Assert.IsTrue(runState.iKOnFeet,
            "Run Foot IK must keep support feet planted during the stride.");
        Assert.IsFalse(jumpState.iKOnFeet,
            "Jump Foot IK must stay off while the player is airborne.");
        Assert.IsFalse(slideState.iKOnFeet,
            "Slide Foot IK must not pull the authored low pose upward.");
    }

    [Test]
    public void AuthoredSlideBakesRootHeightFromFeet()
    {
        const string slidePath =
            "Assets/Animations/HumanMotion/Visvise/" +
            "EchoRun_SlideLow_v1_TextMotion_TextMotion0.fbx";
        ModelImporter importer = AssetImporter.GetAtPath(slidePath)
            as ModelImporter;
        Assert.IsNotNull(importer);

        ModelImporterClipAnimation slide = System.Array.Find(
            importer.clipAnimations,
            clip => clip.name == "EchoRunSlideLow_Candidate1");
        Assert.IsNotNull(slide);
        Assert.IsTrue(slide.lockRootHeightY);
        Assert.IsFalse(slide.keepOriginalPositionY,
            "The slide must not preserve the generated clip's floating Y origin.");
        Assert.IsTrue(slide.heightFromFeet,
            "The baked slide height must use the feet as its ground reference.");
    }

    [TestCase("Assets/Animations/HumanMotion/HumanIdle.fbx", "HumanIdle")]
    [TestCase("Assets/Animations/HumanMotion/HumanRunForwards.fbx", "HumanRun")]
    [TestCase("Assets/Animations/HumanMotion/HumanFalling.fbx", "HumanFalling")]
    public void AuthoredLocomotionBakesRootHeightFromFeet(
        string path, string clipName)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        Assert.IsNotNull(importer);

        ModelImporterClipAnimation clip = System.Array.Find(
            importer.clipAnimations,
            candidate => candidate.name == clipName);
        Assert.IsNotNull(clip);
        Assert.IsTrue(clip.lockRootHeightY);
        Assert.IsFalse(clip.keepOriginalPositionY,
            clipName + " must not preserve a floating source Y origin.");
        Assert.IsTrue(clip.heightFromFeet,
            clipName + " must use the feet as its ground reference.");
    }

    [Test]
    public void CharacterAnimatorPlaysAuthoredSlideAndReturnsToRun()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Models/Mixamo/ExoGray/ExoGray_TPose.fbx");
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animations/HumanMotion/EchoRunHuman.controller");
        Assert.IsNotNull(modelAsset);
        Assert.IsNotNull(controller);

        GameObject model = Object.Instantiate(modelAsset);
        _objects.Add(model);
        Animator animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);

        CharacterAnimator driver = model.AddComponent<CharacterAnimator>();
        driver.useHumanoidRig = true;
        driver.useAuthoredSlide = true;
        SetPrivateField(driver, "_initialized", false);
        InvokePrivate(driver, "Initialize");
        driver.SetExternalDriver();

        driver.ApplyExternalMotion(
            false, true, Vector3.forward, 10f, 1f / 60f);
        Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Slide"));
        Assert.Greater(animator.speed, 1f,
            "The long VISVISE clip must fit the gameplay slide window.");
        Assert.IsFalse(animator.applyRootMotion);

        driver.ApplyExternalMotion(
            false, false, Vector3.forward, 10f, 1f / 60f);
        bool isRunning =
            animator.GetCurrentAnimatorStateInfo(0).IsName("Run")
            || animator.GetNextAnimatorStateInfo(0).IsName("Run");
        Assert.IsTrue(isRunning,
            "Leaving the slide must immediately return or transition to Run.");
    }

    [Test]
    public void AuthoredRunRetargetsOntoTheActualExoGraySkeleton()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Models/Mixamo/ExoGray/ExoGray_TPose.fbx");
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animations/HumanMotion/EchoRunHuman.controller");
        Assert.IsNotNull(modelAsset);
        Assert.IsNotNull(controller);

        GameObject model = Object.Instantiate(modelAsset);
        _objects.Add(model);
        Animator animator = model.GetComponent<Animator>();
        Assert.IsNotNull(animator);
        Assert.IsTrue(animator.isHuman);
        Transform leftArm =
            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightArm =
            animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftLeg =
            animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightLeg =
            animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Quaternion leftArmBind = leftArm.localRotation;
        Quaternion rightArmBind = rightArm.localRotation;
        Quaternion leftLegBind = leftLeg.localRotation;
        Quaternion rightLegBind = rightLeg.localRotation;

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);
        animator.Play(Animator.StringToHash("Run"), 0, 0.25f);
        animator.Update(0.1f);

        Assert.Greater(Quaternion.Angle(leftArmBind, leftArm.localRotation), 8f);
        Assert.Greater(Quaternion.Angle(rightArmBind, rightArm.localRotation), 8f);
        Assert.Greater(Quaternion.Angle(leftLegBind, leftLeg.localRotation), 8f);
        Assert.Greater(Quaternion.Angle(rightLegBind, rightLeg.localRotation), 8f);
        Assert.Greater(Quaternion.Angle(
            leftArm.localRotation, rightArm.localRotation), 15f,
            "The authored run must preserve opposing human arm motion.");
    }

    [Test]
    public void AuthoredRunKeepsFeetForwardWithoutLockingTheBody()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Models/Mixamo/ExoGray/ExoGray_TPose.fbx");
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animations/HumanMotion/EchoRunHuman.controller");
        Assert.IsNotNull(modelAsset);
        Assert.IsNotNull(controller);

        GameObject model = Object.Instantiate(modelAsset);
        _objects.Add(model);
        Animator animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);

        CharacterAnimator characterAnimator =
            model.AddComponent<CharacterAnimator>();
        characterAnimator.useHumanoidRig = true;
        SetPrivateField(characterAnimator, "_initialized", false);
        InvokePrivate(characterAnimator, "Initialize");
        characterAnimator.SetExternalDriver();

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform leftLowerLeg =
            animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightLowerLeg =
            animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform leftFoot =
            animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot =
            animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform leftToes =
            animator.GetBoneTransform(HumanBodyBones.LeftToes);
        Transform rightToes =
            animator.GetBoneTransform(HumanBodyBones.RightToes);
        float hipsCenterX = hips.localPosition.x;
        float spineCenterX = spine.localPosition.x;
        Quaternion hipsBaseRotation = hips.localRotation;
        Quaternion spineBaseRotation = spine.localRotation;
        Quaternion leftLowerLegBase = leftLowerLeg.localRotation;
        Quaternion rightLowerLegBase = rightLowerLeg.localRotation;
        float[] runTimes = { 0.1f, 0.35f, 0.6f, 0.85f };
        float minimumKneeBend = float.PositiveInfinity;
        float maximumKneeBend = float.NegativeInfinity;

        for (int i = 0; i < runTimes.Length; i++)
        {
            animator.Play(Animator.StringToHash("Run"), 0, runTimes[i]);
            animator.Update(0f);
            characterAnimator.ApplyExternalMotion(
                false, false, Vector3.forward, 10f, 0f);

            Quaternion hipsRelative =
                Quaternion.Inverse(hipsBaseRotation) * hips.localRotation;
            Quaternion spineRelative =
                Quaternion.Inverse(spineBaseRotation) * spine.localRotation;
            Assert.AreEqual(hipsCenterX, hips.localPosition.x, 0.0001f,
                "The runner hips must not sway left and right.");
            Assert.AreEqual(spineCenterX, spine.localPosition.x, 0.0001f,
                "The runner spine must stay centered over the lane.");
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(
                0f, hipsRelative.eulerAngles.z)), 0.1f);
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(
                0f, spineRelative.eulerAngles.z)), 0.1f);
            Assert.Greater(Mathf.DeltaAngle(
                0f, spineRelative.eulerAngles.x), 4f,
                "The authored run needs a visible forward athletic lean.");

            float leftKneeBend = KneeBend(
                leftLowerLegBase, leftLowerLeg.localRotation);
            float rightKneeBend = KneeBend(
                rightLowerLegBase, rightLowerLeg.localRotation);
            Assert.GreaterOrEqual(leftKneeBend, 14.9f,
                "The left leg must not lock straight while running.");
            Assert.GreaterOrEqual(rightKneeBend, 14.9f,
                "The right leg must not lock straight while running.");
            minimumKneeBend = Mathf.Min(
                minimumKneeBend, leftKneeBend, rightKneeBend);
            maximumKneeBend = Mathf.Max(
                maximumKneeBend, leftKneeBend, rightKneeBend);

            AssertFootPointsForward(
                model.transform, leftFoot, leftToes, 7.1f);
            AssertFootPointsForward(
                model.transform, rightFoot, rightToes, 7.1f);
        }

        Assert.Greater(maximumKneeBend - minimumKneeBend, 6f,
            "Knee flex must vary through the stride instead of holding a " +
            "single stiff crouch.");
    }

    [Test]
    public void AuthoredSlideUsesRearHandSupportAndAForwardLeadingLeg()
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Models/Mixamo/ExoGray/ExoGray_TPose.fbx");
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animations/HumanMotion/EchoRunHuman.controller");
        Assert.IsNotNull(modelAsset);
        Assert.IsNotNull(controller);

        GameObject model = Object.Instantiate(modelAsset);
        _objects.Add(model);
        Animator animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        animator.Update(0f);

        CharacterAnimator driver = model.AddComponent<CharacterAnimator>();
        driver.useHumanoidRig = true;
        driver.useAuthoredSlide = false;
        SetPrivateField(driver, "_initialized", false);
        InvokePrivate(driver, "Initialize");
        driver.SetExternalDriver();

        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform leftUpperLeg =
            animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform leftLowerLeg =
            animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        Transform rightUpperLeg =
            animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform rightLowerLeg =
            animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        Transform leftFoot =
            animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform rightFoot =
            animator.GetBoneTransform(HumanBodyBones.RightFoot);
        Transform leftToes =
            animator.GetBoneTransform(HumanBodyBones.LeftToes);
        Transform rightToes =
            animator.GetBoneTransform(HumanBodyBones.RightToes);
        Transform rightHand =
            animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform rightUpperArm =
            animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform rightLowerArm =
            animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform leftUpperArm =
            animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform leftLowerArm =
            animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rightMiddleFinger =
            animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
        Transform leftMiddleFinger =
            animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal);
        Transform leftHand =
            animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        Quaternion spineBaseRotation = spine.localRotation;
        Quaternion frozenLeftUpperArmRotation = Quaternion.identity;
        Quaternion frozenLeftLowerArmRotation = Quaternion.identity;

        for (int i = 0; i < 4; i++)
        {
            animator.Update(1f / 60f);
            driver.ApplyExternalMotion(
                false, true, Vector3.forward, 10f, 1f / 60f);
            if (i == 0)
            {
                frozenLeftUpperArmRotation = leftUpperArm.localRotation;
                frozenLeftLowerArmRotation = leftLowerArm.localRotation;
            }
        }
        float turnHipY = hips.position.y;

        for (int i = 0; i < 5; i++)
        {
            animator.Update(1f / 60f);
            driver.ApplyExternalMotion(
                false, true, Vector3.forward, 10f, 1f / 60f);
        }
        float dropHipY = hips.position.y;
        float dropHandY = rightHand.position.y;
        float dropElbowY = rightLowerArm.position.y;

        for (int i = 0; i < 5; i++)
        {
            animator.Update(1f / 60f);
            driver.ApplyExternalMotion(
                false, true, Vector3.forward, 10f, 1f / 60f);
        }
        float contactHipY = hips.position.y;
        float contactHandY = rightHand.position.y;
        float contactElbowY = rightLowerArm.position.y;

        for (int i = 0; i < 13; i++)
        {
            animator.Update(1f / 60f);
            driver.ApplyExternalMotion(
                false, true, Vector3.forward, 10f, 1f / 60f);
        }
        float glideHipY = hips.position.y;
        float glideHandY = rightHand.position.y;
        float glideElbowY = rightLowerArm.position.y;

        Assert.Less(dropHipY, turnHipY - 0.3f,
            "The drop stage must rapidly lower the center of mass.");
        Assert.Less(contactHipY, dropHipY - 0.05f,
            "The contact stage must finish lowering the hips.");
        Assert.Less(contactElbowY, dropElbowY - 0.02f,
            "The support elbow must reach the track during contact.");
        Assert.That(glideHipY, Is.EqualTo(contactHipY).Within(0.04f),
            "The glide stage must hold a stable low center of mass.");
        Assert.That(glideElbowY, Is.EqualTo(contactElbowY).Within(0.05f),
            "The support elbow must stay planted through the glide.");

        Assert.That(hips.position.y - model.transform.position.y,
            Is.InRange(0.08f, 0.16f),
            "The elbow-supported slide must hold the hips 8-16 cm above the track.");
        Assert.GreaterOrEqual(leftFoot.position.y,
            model.transform.position.y - 0.03f,
            "The leading foot must remain above the track.");
        Assert.GreaterOrEqual(rightFoot.position.y,
            model.transform.position.y - 0.03f,
            "The folded foot must remain above the track.");
        Vector3 leadingLeg = Vector3.ProjectOnPlane(
            leftFoot.position - hips.position, Vector3.up);
        Vector3 footSeparation = Vector3.ProjectOnPlane(
            leftFoot.position - rightFoot.position, Vector3.up);
        Vector3 slideForward = Quaternion.AngleAxis(
            driver.slideYawAngle, model.transform.up)
            * model.transform.forward;
        Vector3 slideRight = Quaternion.AngleAxis(
            driver.slideYawAngle, model.transform.up)
            * model.transform.right;
        Assert.Greater(Vector3.Dot(
                leadingLeg, slideForward),
            0.55f,
            "The leading left leg must extend along the travel direction.");
        Assert.Less(Mathf.Abs(Vector3.Dot(
                leadingLeg, slideRight)),
            0.28f,
            "The leading left leg must not flare into a sideways split.");
        Assert.Greater(Vector3.Dot(
                footSeparation, slideForward),
            0.28f,
            "The left foot must lead the folded right foot.");
        Assert.That(WorldJointBend(
                leftUpperLeg, leftLowerLeg, leftFoot),
            Is.InRange(16f, 34f),
            "The leading leg must keep a soft bend instead of locking straight.");
        Assert.That(WorldJointBend(
                rightUpperLeg, rightLowerLeg, rightFoot),
            Is.InRange(90f, 120f),
            "The trailing leg must fold 90-120 degrees beside the hips.");
        Assert.Greater(leftFoot.position.y,
            model.transform.position.y + 0.07f,
            "The leading heel must stay off the track instead of braking the slide.");
        if (leftToes != null)
        {
            Assert.Greater(leftToes.position.y,
                model.transform.position.y + 0.04f,
                "The leading toes must lift clear of the track.");
            Vector3 leadingFootDirection =
                (leftToes.position - leftFoot.position).normalized;
            Assert.Greater(Vector3.Dot(
                    leadingFootDirection, slideForward),
                0.9f,
                "The leading boot must lie along the slide instead of planting its sole.");
        }
        Assert.Greater(Vector3.Dot(
                rightLowerLeg.position - hips.position,
                slideRight),
            0.16f,
            "The tucked knee must open to the right in a figure-four slide.");
        Assert.Less(Mathf.Abs(Vector3.Dot(
                rightFoot.position - leftLowerLeg.position,
                slideRight)),
            0.42f,
            "The tucked foot must stay near the leading knee instead of planting ahead.");
        Assert.Greater(rightFoot.position.y,
            model.transform.position.y + 0.07f,
            "The tucked boot must stay clear of the track.");
        if (rightToes != null)
        {
            Assert.Greater(rightToes.position.y,
                model.transform.position.y + 0.04f,
                "The tucked toes must stay clear of the track.");
            Vector3 tuckedFootDirection =
                (rightToes.position - rightFoot.position).normalized;
            Assert.Greater(Vector3.Dot(
                    tuckedFootDirection, -slideRight),
                0.8f,
                "The tucked boot must lie across the body to complete the figure four.");
        }
        Assert.LessOrEqual(rightLowerArm.position.y,
            model.transform.position.y + 0.145f,
            "The support elbow bone must stay within the grounded elbow armour.");
        Assert.Greater(rightHand.position.y,
            rightLowerArm.position.y + 0.05f,
            "The support forearm must lift the right palm slightly above the elbow.");
        Assert.Less(rightHand.position.y,
            rightLowerArm.position.y + 0.13f,
            "The support forearm must stay low instead of pointing steeply upward.");
        if (rightMiddleFinger != null)
        {
            Assert.Greater(rightMiddleFinger.position.y,
                rightLowerArm.position.y + 0.025f,
                "The supporting fingertips must not touch the track.");
        }
        Assert.Greater(leftHand.position.y,
            model.transform.position.y + 0.18f,
            "The free left palm must remain naturally above the track.");
        Assert.Less(Quaternion.Angle(
                frozenLeftUpperArmRotation, leftUpperArm.localRotation),
            0.5f,
            "The free left upper arm must keep its authored frozen pose.");
        Assert.Less(Quaternion.Angle(
                frozenLeftLowerArmRotation, leftLowerArm.localRotation),
            0.5f,
            "The free left forearm must not perform a slide gesture.");
        Assert.Greater(driver.slideYawAngle, 0f,
            "The torso front must turn slightly toward the runner's right.");
        Assert.Greater(Quaternion.Angle(
            spineBaseRotation, spine.localRotation), 18f,
            "The upper body must visibly recline instead of folding forward.");
        Assert.Less(Vector3.Dot(
                chest.up, model.transform.forward),
            -0.25f,
            "The chest must lean behind the hips, not dive toward the track ahead.");
        float backAngleToTrack = Mathf.Asin(Mathf.Clamp01(
            Mathf.Abs(Vector3.Dot(chest.up, model.transform.up))))
            * Mathf.Rad2Deg;
        Assert.That(backAngleToTrack, Is.InRange(30f, 60f),
            "The elbow-supported torso must recline without collapsing flat.");
        Vector3 supportFromHips = rightLowerArm.position - hips.position;
        Assert.Less(Vector3.Dot(
                supportFromHips, slideForward),
            -0.02f,
            "The support elbow must plant behind the hips.");
        Assert.Greater(Vector3.Dot(
                supportFromHips, slideRight),
            0.22f,
            "The right support elbow must brace beside the body.");
        Assert.That(WorldJointBend(
                rightUpperArm, rightLowerArm, rightHand),
            Is.InRange(25f, 145f),
            "The support elbow must stay in a human bend range.");
        Assert.Greater(Vector3.Dot(
                rightLowerArm.position - hips.position,
                model.transform.right),
            0.05f,
            "The support elbow must not fold through the torso.");
        Assert.Greater(Vector3.Dot(
                rightHand.position - hips.position,
                model.transform.right),
            0.28f,
            "The complete right hand must clear the torso silhouette.");

        Vector3 stableElbow = rightLowerArm.position;
        Vector3 stableHand = rightHand.position;
        Vector3 stableLeftHand = leftHand.position;
        Vector3 stableHips = hips.position;
        Vector3 stableChest = chest.position;
        Vector3 stableRightFinger = rightMiddleFinger != null
            ? rightMiddleFinger.position : Vector3.zero;
        Vector3 stableLeftFinger = leftMiddleFinger != null
            ? leftMiddleFinger.position : Vector3.zero;
        float maximumElbowDrift = 0f;
        float maximumHandDrift = 0f;
        float maximumLeftHandDrift = 0f;
        float maximumBodyDrift = 0f;
        float maximumFingerDrift = 0f;
        Assert.AreEqual(0f, animator.speed,
            "The authored Animator must freeze during the procedural slide.");
        for (int i = 0; i < 8; i++)
        {
            animator.Update(1f / 60f);
            driver.ApplyExternalMotion(
                false, true, Vector3.forward, 10f, 1f / 60f);
            maximumElbowDrift = Mathf.Max(maximumElbowDrift,
                Vector3.Distance(stableElbow, rightLowerArm.position));
            maximumHandDrift = Mathf.Max(maximumHandDrift,
                Vector3.Distance(stableHand, rightHand.position));
            maximumLeftHandDrift = Mathf.Max(maximumLeftHandDrift,
                Vector3.Distance(stableLeftHand, leftHand.position));
            maximumBodyDrift = Mathf.Max(maximumBodyDrift,
                Vector3.Distance(stableHips, hips.position),
                Vector3.Distance(stableChest, chest.position));
            if (rightMiddleFinger != null)
                maximumFingerDrift = Mathf.Max(maximumFingerDrift,
                    Vector3.Distance(stableRightFinger,
                        rightMiddleFinger.position));
            if (leftMiddleFinger != null)
                maximumFingerDrift = Mathf.Max(maximumFingerDrift,
                    Vector3.Distance(stableLeftFinger,
                        leftMiddleFinger.position));
        }
        Assert.Less(maximumElbowDrift, 0.015f,
            "The planted elbow must not jitter during the stable glide.");
        Assert.Less(maximumHandDrift, 0.02f,
            "The raised right hand must not jitter during the stable glide.");
        Assert.Less(maximumLeftHandDrift, 0.02f,
            "The free left hand must not jitter during the stable glide.");
        Assert.Less(maximumBodyDrift, 0.015f,
            "The body must not jitter during the stable glide.");
        Assert.Less(maximumFingerDrift, 0.02f,
            "Neither complete hand shape may jitter during the stable glide.");
        Assert.Greater(Vector3.Distance(
            leftUpperLeg.position, leftLowerLeg.position), 0.2f);
        Assert.Greater(Vector3.Distance(
            rightUpperLeg.position, rightLowerLeg.position), 0.2f);

        for (int i = 0; i < 16; i++)
        {
            animator.Update(1f / 60f);
            driver.ApplyExternalMotion(
                false, true, Vector3.forward, 10f, 1f / 60f);
        }
        Assert.Greater(hips.position.y, glideHipY + 0.3f,
            "Push-off recovery must raise the hips before the slide ends.");
        Assert.Greater(rightLowerArm.position.y, glideElbowY + 0.3f,
            "Push-off recovery must lift the support elbow from the track.");
        driver.ApplyExternalMotion(
            false, false, Vector3.forward, 10f, 1f / 60f);
        Assert.Greater(animator.speed, 0f,
            "The authored Animator must resume after the slide ends.");
    }

    private static float KneeBend(
        Quaternion baseRotation, Quaternion currentRotation)
    {
        Quaternion relative =
            Quaternion.Inverse(baseRotation) * currentRotation;
        return Mathf.Max(0f, -Mathf.DeltaAngle(0f, relative.eulerAngles.x));
    }

    private static float WorldJointBend(
        Transform upper, Transform joint, Transform end)
    {
        Vector3 jointToUpper = upper.position - joint.position;
        Vector3 jointToEnd = end.position - joint.position;
        return 180f - Vector3.Angle(jointToUpper, jointToEnd);
    }

    private static void AssertFootPointsForward(
        Transform runner, Transform foot, Transform toes, float angleLimit)
    {
        Vector3 footForward = Vector3.ProjectOnPlane(
            toes.position - foot.position, Vector3.up);
        Vector3 runnerForward = Vector3.ProjectOnPlane(
            runner.forward, Vector3.up);
        Assert.LessOrEqual(
            Vector3.Angle(footForward, runnerForward), angleLimit,
            "A running foot is pointing across the track.");
    }

    [Test]
    public void ActualRunnerForearmsPointForwardInsteadOfOutward()
    {
        const string modelPath =
            "Assets/Models/Mixamo/ExoGray/ExoGray_TPose.fbx";
        GameObject modelAsset =
            AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        Assert.IsNotNull(modelAsset);
        GameObject model = Object.Instantiate(modelAsset);
        _objects.Add(model);

        CharacterAnimator characterAnimator =
            model.AddComponent<CharacterAnimator>();
        characterAnimator.useHumanoidRig = true;
        characterAnimator.runSwingSpeed = 0f;
        SetPrivateField(characterAnimator, "_initialized", false);
        InvokePrivate(characterAnimator, "Initialize");
        characterAnimator.SetExternalDriver();

        Animator animator = model.GetComponentInChildren<Animator>();
        Assert.IsNotNull(animator);
        Transform leftElbow =
            animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform rightElbow =
            animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Assert.IsNotNull(leftElbow);
        Assert.IsNotNull(rightElbow);
        Assert.IsNotNull(leftHand);
        Assert.IsNotNull(rightHand);

        float[] phases = { 0f, Mathf.PI * 0.5f, Mathf.PI, Mathf.PI * 1.5f };
        for (int i = 0; i < phases.Length; i++)
        {
            SetPrivateField(characterAnimator, "_runPhase", phases[i]);
            characterAnimator.ApplyExternalMotion(
                false, false, Vector3.forward, 10f, 1f);
            Vector3 leftForearm =
                (leftHand.position - leftElbow.position).normalized;
            Vector3 rightForearm =
                (rightHand.position - rightElbow.position).normalized;
            Assert.Greater(
                Vector3.Dot(leftForearm, model.transform.forward), 0.55f,
                "The left hand must travel forward from the elbow at phase " + i);
            Assert.Greater(
                Vector3.Dot(rightForearm, model.transform.forward), 0.55f,
                "The right hand must travel forward from the elbow at phase " + i);
            Assert.Less(Mathf.Abs(
                    Vector3.Dot(leftForearm, model.transform.right)), 0.35f,
                "The left forearm must not flare sideways at phase " + i);
            Assert.Less(Mathf.Abs(
                    Vector3.Dot(rightForearm, model.transform.right)), 0.35f,
                "The right forearm must not flare sideways at phase " + i);
        }
    }

    [Test]
    public void CharacterAnimatorJumpUsesAStaggeredBentLimbPose()
    {
        GameObject visual = new GameObject("CharacterModel");
        _objects.Add(visual);
        Transform leftUpperArm = CreateBone("LeftUpperArm", visual.transform);
        Transform rightUpperArm = CreateBone("RightUpperArm", visual.transform);
        Transform leftLowerArm = CreateBone("LeftLowerArm", leftUpperArm);
        Transform rightLowerArm = CreateBone("RightLowerArm", rightUpperArm);
        Transform leftUpperLeg = CreateBone("LeftUpperLeg", visual.transform);
        Transform rightUpperLeg = CreateBone("RightUpperLeg", visual.transform);
        CreateBone("LeftLowerLeg", leftUpperLeg);
        CreateBone("RightLowerLeg", rightUpperLeg);
        Transform spine = CreateBone("Spine", visual.transform);

        CharacterAnimator characterAnimator =
            visual.AddComponent<CharacterAnimator>();
        characterAnimator.SetExternalDriver();
        characterAnimator.ApplyExternalMotion(
            true, false, Vector3.forward, 10f, 1f);

        Assert.Greater(Quaternion.Angle(
            leftUpperArm.localRotation, rightUpperArm.localRotation), 25f,
            "Jump arms must counter-swing instead of forming a symmetric V.");
        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, leftLowerArm.localRotation), 35f,
            "The left elbow must bend during a jump.");
        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, rightLowerArm.localRotation), 35f,
            "The right elbow must bend during a jump.");
        Assert.Greater(Quaternion.Angle(
            leftUpperLeg.localRotation, rightUpperLeg.localRotation), 40f,
            "One knee must lead while the trailing leg extends behind.");
        Assert.Greater(Quaternion.Angle(
            Quaternion.identity, spine.localRotation), 8f,
            "The torso must lean into the jump instead of staying upright.");
    }

    [Test]
    public void ShadowCreatedBeforeStartStillClonesThePlayerVisual()
    {
        GameObject playerObject = new GameObject("player");
        _objects.Add(playerObject);
        PlayerController player = playerObject.AddComponent<PlayerController>();
        GameObject visual = new GameObject("CharacterModel");
        _objects.Add(visual);
        visual.transform.SetParent(playerObject.transform, false);
        GameObject marker = new GameObject("PlayerVisualMarker");
        _objects.Add(marker);
        marker.transform.SetParent(visual.transform, false);
        player.characterModel = visual.transform;

        AIShadowRunner runner = Create<AIShadowRunner>("AIShadowRunner");
        SetPrivateField(runner, "_player", null);

        InvokePrivate(runner, "CreateGhost");

        GameObject ghost = GetPrivateField<GameObject>(runner, "_ghost");
        Assert.IsNotNull(ghost);
        Assert.IsNotNull(ghost.transform.Find("ShadowVisual/PlayerVisualMarker"),
            "CreateGhost must resolve the scene player instead of permanently " +
            "falling back to a visible capsule when Start has not run yet.");
    }

    [Test]
    public void SceneRuntimeScriptsResolveToCompiledClasses()
    {
        string[] paths =
        {
            "Assets/Scripts/AudioManager.cs",
            "Assets/Scripts/CameraFollow.cs",
            "Assets/Scripts/CharacterAnimator.cs",
            "Assets/Scripts/Coin.cs",
            "Assets/Scripts/GameManager.cs",
            "Assets/Scripts/GroundFollower.cs",
            "Assets/Scripts/InputManager.cs",
            "Assets/Scripts/Obstacle.cs",
            "Assets/Scripts/ParticleManager.cs",
            "Assets/Scripts/PlayerController.cs",
            "Assets/Scripts/TrackSegmentData.cs",
            "Assets/Scripts/UIManager.cs"
        };
        System.Type[] expected =
        {
            typeof(AudioManager),
            typeof(CameraFollow),
            typeof(CharacterAnimator),
            typeof(Coin),
            typeof(GameManager),
            typeof(GroundFollower),
            typeof(InputManager),
            typeof(Obstacle),
            typeof(ParticleManager),
            typeof(PlayerController),
            typeof(TrackSegmentData),
            typeof(UIManager)
        };

        for (int i = 0; i < paths.Length; i++)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(paths[i]);
            Assert.IsNotNull(script, paths[i] + " was not imported as a MonoScript.");
            Assert.AreEqual(expected[i], script.GetClass(),
                paths[i] + " lost its compiled class binding.");
        }
    }

    [Test]
    public void SignalArchLeavesAnOpenThreeLaneSkyline()
    {
        GameObject stylerObject = new GameObject("WorldStylerTest");
        _objects.Add(stylerObject);
        WorldStyler styler = stylerObject.AddComponent<WorldStyler>();
        GameObject parent = new GameObject("ArchParent");
        _objects.Add(parent);

        InvokePrivate(styler, "BuildSignalArch", parent.transform, 6.5f);

        Transform arch = parent.transform.Find("TransitArch");
        Assert.IsNotNull(arch);
        Assert.IsNull(parent.transform.Find("ArchSignal"),
            "The old hanging center signal must not return.");
        Assert.IsNotNull(parent.transform.Find("ArchSignalLeft"));
        Assert.IsNotNull(parent.transform.Find("ArchSignalRight"));

        int jointCount = 0;
        int nodeCount = 0;
        foreach (Transform part in arch)
        {
            if (part.name == "ArcJoint")
            {
                jointCount++;
                Assert.LessOrEqual(part.localScale.x, 0.231f,
                    "The open wing frame must stay visually lightweight.");
            }
            else if (part.name == "ArcNode")
            {
                nodeCount++;
                Assert.GreaterOrEqual(Mathf.Abs(part.localPosition.x), 3.4f,
                    "No detached arch node may float inside the open skyline.");
            }
        }
        Assert.AreEqual(4, jointCount,
            "Only two outer frame segments per wing should remain.");
        Assert.AreEqual(6, nodeCount,
            "Each open wing should retain its three structural nodes.");

        float[] laneCenters = { -3f, 0f, 3f };
        const float cameraHeight = 4.6f;
        const float minimumCameraClearance = 2.2f;
        foreach (Transform part in arch)
        {
            if (part.name != "ArcJoint") continue;
            float halfLength = part.lossyScale.y;
            Vector3 start = part.position - part.up * halfLength;
            Vector3 end = part.position + part.up * halfLength;
            float minY = Mathf.Min(start.y, end.y);
            float maxY = Mathf.Max(start.y, end.y);
            if (cameraHeight < minY || cameraHeight > maxY) continue;

            float t = Mathf.InverseLerp(start.y, end.y, cameraHeight);
            float crossingX = Mathf.Lerp(start.x, end.x, t);
            foreach (float laneCenter in laneCenters)
            {
                Assert.Greater(Mathf.Abs(crossingX - laneCenter),
                    minimumCameraClearance,
                    part.name + " must stay outside every normal-running " +
                    "lane camera path.");
            }
        }

        const float openSkylineHalfWidth = 3.25f;
        foreach (Renderer renderer in parent.GetComponentsInChildren<Renderer>())
        {
            Bounds bounds = renderer.bounds;
            bool entersOpenSkyline = bounds.min.x < openSkylineHalfWidth
                && bounds.max.x > -openSkylineHalfWidth && bounds.max.y > 4.4f;
            Assert.IsFalse(entersOpenSkyline,
                renderer.name + " must not close the three-lane skyline.");
        }
    }

    [Test]
    public void ShadowSlidesOnceForAnApproachingLowObstacle()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        if (TrackManager.Instance != manager)
            InvokePrivate(manager, "Awake");
        GameObject owner = new GameObject("Segment");
        _objects.Add(owner);
        GameObject lowPrefab = CreateObstaclePrefab("LowObstacle", ObstacleType.Low);
        InvokePrivate(manager, "SpawnDynamic", lowPrefab, owner,
            new Vector3(0f, 1f, 3f), Quaternion.identity);
        Assert.IsTrue(manager.TryGetUpcomingObstacleInLane(
            Vector3.zero, Vector3.forward, 1, new HashSet<int>(),
            out _, out ObstacleType detectedType, out _));
        Assert.AreEqual(ObstacleType.Low, detectedType);

        GameObject playerObject = new GameObject("player");
        _objects.Add(playerObject);
        PlayerController player = playerObject.AddComponent<PlayerController>();
        GameObject ghost = new GameObject("ghost");
        _objects.Add(ghost);
        GameObject visual = new GameObject("visual");
        _objects.Add(visual);
        visual.transform.SetParent(ghost.transform, false);
        GameObject head = new GameObject("Head");
        _objects.Add(head);
        head.transform.SetParent(visual.transform, false);
        head.transform.localPosition = new Vector3(0f, 2.08f, 0f);
        GameObject torso = new GameObject("Torso");
        _objects.Add(torso);
        torso.transform.SetParent(visual.transform, false);
        torso.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        GameObject upperLeg = new GameObject("Leg_Upper_L");
        _objects.Add(upperLeg);
        upperLeg.transform.SetParent(visual.transform, false);
        GameObject lowerLeg = new GameObject("Leg_Lower_L");
        _objects.Add(lowerLeg);
        lowerLeg.transform.SetParent(upperLeg.transform, false);
        GameObject rearUpperLeg = new GameObject("Leg_Upper_R");
        _objects.Add(rearUpperLeg);
        rearUpperLeg.transform.SetParent(visual.transform, false);
        GameObject rearLowerLeg = new GameObject("Leg_Lower_R");
        _objects.Add(rearLowerLeg);
        rearLowerLeg.transform.SetParent(rearUpperLeg.transform, false);
        CharacterAnimator characterAnimator = visual.AddComponent<CharacterAnimator>();
        characterAnimator.SetExternalDriver();

        AIShadowRunner runner = manager.GetComponent<AIShadowRunner>();
        Assert.IsNotNull(runner);
        SetPrivateField(runner, "_player", player);
        SetPrivateField(runner, "_ghost", ghost);
        SetPrivateField(runner, "_ghostVisual", visual.transform);
        SetPrivateField(runner, "_ghostVisualPosition", Vector3.zero);
        SetPrivateField(runner, "_ghostAnimator", characterAnimator);
        SetPrivateField(runner, "_ghostGroundY", 0f);

        InvokePrivate(runner, "ApplyObstacleReaction");
        float startedTimer = GetPrivateField<float>(runner, "_ghostSlideTimer");
        Assert.Greater(startedTimer, 0f,
            "A low obstacle in the shadow lane must start a slide.");

        SetPrivateField(runner, "_ghostSlideTimer", startedTimer - 0.12f);
        InvokePrivate(runner, "UpdateGhostPose");
        characterAnimator.ApplyExternalMotion(
            false, true, Vector3.forward, 10f, 0.2f);
        Assert.AreEqual(Vector3.one, visual.transform.localScale,
            "The shadow root must not sink by scaling the whole body.");
        Assert.AreEqual(Vector3.one, torso.transform.localScale,
            "The torso must keep its authored proportions during the slide.");
        Assert.Greater(Quaternion.Angle(Quaternion.identity,
            torso.transform.localRotation), 14f,
            "The torso must visibly recline without tipping into a side slide.");
        Assert.That(Quaternion.Angle(Quaternion.identity,
                upperLeg.transform.localRotation),
            Is.InRange(70f, 90f),
            "The leading thigh must swing forward into the authored slide pose.");
        Assert.That(Quaternion.Angle(Quaternion.identity,
                lowerLeg.transform.localRotation),
            Is.InRange(3f, 12f),
            "The front knee must remain nearly straight during the slide.");
        Assert.Greater(Quaternion.Angle(Quaternion.identity,
            rearLowerLeg.transform.localRotation), 25f,
            "The rear knee must bend without sinking the whole model.");

        SetPrivateField(runner, "_ghostSlideTimer", 0f);
        InvokePrivate(runner, "ApplyObstacleReaction");
        Assert.AreEqual(0f, GetPrivateField<float>(runner, "_ghostSlideTimer"),
            "The same obstacle must not retrigger the slide.");
    }

    [Test]
    public void ShadowHeightDoesNotFollowPlayerJump()
    {
        GameObject playerObject = new GameObject("player");
        _objects.Add(playerObject);
        playerObject.transform.position = new Vector3(0f, 4f, 0f);
        PlayerController player = playerObject.AddComponent<PlayerController>();
        SetPrivateField(player, "<IsJumping>k__BackingField", true);

        AIShadowRunner runner = Create<AIShadowRunner>("AIShadowRunner");
        GameObject ghost = new GameObject("ghost");
        _objects.Add(ghost);
        SetPrivateField(runner, "_player", player);
        SetPrivateField(runner, "_ghost", ghost);
        SetPrivateField(runner, "_ghostGroundY", 1f);

        InvokePrivate(runner, "UpdateGhostPose");

        Assert.AreEqual(1f, ghost.transform.position.y, 0.001f,
            "A player jump must not lift a shadow that did not choose Jump.");
    }

    [Test]
    public void ShadowObstacleQuerySelectsItsOwnLaneAndSkipsHandledObjects()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        GameObject owner = new GameObject("Segment");
        _objects.Add(owner);

        GameObject otherLanePrefab = CreateObstaclePrefab("OtherLane", ObstacleType.Barrier);
        GameObject ownLanePrefab = CreateObstaclePrefab("OwnLane", ObstacleType.High);
        InvokePrivate(manager, "SpawnDynamic", otherLanePrefab, owner,
            new Vector3(-manager.laneDistance, 1f, 1f), Quaternion.identity);
        InvokePrivate(manager, "SpawnDynamic", ownLanePrefab, owner,
            new Vector3(0f, 1f, 1.4f), Quaternion.identity);

        bool found = manager.TryGetUpcomingObstacleInLane(
            Vector3.zero, Vector3.forward, 1, new HashSet<int>(),
            out float distance, out ObstacleType type, out int obstacleId);

        Assert.IsTrue(found);
        Assert.AreEqual(1.4f, distance, 0.001f);
        Assert.AreEqual(ObstacleType.High, type);

        var handled = new HashSet<int> { obstacleId };
        Assert.IsFalse(manager.TryGetUpcomingObstacleInLane(
            Vector3.zero, Vector3.forward, 1, handled,
            out _, out _, out _));
    }

    [Test]
    public void AITrackPlanAlwaysLeavesAReachableLane()
    {
        AITrackDirector director = Create<AITrackDirector>("AITrackDirector");
        director.observationSegments = 0;
        director.explorationRate = 0f;
        int previousSafeLane = 1;

        for (int i = 0; i < 20; i++)
        {
            AITrackPlan plan = director.CreatePlan(
                0.8f, 0.7f, 0.6f, 0.2f, previousSafeLane, true, (i + 1) * 20f);

            Assert.That(plan.safeLane, Is.InRange(0, 2));
            Assert.LessOrEqual(Mathf.Abs(plan.safeLane - previousSafeLane), 1);
            Assert.That(plan.maxBlockedLanes, Is.InRange(1, 2));
            previousSafeLane = plan.safeLane;
        }
    }

    [Test]
    public void DirectorActivatesTheEnteredPlanInsteadOfTheLastPlannedSegment()
    {
        AITrackDirector director = Create<AITrackDirector>("AITrackDirector");
        director.observationSegments = 1;
        director.explorationRate = 0f;

        AITrackPlan first = director.CreatePlan(
            0.2f, 0.4f, 0.6f, 0f, 1, false, 0f, 20f);
        director.CreatePlan(
            0.8f, 0.8f, 0.4f, 0f, 1, false, 20f, 40f);

        Assert.AreEqual(ShadowAIDirective.Neutral.styleInfluence,
            director.CurrentShadowDirective.styleInfluence, 0.0001f);

        director.ActivatePlanForDistance(0f);

        Assert.AreEqual(first.intent, director.CurrentPlan.intent);
        Assert.AreEqual(1f,
            director.CurrentShadowDirective.styleInfluence, 0.0001f);
        Assert.AreEqual(0f,
            director.CurrentShadowDirective.riskBias, 0.0001f);
        Assert.AreEqual(0.05f,
            director.CurrentShadowDirective.decisionNoise, 0.0001f);
    }

    [Test]
    public void DirectorObservationWindowUsesRouteDistanceNotPregenerationCount()
    {
        Assert.IsTrue(AITrackDirector.IsObservationSegment(
            0f, 20f, 2));
        Assert.IsTrue(AITrackDirector.IsObservationSegment(
            20f, 40f, 2));
        Assert.IsFalse(AITrackDirector.IsObservationSegment(
            40f, 60f, 2));
        Assert.IsFalse(AITrackDirector.IsObservationSegment(
            0f, 20f, 0));
    }

    [Test]
    public void ContractOverridesAreExcludedFromDirectorRewardAttribution()
    {
        Assert.IsTrue(AITrackDirector.IsPolicyAttributionEligible(null));
        Assert.IsTrue(AITrackDirector.IsPolicyAttributionEligible(
            new EchoContractData { type = EchoContractType.None }));
        Assert.IsFalse(AITrackDirector.IsPolicyAttributionEligible(
            new EchoContractData
            {
                type = EchoContractType.BreakLaneHabit
            }));
    }

    [Test]
    public void ObstacleFreeTurnsDoNotMasqueradeAsPressure()
    {
        float recovery = AITrackDirector.TurnMultiplierForIntent(
            AIDirectorIntent.Recovery);
        float pressure = AITrackDirector.TurnMultiplierForIntent(
            AIDirectorIntent.Pressure);
        float recordPush = AITrackDirector.TurnMultiplierForIntent(
            AIDirectorIntent.RecordPush);

        Assert.Greater(recovery, pressure);
        Assert.Greater(pressure, recordPush);
    }

    [Test]
    public void DirectorDirectivePreservesShadowIdentityWithinTightBounds()
    {
        foreach (AIDirectorIntent intent in System.Enum.GetValues(
                     typeof(AIDirectorIntent)))
        {
            ShadowAIDirective directive =
                AITrackDirector.BuildShadowDirective(intent);

            Assert.AreEqual(1f, directive.styleInfluence, 0.0001f,
                intent + " must preserve the learned player style.");
            Assert.LessOrEqual(Mathf.Abs(directive.riskBias), 0.12f,
                intent + " exceeded the director-to-shadow risk boundary.");
            Assert.LessOrEqual(directive.decisionNoise, 0.08f,
                intent + " exceeded the director-to-shadow noise boundary.");
        }
    }

    [Test]
    public void DirectorNeverPunishesAPlannedSegmentThatWasNotEntered()
    {
        AITrackDirector director = Create<AITrackDirector>("AITrackDirector");
        director.observationSegments = 1;
        director.explorationRate = 0f;
        int updatesBefore = director.ModelUpdateCount;

        director.CreatePlan(
            0.2f, 0.4f, 0.6f, 0f, 1, false, 0f, 20f);
        director.CreatePlan(
            0.8f, 0.8f, 0.4f, 0f, 1, false, 20f, 40f);
        director.ActivatePlanForDistance(5f);
        director.RecordObstacleHit();

        director.FinalizeActivePlanForRunEnd(5f);

        Assert.AreEqual(updatesBefore, director.ModelUpdateCount);
    }

    [Test]
    public void DirectorRewardsOnlyEventsRecordedAfterSegmentActivation()
    {
        AITrackDirector director = Create<AITrackDirector>("AITrackDirector");
        director.observationSegments = 1;
        director.explorationRate = 0f;

        director.CreatePlan(
            0.2f, 0.4f, 0.6f, 0f, 1, false, 0f, 20f);
        director.CreatePlan(
            0.6f, 0.7f, 0.5f, 0f, 1, false, 20f, 40f);
        director.ActivatePlanForDistance(0f);
        director.RecordCoin();
        director.RecordObstacleHit();
        director.ActivatePlanForDistance(20f);
        int updatesBefore = director.ModelUpdateCount;

        director.RecordCoin();
        director.RecordDodge();
        director.ActivatePlanForDistance(40f);

        Assert.AreEqual(updatesBefore + 1, director.ModelUpdateCount);
    }

    [Test]
    public void TrackObstacleGenerationCapsEmptyStraightsAfterWarmup()
    {
        Assert.IsFalse(TrackManager.ShouldSpawnObstacleRow(
            1, 1, 1, 3, 1f, 0f), "Warmup must remain obstacle-free.");
        Assert.IsFalse(TrackManager.ShouldSpawnObstacleRow(
            3, 2, 1, 3, 0f, 1f));
        Assert.IsTrue(TrackManager.ShouldSpawnObstacleRow(
            4, 3, 1, 3, 0f, 1f),
            "The third consecutive empty straight must force an obstacle row.");
    }

    [Test]
    public void RunDifficultyProfilesIncreaseObstaclePressureMonotonically()
    {
        float relaxedChance = RunDifficultySettings.AdjustObstacleChance(
            0.7f, RunDifficultyLevel.Relaxed);
        float standardChance = RunDifficultySettings.AdjustObstacleChance(
            0.7f, RunDifficultyLevel.Standard);
        float intenseChance = RunDifficultySettings.AdjustObstacleChance(
            0.7f, RunDifficultyLevel.Intense);

        Assert.Less(relaxedChance, standardChance);
        Assert.Less(standardChance, intenseChance);
        Assert.AreEqual(3, RunDifficultySettings.ResolveMaxFreeSegments(
            2, RunDifficultyLevel.Relaxed));
        Assert.AreEqual(1, RunDifficultySettings.ResolveMaxFreeSegments(
            2, RunDifficultyLevel.Standard));
        Assert.AreEqual(1, RunDifficultySettings.ResolveMaxFreeSegments(
            2, RunDifficultyLevel.Intense));
    }

    [TestCase(RunDifficultyLevel.Relaxed)]
    [TestCase(RunDifficultyLevel.Standard)]
    [TestCase(RunDifficultyLevel.Intense)]
    public void EveryRunDifficultyKeepsItsDeclaredActionRecoveryWindow(
        RunDifficultyLevel level)
    {
        const float speed = 24f;
        const float jumpDuration = 0.9f;
        float recovery = RunDifficultySettings.ObstacleRecoverySeconds(level);
        float spacing = TrackSpawnRules.MinimumObstacleRowSpacing(
            speed, jumpDuration, 20f, recovery);

        Assert.GreaterOrEqual(spacing + 0.001f,
            speed * (jumpDuration + recovery));
    }

    [Test]
    public void PlayerJumpArcLandsWithinConfiguredDuration()
    {
        Assert.AreEqual(0f, PlayerController.EvaluateJumpArc(0f), 0.0001f);
        Assert.AreEqual(1f, PlayerController.EvaluateJumpArc(0.5f), 0.0001f);
        Assert.AreEqual(0f, PlayerController.EvaluateJumpArc(1f), 0.0001f);
    }

    [Test]
    public void PlayerLandingUsesContinuousVelocityInsteadOfPositionTeleport()
    {
        MethodInfo method = typeof(PlayerController).GetMethod(
            "CalculateVerticalCorrectionVelocity",
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method);
        float velocity = (float)method.Invoke(null,
            new object[] { 1.42f, 1f, 0.02f });
        Assert.AreEqual(1f, 1.42f + velocity * 0.02f, 0.0001f);

        string source = System.IO.File.ReadAllText(
            "Assets/Scripts/PlayerController.cs");
        StringAssert.DoesNotContain("_rb.position = landedPosition", source);
    }

    [Test]
    public void CameraFollowPartiallyTracksAirborneHeightAndKeepsPlanarMotion()
    {
        MethodInfo method = typeof(CameraFollow).GetMethod(
            "ResolveFollowAnchor", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(method);
        Vector3 airborne = (Vector3)method.Invoke(null,
            new object[] { new Vector3(2f, 4f, 12f), true, 1f });
        Vector3 grounded = (Vector3)method.Invoke(null,
            new object[] { new Vector3(2f, 1.1f, 12f), false, 1f });

        Assert.AreEqual(new Vector3(2f, 2.05f, 12f), airborne);
        Assert.AreEqual(new Vector3(2f, 1.1f, 12f), grounded);
    }

    [TestCase(false, false, 7, false)]
    [TestCase(true, false, 6, false)]
    [TestCase(true, false, 7, true)]
    [TestCase(true, true, 4, true)]
    public void TrackCannotRemainStraightPastTheVisualRhythmLimit(
        bool canTurn, bool planShouldTurn, int straights, bool expected)
    {
        Assert.AreEqual(expected, TrackManager.ShouldSpawnTurn(
            canTurn, planShouldTurn, straights));
    }

    [Test]
    public void ObstacleRowsLeaveAFullJumpAndRecoveryWindowAtMaximumSpeed()
    {
        float spacing = TrackSpawnRules.MinimumObstacleRowSpacing(40f, 0.9f, 20f);

        Assert.AreEqual(48f, spacing, 0.001f);
        Assert.IsFalse(TrackSpawnRules.CanSpawnObstacleRow(47.9f, 0f, spacing));
        Assert.IsTrue(TrackSpawnRules.CanSpawnObstacleRow(48f, 0f, spacing));
    }

    [TestCase(10f)]
    [TestCase(25f)]
    [TestCase(40f)]
    public void ObstacleRowsLeaveActionRecoveryAtEachAcceptanceSpeed(float speed)
    {
        float spacing = TrackSpawnRules.MinimumObstacleRowSpacing(
            speed, 0.9f, 20f);

        Assert.GreaterOrEqual(spacing + 0.001f, speed * 1.2f,
            "A row must leave at least the jump plus recovery window.");
        Assert.IsTrue(TrackSpawnRules.CanSpawnObstacleRow(
            spacing, 0f, spacing));
        Assert.IsFalse(TrackSpawnRules.CanSpawnObstacleRow(
            spacing - 0.01f, 0f, spacing));
    }

    [TestCase(9137)]
    [TestCase(4815)]
    [TestCase(424242)]
    public void FixedSeedObstacleRowsRepeatBlockedLaneBitmap(int seed)
    {
        string first = BuildBlockedLaneBitmap(seed);
        string second = BuildBlockedLaneBitmap(seed);

        Assert.AreEqual(first, second);
        StringAssert.DoesNotContain("111", first,
            "No deterministic obstacle row may block all three lanes.");
    }

    [Test]
    public void SafeLaneCapsuleDoesNotIntersectAdjacentLaneObstacleGeometry()
    {
        const float laneDistance = 3f;
        Vector3 playerSize = new Vector3(0.8f, 2.2f, 0.8f);
        for (int safeLane = 0; safeLane < 3; safeLane++)
        {
            int[] blocked = TrackSpawnRules.SelectBlockedLanes(
                safeLane, 2, new[] { 0, 0, 0 });
            foreach (int blockedLane in blocked)
            {
                foreach (ObstacleType type in new[]
                         { ObstacleType.Low, ObstacleType.High, ObstacleType.Barrier })
                {
                    Bounds player = new Bounds(
                        new Vector3((safeLane - 1) * laneDistance, 1f, 0f),
                        playerSize);
                    Bounds obstacle = new Bounds(
                        new Vector3((blockedLane - 1) * laneDistance, 1f, 0f)
                        + ObstacleGeometryRules.ColliderCenter(type),
                        ObstacleGeometryRules.ColliderSize(type));
                    Assert.IsFalse(player.Intersects(obstacle),
                        "Safe lane intersects " + type + " in lane " + blockedLane);
                }
            }
        }
    }

    [Test]
    public void GeneratedObstacleTypesExcludeAmbiguousFullHeightBarrier()
    {
        for (int difficultyStep = 0; difficultyStep <= 10; difficultyStep++)
        {
            for (int rollStep = 0; rollStep <= 10; rollStep++)
            {
                int type = TrackSpawnRules.SelectObstaclePrefabIndex(
                    difficultyStep / 10f, rollStep / 10f);
                Assert.That(type, Is.InRange(0, 1));
            }
        }
    }

    [Test]
    public void FailedCollisionStopsPlayerInFrontOfObstacle()
    {
        Bounds obstacle = new Bounds(
            new Vector3(0f, 1f, 10f), new Vector3(3.4f, 2.7f, 0.9f));
        Vector3 stopped = PlayerController.CalculateObstacleStopPosition(
            obstacle, new Vector3(0f, 1f, 10.2f), Vector3.forward, 0.45f);

        Assert.LessOrEqual(stopped.z, obstacle.min.z - 0.45f + 0.0001f);
    }

    [Test]
    public void TrackObstacleFairnessTargetsStarvedEdgeLane()
    {
        int[] drought = { 1, 0, 8 };
        int safeLane = TrackManager.ChooseFairSafeLane(2, 1, drought);
        int[] blocked = TrackManager.SelectBlockedLanes(safeLane, 1, drought);

        Assert.AreNotEqual(2, safeLane,
            "A long-starved edge lane must not remain protected indefinitely.");
        CollectionAssert.Contains(blocked, 2,
            "The next obstacle row should refill the long-starved edge lane.");
        Assert.LessOrEqual(Mathf.Abs(safeLane - 1), 1,
            "Fairness must not create an unreachable safe-lane jump.");
    }

    [Test]
    public void TrackManagerRepairsPartiallyMissingObstaclePrefabs()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        manager.obstaclePrefabs = new GameObject[3];
        MethodInfo ensureAssets = typeof(TrackManager).GetMethod(
            "EnsureProceduralAssets", BindingFlags.Instance | BindingFlags.NonPublic);

        ensureAssets.Invoke(manager, null);

        Assert.AreEqual(3, manager.obstaclePrefabs.Length);
        foreach (GameObject prefab in manager.obstaclePrefabs)
            Assert.IsNotNull(prefab);
        Assert.AreEqual(Vector3.one, manager.trackSegmentPrefab.transform.localScale,
            "Dynamic objects require an unscaled track root for correct world placement.");
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void TurnCoverageTouchesFollowingStraightWithoutCoplanarOverlap(
        int turnDirection)
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        GameObject turn = new GameObject("TurnSegment");
        _objects.Add(turn);
        MethodInfo ensureCoverage = typeof(TrackManager).GetMethod(
            "EnsureTurnCoverage", BindingFlags.Instance | BindingFlags.NonPublic);

        ensureCoverage.Invoke(manager, new object[] { turn, turnDirection });

        Transform coverage = turn.transform.Find("RuntimeTurnCoverage");
        Assert.IsNotNull(coverage);
        Transform entry = coverage.Find("EntryCoverage");
        Transform exit = coverage.Find("ExitCoverage");
        Assert.IsNotNull(entry);
        Assert.IsNotNull(exit);
        Assert.IsNotNull(entry.GetComponent<BoxCollider>());
        Assert.IsNotNull(exit.GetComponent<BoxCollider>());

        float exitCenter = exit.localPosition.x * turnDirection;
        float exitHalfLength = exit.localScale.z * 0.5f;
        float coverageJoin = entry.localScale.x * 0.5f;
        float nextStraightNearEdge = manager.segmentLength * 0.5f;
        float entryHalfLength = entry.localScale.z * 0.5f;

        Assert.AreEqual(0f,
            entry.localPosition.z - entryHalfLength, 0.001f,
            "Entry coverage must start at the previous straight's far edge.");
        Assert.AreEqual(nextStraightNearEdge + coverageJoin,
            entry.localPosition.z + entryHalfLength, 0.001f,
            "Entry coverage may extend only across the corner square.");

        Assert.AreEqual(coverageJoin,
            exitCenter - exitHalfLength, 0.001f,
            "Exit coverage must start where entry coverage ends.");
        Assert.AreEqual(nextStraightNearEdge,
            exitCenter + exitHalfLength, 0.001f,
            "Exit coverage must stop at the next straight's near edge.");
        Assert.AreEqual(manager.segmentLength * 0.5f,
            exit.localPosition.z, 0.001f);

        Transform cap = turn.transform.Find(
            TrackManager.TurnInnerCornerCapName);
        Assert.IsNotNull(cap);
        Assert.IsNotNull(cap.GetComponent<Renderer>());
        Assert.IsNull(cap.GetComponent<Collider>(),
            "Runtime fallback cap is visual-only.");
        Vector3 expectedCap = TrackGeometryStandards.TurnInnerCornerCenter(
            manager.segmentLength, turnDirection);
        Assert.AreEqual(expectedCap.x, cap.localPosition.x, 0.001f);
        Assert.AreEqual(expectedCap.z, cap.localPosition.z, 0.001f);

        Transform bridge = turn.transform.Find(
            TrackManager.TurnWalkableBridgeName);
        Assert.IsNotNull(bridge);
        Assert.IsNull(bridge.GetComponent<Renderer>());
        BoxCollider bridgeCollider = bridge.GetComponent<BoxCollider>();
        Assert.IsNotNull(bridgeCollider);
        Assert.IsTrue(bridgeCollider.enabled);
        Vector3 expectedBridge =
            TrackGeometryStandards.TurnWalkableBridgeCenter(
                manager.segmentLength, turnDirection);
        Assert.AreEqual(expectedBridge.x, bridge.localPosition.x, 0.001f);
        Assert.AreEqual(expectedBridge.z, bridge.localPosition.z, 0.001f);
        Assert.AreEqual(TrackGeometryStandards.TurnWalkableBridgeWidth,
            bridgeCollider.size.x, 0.001f);
        Assert.AreEqual(TrackGeometryStandards.WalkableWidth,
            bridgeCollider.size.z, 0.001f);
    }

    [Test]
    public void ShadowTrackPoseFollowsUpcomingTurnAndStaysInLane()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        GameObject turn = new GameObject("Turn");
        _objects.Add(turn);
        TrackSegmentData data = turn.AddComponent<TrackSegmentData>();
        data.segmentType = TrackSegmentType.TurnRight;
        data.entryDirection = Vector3.forward;
        data.exitDirection = Vector3.right;
        data.turnPointWorld = new Vector3(0f, 0f, 5f);

        FieldInfo activeField = typeof(TrackManager).GetField(
            "_activeSegments", BindingFlags.Instance | BindingFlags.NonPublic);
        var activeSegments = (List<GameObject>)activeField.GetValue(manager);
        activeSegments.Add(turn);

        manager.GetTrackPoseAhead(new Vector3(0f, 1f, 0f), Vector3.forward,
            1, 2, 8f, out Vector3 position, out Vector3 forward);

        Assert.AreEqual(Vector3.right, forward);
        Assert.AreEqual(3f, position.x, 0.001f);
        Assert.AreEqual(2f, position.z, 0.001f,
            "The shadow must turn at the corner before applying its lane offset.");

        manager.GetTrackPoseAhead(new Vector3(0f, 1f, 0f), Vector3.forward,
            1, 2f, 4.9f, out Vector3 beforeCorner, out Vector3 beforeForward);
        manager.GetTrackPoseAhead(new Vector3(0f, 1f, 0f), Vector3.forward,
            1, 2f, 5.1f, out Vector3 afterCorner, out Vector3 afterForward);

        Assert.Less(Vector3.Distance(beforeCorner, afterCorner), 1f,
            "The rounded corner pose must stay continuous across the turn point.");
        Assert.Greater(Vector3.Dot(beforeForward, afterForward), 0.9f,
            "The shadow direction must rotate smoothly instead of snapping 90 degrees.");
    }

    [Test]
    public void ShadowTrackPoseUsesPhysicalLateralOffsetDuringRapidReversal()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");

        manager.GetTrackPoseAhead(new Vector3(-3f, 1f, 0f),
            Vector3.forward, -3f, 0f, 0f,
            out Vector3 leftLane, out _);
        manager.GetTrackPoseAhead(new Vector3(3f, 1f, 0f),
            Vector3.forward, 3f, 2f, 0f,
            out Vector3 rightLane, out _);

        Assert.AreEqual(-3f, leftLane.x, 0.001f,
            "A body still on the left must not move the shadow to x=-6 when CurrentLane changes early.");
        Assert.AreEqual(3f, rightLane.x, 0.001f,
            "The symmetric right-edge reversal must remain inside the road.");
    }

    [TestCase(-3f, -2.4f)]
    [TestCase(-2.2f, -2.55f)]
    [TestCase(2.6f, 2.15f)]
    [TestCase(3f, 2.7f)]
    public void RenderedLateralOffsetCancelsPhysicsInterpolationLag(
        float physicsOffset, float renderedOffset)
    {
        Vector3 physicsPosition = new Vector3(physicsOffset, 1f, 12f);
        Vector3 renderedPosition = new Vector3(renderedOffset, 1f, 11.7f);

        float resolved = PlayerController.ResolveRenderedLateralOffset(
            physicsOffset, renderedPosition, physicsPosition,
            Vector3.forward, true);
        Vector3 renderedTrackCenter = renderedPosition
                                      - Vector3.right * resolved;

        Assert.AreEqual(renderedOffset, resolved, 0.0001f);
        Assert.AreEqual(0f, renderedTrackCenter.x, 0.0001f,
            "Render-position and lateral-offset samples must describe the same frame during rapid reversals.");
    }

    [Test]
    public void RenderedLateralOffsetUsesTrackLocalRightAfterTurn()
    {
        float resolved = PlayerController.ResolveRenderedLateralOffset(
            2.8f, new Vector3(8f, 1f, -2.4f),
            new Vector3(8.3f, 1f, -2.8f), Vector3.right, true);

        Assert.AreEqual(2.4f, resolved, 0.0001f,
            "The interpolation correction must follow track-local right, not world X.");
    }

    [Test]
    public void RenderedLateralOffsetKeepsPhysicsValueWithoutInterpolation()
    {
        float resolved = PlayerController.ResolveRenderedLateralOffset(
            -3f, new Vector3(-2f, 1f, 0f),
            new Vector3(-3f, 1f, 0f), Vector3.forward, false);

        Assert.AreEqual(-3f, resolved, 0.0001f);
    }

    [Test]
    public void ChallengeObstacleTagClearsPooledStepIdentity()
    {
        GameObject obstacle = new GameObject("ChallengeObstacle");
        _objects.Add(obstacle);
        EchoChallengeObstacleTag tag =
            obstacle.AddComponent<EchoChallengeObstacleTag>();
        tag.Configure(new EchoChallengeObstacleBinding
        {
            stepId = 12,
            role = EchoChallengeObstacleRole.Required,
            action = ShadowAction.Jump,
            lane = 0
        });

        tag.Clear();

        Assert.IsFalse(tag.Binding.IsBound);
        Assert.AreEqual(0, tag.Binding.stepId);
    }

    [Test]
    public void ChallengeActionWindowFindsTheBoundRowAcrossLanes()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        GameObject owner = new GameObject("Segment");
        _objects.Add(owner);
        GameObject prefab = CreateObstaclePrefab(
            "CounterObstacle", ObstacleType.Low);
        GameObject obstacle = (GameObject)InvokePrivate(
            manager, "SpawnDynamic", prefab, owner,
            new Vector3(-manager.laneDistance, 1f, 12f),
            Quaternion.identity);
        obstacle.AddComponent<EchoChallengeObstacleTag>().Configure(
            new EchoChallengeObstacleBinding
            {
                stepId = 31,
                role = EchoChallengeObstacleRole.Required,
                action = ShadowAction.Slide,
                lane = 0
            });

        Assert.IsTrue(manager.TryGetUpcomingChallengeObstacle(
            Vector3.zero, Vector3.forward, 31, out float distance));
        Assert.AreEqual(12f, distance, 0.001f,
            "The response window must not depend on the player's lane.");
        Assert.IsFalse(manager.TryGetUpcomingChallengeObstacle(
            Vector3.zero, Vector3.forward, 32, out _));
    }

    [Test]
    public void ChallengeSettlementMarginCoversBothStaggeredChoices()
    {
        var plan = new AITrackPlan
        {
            echoEncounterKind = EchoEncounterKind.CounterTest,
            echoObstaclePattern = EchoObstaclePattern.RiskThenPredicted,
            echoRiskChoiceLane = 0,
            echoPredictedLane = 2
        };
        float stagger = Mathf.Abs(
            TrackManager.EchoObstacleLaneOffset(plan, 0)
            - TrackManager.EchoObstacleLaneOffset(plan, 2));

        Assert.Greater(TrackManager.ChallengeSettlementMargin, stagger);
    }

    [Test]
    public void ResolvedChallengeRowIsNotReportedAsMissed()
    {
        var active = new EchoChallengeStep
        {
            stepId = 8,
            status = EchoChallengeStepStatus.Active
        };
        var nextPending = new EchoChallengeStep
        {
            stepId = 9,
            status = EchoChallengeStepStatus.PendingSpawn
        };

        Assert.IsTrue(TrackManager.ShouldMarkChallengeRowMissed(active, 8));
        Assert.IsFalse(TrackManager.ShouldMarkChallengeRowMissed(active, 7));
        Assert.IsFalse(TrackManager.ShouldMarkChallengeRowMissed(
            nextPending, 8),
            "A resolved row must be removed without inflating missed metrics.");
    }

    [Test]
    public void DensityMetricsUseARealPerHundredMeterRate()
    {
        Assert.AreEqual(5f, TrackManager.RowsPer100Meters(10, 200f), 0.001f);
        Assert.AreEqual(4f, TrackManager.RowsPer100Meters(2, 50f), 0.001f);

        TrackManager manager = Create<TrackManager>("TrackManager");
        Assert.AreEqual(2, manager.maxConsecutiveObstacleFreeStraights,
            "Ordinary phases should not leave more than two free straight segments by default.");
    }

    [Test]
    public void TurnTransitionOnlyCoversTheCorner()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        GameObject turn = new GameObject("Turn");
        _objects.Add(turn);
        TrackSegmentData data = turn.AddComponent<TrackSegmentData>();
        data.segmentType = TrackSegmentType.TurnRight;
        data.entryDirection = Vector3.forward;
        data.exitDirection = Vector3.right;
        data.turnPointWorld = new Vector3(0f, 0f, 5f);

        FieldInfo activeField = typeof(TrackManager).GetField(
            "_activeSegments", BindingFlags.Instance | BindingFlags.NonPublic);
        var activeSegments = (List<GameObject>)activeField.GetValue(manager);
        activeSegments.Add(turn);

        Assert.IsTrue(manager.IsInsideTurnTransition(new Vector3(0f, 0f, 3f)));
        Assert.IsTrue(manager.IsInsideTurnTransition(new Vector3(2f, 0f, 5f)));
        Assert.IsFalse(manager.IsInsideTurnTransition(new Vector3(0f, 0f, -5f)));
        Assert.IsFalse(manager.IsInsideTurnTransition(new Vector3(10f, 0f, 5f)));
    }

    [Test]
    public void ShadowDoesNotCountAProjectedObstacleWhileTurning()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        if (TrackManager.Instance != manager)
            InvokePrivate(manager, "Awake");

        GameObject turn = new GameObject("Turn");
        _objects.Add(turn);
        TrackSegmentData data = turn.AddComponent<TrackSegmentData>();
        data.segmentType = TrackSegmentType.TurnRight;
        data.entryDirection = Vector3.forward;
        data.exitDirection = Vector3.right;
        data.turnPointWorld = Vector3.zero;
        FieldInfo activeField = typeof(TrackManager).GetField(
            "_activeSegments", BindingFlags.Instance | BindingFlags.NonPublic);
        var activeSegments = (List<GameObject>)activeField.GetValue(manager);
        activeSegments.Add(turn);

        GameObject owner = new GameObject("ExitSegment");
        _objects.Add(owner);
        GameObject barrierPrefab = CreateObstaclePrefab(
            "BarrierObstacle", ObstacleType.Barrier);
        InvokePrivate(manager, "SpawnDynamic", barrierPrefab, owner,
            new Vector3(1f, 1f, 0f), Quaternion.identity);

        GameObject ghost = new GameObject("ghost");
        _objects.Add(ghost);
        ghost.transform.position = new Vector3(0f, 0f, -1f);
        AIShadowRunner runner = manager.GetComponent<AIShadowRunner>();
        Assert.IsNotNull(runner);
        SetPrivateField(runner, "_ghost", ghost);
        SetPrivateField(runner, "_ghostForward",
            new Vector3(1f, 0f, 1f).normalized);
        SetPrivateField(runner, "_ghostLane", 1);

        Assert.IsTrue(manager.TryGetUpcomingObstacleInLane(
            ghost.transform.position, new Vector3(1f, 0f, 1f), 1,
            new HashSet<int>(), out float projectedDistance, out _, out _));
        Assert.Less(projectedDistance, 1.5f,
            "The setup must reproduce the old diagonal projection false positive.");

        InvokePrivate(runner, "EvaluateGhostObstacle");

        Assert.AreEqual(0, GetPrivateField<int>(runner, "_ghostMistakes"),
            "An obstacle projected across a corner must not count as a mistake.");
        Assert.AreEqual(0f, GetPrivateField<float>(runner, "_ghostStumbleTimer"));
    }

    [Test]
    public void OverlapFallbackFindsObstacleOnColliderParent()
    {
        GameObject obstacleRoot = new GameObject("PooledObstacleRoot");
        _objects.Add(obstacleRoot);
        obstacleRoot.AddComponent<Obstacle>().type = ObstacleType.Low;

        GameObject colliderChild = new GameObject("GameplayTrigger");
        _objects.Add(colliderChild);
        colliderChild.transform.SetParent(obstacleRoot.transform, false);
        BoxCollider trigger = colliderChild.AddComponent<BoxCollider>();
        trigger.isTrigger = true;

        Collider found = PlayerController.FindObstacleCollider(
            new Collider[] { null, trigger }, 2, null);

        Assert.AreSame(trigger, found,
            "An already-overlapping pooled obstacle must still be detected.");
        Assert.IsNull(PlayerController.FindObstacleCollider(
            new Collider[] { trigger }, 1, trigger),
            "The same obstacle contact must not be processed twice.");
    }

    [Test]
    public void ObstacleSweepIncludesLaneSwitchMovement()
    {
        Vector3 velocity = PlayerController.CalculatePlanarVelocity(
            Vector3.forward, 10f, Vector3.right, 0f, 3f, 20f, 0.02f,
            out float nextOffset);

        Assert.AreEqual(0.4f, nextOffset, 0.0001f);
        Assert.AreEqual(20f, velocity.x, 0.0001f,
            "The sweep must include lateral lane-switch movement.");
        Assert.AreEqual(10f, velocity.z, 0.0001f);
        Assert.Greater(velocity.magnitude, 10f,
            "Diagonal sweep distance must exceed forward-only distance.");
    }

    [Test]
    public void SlideDroneSpawnsInTheRequestedLane()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        GameObject segment = new GameObject("StraightSegment");
        _objects.Add(segment);
        GameObject low = CreateObstaclePrefab("Low", ObstacleType.Low);
        GameObject high = CreateObstaclePrefab("High", ObstacleType.High);
        GameObject barrier = CreateObstaclePrefab("Barrier", ObstacleType.Barrier);
        manager.obstaclePrefabs = new[] { low, high, barrier };

        Assert.IsTrue((bool)InvokePrivate(
            manager, "SpawnObstacleAt", segment, 0, 5f, 0));
        Assert.AreEqual(1, segment.transform.childCount);
        Assert.AreEqual(-manager.laneDistance,
            segment.transform.GetChild(0).position.x, 0.0001f,
            "A lane-sized slide drone must remain in its requested lane.");
    }

    [Test]
    public void PredictionGateObstacleIsPrewarmedBeforePresentationDistance()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        if (TrackManager.Instance != manager)
            InvokePrivate(manager, "Awake");
        manager.segmentLength = 20f;
        GameObject low = CreateObstaclePrefab("Low", ObstacleType.Low);
        GameObject high = CreateObstaclePrefab("High", ObstacleType.High);
        GameObject barrier = CreateObstaclePrefab(
            "Barrier", ObstacleType.Barrier);
        manager.obstaclePrefabs = new[] { low, high, barrier };

        var windows = new PredictionGateDistanceWindow[6];
        for (int i = 0; i < windows.Length; i++)
        {
            float presentation = 100f * (i + 1);
            windows[i] = new PredictionGateDistanceWindow
            {
                presentationDistance = presentation,
                commitDistance = presentation + 10f,
                resolveDistance = presentation + 20f,
                exitDistance = presentation + 30f
            };
        }
        var flow = new SingleContractFlow(
            new SingleContractFixedGateWindowFactory(windows), 2, 1f);
        flow.BeginRun(new EchoRunContext
        {
            mode = GameplayFlowMode.SingleContract,
            hasOpponent = true,
            courseDistance = 950f,
            runSequence = 7,
            runSeed = 424242,
            generation = 3
        });
        AIShadowRunner runner = manager.GetComponent<AIShadowRunner>();
        Assert.IsNotNull(runner);
        PropertyInfo runnerInstance = typeof(AIShadowRunner).GetProperty(
            "Instance", BindingFlags.Static | BindingFlags.Public);
        Assert.IsNotNull(runnerInstance);
        runnerInstance.SetValue(null, runner);
        SetPrivateField(runner, "_singleContractFlow", flow);

        GameObject segment = new GameObject("GateSegment");
        _objects.Add(segment);
        TrackSegmentData data = segment.AddComponent<TrackSegmentData>();
        data.segmentType = TrackSegmentType.Straight;
        data.routeDistance = 115f;

        bool handled = (bool)InvokePrivate(manager,
            "TryPopulateSingleContractSegment", segment, data);

        Assert.IsTrue(handled);
        Assert.IsTrue(data.contentSpawned);
        Assert.AreEqual(PredictionGateLifecycle.Scheduled,
            flow.GetGate(0).State,
            "Physical prewarming must not present the gate early.");
        Assert.AreEqual(1, manager.PredictionGateRowsSpawned);
        Assert.IsNotNull(segment.GetComponentInChildren<
            PredictionGateObstacleTag>(true),
            "The obstacle must already exist before the player reaches the "
            + "presentation distance.");
    }

    [Test]
    public void SlideDroneIsUpcomingOnlyInItsLane()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        GameObject owner = new GameObject("Segment");
        _objects.Add(owner);
        GameObject low = CreateObstaclePrefab("SlideDrone", ObstacleType.Low);
        InvokePrivate(manager, "SpawnDynamic", low, owner,
            new Vector3(-manager.laneDistance, 1f, 4f), Quaternion.identity);

        for (int lane = 0; lane < 3; lane++)
        {
            Vector3 position = new Vector3((lane - 1) * manager.laneDistance,
                0f, 0f);
            bool found = manager.TryGetUpcomingObstacleInLane(
                position, Vector3.forward, lane, new HashSet<int>(),
                out _, out ObstacleType type, out _);
            Assert.AreEqual(lane == 0, found,
                "The slide drone lane query was wrong for lane " + lane + ".");
            if (found) Assert.AreEqual(ObstacleType.Low, type);
        }
    }

    [Test]
    public void ProceduralObstaclesKeepUnitRootScale()
    {
        TrackManager manager = Create<TrackManager>("TrackManager");
        var obstacles = (GameObject[])InvokePrivate(
            manager, "CreateProcObstacles");
        foreach (GameObject obstacle in obstacles) _objects.Add(obstacle);

        Assert.AreEqual(3, obstacles.Length);
        foreach (GameObject obstacle in obstacles)
            Assert.AreEqual(Vector3.one, obstacle.transform.localScale,
                "Runtime styling must not inherit a second obstacle scale.");

        BoxCollider lowCollider = obstacles[0].GetComponent<BoxCollider>();
        Assert.AreEqual(new Vector3(3.1f, 0.82f, 1.2f), lowCollider.size);
        Assert.AreEqual(new Vector3(0f, 0.95f, 0f), lowCollider.center);
    }

    [Test]
    public void AuthoredObstaclePrefabsMatchSharedColliderGeometry()
    {
        AssertObstacleGeometry("Assets/Prefabs/Obstacle_Low.prefab",
            ObstacleType.Low);
        AssertObstacleGeometry("Assets/Prefabs/Obstacle_High.prefab",
            ObstacleType.High);
        AssertObstacleGeometry("Assets/Prefabs/Obstacle_Barrier.prefab",
            ObstacleType.Barrier);
    }

    [Test]
    public void MasterVolumeMultipliesChannelsAndMuteForcesSilence()
    {
        Assert.AreEqual(0.4f,
            AudioManager.ResolveOutputVolume(0.8f, 0.5f, false), 0.001f);
        Assert.AreEqual(0f,
            AudioManager.ResolveOutputVolume(0.8f, 0.5f, true), 0.001f);
        Assert.AreEqual(1f,
            AudioManager.ResolveOutputVolume(2f, 2f, false), 0.001f);
    }

    private static string BuildBlockedLaneBitmap(int seed)
    {
        AIRunRandom.BeginRun(seed);
        int previousSafeLane = 1;
        int[] drought = { 0, 0, 0 };
        string bitmap = "";
        for (int row = 0; row < 24; row++)
        {
            int proposed = AIRunRandom.Range(0, 3);
            int safe = TrackSpawnRules.ChooseFairSafeLane(
                proposed, previousSafeLane, drought);
            int[] blocked = TrackSpawnRules.SelectBlockedLanes(
                safe, AIRunRandom.Value > 0.5f ? 2 : 1, drought);
            bitmap += "|";
            for (int lane = 0; lane < 3; lane++)
                bitmap += System.Array.IndexOf(blocked, lane) >= 0 ? "1" : "0";
            previousSafeLane = safe;
        }
        return bitmap;
    }

    private static void AssertObstacleGeometry(string path, ObstacleType type)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        BoxCollider collider = prefab.GetComponent<BoxCollider>();
        Assert.IsNotNull(collider, path);
        Assert.AreEqual(ObstacleGeometryRules.ColliderSize(type), collider.size);
        Assert.AreEqual(ObstacleGeometryRules.ColliderCenter(type), collider.center);
    }

    private T Create<T>(string name) where T : Component
    {
        GameObject go = new GameObject(name);
        _objects.Add(go);
        return go.AddComponent<T>();
    }

    private GameObject CreateObstaclePrefab(string name, ObstacleType type)
    {
        GameObject prefab = new GameObject(name);
        prefab.AddComponent<Obstacle>().type = type;
        _objects.Add(prefab);
        return prefab;
    }

    private Transform CreateBone(string name, Transform parent)
    {
        GameObject bone = new GameObject(name);
        _objects.Add(bone);
        bone.transform.SetParent(parent, false);
        return bone.transform;
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing private field: " + name);
        field.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing private field: " + name);
        return (T)field.GetValue(target);
    }

    private static object InvokePrivate(object target, string name, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Missing private method: " + name);
        return method.Invoke(target, args);
    }
}
