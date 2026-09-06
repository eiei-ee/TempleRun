using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class SingleContractProductionUiTests
{
    [Test]
    public void PredictionGateWorldMarkerBuildsThreeColliderFreeRouteBands()
    {
        GameObject managerObject = new GameObject("TrackManager_Test");
        GameObject segment = new GameObject("Segment_Test");
        try
        {
            TrackManager manager = managerObject.AddComponent<TrackManager>();
            manager.segmentLength = 20f;
            manager.laneDistance = 3f;
            TrackSegmentData data = segment.AddComponent<TrackSegmentData>();
            data.routeDistance = 100f;
            var gate = new PredictionGateDefinition
            {
                gateId = 1,
                sequence = 1,
                commitDistance = 96f,
                resolveDistance = 104f,
                lanes = new[]
                {
                    Lane(0, PredictionGateRole.Predicted),
                    Lane(1, PredictionGateRole.Counter),
                    Lane(2, PredictionGateRole.Neutral)
                }
            };
            MethodInfo spawn = typeof(TrackManager).GetMethod(
                "SpawnPredictionGateVisual",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(spawn);
            spawn.Invoke(manager, new object[] { segment, gate, 100f });

            Transform root = segment.transform.Find(
                TrackManager.PredictionGateVisualRootName);
            Assert.IsNotNull(root,
                "Every formal gate needs an unmistakable world marker.");
            Assert.AreEqual(3, root.childCount,
                "Predicted, counter and neutral routes each need a band.");
            float obstacleLocalZ = 4f;
            Assert.GreaterOrEqual(obstacleLocalZ - root.localPosition.z,
                TrackManager.PredictionGateMinimumObstacleClearance,
                "A cross-segment marker must not be clamped against its obstacle.");
            Assert.AreEqual(
                TrackGeometryStandards.AuthoredRoadSurfaceTopY
                + TrackManager.PredictionGateSurfaceClearance,
                root.localPosition.y, 0.0001f,
                "Route markers must start above the authored road surface.");
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
                Assert.IsFalse(colliders[index].enabled,
                    "Route markers must never change gameplay collisions.");
            for (int laneIndex = 0; laneIndex < root.childCount; laneIndex++)
            {
                Transform lane = root.GetChild(laneIndex);
                Transform ribbon = lane.Find("ApproachRibbon");
                Transform decisionBand = lane.Find("DecisionBand");
                Assert.IsNotNull(ribbon);
                Assert.IsNotNull(decisionBand);
                Assert.AreEqual(TrackManager.PredictionGateRibbonWidth,
                    ribbon.localScale.x, 0.0001f,
                    "The route ribbon should read as a center guide, not a lane carpet.");
                Assert.AreEqual(TrackManager.PredictionGateRibbonLength,
                    ribbon.localScale.z, 0.0001f,
                    "The route ribbon should be a compact marker, not a long runway.");
                Assert.AreEqual(
                    -TrackManager.PredictionGateRibbonLength * 0.5f,
                    ribbon.localPosition.z, 0.0001f,
                    "The compact ribbon should end at the decision band.");
                Assert.AreEqual(
                    TrackManager.PredictionGateDecisionBandWidth,
                    decisionBand.localScale.x, 0.0001f,
                    "The decision band should leave visible road on both sides.");
                Assert.GreaterOrEqual(
                    root.localPosition.y + ribbon.localPosition.y
                    - ribbon.localScale.y * 0.5f,
                    TrackGeometryStandards.AuthoredRoadSurfaceTopY
                    + TrackManager.PredictionGateSurfaceClearance - 0.0001f,
                    "The approach ribbon must not be buried by the road mesh.");
                Assert.GreaterOrEqual(
                    root.localPosition.y + decisionBand.localPosition.y
                    - decisionBand.localScale.y * 0.5f,
                    TrackGeometryStandards.AuthoredRoadSurfaceTopY
                    + TrackManager.PredictionGateSurfaceClearance - 0.0001f,
                    "The decision band must not be buried by the road mesh.");
                Assert.IsNull(lane.Find("LeftPost"));
                Assert.IsNull(lane.Find("RightPost"));
                Assert.IsNull(lane.Find("TopBeam"));
            }

            Color predicted = TrackManager.PredictionGateRoleColor(
                PredictionGateRole.Predicted);
            Color counter = TrackManager.PredictionGateRoleColor(
                PredictionGateRole.Counter);
            Color neutral = TrackManager.PredictionGateRoleColor(
                PredictionGateRole.Neutral);
            Assert.AreNotEqual(predicted, counter);
            Assert.AreNotEqual(counter, neutral);
            Assert.AreNotEqual(predicted, neutral);
        }
        finally
        {
            Object.DestroyImmediate(segment);
            Object.DestroyImmediate(managerObject);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RouteSymbolsRemainDistinctWithoutColorAndInsideTheSafeLaneArea(
        bool finalGate)
    {
        var managerObject = new GameObject("RouteSymbol_Test");
        var segment = new GameObject("RouteSymbolSegment_Test");
        try
        {
            TrackManager manager = managerObject.AddComponent<TrackManager>();
            manager.segmentLength = 20f;
            manager.laneDistance = 3f;
            var gate = new PredictionGateDefinition
            {
                gateId = 1,
                sequence = 1,
                isFinal = finalGate,
                commitDistance = 96f,
                resolveDistance = 104f,
                lanes = new[]
                {
                    Lane(0, PredictionGateRole.Predicted),
                    Lane(1, PredictionGateRole.Counter),
                    Lane(2, PredictionGateRole.Neutral)
                }
            };
            typeof(TrackManager).GetMethod("SpawnPredictionGateVisual",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, new object[] { segment, gate, 100f });
            Transform root = segment.transform.Find(
                TrackManager.PredictionGateVisualRootName);
            Assert.IsNotNull(root);

            for (int index = 0; index < root.childCount; index++)
            {
                Transform lane = root.GetChild(index);
                Transform symbol = lane.Find("RoleSymbol");
                Assert.IsNotNull(symbol, "Every route needs a non-color signal.");
                AssertSymbolGeometry(symbol, gate.lanes[index].role);
                float laneCenter = lane.localPosition.x;
                float decisionFront = lane.Find("DecisionBand")
                    .GetComponent<Renderer>().bounds.max.z;
                foreach (MeshFilter mesh in symbol.GetComponentsInChildren<MeshFilter>())
                {
                    Assert.IsNotNull(mesh.sharedMesh);
                    Assert.Greater(mesh.sharedMesh.vertexCount, 0);
                    foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    {
                        Vector3 point = segment.transform.InverseTransformPoint(
                            mesh.transform.TransformPoint(vertex));
                        Assert.Less(Mathf.Abs(point.x - laneCenter),
                            manager.laneDistance * 0.5f,
                            "Symbol geometry must not spill into another route.");
                        Assert.Greater(point.z, decisionFront,
                            "The symbol must not merge with the decision stripe.");
                        Assert.Less(point.z, gate.resolveDistance - 100f,
                            "All symbol geometry must remain in the reserved area before the obstacle.");
                        Assert.GreaterOrEqual(point.y,
                            TrackGeometryStandards.AuthoredRoadSurfaceTopY
                            + TrackManager.PredictionGateSurfaceClearance - 0.0001f,
                            "Role symbols must remain above the opaque road surface.");
                    }
                }
                foreach (Collider collider in symbol.GetComponentsInChildren<Collider>(true))
                    Assert.IsFalse(collider.enabled,
                        "Symbols must never add physical collisions, including during destruction.");
            }
        }
        finally
        {
            Object.DestroyImmediate(segment);
            Object.DestroyImmediate(managerObject);
        }
    }

    private static void AssertSymbolGeometry(Transform symbol,
        PredictionGateRole role)
    {
        // Infer the visible shape from actual segment endpoints. Object names
        // and material colors cannot make an open arrow pass as a triangle.
        int expectedStrokes = role == PredictionGateRole.Predicted ? 4
            : role == PredictionGateRole.Counter ? 3 : 2;
        Assert.AreEqual(expectedStrokes, symbol.childCount);
        var ends = new System.Collections.Generic.List<Vector3>();
        int horizontal = 0;
        int vertical = 0;
        foreach (Transform stroke in symbol)
        {
            Assert.IsNotNull(stroke.GetComponent<MeshRenderer>());
            Assert.IsNotNull(stroke.GetComponent<MeshFilter>());
            Vector3 from = symbol.InverseTransformPoint(stroke.TransformPoint(
                new Vector3(0f, 0f, -0.5f)));
            Vector3 to = symbol.InverseTransformPoint(stroke.TransformPoint(
                new Vector3(0f, 0f, 0.5f)));
            Vector3 delta = to - from;
            Assert.Greater(delta.magnitude, 0.5f,
                "A zero-length or tiny stroke does not carry a readable role.");
            if (Mathf.Abs(delta.z) < 0.001f) horizontal++;
            if (Mathf.Abs(delta.x) < 0.001f) vertical++;
            ends.Add(from);
            ends.Add(to);
        }
        Assert.AreEqual(role == PredictionGateRole.Counter ? 1 : 2, horizontal);
        Assert.AreEqual(role == PredictionGateRole.Predicted ? 2 : 0, vertical);
        if (role == PredictionGateRole.Neutral)
        {
            Vector3 nearCenter = (ends[0] + ends[1]) * 0.5f;
            Vector3 farCenter = (ends[2] + ends[3]) * 0.5f;
            Assert.Less(Mathf.Abs(nearCenter.x - farCenter.x), 0.001f);
            Assert.Greater(Mathf.Abs(nearCenter.z - farCenter.z), 0.2f,
                "The safe route must read as two parallel lines, not one broken line.");
        }

        int expectedIncidence = role == PredictionGateRole.Neutral ? 1 : 2;
        foreach (Vector3 endpoint in ends)
        {
            int incidence = 0;
            foreach (Vector3 other in ends)
                if (Vector3.Distance(endpoint, other) < 0.001f) incidence++;
            Assert.AreEqual(expectedIncidence, incidence,
                "Prediction and counter shapes must close; safe strokes must remain separate.");
        }
    }

    [Test]
    public void ChallengeResultTitlesDescribeTheRaceWinner()
    {
        Assert.AreEqual("你跑赢了第3代回声",
            UIManager.GetSingleContractGameOverTitle(
                "你跑赢了第3代回声\n第4代回声已经形成",
                RunEndReason.FinishReached, true, true));
        Assert.AreEqual("第3代回声胜出",
            UIManager.GetSingleContractGameOverTitle(
                "第3代回声胜出\n它还记得同样的你",
                RunEndReason.FinishReached, true, false));

        string visible = UIManager.GetSingleContractGameOverTitle(
            "你跑赢了第3代回声\n第4代回声已经形成",
            RunEndReason.FinishReached, true, true);
        StringAssert.DoesNotContain("契约完成", visible);
        StringAssert.DoesNotContain("稳定度", visible);
        StringAssert.DoesNotContain("阶段", visible);
    }

    [Test]
    public void CalibrationResultTitlesStayInCalibrationLanguage()
    {
        Assert.AreEqual("第1代回声已经形成",
            UIManager.GetSingleContractGameOverTitle(
                "第1代回声已经形成\n它记住了：压力出现时，你偏向右侧",
                RunEndReason.FinishReached, false, false));
        Assert.AreEqual("AI 看到了你的跑法",
            UIManager.GetSingleContractGameOverTitle(
                "AI 看到了你的跑法\n本局未形成新的回声；再跑一局，继续观察",
                RunEndReason.FinishReached, false, false));
        Assert.AreEqual("回声保存失败",
            UIManager.GetSingleContractGameOverTitle(
                AIShadowRunner.BuildSingleContractSaveFailureResult(
                    RunEndReason.FinishReached, false, false, 0),
                RunEndReason.FinishReached, false, false));
        Assert.AreEqual("你跑赢了第3代回声",
            UIManager.GetSingleContractGameOverTitle(
                "你跑赢了第3代回声\n身份结算保存失败",
                RunEndReason.FinishReached, true, true));
    }

    [Test]
    public void IncompleteCalibrationShowsHonestProgressWithoutFailureLanguage()
    {
        var progress = new SingleContractCalibrationProgress
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
        };

        string result = EchoRunPresentation
            .BuildSingleContractCalibrationResult(progress);

        StringAssert.StartsWith("AI 看到了你的跑法", result);
        StringAssert.Contains(
            "观察 12/24 · 主动 4/6 · 动作种类 1/2", result);
        StringAssert.Contains("跳跃 1/2 · 滑铲 0/2", result);
        StringAssert.Contains(
            "选路 2/5 · 通过 1/3 · 右路2/3", result);
        StringAssert.Contains("还没到终点", result);
        StringAssert.Contains("本局未形成新的回声；再跑一局，继续观察", result);
        StringAssert.DoesNotContain("未完成", result);
        StringAssert.DoesNotContain("草稿", result);
        StringAssert.DoesNotContain("失败", result);
    }

    [Test]
    public void ResultActionsNameTheEchoOrCalibrationTarget()
    {
        Assert.AreEqual("挑战第4代回声",
            UIManager.GetSingleContractGameOverActionLabel(
                RunEndReason.FinishReached, true, true, 4, true));
        Assert.AreEqual("重试第3代回声",
            UIManager.GetSingleContractGameOverActionLabel(
                RunEndReason.FinishReached, true, false, 3, true));
        Assert.AreEqual("挑战第1代回声",
            UIManager.GetSingleContractGameOverActionLabel(
                RunEndReason.FinishReached, false, true, 1, true));
        Assert.AreEqual("让它再观察一局",
            UIManager.GetSingleContractGameOverActionLabel(
                RunEndReason.Collision, false, false, 0, false));
    }

    [Test]
    public void CalibrationUsesProgressToneUnlessSavingActuallyFails()
    {
        Assert.AreEqual(SingleContractResultTone.Success,
            UIManager.GetSingleContractGameOverTone(
                true, false, false, true, true));
        Assert.AreEqual(SingleContractResultTone.Progress,
            UIManager.GetSingleContractGameOverTone(
                true, false, false, false, false));
        Assert.AreEqual(SingleContractResultTone.Danger,
            UIManager.GetSingleContractGameOverTone(
                false, false, false, false, false));
        Assert.AreEqual(SingleContractResultTone.Danger,
            UIManager.GetSingleContractGameOverTone(
                true, true, false, false, true));
        Assert.AreEqual(SingleContractResultTone.Danger,
            UIManager.GetSingleContractGameOverTone(
                true, false, false, false, false, true));
    }

    [Test]
    public void FullVisibleProgressReportsAnInternalGenerationProblem()
    {
        var progress = new SingleContractCalibrationProgress
        {
            available = true,
            finishReached = true,
            evidenceReady = true,
            promotionReady = false,
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
        };

        string result = EchoRunPresentation
            .BuildSingleContractCalibrationResult(progress);

        StringAssert.StartsWith("回声形成遇到问题", result);
        StringAssert.Contains("已经到终点", result);
        StringAssert.Contains("观察已充分", result);
    }

    [Test]
    public void SaveFailureNeverClaimsThatANewIdentityExists()
    {
        string challenge =
            AIShadowRunner.BuildSingleContractSaveFailureResult(
                RunEndReason.FinishReached, true, true, 3);
        string calibration =
            AIShadowRunner.BuildSingleContractSaveFailureResult(
                RunEndReason.FinishReached, false, false, 0);

        StringAssert.Contains("你跑赢了第3代回声", challenge);
        StringAssert.Contains("下一代未形成", challenge);
        StringAssert.Contains("当前回声保持不变", challenge);
        StringAssert.DoesNotContain("已经形成", challenge);
        StringAssert.Contains("回声保存失败", calibration);
        StringAssert.Contains("当前回声未改变", calibration);
        StringAssert.DoesNotContain("校准完成", calibration);
    }

    [Test]
    public void FixedValidationResultNamesWinnerWithoutClaimingPersistence()
    {
        string won = AIShadowRunner.BuildSingleContractValidationResult(
            RunEndReason.FinishReached, true, true, 1);
        string lost = AIShadowRunner.BuildSingleContractValidationResult(
            RunEndReason.FinishReached, true, false, 1);
        string interrupted =
            AIShadowRunner.BuildSingleContractValidationResult(
                RunEndReason.Collision, true, false, 1);

        StringAssert.StartsWith("你跑赢了第1代固定回声", won);
        StringAssert.StartsWith("第1代固定回声胜出", lost);
        StringAssert.Contains("身份档未修改", won);
        StringAssert.Contains("身份档未修改", lost);
        StringAssert.Contains("身份档未修改", interrupted);
        StringAssert.DoesNotContain("已经形成", won);
        Assert.AreEqual("你跑赢了第1代固定回声",
            UIManager.GetSingleContractGameOverTitle(won,
                RunEndReason.FinishReached, true, true));
    }

    [TestCase(0, 0.61f, 0, 0.83f, 5,
        EchoCognitionChangeKind.Consolidated)]
    [TestCase(0, 0.83f, 0, 0.67f, 4,
        EchoCognitionChangeKind.Shaken)]
    [TestCase(0, 0.83f, 1, 0.50f, 3,
        EchoCognitionChangeKind.Shifted)]
    [TestCase(0, 0.83f, 1, 0.67f, 4,
        EchoCognitionChangeKind.Reversed)]
    [TestCase(0, 0.67f, 0, 0.70f, 4,
        EchoCognitionChangeKind.NoNewCognition)]
    public void CognitionAssessmentClassifiesVisibleRouteMemoryChange(
        int previousLane, float previousConfidence,
        int nextLane, float nextConfidence, int nextEvidence,
        EchoCognitionChangeKind expected)
    {
        EchoCognitionAssessment assessment = EchoCognitionAssessment.Compare(
            CognitionIdentity(3, previousLane, previousConfidence, 4),
            CognitionIdentity(4, nextLane, nextConfidence, nextEvidence),
            successfulCounterCount: 4, totalGateCount: 6,
            relearnStartGateNumber: 3,
            nextLaneHasUniqueEvidence: true);

        Assert.IsTrue(assessment.IsAvailable);
        Assert.AreEqual(expected, assessment.ChangeKind);
        Assert.AreEqual(3, assessment.PreviousGeneration);
        Assert.AreEqual(4, assessment.NextGeneration);
        Assert.AreEqual(4, assessment.SuccessfulCounterCount);
        Assert.AreEqual(6, assessment.TotalGateCount);
        Assert.AreEqual(3, assessment.RelearnStartGateNumber);
    }

    [Test]
    public void CognitionSummaryShowsOldBeliefRunEvidenceAndNewBelief()
    {
        EchoCognitionAssessment assessment = EchoCognitionAssessment.Compare(
            CognitionIdentity(3, 0, 0.83f, 5),
            CognitionIdentity(4, 1, 0.67f, 4),
            successfulCounterCount: 4, totalGateCount: 6,
            relearnStartGateNumber: 3,
            nextLaneHasUniqueEvidence: true);

        string summary = EchoRunPresentation
            .BuildSingleContractCognitionSummary(assessment);

        Assert.AreEqual(
            "此前记录：选路偏向左侧\n"
            + "本局反制通过 4/6 次 · 从第3次选路起，后续预测已调整\n"
            + "下一局记录：已更新为偏向中间",
            summary);
        foreach (string forbidden in new[]
                 {
                     "校准", "契约", "正式选择", "草稿", "身份",
                     "采样", "追学", "置信度", "路线认知"
                 })
            StringAssert.DoesNotContain(forbidden, summary);
    }

    [Test]
    public void MissingPromotionCannotClaimNewCognition()
    {
        EchoCognitionAssessment assessment = EchoCognitionAssessment.Compare(
            CognitionIdentity(3, 0, 0.83f, 5), null,
            successfulCounterCount: 4, totalGateCount: 6,
            relearnStartGateNumber: 3,
            nextLaneHasUniqueEvidence: true);

        Assert.IsFalse(assessment.IsAvailable);
        Assert.AreEqual(EchoCognitionChangeKind.Unavailable,
            assessment.ChangeKind);
        Assert.AreEqual("", EchoRunPresentation
            .BuildSingleContractCognitionSummary(assessment));
    }

    [Test]
    public void AmbiguousLaneTieCannotClaimCognitionShift()
    {
        EchoCognitionAssessment assessment = EchoCognitionAssessment.Compare(
            CognitionIdentity(3, 0, 0.80f, 4),
            CognitionIdentity(4, 1, 0.40f, 2),
            successfulCounterCount: 2, totalGateCount: 5,
            relearnStartGateNumber: 0,
            nextLaneHasUniqueEvidence: false);

        Assert.IsTrue(assessment.IsAvailable);
        Assert.AreEqual(EchoCognitionChangeKind.Shaken,
            assessment.ChangeKind);
        StringAssert.DoesNotContain("开始改猜",
            EchoRunPresentation.BuildSingleContractCognitionSummary(
                assessment));
    }

    [Test]
    public void ImprecisePreviousMemoryCannotClaimCognitionChange()
    {
        EchoCognitionAssessment assessment = EchoCognitionAssessment.Compare(
            CognitionIdentity(3, 0, 0.50f, 3),
            CognitionIdentity(4, 1, 0.67f, 4),
            successfulCounterCount: 3, totalGateCount: 6,
            relearnStartGateNumber: 3,
            nextLaneHasUniqueEvidence: true);

        Assert.IsFalse(assessment.IsAvailable);
        Assert.AreEqual(EchoCognitionChangeKind.Unavailable,
            assessment.ChangeKind);
    }

    [Test]
    public void CognitionComparisonDoesNotMutateEitherIdentity()
    {
        ActiveEchoIdentity previous = CognitionIdentity(3, 0, 0.80f, 4);
        ActiveEchoIdentity next = CognitionIdentity(4, 2, 0.67f, 4);
        string previousJson = JsonUtility.ToJson(previous);
        string nextJson = JsonUtility.ToJson(next);

        EchoCognitionAssessment.Compare(previous, next,
            successfulCounterCount: 3, totalGateCount: 6,
            relearnStartGateNumber: 3,
            nextLaneHasUniqueEvidence: true);

        Assert.AreEqual(previousJson, JsonUtility.ToJson(previous));
        Assert.AreEqual(nextJson, JsonUtility.ToJson(next));
    }

    [Test]
    public void CognitionSummaryStatesWhenEchoDidNotRelearn()
    {
        EchoCognitionAssessment assessment = EchoCognitionAssessment.Compare(
            CognitionIdentity(3, 0, 0.80f, 4),
            CognitionIdentity(4, 0, 0.82f, 5),
            successfulCounterCount: 1, totalGateCount: 6,
            relearnStartGateNumber: 0,
            nextLaneHasUniqueEvidence: true);

        string summary = EchoRunPresentation
            .BuildSingleContractCognitionSummary(assessment);
        StringAssert.Contains("本局反制通过 1/6 次 · 后续预测未调整", summary);
        StringAssert.Contains("下一局记录：仍偏向左侧", summary);
    }

    [Test]
    public void WrongParentCannotClaimNewCognition()
    {
        ActiveEchoIdentity next = CognitionIdentity(4, 1, 0.67f, 4);
        next.parentIdentityId = "unrelated-identity";

        EchoCognitionAssessment assessment = EchoCognitionAssessment.Compare(
            CognitionIdentity(3, 0, 0.83f, 5), next,
            successfulCounterCount: 4, totalGateCount: 6,
            relearnStartGateNumber: 3,
            nextLaneHasUniqueEvidence: true);

        Assert.IsFalse(assessment.IsAvailable);
        Assert.AreEqual(EchoCognitionChangeKind.Unavailable,
            assessment.ChangeKind);
    }

    [TestCase(GateExecutionReason.Completed, "成功通过")]
    [TestCase(GateExecutionReason.Collision, "发生碰撞，通过未完成")]
    [TestCase(GateExecutionReason.RouteAbandoned,
        "离开原提交路线，通过未完成")]
    [TestCase(GateExecutionReason.Unresolved,
        "未取得通过证据，无法确认完成")]
    [TestCase(GateExecutionReason.Cancelled,
        "观察中断，通过结果未确认")]
    public void GateReviewSeparatesSubmittedChoiceFromExecutionReason(
        GateExecutionReason reason, string expectedExecution)
    {
        var attempt = new GateAttempt
        {
            gateId = 2,
            committedLane = 2,
            chosenRole = PredictionGateRole.Counter,
            executionReason = reason,
            hasLateralEvidence = true,
            lateralOffset = 0.8f,
            laneChangeInProgress = true
        };

        string review = EchoRunPresentation.BuildSingleContractGateReview(attempt);

        Assert.AreEqual("关键选择：提交右路（反制），当时仍在换道\n动作结果："
            + expectedExecution, review);
        StringAssert.DoesNotContain("到达右路", review);
        StringAssert.DoesNotContain("它猜中了", review);
        StringAssert.DoesNotContain("本次不计", review);
    }

    [Test]
    public void LegacyHitWithoutReasonDoesNotInventACollision()
    {
        string review = EchoRunPresentation.BuildSingleContractGateReview(
            new GateAttempt
            {
                gateId = 1,
                committedLane = 0,
                chosenRole = PredictionGateRole.Predicted,
                execution = GateExecutionOutcome.Hit
            });

        StringAssert.Contains("未取得通过证据", review);
        StringAssert.DoesNotContain("碰撞", review);
    }

    [Test]
    public void GateWithoutACommittedChoiceDoesNotProduceAReview()
    {
        Assert.IsEmpty(EchoRunPresentation.BuildSingleContractGateReview(null));
        Assert.IsEmpty(EchoRunPresentation.BuildSingleContractGateReview(
            new GateAttempt
            {
                gateId = 1,
                committedLane = -1,
                executionReason = GateExecutionReason.Cancelled
            }));
    }

    private static PredictionGateLane Lane(int physicalLane,
        PredictionGateRole role)
    {
        return new PredictionGateLane
        {
            physicalLane = physicalLane,
            role = role
        };
    }

    private static ActiveEchoIdentity CognitionIdentity(int generation,
        int preferredLane, float confidence, int evidenceCount)
    {
        return new ActiveEchoIdentity
        {
            generation = generation,
            identityId = "identity-" + generation,
            parentIdentityId = generation > 1
                ? "identity-" + (generation - 1)
                : "",
            memoryContract = new EchoMemoryContract
            {
                preferredLane = preferredLane,
                confidence = confidence,
                evidenceCount = evidenceCount
            }
        };
    }
}
