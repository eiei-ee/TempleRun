using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Integration at the production state/event boundary, not a simulated player run.
// UIManager starts normally and owns the bundled HUD. The inactive GameManager
// still executes StartGame/Pause/Resume/CompleteCourse and raises its real events,
// but cannot spawn a track or advance the race. Feedback is injected at the
// runner's existing publication boundary; collisions and scene reload are not tested.
public sealed class RacingFeedbackUiLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly List<GameObject> _owned = new List<GameObject>();
    private readonly Dictionary<Behaviour, bool> _enabled = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<GameObject, bool> _active = new Dictionary<GameObject, bool>();
    private readonly Dictionary<FieldInfo, object> _statics = new Dictionary<FieldInfo, object>();
    private PreferenceSnapshot _preferences;
    private GameManager _game;
    private AIShadowRunner _runner;
    private UIManager _ui;
    private Canvas _canvas;
    private GameObject _hud;
    private Text _feedback;
    private CanvasGroup _feedbackGroup;
    private float _timeScale;
    private GameObject _selected;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _preferences = new PreferenceSnapshot();
        _timeScale = Time.timeScale;
        _selected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject : null;
        // Existing runtime components remain owned by their original fixtures.
        // Stop their Update loops before replacing singleton references.
        foreach (MonoBehaviour component in Object.FindObjectsOfType<MonoBehaviour>())
        {
            if (component.GetType().Assembly != typeof(GameManager).Assembly) continue;
            _enabled[component] = component.enabled;
            component.enabled = false;
        }
        foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
            HideExisting(canvas.gameObject);
        GameObject player = GameObject.Find("player");
        if (player != null) HideExisting(player);

        foreach (Type type in new[]
        {
            typeof(GameManager), typeof(EchoRunSaveSystem), typeof(AIRunTelemetry), typeof(AIRunRandom),
            typeof(AIPlayerSkillEstimator), typeof(StyleTracker), typeof(EchoRunAccessibility)
        })
            CaptureStatics(type);
        foreach (Type type in new[]
        {
            typeof(GameManager), typeof(AIShadowRunner), typeof(AITrackDirector),
            typeof(PowerUpController), typeof(AudioManager), typeof(InputManager),
            typeof(EchoPhaseVisualController), typeof(MenuScreenRouter)
        })
        {
            FieldInfo instance = type.GetField("<Instance>k__BackingField", Static);
            Assert.IsNotNull(instance, type.Name);
            _statics[instance] = instance.GetValue(null);
            instance.SetValue(null, null);
        }

        _preferences.InstallEmpty();
        SetStatic(typeof(EchoRunSaveSystem), "_initialized", false);
        SetStatic(typeof(EchoRunSaveSystem), "_singleContractInitialized", false);
        SetStatic(typeof(EchoRunSaveSystem), "_data", null);
        SetStatic(typeof(EchoRunSaveSystem), "_singleContractData", null);
        SetStatic(typeof(EchoRunSaveSystem), "_trainingResetInProgress", false);
        SetStatic(typeof(EchoRunSaveSystem), "_trainingWritesEnabled", true);
        EchoRunSaveSystem.EnsureInitialized();
        StyleTracker.ResetTrainingInMemory();
        AIPlayerSkillEstimator.ResetTrainingInMemory();
        AIRunTelemetry.ResetTrainingInMemory();
        // Disabling an existing UI does not unsubscribe this static event.
        // Keep preference changes inside this fixture; restore its original
        // subscribers with the other static fields after destroying our UI.
        SetStatic(typeof(EchoRunAccessibility), "Changed", null);
        SetStatic(typeof(EchoRunAccessibility), "_initialized", false);
        EchoRunAccessibility.SetReducedMotion(false);

        _game = CreateInactive<GameManager>("LifecycleGame");
        SetStatic(typeof(GameManager), "<Instance>k__BackingField", _game);
        Assert.IsTrue(_game.TryConfigureGameplayFlow(GameplayFlowMode.SingleContract,
            new SingleContractValidationConfig
            {
                enabled = true, fixedSeed = 51005, freezeDirector = true,
                disablePowerUps = true, forceStandardDifficulty = true,
                useFixedIdentity = true
            }));
        _runner = CreateInactive<AIShadowRunner>("LifecycleFeedbackSource");
        SetField(_runner, "_activeGameplayFlowMode", GameplayFlowMode.SingleContract);
        SetAuto(_runner, "HasActiveOpponent", true);
        SetStatic(typeof(AIShadowRunner), "<Instance>k__BackingField", _runner);
        _canvas = Own("LifecycleCanvas").AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        if (EventSystem.current == null)
            Own("LifecycleEventSystem").AddComponent<EventSystem>();
        _ui = Own("LifecycleUI").AddComponent<UIManager>();
        yield return null; // Real UIManager.Start creates and binds the resource prefab.
        _hud = GetField<GameObject>(_ui, "_hudPanel");
        Assert.IsTrue(_hud.transform.IsChildOf(_canvas.transform),
            "UIManager must use the isolated canvas, never an existing scene canvas.");
        Assert.IsNotNull(_hud.GetComponent<EchoHudPresenter>(), "Use the shipping HUD, not fallback text.");
        _feedback = _hud.transform.Find("HudDynamicCanvas/FeedbackGroup/Feedback").GetComponent<Text>();
        _feedbackGroup = _feedback.transform.parent.GetComponent<CanvasGroup>();
        _game.StartGame();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // Destroy listeners before restoring caches/preferences, so no teardown
        // callback can save a test identity or test score over the user's archive.
        for (int i = _owned.Count - 1; i >= 0; i--)
            if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
        _owned.Clear();
        _preferences?.Restore();
        foreach (var pair in _statics) pair.Key.SetValue(null, pair.Value);
        _statics.Clear();
        Time.timeScale = _timeScale;
        foreach (var pair in _active)
            if (pair.Key != null) pair.Key.SetActive(pair.Value);
        _active.Clear();
        foreach (var pair in _enabled)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        _enabled.Clear();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_selected);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PauseAndResumeDiscardDisplayedAndNotYetRefreshedEvents()
    {
        Publish(SingleContractInstantFeedback.RewriteSucceeded);
        yield return new WaitForSecondsRealtime(0.35f);
        AssertVisible();

        _hud.GetComponent<EchoHudView>().PauseButton.onClick.Invoke();
        Assert.AreEqual(GameState.Paused, _game.State);
        Assert.AreEqual(0f, Time.timeScale);
        Assert.IsTrue(GetField<GameObject>(_ui, "_pausePanel").activeSelf);
        AssertHidden();

        // Represents a result published after the last 10 Hz HUD refresh.
        Publish(SingleContractInstantFeedback.CounterFailed);
        yield return new WaitForSecondsRealtime(0.15f);
        GetField<Button>(_ui, "_resumeBtn").onClick.Invoke();
        Assert.AreEqual(GameState.Playing, _game.State);
        yield return new WaitForSecondsRealtime(0.25f);
        AssertHidden();

        Publish(SingleContractInstantFeedback.RewriteSucceeded);
        yield return new WaitForSecondsRealtime(0.35f);
        AssertVisible();
        yield return new WaitForSecondsRealtime(EchoRunPresentation.SingleContractFeedbackDurationSeconds);
        AssertHidden();
    }

    [UnityTest]
    public IEnumerator ReducedMotionStillExpiresAndCourseEndClearsBeforeNextRunFirstEvent()
    {
        EchoRunAccessibility.SetReducedMotion(true);
        Publish(SingleContractInstantFeedback.SafePass);
        yield return new WaitForSecondsRealtime(0.2f);
        AssertVisible();
        Assert.AreEqual(1f, _feedbackGroup.alpha, 0.001f);
        yield return new WaitForSecondsRealtime(EchoRunPresentation.SingleContractFeedbackDurationSeconds);
        AssertHidden();

        Publish(SingleContractInstantFeedback.RewriteSucceeded);
        yield return new WaitForSecondsRealtime(0.2f);
        AssertVisible();
        Invoke(_game, "CompleteCourse"); // Production finish settlement, state and listeners.
        Assert.AreEqual(GameState.GameOver, _game.State);
        Assert.IsTrue(GetField<GameObject>(_ui, "_gameOverPanel").activeSelf);
        AssertHidden();

        // Re-establish the menu boundary without loading a scene or touching
        // a real player. StartGame and UIManager's new-run reset remain real.
        SetAuto(_game, "State", GameState.Menu);
        SetField(_runner, "_singleContractFeedbackSequence", 0);
        SetField(_runner, "_singleContractFeedback", SingleContractInstantFeedback.None);
        _game.OnStateChanged.Invoke(GameState.Menu);
        _game.StartGame();
        yield return null;
        AssertHidden();
        Publish(SingleContractInstantFeedback.SafePass);
        Assert.AreEqual(1, _runner.SingleContractFeedbackSequence);
        yield return new WaitForSecondsRealtime(0.2f);
        AssertVisible();
    }

    [UnityTest]
    public IEnumerator ResultDetailsTogglePreservesUnconfirmedEvidenceAndResetsForNextResult()
    {
        Invoke(_game, "CompleteCourse");
        Assert.AreEqual(GameState.GameOver, _game.State);
        Assert.IsTrue(GetField<GameObject>(_ui, "_gameOverPanel").activeInHierarchy);

        const string title = "你跑赢了第7代回声";
        const string unconfirmed = "动作结果：观察中断，通过结果未确认";
        const string fullResult = title + "\n第8代回声已经形成\n"
            + "此前记录：选路偏向右侧\n"
            + "本局反制通过 4/6 次 · 从第3次选路起，后续预测已调整\n"
            + "下一局记录：已更新为偏向左侧\n"
            + "关键选择：提交右路（反制），当时仍在换道\n" + unconfirmed;
        // Inject a complete result document at the production presentation
        // boundary, without pretending this fixture played six gates.
        _ui.PresentResultSummary(fullResult, title, true);
        yield return null;

        Text summary = GetField<Text>(_ui, "_shadowResultText");
        Button toggle = GetField<Button>(_ui, "_resultDetailsBtn");
        ScrollRect scroll = GetField<ScrollRect>(_ui, "_resultDetailsScroll");
        Text details = GetField<Text>(_ui, "_resultDetailsText");
        Assert.IsTrue(toggle.gameObject.activeInHierarchy);
        Assert.IsTrue(toggle.IsInteractable());
        Assert.IsFalse(scroll.gameObject.activeInHierarchy,
            "An ordinary result must start with details collapsed.");
        StringAssert.DoesNotContain(title, summary.text);
        StringAssert.DoesNotContain(unconfirmed, summary.text,
            "The last inconclusive observation belongs in the optional detail.");
        StringAssert.DoesNotContain("4/6", summary.text);
        StringAssert.Contains("4/6", details.text);

        toggle.onClick.Invoke();
        yield return null;
        Canvas.ForceUpdateCanvases();
        Assert.IsTrue(scroll.gameObject.activeInHierarchy);
        Assert.IsTrue(details.gameObject.activeInHierarchy);
        StringAssert.Contains(unconfirmed, details.text,
            "Collapsing the page must not erase unresolved evidence.");
        StringAssert.DoesNotContain(title, details.text,
            "The page title must not be repeated in the detail body.");

        EchoRunAccessibility.SetLargeText(true);
        yield return null;
        Canvas.ForceUpdateCanvases();
        Assert.Greater(scroll.viewport.rect.width, 0f);
        Assert.Greater(scroll.viewport.rect.height, 0f);
        Assert.Greater(details.rectTransform.rect.width, 0f);
        Assert.GreaterOrEqual(details.rectTransform.rect.height + 1f,
            details.preferredHeight,
            "Large detail text needs enough content height to remain scrollable.");
        Assert.GreaterOrEqual(scroll.content.rect.height + 1f,
            details.rectTransform.rect.height);
        Assert.IsTrue(toggle.IsInteractable());

        toggle.onClick.Invoke();
        Assert.IsFalse(scroll.gameObject.activeInHierarchy);
        toggle.onClick.Invoke();
        Assert.IsTrue(scroll.gameObject.activeInHierarchy);
        const string nextTitle = "你跑赢了第8代回声";
        _ui.PresentResultSummary(fullResult.Replace(title, nextTitle), nextTitle, true);
        yield return null;
        Assert.IsFalse(scroll.gameObject.activeInHierarchy,
            "An expanded previous result must not leave the next result expanded.");
        StringAssert.DoesNotContain(nextTitle, summary.text);
        Assert.IsFalse(summary.resizeTextForBestFit);

        _ui.PresentResultSummary(fullResult, title, false);
        Assert.AreEqual(fullResult, summary.text);
        Assert.IsTrue(summary.resizeTextForBestFit,
            "The older full-result screen keeps its existing automatic text sizing.");
        Assert.IsFalse(toggle.gameObject.activeInHierarchy);
        Assert.IsFalse(scroll.gameObject.activeInHierarchy);
    }

    private void Publish(SingleContractInstantFeedback feedback)
    {
        // Inject publication, not synthetic user input or a claimed gate collision.
        Invoke(_runner, "SetSingleContractFeedback", feedback, 0f);
    }

    private void AssertVisible()
    {
        Assert.IsTrue(_feedback.gameObject.activeInHierarchy);
        Assert.IsTrue(_feedback.enabled);
        Assert.IsNotEmpty(_feedback.text);
        Assert.Greater(_feedbackGroup.alpha, 0.9f);
    }

    private void AssertHidden()
    {
        Assert.IsFalse(_feedback.gameObject.activeInHierarchy);
        Assert.AreEqual(0f, _feedbackGroup.alpha, 0.001f);
    }

    private GameObject Own(string name)
    {
        var obj = new GameObject(name);
        _owned.Add(obj);
        return obj;
    }

    private T CreateInactive<T>(string name) where T : Component
    {
        GameObject obj = Own(name);
        obj.SetActive(false);
        return obj.AddComponent<T>();
    }

    private void HideExisting(GameObject obj)
    {
        if (!_active.ContainsKey(obj)) _active[obj] = obj.activeSelf;
        obj.SetActive(false);
    }

    private void CaptureStatics(Type type)
    {
        foreach (FieldInfo field in type.GetFields(Static))
            if (!field.IsLiteral && !field.IsInitOnly)
                _statics[field] = field.GetValue(null);
    }

    private static void SetStatic(Type type, string field, object value)
    {
        FieldInfo info = type.GetField(field, Static);
        Assert.IsNotNull(info, type.Name + "." + field);
        info.SetValue(null, value);
    }

    private static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, Private);
        Assert.IsNotNull(info, field);
        info.SetValue(target, value);
    }

    private static T GetField<T>(object target, string field) =>
        (T)target.GetType().GetField(field, Private).GetValue(target);

    private static void SetAuto(object target, string property, object value) =>
        SetField(target, "<" + property + ">k__BackingField", value);

    private static void Invoke(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(method, Private);
        Assert.IsNotNull(info, method);
        info.Invoke(target, arguments);
    }

    private sealed class PreferenceSnapshot
    {
        private static readonly string[] StringKeys =
        {
            EchoRunSaveSystem.SaveKey, EchoRunSaveSystem.SaveSlotAKey,
            EchoRunSaveSystem.SaveSlotBKey, EchoRunSaveSystem.TelemetryKey,
            EchoRunSaveSystem.SingleContractSaveSlotAKey,
            EchoRunSaveSystem.SingleContractSaveSlotBKey, "AIShadowProfileV1"
        };
        private static readonly string[] IntKeys =
        {
            EchoRunSaveSystem.ActiveSaveSlotKey,
            EchoRunSaveSystem.SingleContractActiveSaveSlotKey,
            EchoRunSaveSystem.TrainingResetPendingKey,
            "HighScore", "TotalCoins", "TargetFrameRate", "AudioMuted",
            "CharacterPreset", RunDifficultySettings.PreferenceKey,
            "EchoRunLargeText", "EchoRunHighContrast", "EchoRunReducedMotion"
        };
        private static readonly string[] FloatKeys = { "MasterVolume", "MusicVolume", "SfxVolume" };
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>();
        private readonly Dictionary<string, float> _floats = new Dictionary<string, float>();

        public PreferenceSnapshot()
        {
            foreach (string key in StringKeys)
                if (PlayerPrefs.HasKey(key)) _strings[key] = PlayerPrefs.GetString(key);
            foreach (string key in IntKeys)
                if (PlayerPrefs.HasKey(key)) _ints[key] = PlayerPrefs.GetInt(key);
            foreach (string key in FloatKeys)
                if (PlayerPrefs.HasKey(key)) _floats[key] = PlayerPrefs.GetFloat(key);
        }

        public void InstallEmpty()
        {
            Clear();
            PlayerPrefs.SetString(EchoRunSaveSystem.SaveKey, JsonUtility.ToJson(new EchoRunSaveData()));
            PlayerPrefs.Save();
        }

        public void Restore()
        {
            Clear();
            foreach (var pair in _strings) PlayerPrefs.SetString(pair.Key, pair.Value);
            foreach (var pair in _ints) PlayerPrefs.SetInt(pair.Key, pair.Value);
            foreach (var pair in _floats) PlayerPrefs.SetFloat(pair.Key, pair.Value);
            PlayerPrefs.Save();
        }

        private static void Clear()
        {
            foreach (string key in StringKeys) PlayerPrefs.DeleteKey(key);
            foreach (string key in IntKeys) PlayerPrefs.DeleteKey(key);
            foreach (string key in FloatKeys) PlayerPrefs.DeleteKey(key);
        }
    }
}
