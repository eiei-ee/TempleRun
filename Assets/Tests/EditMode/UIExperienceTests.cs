using NUnit.Framework;
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class UIExperienceTests
{
    [Test]
    public void SafeAreaFallsBackWhenReportedCoordinatesLeaveTheCanvas()
    {
        Rect normalized = UILayoutRules.NormalizeSafeArea(
            new Rect(904f, 508f, 1808f, 1017f), 1808, 1017);

        Assert.AreEqual(new Rect(0f, 0f, 1808f, 1017f), normalized);
    }

    [Test]
    public void SafeAreaKeepsAValidMobileInset()
    {
        Rect normalized = UILayoutRules.NormalizeSafeArea(
            new Rect(0f, 54f, 1080f, 1812f), 1080, 1920);

        Assert.AreEqual(new Rect(0f, 54f, 1080f, 1812f), normalized);
    }

    [Test]
    public void PrimaryAndResultActionsFitLandscapeAndPortraitReferences()
    {
        Vector2 landscape = UILayoutRules.GetReferenceResolution(1920, 1080);
        Vector2 portrait = UILayoutRules.GetReferenceResolution(1080, 1920);
        Vector2 landscapeStart = UILayoutRules.GetPrimaryActionSize(
            1920, 1080, false);
        Vector2 portraitStart = UILayoutRules.GetPrimaryActionSize(
            1080, 1920, true);
        Vector2 landscapeRestart = UILayoutRules.GetRestartButtonSize(
            1920, 1080, false);
        Vector2 portraitRestart = UILayoutRules.GetRestartButtonSize(
            1080, 1920, true);
        Vector2 landscapeMenu = UILayoutRules.GetMenuButtonSize(
            1920, 1080, false);
        Vector2 portraitMenu = UILayoutRules.GetMenuButtonSize(
            1080, 1920, true);
        Vector2 landscapeResult = UILayoutRules.GetResultTextSize(1920, 1080);
        Vector2 portraitResult = UILayoutRules.GetResultTextSize(1080, 1920);

        Assert.AreEqual(new Vector2(520f, 78f), landscapeStart);
        Assert.AreEqual(new Vector2(760f, 104f), portraitStart);
        Assert.AreEqual(new Vector2(380f, 76f), landscapeRestart);
        Assert.AreEqual(new Vector2(520f, 104f), portraitRestart);
        Assert.AreEqual(new Vector2(280f, 60f), landscapeMenu);
        Assert.AreEqual(new Vector2(420f, 104f), portraitMenu);
        Assert.AreEqual(new Vector2(1160f, 260f), landscapeResult);
        Assert.AreEqual(new Vector2(900f, 360f), portraitResult);

        AssertCenteredRectFits(new Vector2(0.5f, 0.255f),
            landscapeStart, landscape);
        AssertCenteredRectFits(new Vector2(0.5f, 0.255f),
            portraitStart, portrait);
        AssertCenteredRectFits(new Vector2(0.5f, 0.40f),
            landscapeResult, landscape);
        AssertCenteredRectFits(new Vector2(0.5f, 0.40f),
            portraitResult, portrait);
        AssertCenteredRectFits(new Vector2(0.5f, 0.18f),
            landscapeRestart, landscape);
        AssertCenteredRectFits(new Vector2(0.5f, 0.18f),
            portraitRestart, portrait);
        AssertCenteredRectFits(new Vector2(0.5f, 0.07f),
            landscapeMenu, landscape);
        AssertCenteredRectFits(new Vector2(0.5f, 0.07f),
            portraitMenu, portrait);
    }

    [TestCase(false, 1920f)]
    [TestCase(true, 1080f)]
    public void HomeNavigationKeepsReadableGapsAtEveryReferenceSize(
        bool portrait, float referenceWidth)
    {
        Vector2 size = UILayoutRules.GetHomeNavigationSize(portrait, portrait);
        Vector2 runner = UILayoutRules.GetHomeNavigationAnchor(0, portrait);
        Vector2 supply = UILayoutRules.GetHomeNavigationAnchor(1, portrait);
        Vector2 settings = UILayoutRules.GetHomeNavigationAnchor(2, portrait);

        Assert.AreEqual(supply.x - runner.x, settings.x - supply.x, 0.0001f);
        float firstSeam = (supply.x - runner.x) * referenceWidth - size.x;
        float secondSeam = (settings.x - supply.x) * referenceWidth - size.x;
        float expectedGap = portrait ? 42.4f : 21.6f;
        Assert.AreEqual(expectedGap, firstSeam, 0.2f);
        Assert.AreEqual(expectedGap, secondSeam, 0.2f);
    }

    private static void AssertCenteredRectFits(
        Vector2 anchor, Vector2 size, Vector2 reference)
    {
        Vector2 center = Vector2.Scale(anchor, reference);
        Vector2 half = size * 0.5f;
        Assert.GreaterOrEqual(center.x - half.x, 0f);
        Assert.LessOrEqual(center.x + half.x, reference.x);
        Assert.GreaterOrEqual(center.y - half.y, 0f);
        Assert.LessOrEqual(center.y + half.y, reference.y);
    }

    [Test]
    public void MenuPresentationSeparatesCalibrationFromChallenge()
    {
        EchoMenuViewData calibration = EchoRunPresentation.BuildMenu(
            0, new PlayerStyleData(), 2, 3);

        Assert.AreEqual("首次回声校准", calibration.generation);
        StringAssert.Contains("跳跃 2 次", calibration.objective);
        StringAssert.Contains("滑铲 3 次", calibration.objective);
        Assert.AreEqual("开始校准", calibration.primaryAction);

        EchoDuelViewData calibrationHud = EchoRunPresentation.BuildDuel(
            false, null, 0f, 2, 3, 1, 2, 0.4f);
        Assert.AreEqual("跳跃 1/2 · 滑铲 2/3", calibrationHud.progress);
        Assert.AreEqual(0.4f, calibrationHud.progress01, 0.001f);

        EchoMenuViewData challenge = EchoRunPresentation.BuildMenu(
            4, new PlayerStyleData(), 2, 3);
        Assert.AreEqual("第 4 代回声", challenge.generation);
        Assert.AreEqual("挑战第 4 代回声", challenge.primaryAction);
        Assert.IsNotEmpty(challenge.learned);
        StringAssert.DoesNotContain("AI识别：", challenge.learned);
        StringAssert.DoesNotContain("权重", challenge.rule);
        StringAssert.DoesNotContain("置信", challenge.rule);
    }

    [Test]
    public void MenuMemoryCorridorBackgroundIsBundledAtFullQuality()
    {
        const string path =
            "Assets/Resources/Art/Menu/MemoryCorridorMenu.png";
        Texture2D background = Resources.Load<Texture2D>(
            "Art/Menu/MemoryCorridorMenu");
        Assert.IsNotNull(background);
        Assert.GreaterOrEqual(background.width, 1600);
        Assert.GreaterOrEqual(background.height, 900);

        TextureImporter importer = AssetImporter.GetAtPath(path)
            as TextureImporter;
        Assert.IsNotNull(importer);
        Assert.IsFalse(importer.mipmapEnabled);
        Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale);
        Assert.AreEqual(TextureImporterCompression.Uncompressed,
            importer.textureCompression);
    }

    [Test]
    public void DuelPresentationMakesContractProgressAndLeadExplicit()
    {
        var contract = new EchoContractData
        {
            type = EchoContractType.ChangeVerticalHabit,
            targetLane = 2,
            targetAction = ShadowAction.Slide,
            progress = 2f,
            targetProgress = 3f,
            lastFeedback = "反制生效：动作正确",
            feedbackSequence = 7
        };

        EchoDuelViewData leading = EchoRunPresentation.BuildDuel(
            true, contract, 2.75f, 2, 2);
        Assert.AreEqual("AI预测已公开 · 打破旧习惯", leading.contract);
        Assert.AreEqual("深度裂解", leading.progress);
        Assert.AreEqual(1f / 3f, leading.progress01, 0.001f);
        Assert.AreEqual(EchoLeadState.Leading, leading.leadState);
        StringAssert.StartsWith("领先 +2.8m", leading.lead);
        StringAssert.StartsWith("反制成功", leading.feedback);
        Assert.AreEqual(7, leading.feedbackSequence);

        EchoDuelViewData trailing = EchoRunPresentation.BuildDuel(
            true, contract, -1.2f, 2, 2);
        Assert.AreEqual(EchoLeadState.Trailing, trailing.leadState);
        StringAssert.StartsWith("落后 -1.2m", trailing.lead);
    }

    [Test]
    public void DuelPredictionAppearsOnlyAfterDetection()
    {
        var contract = new EchoContractData
        {
            type = EchoContractType.BreakLaneHabit,
            learnedLane = 2,
            predictionLane = 2,
            targetProgress = 100f
        };

        EchoDuelViewData detection = EchoRunPresentation.BuildDuel(
            true, contract, 0f, 2, 2,
            duelPhase: EchoDuelPhase.Detection,
            publicPrediction: "回声预判：你会继续依赖右侧路线");
        EchoDuelViewData reveal = EchoRunPresentation.BuildDuel(
            true, contract, 0f, 2, 2,
            duelPhase: EchoDuelPhase.Reveal,
            publicPrediction: "回声预判：你会继续依赖右侧路线");

        Assert.IsEmpty(detection.prediction);
        Assert.AreEqual("回声预判：你会继续依赖右侧路线",
            reveal.prediction);
    }

    [Test]
    public void RhythmHudDoesNotExposeTheNextRequiredAction()
    {
        var contract = new EchoContractData
        {
            type = EchoContractType.DisruptRhythm,
            targetAction = ShadowAction.Jump,
            targetProgress = 4f
        };

        EchoDuelViewData view = EchoRunPresentation.BuildDuel(
            true, contract, 0f, 2, 2);

        Assert.AreEqual("AI预测已公开 · 打破旧习惯", view.contract);
        StringAssert.DoesNotContain("跳跃", view.contract);
    }

    [Test]
    public void MenuRouterKeepsExactlyOneScreenAndHomeNavigationState()
    {
        GameObject root = new GameObject("MenuRouterTest");
        GameObject home = new GameObject("Home");
        GameObject settings = new GameObject("Settings");
        GameObject launcher = new GameObject("Launcher");
        home.transform.SetParent(root.transform);
        settings.transform.SetParent(root.transform);
        launcher.transform.SetParent(root.transform);

        try
        {
            MenuScreenRouter router = root.AddComponent<MenuScreenRouter>();
            router.Initialize(null);
            router.Register(MenuScreen.Home, home);
            router.Register(MenuScreen.Settings, settings);
            router.RegisterHomeNavigation(launcher);
            router.EnterMenu();

            Assert.IsTrue(home.activeSelf);
            Assert.IsFalse(settings.activeSelf);
            Assert.IsTrue(launcher.activeSelf);

            Assert.IsTrue(router.Show(MenuScreen.Settings));
            Assert.IsFalse(home.activeSelf);
            Assert.IsTrue(settings.activeSelf);
            Assert.IsFalse(launcher.activeSelf);

            router.BackToHome();
            Assert.IsTrue(home.activeSelf);
            Assert.IsFalse(settings.activeSelf);
            Assert.IsTrue(launcher.activeSelf);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RunnerAppearanceUsesPropertyBlocksWithoutCloningMaterial()
    {
        Shader shader = Shader.Find("EchoRun/ExoGrayBlueTech");
        Assert.IsNotNull(shader);
        GameObject runner = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Material shared = new Material(shader);
        Renderer renderer = runner.GetComponent<Renderer>();
        renderer.sharedMaterial = shared;

        try
        {
            Color dark = new Color(0.02f, 0.03f, 0.04f);
            Color light = new Color(0.4f, 0.5f, 0.6f);
            Color emission = new Color(0.1f, 0.8f, 1.2f);
            Assert.AreEqual(1, RunnerAppearanceService.Apply(
                runner.transform, dark, light, emission));
            Assert.AreSame(shared, renderer.sharedMaterial);

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties, 0);
            AssertColor(dark, properties.GetColor("_DarkColor"));
            AssertColor(light, properties.GetColor("_LightColor"));
            AssertColor(emission, properties.GetColor("_EmissionColor"));
        }
        finally
        {
            Object.DestroyImmediate(runner);
            Object.DestroyImmediate(shared);
        }
    }

    [Test]
    public void RunnerAppearanceSeparatesHardArmorFromSoftInnerLayer()
    {
        Color dark = new Color(0.03f, 0.06f, 0.10f);
        Color light = new Color(0.32f, 0.46f, 0.60f);
        Color emission = new Color(0.95f, 0.72f, 0.48f);

        RunnerAppearanceService.ResolveMaterialPalette(
            "Exo_MAT_BlueTech", dark, light, emission,
            out Color armorDark, out Color armorLight,
            out Color armorEmission);
        RunnerAppearanceService.ResolveMaterialPalette(
            "Body_MAT_BlueTech", dark, light, emission,
            out Color bodyDark, out Color bodyLight,
            out Color bodyEmission);

        Assert.Greater(armorLight.grayscale, light.grayscale * 1.15f);
        Assert.Less(bodyLight.grayscale, light.grayscale * 0.70f);
        Assert.Less(bodyDark.grayscale, armorDark.grayscale);
        Assert.Less(bodyEmission.grayscale, armorEmission.grayscale);
    }

    [Test]
    public void PlayerFacingFontCoversNewInterfaceCopy()
    {
        Font font = Resources.Load<Font>("Fonts/EchoRunSansSC-Regular");
        Assert.IsNotNull(font);
        const string copy =
            "首次回声校准挑战契约补给舱跑者外观库存装备领先落后已破解"
            + "设置主音量音乐音效一键静音已画面帧率辅助显示选择配色立即预览并保存"
            + "跑酷难度休闲标准高压障碍较少密集恢复窗"
            + "大字高对比减少动态返回百分比补充"
            + "本机实时观察你的选路跳跃和滑铲不同动作学习条变亮形成下一局的对手"
            + "还没看清路线习惯旧回声不会丢让它再观察猜中抢先连续两次骗过改猜"
            + "留在身后正在学跑法学够了去终点主动种类受伤红它猜的路青骗它的路白安全路"
            + "后续更新看到了这局观察不会带到下一局重新已经全亮没有形成遇到问题"
            + "本局结果整理变化同样不足当前保持不变原本认为压力偏向开始仍可能"
            + "伤势再受伤即出局恢复中"
            + "《影迹》过去正在追上你米回声现身上一局学到×";
        foreach (char character in copy)
            Assert.IsTrue(font.HasCharacter(character), "UI font is missing: " + character);
    }

    [Test]
    public void BundledFontMatchesValidatedStaticRegularSubset()
    {
        string path = Path.Combine(Application.dataPath,
            "Resources/Fonts/EchoRunSansSC-Regular.otf");
        Assert.IsTrue(File.Exists(path));
        using (SHA256 sha = SHA256.Create())
        {
            string actual = BitConverter.ToString(
                sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
            Assert.AreEqual(
                "CCCAD320E18B33279AB48E88517D6312A9541B5DE06E65E3C777B67BA09724FA",
                actual,
                "The bundled font must stay the validated static Regular 400 subset.");
        }
    }

    [Test]
    public void AccessibilityPreferencesApplyToRuntimeTextAndPersistMotionChoice()
    {
        bool oldLarge = EchoRunAccessibility.LargeText;
        bool oldContrast = EchoRunAccessibility.HighContrast;
        bool oldMotion = EchoRunAccessibility.ReducedMotion;
        GameObject textObject = new GameObject("AccessibleText", typeof(UnityEngine.UI.Text));

        try
        {
            UnityEngine.UI.Text text = textObject.GetComponent<UnityEngine.UI.Text>();
            text.fontSize = 20;
            EchoRunAccessibility.SetLargeText(false);
            EchoRunAccessibility.SetHighContrast(false);
            EchoRunAccessibility.Prepare(text);
            Assert.AreEqual(20, text.fontSize);

            EchoRunAccessibility.SetLargeText(true);
            EchoRunAccessibility.SetHighContrast(true);
            EchoRunAccessibility.SetReducedMotion(true);
            EchoRunAccessibility.ApplyToHierarchy(textObject.transform);

            Assert.AreEqual(22, text.fontSize);
            EchoRunAccessibleText marker = text.GetComponent<EchoRunAccessibleText>();
            Assert.IsNotNull(marker);
            Assert.IsNotNull(marker.contrastOutline);
            Assert.IsTrue(marker.contrastOutline.enabled);
            Assert.IsTrue(EchoRunAccessibility.ReducedMotion);
        }
        finally
        {
            EchoRunAccessibility.SetLargeText(oldLarge);
            EchoRunAccessibility.SetHighContrast(oldContrast);
            EchoRunAccessibility.SetReducedMotion(oldMotion);
            Object.DestroyImmediate(textObject);
        }
    }

    [Test]
    public void TouchTargetsAndCameraAdaptToOrientation()
    {
        Assert.AreEqual(104f, UILayoutRules.EnsureTouchButtonSize(
            new Vector2(180f, 56f), true, false).y);
        Assert.AreEqual(72f, UILayoutRules.EnsureTouchSliderSize(
            new Vector2(500f, 40f), true, false).y);
        Assert.AreEqual(56f, UILayoutRules.EnsureTouchButtonSize(
            new Vector2(180f, 56f), false, false).y);
        Assert.AreEqual(62f, WorldStyler.GetCameraFieldOfView(true));
        Assert.AreEqual(56f, WorldStyler.GetCameraFieldOfView(false));
        Assert.AreEqual(new Vector3(0f, 3.75f, -6.3f),
            WorldStyler.GetCameraOffset(true));
        Assert.AreEqual(new Vector3(0f, 3.85f, -6.45f),
            WorldStyler.GetCameraOffset(false));
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.AreEqual(expected.r, actual.r, 0.001f);
        Assert.AreEqual(expected.g, actual.g, 0.001f);
        Assert.AreEqual(expected.b, actual.b, 0.001f);
        Assert.AreEqual(expected.a, actual.a, 0.001f);
    }
}
