using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PredictionGateTests
{
    [Test]
    public void LifecycleAdvancesInOrderAndEachStepIsIdempotent()
    {
        var gate = CreateController(7);
        var choice = new GateChoice { gateId = 7, physicalLane = 1 };

        Assert.AreEqual(GateTransitionResult.Rejected,
            gate.CommitChoice(choice));
        Assert.AreEqual(GateTransitionResult.Applied, gate.Present());
        Assert.AreEqual(GateTransitionResult.AlreadyApplied, gate.Present());
        Assert.AreEqual(GateTransitionResult.Applied,
            gate.CommitChoice(choice));
        Assert.AreEqual(GateTransitionResult.AlreadyApplied,
            gate.CommitChoice(choice));
        Assert.AreEqual(GateTransitionResult.Rejected,
            gate.CommitChoice(new GateChoice
            {
                gateId = 7,
                physicalLane = 2
            }));

        Assert.AreEqual(GateTransitionResult.Applied,
            gate.ResolveExecution(GateExecutionOutcome.Success,
                20f, 1f, out PredictionGateSettlement settlement));
        Assert.AreEqual(GateTransitionResult.AlreadyApplied,
            gate.ResolveExecution(GateExecutionOutcome.Success,
                99f, 0f, out PredictionGateSettlement repeated));
        Assert.AreEqual(settlement.signedLeadMeters,
            repeated.signedLeadMeters);
        Assert.AreEqual(GateTransitionResult.Rejected,
            gate.ResolveExecution(GateExecutionOutcome.Hit,
                20f, 1f, out _));
        Assert.AreEqual(GateTransitionResult.Applied,
            gate.ApplyDistance(out settlement));
        Assert.AreEqual(GateTransitionResult.AlreadyApplied,
            gate.ApplyDistance(out repeated));
        Assert.AreEqual(settlement.signedLeadMeters,
            repeated.signedLeadMeters);
        Assert.AreEqual(GateTransitionResult.Applied, gate.Close());
        Assert.AreEqual(GateTransitionResult.AlreadyApplied, gate.Close());
        Assert.AreEqual(GateTransitionResult.Rejected, gate.Present());
        Assert.AreEqual(PredictionGateLifecycle.Closed, gate.State);
    }

    [Test]
    public void CancellationIsTerminalAndIdempotent()
    {
        PredictionGateController gate = CreateController(8);

        Assert.AreEqual(GateTransitionResult.Applied,
            gate.Cancel(out PredictionGateSettlement settlement));
        Assert.AreEqual(GateExecutionOutcome.Cancelled,
            settlement.execution);
        Assert.AreEqual(GateExecutionReason.Cancelled,
            settlement.executionReason);
        Assert.AreEqual(GateExecutionReason.Cancelled,
            gate.BuildAttempt().executionReason);
        Assert.AreEqual(0f, settlement.signedLeadMeters);
        Assert.AreEqual(GateTransitionResult.AlreadyApplied,
            gate.Cancel(out PredictionGateSettlement repeated));
        Assert.AreEqual(settlement.signedLeadMeters,
            repeated.signedLeadMeters);
        Assert.AreEqual(GateTransitionResult.Rejected, gate.Present());
        Assert.AreEqual(GateTransitionResult.Rejected,
            gate.ApplyDistance(out _));
        Assert.AreEqual(PredictionGateLifecycle.Cancelled, gate.State);
    }

    [TestCase(PredictionGateRole.Predicted,
        GateExecutionOutcome.Success, 0f, 0.225f)]
    [TestCase(PredictionGateRole.Counter,
        GateExecutionOutcome.Success, 0.375f, 0f)]
    [TestCase(PredictionGateRole.Neutral,
        GateExecutionOutcome.Success, 0f, 0f)]
    [TestCase(PredictionGateRole.Predicted,
        GateExecutionOutcome.Hit, 0f, 0.45f)]
    [TestCase(PredictionGateRole.Counter,
        GateExecutionOutcome.Hit, 0f, 0.45f)]
    [TestCase(PredictionGateRole.Neutral,
        GateExecutionOutcome.Hit, 0f, 0.45f)]
    [TestCase(PredictionGateRole.Predicted,
        GateExecutionOutcome.Cancelled, 0f, 0f)]
    [TestCase(PredictionGateRole.Counter,
        GateExecutionOutcome.Cancelled, 0f, 0f)]
    [TestCase(PredictionGateRole.Neutral,
        GateExecutionOutcome.Cancelled, 0f, 0f)]
    public void SettlementMatrixHasOneAuthoritativeOutcome(
        PredictionGateRole role, GateExecutionOutcome execution,
        float expectedPlayerSeconds, float expectedEchoSeconds)
    {
        PredictionGateSettlement settlement =
            PredictionGateEvaluator.Evaluate(
                3, role, execution, 20f, 1f);

        Assert.AreEqual(expectedPlayerSeconds,
            settlement.playerLeadSeconds, 0.00001f);
        Assert.AreEqual(expectedEchoSeconds,
            settlement.echoLeadSeconds, 0.00001f);
        Assert.AreEqual(expectedPlayerSeconds * 20f,
            settlement.playerLeadMeters, 0.00001f);
        Assert.AreEqual(expectedEchoSeconds * 20f,
            settlement.echoLeadMeters, 0.00001f);
        Assert.AreEqual((expectedPlayerSeconds - expectedEchoSeconds) * 20f,
            settlement.signedLeadMeters, 0.00001f);
    }

    [Test]
    public void ConfidenceLinearlyScalesSecondsToMeters()
    {
        PredictionGateSettlement low = PredictionGateEvaluator.Evaluate(
            1, PredictionGateRole.Counter,
            GateExecutionOutcome.Success, 10f, 0f);
        PredictionGateSettlement high = PredictionGateEvaluator.Evaluate(
            1, PredictionGateRole.Counter,
            GateExecutionOutcome.Success, 10f, 1f);

        Assert.AreEqual(0.4f, low.effectScale, 0.00001f);
        Assert.AreEqual(1.5f, low.playerLeadMeters, 0.00001f);
        Assert.AreEqual(1f, high.effectScale, 0.00001f);
        Assert.AreEqual(3.75f, high.playerLeadMeters, 0.00001f);
    }

    [TestCase(GateExecutionReason.None, GateExecutionReason.Unresolved)]
    [TestCase(GateExecutionReason.Collision, GateExecutionReason.Collision)]
    [TestCase(GateExecutionReason.RouteAbandoned,
        GateExecutionReason.RouteAbandoned)]
    [TestCase(GateExecutionReason.Unresolved, GateExecutionReason.Unresolved)]
    public void FailureReasonPreservesScoringWithoutInventingACollision(
        GateExecutionReason suppliedReason, GateExecutionReason expectedReason)
    {
        PredictionGateSettlement result = PredictionGateEvaluator.Evaluate(
            1, PredictionGateRole.Counter, GateExecutionOutcome.Hit,
            20f, 1f, suppliedReason);

        Assert.AreEqual(expectedReason, result.executionReason);
        Assert.AreEqual(GateExecutionOutcome.Hit, result.execution);
        Assert.AreEqual(-9f, result.signedLeadMeters, 0.0001f);
        Assert.IsFalse(result.IsCounterSuccess);
    }

    [Test]
    public void ShieldAbsorptionCannotRewriteARecordedGateHit()
    {
        PredictionGateController gate = CreateController(9);
        Assert.AreEqual(GateTransitionResult.Applied, gate.Present());
        int counterLane = FindLaneForRole(
            gate.Definition, PredictionGateRole.Counter);
        Assert.AreEqual(GateTransitionResult.Applied,
            gate.CommitChoice(new GateChoice
            {
                gateId = 9,
                physicalLane = counterLane
            }));
        Assert.AreEqual(GateTransitionResult.Applied,
            gate.ResolveExecution(GateExecutionOutcome.Hit,
                20f, 1f, out PredictionGateSettlement hit));

        GameObject host = new GameObject("Shield Gate Result Test");
        host.SetActive(false);
        try
        {
            PowerUpController shield = host.AddComponent<PowerUpController>();
            typeof(PowerUpController).GetField(
                    "<ActivePowerUp>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(shield, PowerUpId.Shield);
            typeof(PowerUpController).GetField("_shieldCharges",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(shield, 1);

            Assert.IsTrue(shield.TryAbsorbCollision());
            Assert.AreEqual(GateExecutionOutcome.Hit, hit.execution);
            Assert.IsFalse(hit.IsCounterSuccess);
            Assert.AreEqual(GateTransitionResult.Rejected,
                gate.ResolveExecution(GateExecutionOutcome.Success,
                    20f, 1f, out PredictionGateSettlement repeated));
            Assert.AreEqual(GateExecutionOutcome.Hit, repeated.execution);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void TwoConsecutiveCounterSuccessesRelearnOnlyScheduledGatesOnce()
    {
        SingleContractGatePlan plan = CreatePlan(1931);
        PredictionGateController first = plan.GetGate(0);
        PredictionGateController second = plan.GetGate(1);
        PredictionGateController visibleFuture = plan.GetGate(2);
        ResolveAsRole(first, PredictionGateRole.Counter,
            out PredictionGateSettlement firstSettlement);
        ResolveAsRole(second, PredictionGateRole.Counter,
            out PredictionGateSettlement secondSettlement);
        Assert.AreEqual(GateTransitionResult.Applied,
            visibleFuture.Present());
        PredictionGateDefinition visibleBefore = visibleFuture.Definition;

        EchoRelearnResult firstResult =
            plan.RecordSettlement(firstSettlement);
        EchoRelearnResult secondResult =
            plan.RecordSettlement(secondSettlement);

        Assert.IsTrue(firstResult.accepted);
        Assert.IsFalse(firstResult.triggered);
        Assert.IsTrue(secondResult.accepted);
        Assert.IsTrue(secondResult.triggered);
        Assert.AreEqual(2, plan.HypothesisVersion);
        Assert.AreEqual(StrategyKey.AvoidOriginal,
            plan.PredictedStrategy);
        Assert.AreEqual(3, secondResult.remappedGateCount);

        PredictionGateDefinition visibleAfter = visibleFuture.Definition;
        Assert.AreEqual(visibleBefore.hypothesisVersion,
            visibleAfter.hypothesisVersion);
        Assert.AreEqual(visibleBefore.predictedStrategy,
            visibleAfter.predictedStrategy);
        AssertLaneMappingsEqual(visibleBefore, visibleAfter);

        for (int i = 3; i < plan.GateCount; i++)
        {
            PredictionGateDefinition remapped = plan.GetGate(i).Definition;
            Assert.AreEqual(PredictionGateLifecycle.Scheduled,
                plan.GetGate(i).State);
            Assert.AreEqual(2, remapped.hypothesisVersion);
            Assert.AreEqual(StrategyKey.AvoidOriginal,
                remapped.predictedStrategy);
            Assert.IsTrue(remapped.TryGetLane(
                FindLaneForStrategy(remapped, StrategyKey.AvoidOriginal),
                out PredictionGateLane predicted));
            Assert.AreEqual(PredictionGateRole.Predicted, predicted.role);
        }

        EchoRelearnResult duplicate =
            plan.RecordSettlement(secondSettlement);
        Assert.IsFalse(duplicate.accepted);
        Assert.IsFalse(duplicate.triggered);
        Assert.AreEqual(2, plan.HypothesisVersion);
    }

    [Test]
    public void RelearnRequiresAtLeastTwoUnpresentedGates()
    {
        SingleContractGatePlan plan = CreatePlan(611);
        for (int i = 1; i < plan.GateCount - 1; i++)
            Assert.AreEqual(GateTransitionResult.Applied,
                plan.GetGate(i).Present());

        ResolveAsRole(plan.GetGate(0), PredictionGateRole.Counter,
            out PredictionGateSettlement first);
        ResolveAsRole(plan.GetGate(1), PredictionGateRole.Counter,
            out PredictionGateSettlement second, alreadyPresented: true);

        Assert.IsFalse(plan.RecordSettlement(first).triggered);
        EchoRelearnResult result = plan.RecordSettlement(second);

        Assert.IsTrue(result.accepted);
        Assert.IsFalse(result.triggered);
        Assert.IsFalse(plan.RelearnTriggered);
        Assert.AreEqual(1, plan.HypothesisVersion);
        Assert.AreEqual(1, CountScheduled(plan));
    }

    [Test]
    public void FixedSeedPlanIsIndependentFromSharedRunRandomConsumption()
    {
        PredictionGateDistanceWindow[] windows = CreateWindows();
        AIRunRandom.BeginRun(44);
        for (int i = 0; i < 37; i++)
            _ = AIRunRandom.Value;
        PredictionGateDefinition[] first = PredictionGateTemplates.Create(
            12, 91273, 2, windows);

        AIRunRandom.BeginRun(991);
        for (int i = 0; i < 113; i++)
            _ = AIRunRandom.Range(0, 1000);
        PredictionGateDefinition[] second = PredictionGateTemplates.Create(
            12, 91273, 2, windows);

        Assert.AreEqual(6, first.Length);
        Assert.AreEqual(5, CountNormal(first));
        Assert.IsTrue(first[5].isFinal);
        for (int i = 0; i < first.Length; i++)
            AssertDefinitionsEqual(first[i], second[i]);
    }

    private static SingleContractGatePlan CreatePlan(int runSeed)
    {
        return new SingleContractGatePlan(PredictionGateTemplates.Create(
            4, runSeed, 2, CreateWindows()));
    }

    private static PredictionGateController CreateController(int gateId)
    {
        return new PredictionGateController(CreateDefinition(gateId));
    }

    private static PredictionGateDefinition CreateDefinition(int gateId)
    {
        return new PredictionGateDefinition
        {
            runId = 1,
            gateId = gateId,
            sequence = 1,
            hypothesisVersion = 1,
            predictedStrategy = StrategyKey.OriginalHabit,
            presentationDistance = 10f,
            commitDistance = 20f,
            resolveDistance = 25f,
            exitDistance = 30f,
            lanes = new[]
            {
                new PredictionGateLane
                {
                    physicalLane = 0,
                    role = PredictionGateRole.Predicted,
                    strategyKey = StrategyKey.OriginalHabit
                },
                new PredictionGateLane
                {
                    physicalLane = 1,
                    role = PredictionGateRole.Counter,
                    strategyKey = StrategyKey.AvoidOriginal
                },
                new PredictionGateLane
                {
                    physicalLane = 2,
                    role = PredictionGateRole.Neutral,
                    strategyKey = StrategyKey.Neutral
                }
            }
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

    private static void ResolveAsRole(PredictionGateController gate,
        PredictionGateRole role, out PredictionGateSettlement settlement,
        bool alreadyPresented = false)
    {
        if (!alreadyPresented)
            Assert.AreEqual(GateTransitionResult.Applied, gate.Present());
        PredictionGateDefinition definition = gate.Definition;
        int lane = FindLaneForRole(definition, role);
        Assert.AreEqual(GateTransitionResult.Applied,
            gate.CommitChoice(new GateChoice
            {
                gateId = definition.gateId,
                physicalLane = lane
            }));
        Assert.AreEqual(GateTransitionResult.Applied,
            gate.ResolveExecution(GateExecutionOutcome.Success,
                20f, 1f, out settlement));
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

    private static int FindLaneForStrategy(
        PredictionGateDefinition definition, StrategyKey strategy)
    {
        for (int i = 0; i < definition.lanes.Length; i++)
        {
            if (definition.lanes[i].strategyKey == strategy)
                return definition.lanes[i].physicalLane;
        }
        Assert.Fail("Requested strategy was not present in the gate.");
        return -1;
    }

    private static int CountScheduled(SingleContractGatePlan plan)
    {
        int count = 0;
        for (int i = 0; i < plan.GateCount; i++)
        {
            if (plan.GetGate(i).State == PredictionGateLifecycle.Scheduled)
                count++;
        }
        return count;
    }

    private static int CountNormal(PredictionGateDefinition[] definitions)
    {
        int count = 0;
        for (int i = 0; i < definitions.Length; i++)
        {
            if (!definitions[i].isFinal)
                count++;
        }
        return count;
    }

    private static void AssertLaneMappingsEqual(
        PredictionGateDefinition expected,
        PredictionGateDefinition actual)
    {
        Assert.AreEqual(expected.lanes.Length, actual.lanes.Length);
        for (int i = 0; i < expected.lanes.Length; i++)
        {
            Assert.AreEqual(expected.lanes[i].physicalLane,
                actual.lanes[i].physicalLane);
            Assert.AreEqual(expected.lanes[i].role, actual.lanes[i].role);
            Assert.AreEqual(expected.lanes[i].strategyKey,
                actual.lanes[i].strategyKey);
        }
    }

    private static void AssertDefinitionsEqual(
        PredictionGateDefinition expected,
        PredictionGateDefinition actual)
    {
        Assert.AreEqual(expected.runId, actual.runId);
        Assert.AreEqual(expected.gateId, actual.gateId);
        Assert.AreEqual(expected.sequence, actual.sequence);
        Assert.AreEqual(expected.hypothesisVersion,
            actual.hypothesisVersion);
        Assert.AreEqual(expected.predictedStrategy,
            actual.predictedStrategy);
        Assert.AreEqual(expected.isFinal, actual.isFinal);
        Assert.AreEqual(expected.presentationDistance,
            actual.presentationDistance);
        Assert.AreEqual(expected.commitDistance, actual.commitDistance);
        Assert.AreEqual(expected.resolveDistance, actual.resolveDistance);
        Assert.AreEqual(expected.exitDistance, actual.exitDistance);
        AssertLaneMappingsEqual(expected, actual);
    }
}
