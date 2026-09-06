using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class AIShadowSingleContractIsolationTests
{
    private const string LegacyShadowKey = "AIShadowProfileV1";
    private const string LegacyProfileJson =
        "{\"version\":5,\"sampleCount\":7,\"activeSampleCount\":3,"
        + "\"actionCounts\":[0,1,1,1,0]}";

    private static readonly string[] PreferenceStringKeys =
    {
        EchoRunSaveSystem.SaveKey,
        EchoRunSaveSystem.SaveSlotAKey,
        EchoRunSaveSystem.SaveSlotBKey,
        EchoRunSaveSystem.SingleContractSaveSlotAKey,
        EchoRunSaveSystem.SingleContractSaveSlotBKey,
        LegacyShadowKey
    };

    private static readonly string[] PreferenceIntKeys =
    {
        EchoRunSaveSystem.ActiveSaveSlotKey,
        EchoRunSaveSystem.SingleContractActiveSaveSlotKey,
        EchoRunSaveSystem.TrainingResetPendingKey
    };

    private static readonly string[] SaveSystemStaticFields =
    {
        "_data", "_initialized", "_activeSlot", "_generation",
        "_singleContractData", "_singleContractInitialized",
        "_singleContractActiveSlot", "_singleContractGeneration",
        "_trainingResetInProgress",
        "_trainingWritesEnabled",
        "<LoadedExistingSingleContractArchive>k__BackingField",
        "<MigratedLegacySingleContractIdentity>k__BackingField",
        "<RecoveredSingleContractFromBackup>k__BackingField"
    };

    private readonly Dictionary<string, string> _strings =
        new Dictionary<string, string>();
    private readonly Dictionary<string, int> _ints =
        new Dictionary<string, int>();
    private readonly Dictionary<string, object> _staticValues =
        new Dictionary<string, object>();

    [SetUp]
    public void SetUp()
    {
        Assert.IsNull(AIShadowRunner.Instance);
        foreach (string key in PreferenceStringKeys)
        {
            if (PlayerPrefs.HasKey(key))
                _strings[key] = PlayerPrefs.GetString(key);
            PlayerPrefs.DeleteKey(key);
        }
        foreach (string key in PreferenceIntKeys)
        {
            if (PlayerPrefs.HasKey(key))
                _ints[key] = PlayerPrefs.GetInt(key);
            PlayerPrefs.DeleteKey(key);
        }
        foreach (string field in SaveSystemStaticFields)
            _staticValues[field] = SaveField(field).GetValue(null);
        PlayerPrefs.Save();

        SetSaveField("_data", new EchoRunSaveData
        {
            shadowProfileJson = LegacyProfileJson
        });
        SetSaveField("_initialized", true);
        SetSaveField("_trainingResetInProgress", false);
        SetSaveField("_trainingWritesEnabled", true);
        ResetSingleContractCache();
        ActiveEchoIdentity identity = CreateIdentity();
        SaveCommitResult committed =
            EchoRunSaveSystem.TryCommitSingleContractSettlement(
                new RunSettlementCommit
                {
                    transactionId = "calibration-1",
                    runSequence = 1,
                    endReason = RunEndReason.FinishReached,
                    calibrationCompleted = true,
                    playerWon = false,
                    promotedIdentity = identity
                });
        Assert.IsTrue(committed.succeeded, committed.error);
    }

    [TearDown]
    public void TearDown()
    {
        if (AIShadowRunner.Instance != null)
            Object.DestroyImmediate(AIShadowRunner.Instance.gameObject);
        foreach (string key in PreferenceStringKeys)
            PlayerPrefs.DeleteKey(key);
        foreach (string key in PreferenceIntKeys)
            PlayerPrefs.DeleteKey(key);
        foreach (KeyValuePair<string, string> pair in _strings)
            PlayerPrefs.SetString(pair.Key, pair.Value);
        foreach (KeyValuePair<string, int> pair in _ints)
            PlayerPrefs.SetInt(pair.Key, pair.Value);
        PlayerPrefs.Save();
        foreach (KeyValuePair<string, object> pair in _staticValues)
            SaveField(pair.Key).SetValue(null, pair.Value);
    }

    [Test]
    public void FourSamplesOnlyMutateTheRunDraftAndNeverCheckpoint()
    {
        string oldProfileBefore = EchoRunSaveSystem.GetShadowProfileJson();
        string identityBefore =
            EchoRunSaveSystem.GetActiveEchoIdentity().ToJson();
        string slotABefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotAKey, "");
        string slotBBefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotBKey, "");
        GameObject host = new GameObject("Single Contract Shadow Test");
        AIShadowRunner runner = host.AddComponent<AIShadowRunner>();
        SetRunnerField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        InvokeRunner(runner, "BeginRun");

        Assert.IsNull(RunnerField(runner, "_duelFlow").GetValue(runner));
        Assert.IsNull(RunnerField(runner, "_contractEvaluator").GetValue(runner));
        for (int index = 0; index < 4; index++)
        {
            InvokeRunner(runner, "Learn",
                index % 2 == 0 ? ShadowAction.Jump : ShadowAction.Slide,
                new[] { 1f, 0f, 0.4f, 1f, 0f, 0.3f, 0f, 0f }, true);
        }

        var draft = (RunIdentityDraft)RunnerField(
            runner, "_runIdentityDraft").GetValue(runner);
        Assert.IsNotNull(draft);
        Assert.AreEqual(4, draft.sampleCount);
        Assert.AreEqual(4, draft.activeSampleCount);
        Assert.AreEqual(oldProfileBefore,
            EchoRunSaveSystem.GetShadowProfileJson());
        Assert.AreEqual(identityBefore,
            EchoRunSaveSystem.GetActiveEchoIdentity().ToJson());
        Assert.AreEqual(slotABefore, PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotAKey, ""));
        Assert.AreEqual(slotBBefore, PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotBKey, ""));
        Object.DestroyImmediate(host);
    }

    [Test]
    public void DestroyImmediateDiscardsDraftWithoutSavingEitherArchive()
    {
        GameObject host = new GameObject("Single Contract Destroy Test");
        AIShadowRunner runner = host.AddComponent<AIShadowRunner>();
        SetRunnerField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        InvokeRunner(runner, "BeginRun");
        InvokeRunner(runner, "Learn", ShadowAction.Jump,
            new[] { 1f, 0f, 0.4f, 1f, 0f, 0.3f, 0f, 0f }, true);
        string identityBefore =
            EchoRunSaveSystem.GetActiveEchoIdentity().ToJson();
        string oldProfileBefore = EchoRunSaveSystem.GetShadowProfileJson();
        string oldSlotABefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SaveSlotAKey, "");
        string oldSlotBBefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SaveSlotBKey, "");
        string singleSlotABefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotAKey, "");
        string singleSlotBBefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotBKey, "");

        Object.DestroyImmediate(host);

        Assert.AreEqual(oldProfileBefore,
            EchoRunSaveSystem.GetShadowProfileJson());
        Assert.AreEqual(identityBefore,
            EchoRunSaveSystem.GetActiveEchoIdentity().ToJson());
        Assert.AreEqual(oldSlotABefore, PlayerPrefs.GetString(
            EchoRunSaveSystem.SaveSlotAKey, ""));
        Assert.AreEqual(oldSlotBBefore, PlayerPrefs.GetString(
            EchoRunSaveSystem.SaveSlotBKey, ""));
        Assert.AreEqual(singleSlotABefore, PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotAKey, ""));
        Assert.AreEqual(singleSlotBBefore, PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotBKey, ""));
    }

    [Test]
    public void LegacyModeNeverCreatesSingleContractRuntimeOrDraft()
    {
        GameObject host = new GameObject("Legacy Shadow Isolation Test");
        AIShadowRunner runner = host.AddComponent<AIShadowRunner>();
        SetRunnerField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SixPhaseLegacy);

        InvokeRunner(runner, "BeginRun");

        Assert.IsNull(RunnerField(runner, "_singleContractFlow")
            .GetValue(runner));
        Assert.IsNull(RunnerField(runner, "_runIdentityDraft")
            .GetValue(runner));
        Assert.IsNotNull(RunnerField(runner, "_duelFlow")
            .GetValue(runner));
        Object.DestroyImmediate(host);
    }

    [Test]
    public void GenerationZeroDraftStartsWithUnboostedFallbackPace()
    {
        SetSaveField("_singleContractData",
            new EchoSingleContractSaveData());
        SetSaveField("_singleContractInitialized", true);
        GameObject host = new GameObject("Generation Zero Pace Test");
        AIShadowRunner runner = host.AddComponent<AIShadowRunner>();
        SetRunnerField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);

        InvokeRunner(runner, "BeginRun");

        var draft = (RunIdentityDraft)RunnerField(
            runner, "_runIdentityDraft").GetValue(runner);
        float duration = SingleContractFlow.CalibrationDurationSeconds;
        float expectedDistance = EchoTimeRules.DistanceForAcceleratingRun(
            10f, 40f, 0.5f, duration);
        Assert.AreEqual(AIShadowRunner.CalculatePhysicalPace(
                expectedDistance, duration),
            draft.physicalPace, 0.0001f);
        Assert.AreEqual(duration, draft.sourceCourseDuration, 0.0001f);
        Object.DestroyImmediate(host);
    }

    [Test]
    public void CalibrationDraftFlowsThroughRunnerAndPresenterWithoutPersistence()
    {
        SetSaveField("_singleContractData",
            new EchoSingleContractSaveData());
        SetSaveField("_singleContractInitialized", true);
        GameObject host = new GameObject("Calibration HUD Flow Test");
        AIShadowRunner runner = host.AddComponent<AIShadowRunner>();
        runner.minimumTrainingSamples = 6;
        runner.minimumActiveTrainingSamples = 4;
        runner.minimumActionCategories = 2;
        runner.minimumJumpSamples = 2;
        runner.minimumSlideSamples = 2;
        SetRunnerField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        InvokeRunner(runner, "BeginRun");
        var draft = (RunIdentityDraft)RunnerField(
            runner, "_runIdentityDraft").GetValue(runner);
        draft.RecordSample(ShadowAction.Jump);
        draft.RecordSample(ShadowAction.Jump);
        draft.RecordSample(ShadowAction.Slide);
        draft.RecordFormalGateChoice(1, 2, true);
        draft.RecordFormalGateChoice(2, 2, false);

        SingleContractHudData data =
            EchoHudPresenter.BuildSingleContractHudData(null, runner, "");

        Assert.IsTrue(data.showCalibrationProgress);
        Assert.AreEqual("AI 学习 3/6 · 主动 3/4 · 种类 2/2",
            data.memory);
        Assert.AreEqual("跳 2/2 · 滑 1/2 · 受伤 0",
            data.calibrationActionProgress);
        Assert.AreEqual("选路 2/5 · 通过 1/3 · 右路2/3",
            data.calibrationRouteProgress);
        Assert.AreEqual(1f / 3f, data.calibrationProgress01, 0.0001f);
        Object.DestroyImmediate(host);
    }

    [Test]
    public void InterruptedCalibrationKeepsOnlyAResultSnapshotAfterDraftDiscard()
    {
        SetSaveField("_singleContractData",
            new EchoSingleContractSaveData
            {
                importedLegacyFingerprint =
                    "calibration-result-snapshot-test"
            });
        SetSaveField("_singleContractInitialized", true);
        GameObject host = new GameObject("Calibration Result Snapshot Test");
        AIShadowRunner runner = host.AddComponent<AIShadowRunner>();
        runner.minimumTrainingSamples = 6;
        runner.minimumActiveTrainingSamples = 4;
        runner.minimumActionCategories = 2;
        runner.minimumJumpSamples = 2;
        runner.minimumSlideSamples = 2;
        SetRunnerField(runner, "_activeGameplayFlowMode",
            GameplayFlowMode.SingleContract);
        InvokeRunner(runner, "BeginRun");
        var draft = (RunIdentityDraft)RunnerField(
            runner, "_runIdentityDraft").GetValue(runner);
        draft.RecordSample(ShadowAction.Jump);
        draft.RecordFormalGateChoice(1, 2, true);

        InvokeRunner(runner, "FinishSingleContractRun",
            RunEndReason.Collision);

        Assert.IsNull(RunnerField(runner, "_runIdentityDraft")
            .GetValue(runner));
        SingleContractCalibrationProgress snapshot =
            runner.LastSingleContractCalibrationProgress;
        Assert.IsTrue(snapshot.available);
        Assert.IsFalse(snapshot.finishReached);
        Assert.AreEqual(1, snapshot.totalSamples);
        Assert.AreEqual(1, snapshot.formalChoices);
        StringAssert.Contains("观察 1/6", runner.LastResult);
        StringAssert.Contains("选路 1/5", runner.LastResult);
        StringAssert.Contains("还没到终点", runner.LastResult);
        StringAssert.Contains("本局未形成新的回声；再跑一局，继续观察",
            runner.LastResult);
        StringAssert.DoesNotContain("未完成", runner.LastResult);
        StringAssert.DoesNotContain("草稿", runner.LastResult);
        StringAssert.DoesNotContain("失败", runner.LastResult);
        Assert.IsNull(EchoRunSaveSystem.GetActiveEchoIdentity());
        Object.DestroyImmediate(host);
    }

    [Test]
    public void FixedValidationChallengeUsesTransientIdentityAndNeverWritesArchive()
    {
        string persistedIdentityBefore =
            EchoRunSaveSystem.GetActiveEchoIdentity().ToJson();
        string slotABefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotAKey, "");
        string slotBBefore = PlayerPrefs.GetString(
            EchoRunSaveSystem.SingleContractSaveSlotBKey, "");
        GameObject managerHost = new GameObject(
            "Fixed Validation Manager Test");
        managerHost.SetActive(false);
        GameObject runnerHost = null;
        try
        {
            GameManager manager = managerHost.AddComponent<GameManager>();
            Assert.IsTrue(manager.TryConfigureGameplayFlow(
                GameplayFlowMode.SingleContract,
                new SingleContractValidationConfig
                {
                    enabled = true,
                    fixedSeed = 424242,
                    freezeDirector = true,
                    disablePowerUps = true,
                    forceStandardDifficulty = true,
                    useFixedIdentity = true
                }));
            MethodInfo freeze = typeof(GameManager).GetMethod(
                "FreezeGameplayFlowConfiguration",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(freeze);
            freeze.Invoke(manager, null);

            runnerHost = new GameObject("Fixed Validation Runner Test");
            AIShadowRunner runner = runnerHost.AddComponent<AIShadowRunner>();
            SetRunnerField(runner, "_activeGameplayFlowMode",
                GameplayFlowMode.SingleContract);
            SetRunnerField(runner, "_gameManager", manager);
            InvokeRunner(runner, "BeginRun");

            ActiveEchoIdentity validationIdentity =
                runner.ActiveSingleContractIdentityPreview;
            Assert.IsNotNull(validationIdentity);
            Assert.AreEqual(SingleContractValidationIdentity.Create().ToJson(),
                validationIdentity.ToJson());
            Assert.AreNotEqual(
                EchoRunSaveSystem.GetActiveEchoIdentity().identityId,
                validationIdentity.identityId);
            Assert.IsTrue(runner.HasActiveOpponent);
            Assert.AreEqual(6, runner.SingleContractRuntime.GateCount);
            Assert.IsNull(RunnerField(runner, "_duelFlow").GetValue(runner));
            Assert.IsNull(RunnerField(runner, "_contractEvaluator")
                .GetValue(runner));

            InvokeRunner(runner, "FinishSingleContractRun",
                RunEndReason.Collision);

            Assert.IsTrue(runner.LastRunWasTransientValidation);
            Assert.IsFalse(runner.LastSingleContractCommitSucceeded);
            Assert.IsFalse(runner.LastSingleContractIdentityPromoted);
            StringAssert.Contains("真实身份档未修改", runner.LastResult);
            Assert.AreEqual(persistedIdentityBefore,
                EchoRunSaveSystem.GetActiveEchoIdentity().ToJson());
            Assert.AreEqual(slotABefore, PlayerPrefs.GetString(
                EchoRunSaveSystem.SingleContractSaveSlotAKey, ""));
            Assert.AreEqual(slotBBefore, PlayerPrefs.GetString(
                EchoRunSaveSystem.SingleContractSaveSlotBKey, ""));
        }
        finally
        {
            if (runnerHost != null)
                Object.DestroyImmediate(runnerHost);
            Object.DestroyImmediate(managerHost);
        }
    }

    private static ActiveEchoIdentity CreateIdentity()
    {
        AIShadowSequenceState sequence =
            new AIShadowSequencePolicy().ExportState();
        var identity = new ActiveEchoIdentity
        {
            generation = 1,
            sourceRunSequence = 1,
            policyWeights = new AIShadowPolicy().ExportWeights(),
            sequenceTransitions = sequence.transitions,
            sequencePairCount = sequence.pairCount,
            style = EchoIdentityStyleSnapshot.FromPlayerStyle(
                new PlayerStyleData()),
            pace = 13f,
            clarity = 1f,
            memoryContract = new EchoMemoryContract
            {
                contractId = "route-contract-1",
                preferredLane = 2,
                confidence = 1f,
                evidenceCount = 5
            }
        };
        identity.identityId = ActiveEchoIdentity.CreateIdentityId(identity);
        identity.memoryContract.identityId = identity.identityId;
        return identity;
    }

    private static void ResetSingleContractCache()
    {
        SetSaveField("_singleContractData", null);
        SetSaveField("_singleContractInitialized", false);
        SetSaveField("_singleContractActiveSlot", -1);
        SetSaveField("_singleContractGeneration", 0L);
        SetSaveField(
            "<LoadedExistingSingleContractArchive>k__BackingField", false);
        SetSaveField(
            "<MigratedLegacySingleContractIdentity>k__BackingField", false);
        SetSaveField(
            "<RecoveredSingleContractFromBackup>k__BackingField", false);
    }

    private static void InvokeRunner(AIShadowRunner runner, string method,
        params object[] arguments)
    {
        MethodInfo info = typeof(AIShadowRunner).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, "Missing runner method: " + method);
        info.Invoke(runner, arguments);
    }

    private static void SetRunnerField(AIShadowRunner runner,
        string name, object value)
    {
        RunnerField(runner, name).SetValue(runner, value);
    }

    private static FieldInfo RunnerField(AIShadowRunner runner, string name)
    {
        FieldInfo field = runner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing runner field: " + name);
        return field;
    }

    private static void SetSaveField(string name, object value)
    {
        SaveField(name).SetValue(null, value);
    }

    private static FieldInfo SaveField(string name)
    {
        FieldInfo field = typeof(EchoRunSaveSystem).GetField(name,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing save field: " + name);
        return field;
    }
}
