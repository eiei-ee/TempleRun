using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class SingleContractRuntimeTests
{
    [Test]
    public void RuntimeClaimsAllSingleContractOwnership()
    {
        var flow = new SingleContractFlow();

        Assert.AreEqual(GameplayFlowMode.SingleContract, flow.Mode);
        Assert.IsTrue(flow.OwnsSpecialEncounters);
        Assert.IsTrue(flow.OwnsLeadSettlement);
        Assert.IsTrue(flow.OwnsFinishSchedule);
    }

    [Test]
    public void FixedValidationIdentityIsStablePreciseAndChallengeReady()
    {
        ActiveEchoIdentity first =
            SingleContractValidationIdentity.Create();
        ActiveEchoIdentity second =
            SingleContractValidationIdentity.Create();

        Assert.IsTrue(first.IsSemanticallyValid());
        Assert.AreEqual(first.ToJson(), second.ToJson());
        Assert.AreEqual(first.ComputeHash(), second.ComputeHash());
        Assert.AreEqual(SingleContractValidationIdentity.Generation,
            first.generation);
        Assert.AreEqual(SingleContractValidationIdentity.PreferredLane,
            first.memoryContract.preferredLane);
        Assert.IsTrue(first.memoryContract.HasPreciseRouteMemory);
        Assert.IsFalse(first.RequiresRouteCalibration);
        Assert.Greater(first.pace, 0f);
        Assert.AreEqual(SingleContractFlow.CalibrationDurationSeconds,
            first.sourceCourseDuration);
    }

    [Test]
    public void ChallengeFreezesContextAndBuildsFiveNormalPlusFinalGate()
    {
        var flow = new SingleContractFlow();
        flow.BeginRun(CreateContext(true, 950f, 17, 424242, 4));

        Assert.AreEqual(17, flow.RunSequence);
        Assert.AreEqual(424242, flow.RunSeed);
        Assert.AreEqual(4, flow.IdentityGeneration);
        Assert.IsTrue(flow.HasOpponent);
        Assert.AreEqual(95f, flow.RunDurationSeconds);
        Assert.AreEqual(6, flow.GateCount);
        for (int i = 0; i < flow.GateCount; i++)
        {
            PredictionGateDefinition definition = flow.GetGate(i).Definition;
            Assert.AreEqual(i + 1, definition.sequence);
            Assert.AreEqual(i == 5, definition.isFinal);
            Assert.AreEqual(
                SingleContractFlow.GetGatePresentationTimeSeconds(i) * 10f,
                definition.presentationDistance, 0.0001f);
        }

        flow.Tick(new EchoRunFrame { elapsedTime = 2f });
        Assert.IsTrue(flow.IsOpeningMemoryActive);
        flow.Tick(new EchoRunFrame { elapsedTime = 70f });
        Assert.IsTrue(flow.IsFinaleActive);
    }

    [Test]
    public void CalibrationUsesShortDurationAndOnlyReportsArrival()
    {
        var flow = new SingleContractFlow();
        flow.BeginRun(CreateContext(false, 550f, 8, 99, 0));

        Assert.IsTrue(flow.IsCalibration);
        Assert.AreEqual(55f, flow.RunDurationSeconds);
        Assert.AreEqual(5, flow.GateCount);
        for (int i = 0; i < flow.GateCount; i++)
        {
            PredictionGateDefinition definition = flow.GetGate(i).Definition;
            Assert.IsFalse(definition.isFinal);
            Assert.AreEqual(
                SingleContractFlow.GetCalibrationGatePresentationTimeSeconds(i)
                * 10f,
                definition.presentationDistance, 0.0001f);
        }
        Assert.Less(flow.GetGate(4).Definition.exitDistance, 550f);

        RunSettlement result = flow.FinishRun(
            RunEndReason.FinishReached);
        Assert.IsTrue(result.reachedFinish);
        Assert.IsFalse(result.playerWon);
        Assert.AreEqual(0f, result.playerLeadMeters);
    }

    [Test]
    public void SettlementFeedbackNeverTreatsCancellationOrCalibrationHitAsEchoSuccess()
    {
        PredictionGateSettlement cancelled =
            PredictionGateEvaluator.Evaluate(1,
                PredictionGateRole.Counter,
                GateExecutionOutcome.Cancelled, 20f, 1f);
        PredictionGateSettlement calibrationHit =
            PredictionGateEvaluator.Evaluate(2,
                PredictionGateRole.Predicted,
                GateExecutionOutcome.Hit, 20f, 1f);
        PredictionGateSettlement counterSuccess =
            PredictionGateEvaluator.Evaluate(3,
                PredictionGateRole.Counter,
                GateExecutionOutcome.Success, 20f, 1f);
        PredictionGateSettlement predictedSuccess =
            PredictionGateEvaluator.Evaluate(4,
                PredictionGateRole.Predicted,
                GateExecutionOutcome.Success, 20f, 1f);

        Assert.AreEqual(SingleContractInstantFeedback.None,
            AIShadowRunner.FeedbackForSingleContractSettlement(
                true, cancelled));
        Assert.AreEqual(SingleContractInstantFeedback.None,
            AIShadowRunner.FeedbackForSingleContractSettlement(
                false, calibrationHit));
        Assert.AreEqual(SingleContractInstantFeedback.RewriteSucceeded,
            AIShadowRunner.FeedbackForSingleContractSettlement(
                true, counterSuccess));
        Assert.AreEqual(SingleContractInstantFeedback.PredictionHit,
            AIShadowRunner.FeedbackForSingleContractSettlement(
                true, predictedSuccess));
        Assert.AreEqual(SingleContractInstantFeedback.SafePass,
            AIShadowRunner.FeedbackForSingleContractSettlement(
                false, counterSuccess));
    }

    [Test]
    public void DistanceJumpNeverPresentsMoreThanOnePendingGate()
    {
        SingleContractFlow flow = CreateFixedFlow(91, originalHabitLane: 0);
        int counterLane = FindLaneForRole(
            flow.GetGate(0).Definition, PredictionGateRole.Counter);

        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 20f,
            playerDistance = 1000f,
            currentSpeed = 20f,
            playerLane = counterLane
        });

        Assert.AreEqual(-1, flow.ActiveGateIndex);
        Assert.AreEqual(PredictionGateLifecycle.Closed,
            flow.GetGate(0).State);
        Assert.AreEqual(PredictionGateLifecycle.Scheduled,
            flow.GetGate(1).State);
        Assert.AreEqual(0, CountPresentedOrPending(flow));
        Assert.AreEqual(1, flow.SettlementCount,
            "A distance jump may fail the committed gate, but cannot cascade into the next one in the same tick.");

        int secondCounterLane = FindLaneForRole(
            flow.GetGate(1).Definition, PredictionGateRole.Counter);
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 21f,
            playerDistance = 1000f,
            currentSpeed = 20f,
            playerLane = secondCounterLane
        });
        Assert.AreEqual(-1, flow.ActiveGateIndex);
        Assert.AreEqual(PredictionGateLifecycle.Closed,
            flow.GetGate(1).State);
        Assert.AreEqual(PredictionGateLifecycle.Scheduled,
            flow.GetGate(2).State);
        Assert.AreEqual(0, CountPresentedOrPending(flow));
        Assert.AreEqual(2, flow.SettlementCount);
    }

    [Test]
    public void LaneChangeAfterCommitDoesNotRewriteChoice()
    {
        SingleContractFlow flow = CreateFixedFlow(222);
        PredictionGateDefinition first = flow.GetGate(0).Definition;
        int counterLane = FindLaneForRole(
            first, PredictionGateRole.Counter);
        int otherLane = (counterLane + 1) % 3;

        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 12f,
            playerDistance = first.presentationDistance,
            currentSpeed = 18f,
            playerLane = counterLane
        });
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 13f,
            playerDistance = first.commitDistance,
            currentSpeed = 18f,
            playerLane = counterLane
        });
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 14f,
            playerDistance = first.resolveDistance,
            currentSpeed = 18f,
            playerLane = otherLane
        });

        Assert.AreEqual(counterLane,
            flow.GetGate(0).CommittedChoice.physicalLane);
        Assert.AreEqual(PredictionGateRole.Counter,
            flow.GetGate(0).CommittedRole);
        Assert.AreEqual(12f,
            flow.GetGatePresentedElapsedTime(0), 0.0001f);
        Assert.AreEqual(1f, flow.GetGate(0).ReactionTime, 0.0001f);
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.TryCommitChoice(new GateChoice
            {
                gateId = first.gateId,
                physicalLane = otherLane,
                routeDistance = first.resolveDistance
            }));
    }

    [Test]
    public void ObstacleCallbacksMatchCommittedGateAndSettleOnlyOnce()
    {
        SingleContractFlow flow = CreateFixedFlow(431);
        CommitFirstCounterLane(flow, 20f);
        GateObstacleEvent matching = EventForCommittedGate(flow);
        var wrongGate = matching;
        wrongGate.gateId++;
        var wrongLane = matching;
        wrongLane.physicalLane = (matching.physicalLane + 1) % 3;

        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstaclePassed(wrongGate));
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstacleHit(wrongLane));
        Assert.AreEqual(GateTransitionResult.Applied,
            flow.ResolveObstaclePassed(matching));
        Assert.AreEqual(1, flow.SettlementCount);
        float lead = flow.AccumulatedSignedLeadMeters;
        Assert.IsTrue(flow.TryConsumeSettlement(0,
            out PredictionGateSettlement consumed));
        Assert.AreEqual(lead, consumed.signedLeadMeters);
        Assert.IsFalse(flow.TryConsumeSettlement(0, out _));

        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstaclePassed(matching));
        Assert.AreEqual(1, flow.SettlementCount);
        Assert.AreEqual(lead, flow.AccumulatedSignedLeadMeters);
    }

    [Test]
    public void CommitKeepsLateralEvidenceWhenLaterInputRedirectsThePlayer()
    {
        SingleContractFlow flow = CreateFixedFlow(222);
        PredictionGateDefinition first = flow.GetGate(0).Definition;
        int committedLane = FindLaneForRole(first, PredictionGateRole.Counter);
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 12f,
            playerDistance = first.commitDistance,
            currentSpeed = 20f,
            playerLane = committedLane,
            hasLateralEvidence = true,
            lateralOffset = 0.35f,
            laneChangeInProgress = true
        });
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 13f,
            playerDistance = first.resolveDistance,
            currentSpeed = 20f,
            playerLane = (committedLane + 1) % 3,
            hasLateralEvidence = true,
            lateralOffset = -1.2f,
            laneChangeInProgress = false
        });
        Assert.AreEqual(GateTransitionResult.AlreadyApplied,
            flow.TryCommitChoice(new GateChoice
            {
                gateId = first.gateId,
                physicalLane = committedLane,
                hasLateralEvidence = true,
                lateralOffset = 99f,
                laneChangeInProgress = false
            }));

        GateAttempt attempt = flow.GetGate(0).BuildAttempt();
        Assert.AreEqual(committedLane, attempt.committedLane);
        Assert.IsTrue(attempt.hasLateralEvidence);
        Assert.AreEqual(0.35f, attempt.lateralOffset, 0.0001f);
        Assert.IsTrue(attempt.laneChangeInProgress);
    }

    [TestCase(false, GateExecutionReason.Unresolved)]
    [TestCase(true, GateExecutionReason.RouteAbandoned)]
    public void MissingPassAtExitRecordsTheCauseWithoutChangingThePenalty(
        bool changedLane, GateExecutionReason expectedReason)
    {
        SingleContractFlow flow = CreateFixedFlow(431);
        CommitFirstCounterLane(flow, 20f);
        GateObstacleEvent matching = EventForCommittedGate(flow);
        PredictionGateDefinition first = flow.GetGate(0).Definition;
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 15f,
            playerDistance = first.exitDistance,
            currentSpeed = 20f,
            playerLane = changedLane
                ? (matching.physicalLane + 1) % 3 : matching.physicalLane
        });

        PredictionGateSettlement result = flow.GetSettlement(0);
        Assert.AreEqual(GateExecutionOutcome.Hit, result.execution);
        Assert.AreEqual(expectedReason, result.executionReason);
        Assert.AreEqual(expectedReason,
            flow.GetGate(0).BuildAttempt().executionReason);
        Assert.AreEqual(-9f, result.signedLeadMeters, 0.0001f);
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstacleHit(matching));
        Assert.AreEqual(1, flow.SettlementCount);
        Assert.AreEqual(expectedReason, flow.GetSettlement(0).executionReason);
    }

    [Test]
    public void CollisionCallbackRecordsCollisionAndCannotBeRewrittenByAPass()
    {
        SingleContractFlow flow = CreateFixedFlow(431);
        CommitFirstCounterLane(flow, 20f);
        GateObstacleEvent matching = EventForCommittedGate(flow);

        Assert.AreEqual(GateTransitionResult.Applied,
            flow.ResolveObstacleHit(matching));
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstaclePassed(matching));
        Assert.AreEqual(GateTransitionResult.Rejected,
            flow.ResolveObstacleHit(matching));
        Assert.AreEqual(1, flow.SettlementCount);
        Assert.AreEqual(GateExecutionReason.Collision,
            flow.GetSettlement(0).executionReason);
        Assert.AreEqual(GateExecutionReason.Collision,
            flow.GetGate(0).BuildAttempt().executionReason);
        Assert.AreEqual(-9f, flow.AccumulatedSignedLeadMeters, 0.0001f);
    }

    [Test]
    public void NonObstacleLaneResolvesAtExitAndRecycleIsIdempotent()
    {
        SingleContractFlow flow = CreateFixedFlow(783);
        PredictionGateDefinition first = flow.GetGate(0).Definition;
        int neutralLane = FindLaneForRole(
            first, PredictionGateRole.Neutral);
        AdvanceThroughExit(flow, first, neutralLane, 20f);

        Assert.AreEqual(1, flow.SettlementCount);
        Assert.AreEqual(GateExecutionOutcome.Success,
            flow.GetSettlement(0).execution);
        Assert.AreEqual(PredictionGateRole.Neutral,
            flow.GetSettlement(0).chosenRole);

        PredictionGateDefinition second = flow.GetGate(1).Definition;
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 30f,
            playerDistance = second.presentationDistance,
            currentSpeed = 20f,
            playerLane = 0
        });
        int activeId = flow.ActiveGateId;
        Assert.AreEqual(GateTransitionResult.Applied,
            flow.RecycleGate(activeId));
        Assert.AreEqual(GateTransitionResult.AlreadyApplied,
            flow.RecycleGate(activeId));
        Assert.AreEqual(2, flow.SettlementCount);
        Assert.AreEqual(GateExecutionOutcome.Cancelled,
            flow.GetSettlement(1).execution);
    }

    [Test]
    public void CounterSuccessPairForwardsToOneTimeRelearnPlan()
    {
        SingleContractFlow flow = CreateFixedFlow(1871);
        CommitFirstCounterLane(flow, 20f);
        flow.ResolveObstaclePassed(EventForCommittedGate(flow));

        PredictionGateDefinition second = flow.GetGate(1).Definition;
        int counterLane = FindLaneForRole(
            second, PredictionGateRole.Counter);
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 26f,
            playerDistance = second.commitDistance,
            currentSpeed = 20f,
            playerLane = counterLane
        });
        flow.ResolveObstaclePassed(EventForCommittedGate(flow));

        Assert.IsTrue(flow.LastRelearnResult.triggered);
        Assert.IsTrue(flow.RelearnTriggered);
        Assert.AreEqual(second.gateId, flow.RelearnTriggerGateId);
        Assert.AreEqual(3, flow.RelearnStartGateNumber);
        Assert.AreEqual(2, flow.HypothesisVersion);
        Assert.AreEqual(StrategyKey.AvoidOriginal,
            flow.PredictedStrategy);
        Assert.AreEqual(2,
            flow.GetGate(2).Definition.hypothesisVersion);
        Assert.AreEqual(StrategyKey.AvoidOriginal,
            flow.GetGate(2).Definition.predictedStrategy);

        GameObject runnerObject = new GameObject(
            "Batched Relearn Settlement Test");
        runnerObject.SetActive(false);
        try
        {
            AIShadowRunner runner = runnerObject.AddComponent<AIShadowRunner>();
            var adaptation = new RunAdaptationState();
            SetPrivateField(runner, "_singleContractFlow", flow);
            SetPrivateField(runner, "_runAdaptationState", adaptation);
            InvokePrivate(runner, "ConsumeSingleContractSettlements");

            Assert.AreEqual(2, adaptation.resolvedGateCount);
            Assert.AreEqual(2, adaptation.successfulCounterCount);
            Assert.IsTrue(adaptation.relearnUsed);
            Assert.AreEqual(2, adaptation.hypothesisVersion);
            Assert.AreEqual(3, adaptation.relearnStartGateNumber);
        }
        finally
        {
            Object.DestroyImmediate(runnerObject);
        }

        PredictionGateDefinition third = flow.GetGate(2).Definition;
        int thirdNeutralLane = FindLaneForRole(
            third, PredictionGateRole.Neutral);
        AdvanceThroughExit(flow, third, thirdNeutralLane, 20f);

        Assert.IsFalse(flow.LastRelearnResult.triggered);
        Assert.AreEqual(second.gateId, flow.RelearnTriggerGateId);
        Assert.AreEqual(3, flow.RelearnStartGateNumber);
    }

    [Test]
    public void ChallengeFinishUsesOnlyArrivalOpponentAndSignedLead()
    {
        SingleContractFlow ahead = CreateFixedFlow(12);
        CommitFirstCounterLane(ahead, 20f);
        ahead.ResolveObstaclePassed(EventForCommittedGate(ahead));
        RunSettlement aheadResult = ahead.FinishRun(
            RunEndReason.FinishReached);
        Assert.Greater(aheadResult.playerLeadMeters, 0f);
        Assert.IsTrue(aheadResult.playerWon);

        SingleContractFlow behind = CreateFixedFlow(13);
        PredictionGateDefinition first = behind.GetGate(0).Definition;
        int predictedLane = FindLaneForRole(
            first, PredictionGateRole.Predicted);
        AdvanceThroughExit(behind, first, predictedLane, 20f);
        RunSettlement behindResult = behind.FinishRun(
            RunEndReason.FinishReached);
        Assert.Less(behindResult.playerLeadMeters, 0f);
        Assert.IsFalse(behindResult.playerWon);

        SingleContractFlow tie = CreateFixedFlow(14);
        RunSettlement tieResult = tie.FinishRun(
            RunEndReason.FinishReached);
        Assert.AreEqual(0f, tieResult.playerLeadMeters);
        Assert.IsTrue(tieResult.playerWon);

        SingleContractFlow abandoned = CreateFixedFlow(15);
        RunSettlement abandonedResult = abandoned.FinishRun(
            RunEndReason.Abandoned);
        Assert.IsFalse(abandonedResult.playerWon);
    }

    [Test]
    public void SingleContractVictoryDoesNotReadLegacyContractCompletion()
    {
        Assert.IsFalse(AIShadowRunner.IsContractVictory(
            1f, true, false, RunEndReason.FinishReached));
        Assert.IsTrue(AIShadowRunner.IsSingleContractVictory(
            1f, true, RunEndReason.FinishReached));
    }

    [Test]
    public void AuthoritativePhysicalLeadOverridesGateOnlyLeadAtFinish()
    {
        SingleContractFlow flow = CreateFixedFlow(16);
        PredictionGateDefinition first = flow.GetGate(0).Definition;
        int predictedLane = FindLaneForRole(
            first, PredictionGateRole.Predicted);
        AdvanceThroughExit(flow, first, predictedLane, 20f);
        Assert.Less(flow.AccumulatedSignedLeadMeters, 0f);

        RunSettlement result = flow.FinishRun(
            RunEndReason.FinishReached, 0.25f);

        Assert.IsTrue(result.playerWon);
        Assert.AreEqual(0.25f, result.playerLeadMeters, 0.0001f);
    }

    [Test]
    public void CalibrationPaceNormalizesToChallengeAccelerationCurve()
    {
        const float startSpeed = 10f;
        const float maximumSpeed = 40f;
        const float acceleration = 0.5f;
        float calibrationDuration =
            SingleContractFlow.CalibrationDurationSeconds;
        float calibrationPace = EchoTimeRules.DistanceForAcceleratingRun(
                                    startSpeed, maximumSpeed, acceleration,
                                    calibrationDuration)
                                / calibrationDuration;
        float scale = AIShadowRunner.CalculateSingleContractGhostPaceScale(
            calibrationPace, calibrationDuration, startSpeed,
            maximumSpeed, acceleration);
        Assert.AreEqual(1f, scale, 0.0001f);

        float challengeDuration =
            SingleContractFlow.ChallengeDurationSeconds;
        float integratedDistance = 0f;
        const float step = 0.01f;
        for (float time = step; time <= challengeDuration; time += step)
        {
            integratedDistance += AIShadowRunner
                .CalculateSingleContractGhostSpeed(startSpeed,
                    maximumSpeed, acceleration, time, scale) * step;
        }
        float expectedDistance = EchoTimeRules.DistanceForAcceleratingRun(
            startSpeed, maximumSpeed, acceleration, challengeDuration);
        Assert.AreEqual(expectedDistance, integratedDistance, 0.5f);
    }

    [Test]
    public void EqualNormalizedPaceStaysEqualAcrossDifferentCourseDurations()
    {
        const float startSpeed = 10f;
        const float maximumSpeed = 24f;
        const float acceleration = 0.12f;
        const float oldDuration = 55f;
        const float newDuration = 95f;
        float oldPace = EchoTimeRules.DistanceForAcceleratingRun(
                            startSpeed, maximumSpeed, acceleration,
                            oldDuration)
                        / oldDuration;
        float measuredPace = EchoTimeRules.DistanceForAcceleratingRun(
                                 startSpeed, maximumSpeed, acceleration,
                                 newDuration)
                             / newDuration;

        float blendedPace = AIShadowRunner
            .BlendSingleContractNormalizedPace(oldPace, oldDuration,
                measuredPace, newDuration, startSpeed, maximumSpeed,
                acceleration, 0.35f);
        float nextScale = AIShadowRunner
            .CalculateSingleContractGhostPaceScale(blendedPace,
                newDuration, startSpeed, maximumSpeed, acceleration);

        Assert.AreEqual(1f, nextScale, 0.0001f);
    }

    private static SingleContractFlow CreateFixedFlow(int seed,
        int originalHabitLane = 2)
    {
        var flow = new SingleContractFlow(
            new SingleContractFixedGateWindowFactory(CreateWindows()),
            originalHabitLane, 1f);
        flow.BeginRun(CreateContext(true, 950f, 5, seed, 3));
        return flow;
    }

    private static EchoRunContext CreateContext(bool hasOpponent,
        float courseDistance, int runSequence, int runSeed, int generation)
    {
        return new EchoRunContext
        {
            mode = GameplayFlowMode.SingleContract,
            hasOpponent = hasOpponent,
            courseDistance = courseDistance,
            runSequence = runSequence,
            runSeed = runSeed,
            generation = generation
        };
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

    private static void CommitFirstCounterLane(
        SingleContractFlow flow, float speed)
    {
        PredictionGateDefinition first = flow.GetGate(0).Definition;
        int counterLane = FindLaneForRole(
            first, PredictionGateRole.Counter);
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 12f,
            playerDistance = first.commitDistance,
            currentSpeed = speed,
            playerLane = counterLane
        });
        Assert.AreEqual(PredictionGateLifecycle.ChoiceCommitted,
            flow.GetGate(0).State);
    }

    private static void AdvanceThroughExit(SingleContractFlow flow,
        PredictionGateDefinition gate, int lane, float speed)
    {
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 12f,
            playerDistance = gate.commitDistance,
            currentSpeed = speed,
            playerLane = lane
        });
        flow.Tick(new EchoRunFrame
        {
            elapsedTime = 13f,
            playerDistance = gate.exitDistance,
            currentSpeed = speed,
            playerLane = lane
        });
    }

    private static GateObstacleEvent EventForCommittedGate(
        SingleContractFlow flow)
    {
        PredictionGateController gate = flow.GetGate(flow.ActiveGateIndex);
        return new GateObstacleEvent
        {
            gateId = gate.Definition.gateId,
            obstacleId = gate.Definition.gateId * 10,
            physicalLane = gate.CommittedChoice.physicalLane
        };
    }

    private static int FindLaneForRole(PredictionGateDefinition definition,
        PredictionGateRole role)
    {
        for (int i = 0; i < definition.lanes.Length; i++)
        {
            if (definition.lanes[i].role == role)
                return definition.lanes[i].physicalLane;
        }
        Assert.Fail("Requested role was not present in the gate.");
        return -1;
    }

    private static int CountPresentedOrPending(SingleContractFlow flow)
    {
        int count = 0;
        for (int i = 0; i < flow.GateCount; i++)
        {
            PredictionGateLifecycle state = flow.GetGate(i).State;
            if (state == PredictionGateLifecycle.Presented
                || state == PredictionGateLifecycle.ChoiceCommitted
                || state == PredictionGateLifecycle.ExecutionResolved
                || state == PredictionGateLifecycle.DistanceApplied)
                count++;
        }
        return count;
    }

    private static void SetPrivateField(object target, string name,
        object value)
    {
        FieldInfo field = target.GetType().GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing private field: " + name);
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Missing private method: " + name);
        method.Invoke(target, null);
    }
}
