using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Captures real HUD and UIManager components using supplied presentation data.
// This checks injected layout/alpha states, not gameplay or event timing.
public static class RacingFeedbackCapture
{
    private const int CaptureLayer = 31;
    private const float ReferenceWidth = 1920f;

    public static void CaptureAndBuildValidationPlayer()
    {
        Capture();
        BuildValidationPlayer();
    }

    // Keep visual verification builds separate from the user's playable build.
    public static void BuildValidationPlayer()
    {
        BuildCapturePlayer("RacingFeedbackV1", "RACING_FEEDBACK");
    }

    public static void BuildResultSummaryPlayer()
    {
        BuildCapturePlayer("ResultSummaryV1", "RESULT_SUMMARY");
    }

    public static void CaptureAndBuildResultSummaryPlayer()
    {
        CaptureResultSummary();
        BuildResultSummaryPlayer();
    }

    private static void BuildCapturePlayer(string suite, string logPrefix)
    {
        var scenes = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            if (scene.enabled) scenes.Add(scene.path);
        if (scenes.Count == 0)
            throw new System.InvalidOperationException("No enabled gameplay scene.");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath,
            "..", "TestResults", suite, "Windows", "EchoRun.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(scenes.ToArray(), output,
            BuildTarget.StandaloneWindows64, BuildOptions.CompressWithLz4HC);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.InvalidOperationException("Visual validation build failed: "
                + report.summary.result);
        Debug.Log(logPrefix + "_PLAYER_BUILD_OK " + output);
    }

    [MenuItem("Tools/Echo Runner/Capture Result Summary V1")]
    public static void CaptureResultSummary()
    {
        if (EditorApplication.isPlaying)
            throw new System.InvalidOperationException(
                "Run result-summary captures outside Play Mode.");
        Scene originalScene = SceneManager.GetActiveScene();
        bool replaceEmptyUntitledScene = SceneManager.sceneCount == 1
            && string.IsNullOrEmpty(originalScene.path) && !originalScene.isDirty;
        Scene captureScene = default;
        using (var accessibility = new AccessibilityMemoryOverride())
        {
            try
            {
                accessibility.Set(false, false, false);
                captureScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene, replaceEmptyUntitledScene
                        ? NewSceneMode.Single : NewSceneMode.Additive);
                SceneManager.SetActiveScene(captureScene);
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "..", "TestResults", "ResultSummaryV1", "Captures"));
                Directory.CreateDirectory(directory);
                var report = new StringBuilder();
                report.AppendLine("Actual UIManager result components with injected full-result data.");
                report.AppendLine("Summary is set through PresentResultSummary; details open through the actual button onClick.");
                report.AppendLine("Offscreen fixed viewports, not live window resizing or human acceptance.");
                report.AppendLine("Accessibility overrides are memory-only and restored; no gameplay/save lifecycle.");
                int[] widths = { 1920, 1280, 1280 };
                int[] heights = { 1080, 720, 800 };
                for (int index = 0; index < widths.Length; index++)
                {
                    CaptureResultSummaryResolution(widths[index], heights[index],
                        false, directory, report);
                    CaptureResultSummaryResolution(widths[index], heights[index],
                        true, directory, report);
                }
                string reportPath = Path.Combine(directory, "result-summary-states.txt");
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                Debug.Log("RESULT_SUMMARY_CAPTURE_OK " + reportPath);
            }
            finally
            {
                if (replaceEmptyUntitledScene && captureScene.IsValid() && captureScene.isLoaded)
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else
                {
                    if (originalScene.IsValid() && originalScene.isLoaded)
                        SceneManager.SetActiveScene(originalScene);
                    if (captureScene.IsValid() && captureScene.isLoaded)
                        EditorSceneManager.CloseScene(captureScene, true);
                }
            }
        }
    }

    private static void CaptureResultSummaryResolution(int width, int height,
        bool largeText, string directory, StringBuilder report)
    {
        GameObject cameraObject = null;
        GameObject canvasObject = null;
        GameObject host = null;
        Camera camera = null;
        RenderTexture target = null;
        UIManager manager = null;
        using (var accessibility = new AccessibilityMemoryOverride())
        {
            try
            {
                accessibility.Set(largeText, false, false);
                cameraObject = new GameObject("ResultSummaryCaptureCamera");
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = EchoRunUITheme.Backdrop;
                camera.cullingMask = 1 << CaptureLayer;
                camera.orthographic = true;
                camera.orthographicSize = height * 0.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                canvasObject = new GameObject("ResultSummaryCaptureCanvas",
                    typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = width / ReferenceWidth;
                canvas.scaleFactor = scaler.scaleFactor;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;

                host = new GameObject("InactiveResultSummaryFixture");
                host.SetActive(false);
                GameManager gameManager = host.AddComponent<GameManager>();
                manager = host.AddComponent<UIManager>();
                SetUiField(manager, "_gm", gameManager);
                SetUiField(manager, "_safeAreaRoot", canvasObject.GetComponent<RectTransform>());
                SetUiField(manager, "_font", Resources.Load<Font>("Fonts/EchoRunSansSC-Regular"));
                SetUiField(manager, "_titleFont", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                InvokeUiBuilder(manager, "CreateGameOverPanel");
                GameObject result = GetUiField<GameObject>(manager, "_gameOverPanel");
                result.SetActive(true);
                foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = CaptureLayer;
                Canvas.ForceUpdateCanvases();

                string statePrefix = height + "-" + (largeText ? "large" : "normal") + "-";
                ActiveEchoIdentity previous = CaptureIdentity(3, 2, 0.83f, 5);
                ActiveEchoIdentity next = CaptureIdentity(4, 0, 0.67f, 4);
                var attempt = new GateAttempt
                {
                    gateId = 6, committedLane = 0,
                    chosenRole = PredictionGateRole.Counter,
                    execution = GateExecutionOutcome.Success,
                    executionReason = GateExecutionReason.Completed,
                    hasLateralEvidence = true, laneChangeInProgress = true,
                    lateralOffset = -1.2f
                };
                PresentResultCapture(manager, RunEndReason.FinishReached, true,
                    next, EchoCognitionAssessment.Compare(previous, next, 4, 6, 3, true), attempt);
                CaptureResultSummaryPair(statePrefix + "promotion", manager,
                    camera, width, height, directory, report);

                // Supplemental long-copy cases at the reference viewport and
                // the smallest large-text viewport; do not rerender the old HUD suite.
                if ((width == 1920 && !largeText)
                    || (width == 1280 && height == 720 && largeText))
                {
                    string saveFailure = AIShadowRunner.BuildSingleContractSaveFailureResult(
                        RunEndReason.FinishReached, true, true, 3)
                        + "\n" + EchoRunPresentation.BuildSingleContractGateReview(attempt);
                    PresentResultSummaryFixture(manager, saveFailure,
                        RunEndReason.FinishReached, true, true);
                    CaptureResultSummaryPair(statePrefix + "save-failure", manager,
                        camera, width, height, directory, report);

                    var progress = new SingleContractCalibrationProgress
                    {
                        available = true, totalSamples = 12, minimumTotalSamples = 24,
                        activeSamples = 3, minimumActiveSamples = 6,
                        actionCategories = 2, minimumActionCategories = 2,
                        jumpSamples = 1, minimumJumpSamples = 2,
                        slideSamples = 1, minimumSlideSamples = 2,
                        formalChoices = 2, minimumFormalChoices = 5,
                        successfulChoices = 1, minimumSuccessfulChoices = 3,
                        strongestRouteChoices = 2, minimumStrongestRouteChoices = 3,
                        preferredLane = 2, preferredLaneUnique = true
                    };
                    PresentResultSummaryFixture(manager,
                        EchoRunPresentation.BuildSingleContractCalibrationResult(progress),
                        RunEndReason.Collision, false, false);
                    CaptureResultSummaryPair(statePrefix + "calibration-insufficient", manager,
                        camera, width, height, directory, report);
                }
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                if (manager != null)
                    GetUiField<RuntimeRoundedSprite>(manager, "_roundedUi")?.Dispose();
                if (host != null) Object.DestroyImmediate(host);
                if (canvasObject != null) Object.DestroyImmediate(canvasObject);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }
        }
    }

    private static void PresentResultSummaryFixture(UIManager manager,
        string fullResult, RunEndReason endReason, bool wasChallenge, bool won)
    {
        string title = UIManager.GetSingleContractGameOverTitle(
            fullResult, endReason, wasChallenge, won);
        manager.PresentResultSummary(fullResult, title, true);
        GetUiField<Text>(manager, "_gameOverTitleText").color = won
            ? EchoRunUITheme.Success : wasChallenge ? EchoRunUITheme.Danger : EchoRunUITheme.HudInk;
        GetUiField<Text>(manager, "_gameOverStatsText").text = wasChallenge
            ? "得分 2480 · 金币 42" : "得分 680 · 金币 20";
        GetUiField<Button>(manager, "_restartBtn").GetComponentInChildren<Text>(true).text =
            UIManager.GetSingleContractGameOverActionLabel(endReason,
                wasChallenge, false, wasChallenge ? 3 : 0, wasChallenge);
    }

    private static void CaptureResultSummaryPair(string state, UIManager manager,
        Camera camera, int width, int height, string directory, StringBuilder report)
    {
        Transform result = GetUiField<GameObject>(manager, "_gameOverPanel").transform;
        EchoRunAccessibility.ApplyToHierarchy(result);
        ApplyResultCaptureLayout(manager, width, height);
        ScrollRect details = GetUiField<ScrollRect>(manager, "_resultDetailsScroll");
        if (details.gameObject.activeInHierarchy)
            throw new System.InvalidOperationException("New result fixture did not start collapsed.");
        CaptureUiManagerState(state + "-summary", result, camera,
            width, height, directory, report);
        Button detailsButton = GetUiField<Button>(manager, "_resultDetailsBtn");
        if (!detailsButton.gameObject.activeInHierarchy)
            throw new System.InvalidOperationException("Result fixture has no visible details button.");
        detailsButton.onClick.Invoke();
        EchoRunAccessibility.ApplyToHierarchy(result);
        ApplyResultCaptureLayout(manager, width, height);
        if (!details.gameObject.activeInHierarchy)
            throw new System.InvalidOperationException("The result details button did not expand the actual panel.");
        CaptureUiManagerState(state + "-details", result, camera,
            width, height, directory, report);
        report.AppendLine("  details viewportHeight=" + details.viewport.rect.height
            + " | contentHeight=" + details.content.rect.height
            + " | normalizedPosition=" + details.verticalNormalizedPosition);
        foreach (Text text in result.GetComponentsInChildren<Text>(true))
        {
            if (!text.gameObject.activeInHierarchy) continue;
            report.AppendLine("  text geometry=" + text.name
                + " | renderedLines=" + text.cachedTextGenerator.lineCount
                + " | rect=" + text.rectTransform.rect.size
                + " | font=" + text.fontSize);
            foreach (char character in text.text)
                if (character >= '\u4e00' && character <= '\u9fff'
                    && text.font != null && !text.font.HasCharacter(character))
                    throw new System.InvalidOperationException(
                        "Missing result glyph U+" + ((int)character).ToString("X4")
                        + " in " + text.name);
        }
    }

    private static void ApplyResultCaptureLayout(UIManager manager, int width, int height)
    {
        MethodInfo layout = typeof(UIManager).GetMethod("ApplyResultSummaryLayout",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (layout == null)
            throw new System.MissingMethodException("UIManager.ApplyResultSummaryLayout");
        layout.Invoke(manager, new object[] { width, height });
    }

    [MenuItem("Tools/Echo Runner/Capture Racing Feedback V1")]
    public static void Capture()
    {
        CaptureInternal(true);
        CaptureRouteSymbols();
    }

    [MenuItem("Tools/Echo Runner/Capture Racing Feedback V1 Followup")]
    public static void CaptureFollowup()
    {
        CaptureInternal(false);
        CaptureRouteSymbols();
    }

    [MenuItem("Tools/Echo Runner/Capture Racing Feedback V1 Route Symbols")]
    public static void CaptureRouteSymbols()
    {
        if (EditorApplication.isPlaying)
            throw new System.InvalidOperationException(
                "Run route-symbol fixture captures outside Play Mode.");
        const int width = 1920;
        const int height = 1080;
        Scene originalScene = SceneManager.GetActiveScene();
        bool replaceEmptyUntitledScene = SceneManager.sceneCount == 1
            && string.IsNullOrEmpty(originalScene.path) && !originalScene.isDirty;
        Scene fixtureScene = default;
        GameObject fixture = null;
        GameObject managerObject = null;
        Camera camera = null;
        RenderTexture target = null;
        Material groundMaterial = null;
        Material markerMaterial = null;
        TrackManager manager = null;
        FieldInfo materialField = typeof(TrackManager).GetField(
            "_predictionGateMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
        if (materialField == null)
            throw new System.MissingFieldException("TrackManager._predictionGateMaterial");
        try
        {
            fixtureScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, replaceEmptyUntitledScene
                    ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(fixtureScene);
            fixture = new GameObject("RacingFeedbackRouteSymbolFixture");
            managerObject = new GameObject("InactiveRouteFixtureTrackManager");
            managerObject.SetActive(false);
            manager = managerObject.AddComponent<TrackManager>();
            manager.segmentLength = TrackGeometryStandards.StandardSegmentLength;
            manager.laneDistance = TrackGeometryStandards.LaneSpacing;

            GameObject segment = new GameObject("RouteFixtureSegment");
            segment.transform.SetParent(fixture.transform, false);
            segment.AddComponent<TrackSegmentData>().routeDistance = 100f;
            var gate = new PredictionGateDefinition
            {
                gateId = 1,
                sequence = 1,
                commitDistance = 100f,
                resolveDistance = 112f,
                lanes = new[]
                {
                    new PredictionGateLane { physicalLane = 0, role = PredictionGateRole.Predicted },
                    new PredictionGateLane { physicalLane = 1, role = PredictionGateRole.Counter },
                    new PredictionGateLane { physicalLane = 2, role = PredictionGateRole.Neutral }
                }
            };
            MethodInfo spawn = typeof(TrackManager).GetMethod(
                "SpawnPredictionGateVisual", BindingFlags.Instance | BindingFlags.NonPublic);
            if (spawn == null)
                throw new System.MissingMethodException("TrackManager.SpawnPredictionGateVisual");
            spawn.Invoke(manager, new object[] { segment, gate, 100f });
            markerMaterial = (Material)materialField.GetValue(manager);
            Transform markerRoot = segment.transform.Find(TrackManager.PredictionGateVisualRootName);
            if (markerRoot == null || markerMaterial == null)
                throw new System.InvalidOperationException("Production route marker fixture did not build.");

            Shader groundShader = Shader.Find("Unlit/Color");
            if (groundShader == null)
                throw new System.InvalidOperationException("Route fixture needs the Unlit/Color background shader.");
            groundMaterial = new Material(groundShader)
            {
                name = "RouteSymbolFixtureGround",
                hideFlags = HideFlags.HideAndDontSave,
                color = new Color(0.065f, 0.065f, 0.065f, 1f)
            };
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "NeutralFixtureGround";
            ground.transform.SetParent(fixture.transform, false);
            ground.transform.localPosition = new Vector3(0f,
                TrackGeometryStandards.AuthoredRoadSurfaceTopY - 0.1f, 0f);
            ground.transform.localScale = new Vector3(
                TrackGeometryStandards.VisualRoadWidth, 0.2f, 10f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

            GameObject cameraObject = new GameObject("RouteFixtureCamera");
            cameraObject.transform.SetParent(fixture.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.025f, 0.025f, 1f);
            camera.cullingMask = 1 << CaptureLayer;
            camera.orthographic = true;
            camera.orthographicSize = 4.2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            camera.transform.position = new Vector3(0f, 9f, -10f);
            camera.transform.LookAt(new Vector3(0f,
                TrackGeometryStandards.AuthoredRoadSurfaceTopY, 0f));
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            foreach (Transform child in fixture.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = CaptureLayer;

            string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", "TestResults", "RacingFeedbackV1", "Captures", "Followup"));
            Directory.CreateDirectory(outputDirectory);
            string colorPath = Path.Combine(outputDirectory, "1920-route-symbols-fixture-color.png");
            string monoPath = Path.Combine(outputDirectory, "1920-route-symbols-fixture-monochrome.png");
            Renderer[] renderers = markerRoot.GetComponentsInChildren<Renderer>();
            // Keep phase tint out of both fixture views, without changing any
            // global shader state or shared production material.
            foreach (Renderer renderer in renderers)
            {
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetFloat("_EchoPhaseIntensity", 0f);
                properties.SetFloat("_EchoVisualHigh", 0f);
                renderer.SetPropertyBlock(properties);
            }
            camera.Render();
            SaveCameraImage(colorPath, camera, width, height);

            // Same transforms, meshes, materials, camera and exposure: remove
            // role hue/brightness differences so only geometry distinguishes
            // the routes. This is not a screenshot of a running race.
            foreach (Renderer renderer in renderers)
            {
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Color monochrome = new Color(0.72f, 0.72f, 0.72f, 1f);
                properties.SetColor("_CoreColor", monochrome);
                properties.SetColor("_SignalColor", monochrome);
                properties.SetColor("_StructureColor", monochrome * 0.45f);
                properties.SetColor("_Color", monochrome);
                renderer.SetPropertyBlock(properties);
            }
            camera.Render();
            SaveCameraImage(monoPath, camera, width, height);

            var report = new StringBuilder();
            report.AppendLine("Route-symbol fixture rendered by production TrackManager.SpawnPredictionGateVisual.");
            report.AppendLine("Left: Predicted. Middle: Counter. Right: Neutral/safe.");
            report.AppendLine("Ground is a neutral fixture plane; this is not a running game or a gameplay-camera capture.");
            report.AppendLine("The monochrome view uses the same geometry and camera with identical gray role colors.");
            report.AppendLine("Per-renderer phase tint disabled in both views; no global shader or PlayerPrefs changes.");
            report.AppendLine("Viewport=1920x1080, marker shader=" + markerMaterial.shader.name);
            foreach (Transform lane in markerRoot)
            {
                Transform symbol = lane.Find("RoleSymbol");
                report.AppendLine(lane.name + " | symbolParts="
                    + (symbol != null ? symbol.childCount : 0));
                if (symbol != null)
                    foreach (Transform part in symbol)
                        report.AppendLine("  " + part.name);
            }
            report.AppendLine(colorPath);
            report.AppendLine(monoPath);
            File.WriteAllText(Path.Combine(outputDirectory, "route-symbols-fixture.txt"),
                report.ToString(), Encoding.UTF8);
            Debug.Log("RACING_FEEDBACK_ROUTE_FIXTURE_CAPTURE_OK " + outputDirectory);
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            if (fixture != null) Object.DestroyImmediate(fixture);
            // An always-inactive MonoBehaviour may not receive OnDestroy.
            // Explicitly release the material it generated and clear the field
            // so its normal cleanup cannot destroy it for a second time.
            if (manager != null)
            {
                Material ownedMaterial = (Material)materialField.GetValue(manager);
                materialField.SetValue(manager, null);
                if (ownedMaterial != null) Object.DestroyImmediate(ownedMaterial);
            }
            if (managerObject != null) Object.DestroyImmediate(managerObject);
            if (groundMaterial != null) Object.DestroyImmediate(groundMaterial);
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
            if (replaceEmptyUntitledScene && fixtureScene.IsValid() && fixtureScene.isLoaded)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            else
            {
                if (originalScene.IsValid() && originalScene.isLoaded)
                    SceneManager.SetActiveScene(originalScene);
                if (fixtureScene.IsValid() && fixtureScene.isLoaded)
                    EditorSceneManager.CloseScene(fixtureScene, true);
            }
        }
    }

    private static void CaptureInternal(bool includeBaseline)
    {
        if (EditorApplication.isPlaying)
            throw new System.InvalidOperationException(
                "Run injected-state captures outside Play Mode.");
        Scene originalScene = SceneManager.GetActiveScene();
        bool replaceEmptyUntitledScene = SceneManager.sceneCount == 1
            && string.IsNullOrEmpty(originalScene.path) && !originalScene.isDirty;
        Scene captureScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, replaceEmptyUntitledScene
                ? NewSceneMode.Single : NewSceneMode.Additive);
        string outputDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "TestResults", "RacingFeedbackV1", "Captures"));
        var report = new StringBuilder();
        report.AppendLine("Real Unity HUD rendering at injected feedback ages.");
        report.AppendLine("Scope: layout and alpha states only; not gameplay or event timing.");
        report.AppendLine("Menu/result: actual UIManager builders with injected state; no run or save lifecycle.");
        report.AppendLine("Reference layout: 1920x1080, proportional scale at 1280x720.");
        // Public accessibility setters save PlayerPrefs and notify live UIs.
        // Change only the cached state for these temporary objects, then put
        // every cached field back, including its lazy-initialization state.
        using (var accessibility = new AccessibilityMemoryOverride())
        {
            try
            {
                accessibility.Set(false, false, false);
                SceneManager.SetActiveScene(captureScene);
                // Build in the temporary scene so its transient objects cannot
                // dirty the user's open gameplay scene.
                EchoHudPrefabBuilder.Build();
                Directory.CreateDirectory(outputDirectory);
                if (includeBaseline)
                {
                    CaptureResolution(1920, 1080, outputDirectory, report);
                    CaptureResolution(1280, 720, outputDirectory, report);
                    string reportPath = Path.Combine(outputDirectory, "alpha-states.txt");
                    File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                    Debug.Log("RACING_FEEDBACK_CAPTURE_OK " + reportPath);
                }

                string followupDirectory = Path.Combine(outputDirectory, "Followup");
                Directory.CreateDirectory(followupDirectory);
                var followupReport = new StringBuilder();
                followupReport.AppendLine("Actual Unity HUD at injected accessibility, injury and feedback-age states.");
                followupReport.AppendLine("RenderTexture viewports: 1920x1080, 1280x720 and 1280x800; not runtime window resizing.");
                followupReport.AppendLine("Accessibility changes are temporary memory-only overrides; PlayerPrefs are not written.");
                followupReport.AppendLine("Scope: rendered state/layout evidence; not live timing, input, collision or unfamiliar-player acceptance.");
                CaptureResolution(1920, 1080, followupDirectory, followupReport, true);
                CaptureResolution(1280, 720, followupDirectory, followupReport, true);
                CaptureResolution(1280, 800, followupDirectory, followupReport, true);
                string followupPath = Path.Combine(followupDirectory, "alpha-states.txt");
                File.WriteAllText(followupPath, followupReport.ToString(), Encoding.UTF8);
                Debug.Log("RACING_FEEDBACK_FOLLOWUP_CAPTURE_OK " + followupPath);
            }
            finally
            {
                if (replaceEmptyUntitledScene)
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                else
                {
                    if (originalScene.IsValid() && originalScene.isLoaded)
                        SceneManager.SetActiveScene(originalScene);
                    if (captureScene.IsValid() && captureScene.isLoaded)
                        EditorSceneManager.CloseScene(captureScene, true);
                }
            }
        }
    }

    private static void CaptureResolution(int width, int height,
        string outputDirectory, StringBuilder report, bool followup = false)
    {
        GameObject cameraObject = new GameObject("RacingFeedbackCaptureCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.Lerp(EchoRunUITheme.HudPanel, Color.black, 0.5f);
        camera.cullingMask = 1 << CaptureLayer;
        camera.orthographic = true;
        camera.orthographicSize = height * 0.5f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        RenderTexture target = new RenderTexture(width, height, 24,
            RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        GameObject canvasObject = new GameObject("RacingFeedbackCaptureCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        // Explicit target-relative scale avoids the Editor's Game-view size
        // leaking into an offscreen RenderTexture capture.
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = width / ReferenceWidth;
        canvas.scaleFactor = scaler.scaleFactor;
        try
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/UI/EchoHud.prefab");
            if (prefab == null)
                throw new FileNotFoundException("The rebuilt Echo HUD prefab is missing.");
            GameObject hud = Object.Instantiate(prefab, canvasObject.transform, false);
            foreach (EchoHudPresenter presenter in hud.GetComponentsInChildren<EchoHudPresenter>(true))
                presenter.enabled = false;
            foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = CaptureLayer;
            foreach (Canvas layer in canvasObject.GetComponentsInChildren<Canvas>(true))
            {
                layer.renderMode = RenderMode.ScreenSpaceCamera;
                layer.worldCamera = camera;
                layer.planeDistance = 1f;
            }
            EchoHudView view = hud.GetComponent<EchoHudView>();
            CanvasGroup feedback = hud.transform.Find("HudDynamicCanvas/FeedbackGroup")
                .GetComponent<CanvasGroup>();

            if (followup)
            {
                CaptureFollowupStates(view, feedback, camera, width, height,
                    outputDirectory, report);
                return;
            }

            SingleContractHudInput challenge = ChallengeInput();
            CaptureState("fade-in", 0.075f, challenge, view, feedback, camera,
                width, height, outputDirectory, report);
            CaptureState("hold", 0.6f, challenge, view, feedback, camera,
                width, height, outputDirectory, report);
            CaptureState("fade-out", 2.025f, challenge, view, feedback, camera,
                width, height, outputDirectory, report);
            CaptureState("hidden", 2.25f, challenge, view, feedback, camera,
                width, height, outputDirectory, report);

            SingleContractHudInput opening = challenge;
            opening.openingMemory = true;
            opening.instantFeedback = SingleContractInstantFeedback.None;
            opening.finishRemaining = 950f;
            opening.leadMeters = 0f;
            opening.injuries = 0;
            CaptureState("opening", 0f, opening, view, feedback, camera,
                width, height, outputDirectory, report);

            SingleContractHudInput calibration = challenge;
            calibration.visualState = SingleContractVisualState.Calibration;
            calibration.generation = 0;
            calibration.predictionGateActive = false;
            calibration.instantFeedback = SingleContractInstantFeedback.None;
            calibration.injuries = 0;
            calibration.calibrationProgress = new SingleContractCalibrationProgress
            {
                available = true,
                totalSamples = 12, minimumTotalSamples = 24,
                activeSamples = 3, minimumActiveSamples = 6,
                actionCategories = 2, minimumActionCategories = 2,
                jumpSamples = 1, minimumJumpSamples = 2,
                slideSamples = 1, minimumSlideSamples = 2,
                formalChoices = 2, minimumFormalChoices = 5,
                successfulChoices = 1, minimumSuccessfulChoices = 3,
                strongestRouteChoices = 2, minimumStrongestRouteChoices = 3
            };
            CaptureState("calibration", 0f, calibration, view, feedback, camera,
                width, height, outputDirectory, report);

            challenge.feedbackRelearned = true;
            challenge.visualState = SingleContractVisualState.RelearnPulse;
            CaptureState("relearn-combined", 0.6f, challenge, view, feedback, camera,
                width, height, outputDirectory, report);

            hud.SetActive(false);
            CaptureUiManagerScreens(canvasObject, camera, width, height,
                outputDirectory, report);
        }
        finally
        {
            camera.targetTexture = null;
            Object.DestroyImmediate(canvasObject);
            Object.DestroyImmediate(cameraObject);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static void CaptureFollowupStates(EchoHudView view,
        CanvasGroup feedback, Camera camera, int width, int height,
        string outputDirectory, StringBuilder report)
    {
        // Both 1280-wide viewport aspect ratios receive unique file names.
        string viewport = height + "-";
        SingleContractHudInput challenge = ChallengeInput();
        challenge.maximumCollisionStrikes = 2;
        using (var accessibility = new AccessibilityMemoryOverride())
        {
            accessibility.Set(true, true, false);
            CaptureState(viewport + "large-text-high-contrast", 0.6f,
                challenge, view, feedback, camera, width, height,
                outputDirectory, report);

            accessibility.Set(false, false, true);
            CaptureState(viewport + "reduced-motion-visible", 0.075f,
                challenge, view, feedback, camera, width, height,
                outputDirectory, report);
            CaptureState(viewport + "reduced-motion-before-expiry", 2.15f,
                challenge, view, feedback, camera, width, height,
                outputDirectory, report);
            CaptureState(viewport + "reduced-motion-expired", 2.25f,
                challenge, view, feedback, camera, width, height,
                outputDirectory, report);

            accessibility.Set(false, false, false);
            challenge.instantFeedback = SingleContractInstantFeedback.None;
            challenge.collisionRecoveryTimeRemaining = 0.8f;
            CaptureState(viewport + "injury-recovering", 0f,
                challenge, view, feedback, camera, width, height,
                outputDirectory, report);
            challenge.collisionRecoveryTimeRemaining = 0f;
            CaptureState(viewport + "injury-next-hit-out", 0f,
                challenge, view, feedback, camera, width, height,
                outputDirectory, report);
            challenge.injuries = 2;
            CaptureState(viewport + "injury-out", 0f,
                challenge, view, feedback, camera, width, height,
                outputDirectory, report);
        }
    }

    private sealed class AccessibilityMemoryOverride : System.IDisposable
    {
        private readonly FieldInfo[] _fields;
        private readonly object[] _originalValues;

        public AccessibilityMemoryOverride()
        {
            string[] names = { "_initialized", "_largeText", "_highContrast", "_reducedMotion" };
            _fields = new FieldInfo[names.Length];
            _originalValues = new object[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                _fields[index] = typeof(EchoRunAccessibility).GetField(names[index],
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (_fields[index] == null)
                    throw new System.MissingFieldException("EchoRunAccessibility." + names[index]);
                _originalValues[index] = _fields[index].GetValue(null);
            }
        }

        public void Set(bool largeText, bool highContrast, bool reducedMotion)
        {
            _fields[0].SetValue(null, true);
            _fields[1].SetValue(null, largeText);
            _fields[2].SetValue(null, highContrast);
            _fields[3].SetValue(null, reducedMotion);
        }

        public void Dispose()
        {
            for (int index = _fields.Length - 1; index >= 0; index--)
                _fields[index].SetValue(null, _originalValues[index]);
        }
    }

    private static SingleContractHudInput ChallengeInput()
    {
        return new SingleContractHudInput
        {
            visualState = SingleContractVisualState.Challenge,
            generation = 3,
            memory = "最近选路：偏右",
            showPrediction = true,
            predictedLane = 2,
            predictionGateActive = true,
            predictionGateNumber = 3,
            predictionGateCount = 6,
            leadMeters = 12.6f,
            injuries = 1,
            finishRemaining = 470f,
            instantFeedback = SingleContractInstantFeedback.RewriteSucceeded,
            feedbackLeadDeltaMeters = 7.5f,
            feedbackSequence = 1
        };
    }

    private static void CaptureUiManagerScreens(GameObject canvasObject,
        Camera camera, int width, int height, string outputDirectory,
        StringBuilder report)
    {
        // These hosts stay inactive. Do not invoke Awake, Start, gameplay
        // transitions or settlement: those paths can initialize/write saves.
        GameObject host = new GameObject("RacingFeedbackUiManagerState");
        host.SetActive(false);
        GameManager gameManager = host.AddComponent<GameManager>();
        UIManager manager = host.AddComponent<UIManager>();
        try
        {
            SetUiField(manager, "_gm", gameManager);
            SetUiField(manager, "_safeAreaRoot",
                canvasObject.GetComponent<RectTransform>());
            SetUiField(manager, "_font", Resources.Load<Font>(
                "Fonts/EchoRunSansSC-Regular"));
            SetUiField(manager, "_titleFont", Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf"));
            InvokeUiBuilder(manager, "CreateMenuPanel");
            InvokeUiBuilder(manager, "CreateGameOverPanel");

            GameObject menu = GetUiField<GameObject>(manager, "_menuPanel");
            GameObject result = GetUiField<GameObject>(manager, "_gameOverPanel");
            foreach (Transform child in canvasObject.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = CaptureLayer;

            PresentMenuCapture(manager, null);
            menu.SetActive(true);
            CaptureUiManagerState("menu-first-run", menu.transform, camera,
                width, height, outputDirectory, report);

            ActiveEchoIdentity previous = CaptureIdentity(3, 2, 0.83f, 5);
            PresentMenuCapture(manager, previous);
            CaptureUiManagerState("menu-challenge", menu.transform, camera,
                width, height, outputDirectory, report);

            menu.SetActive(false);
            result.SetActive(true);
            ActiveEchoIdentity next = CaptureIdentity(4, 0, 0.67f, 4);
            EchoCognitionAssessment cognition = EchoCognitionAssessment.Compare(
                previous, next, 4, 6, 3, true);
            PresentResultCapture(manager, RunEndReason.FinishReached, true,
                next, cognition, new GateAttempt
                {
                    gateId = 6, committedLane = 0,
                    chosenRole = PredictionGateRole.Counter,
                    execution = GateExecutionOutcome.Success,
                    executionReason = GateExecutionReason.Completed,
                    hasLateralEvidence = true,
                    laneChangeInProgress = true,
                    lateralOffset = -1.2f
                });
            CaptureUiManagerState("result-promotion", result.transform, camera,
                width, height, outputDirectory, report);

            PresentResultCapture(manager, RunEndReason.Collision, false,
                null, default, new GateAttempt
                {
                    gateId = 3, committedLane = 0,
                    chosenRole = PredictionGateRole.Counter,
                    execution = GateExecutionOutcome.Hit,
                    executionReason = GateExecutionReason.Collision,
                    hasLateralEvidence = true,
                    laneChangeInProgress = true,
                    lateralOffset = -0.8f
                });
            CaptureUiManagerState("result-counter-collision", result.transform,
                camera, width, height, outputDirectory, report);
        }
        finally
        {
            // UI components live on the temporary capture canvas and will be
            // destroyed by CaptureResolution. The inactive host never runs.
            GetUiField<RuntimeRoundedSprite>(manager, "_roundedUi").Dispose();
            Object.DestroyImmediate(host);
        }
    }

    private static void PresentMenuCapture(UIManager manager,
        ActiveEchoIdentity identity)
    {
        EchoMenuViewData data = EchoRunPresentation.BuildSingleContractMenu(identity);
        GetUiField<Text>(manager, "_menuGenerationText").text = data.generation;
        GetUiField<Text>(manager, "_menuLearnedText").text = data.learned;
        GetUiField<Text>(manager, "_menuRuleText").text = data.rule + "\n"
            + EchoRunPresentation.SingleContractRouteGuide;
        GetUiField<Text>(manager, "_menuObjectiveText").text = data.objective;
        GetUiField<Text>(manager, "_menuProtocolText").text =
            "A / D 变道 · W / 空格跳跃 · S / Ctrl 滑铲";
        GetUiField<Button>(manager, "_startBtn")
            .GetComponentInChildren<Text>(true).text = data.primaryAction;
    }

    private static void PresentResultCapture(UIManager manager,
        RunEndReason endReason, bool won, ActiveEchoIdentity promoted,
        EchoCognitionAssessment cognition, GateAttempt attempt)
    {
        MethodInfo buildResult = typeof(AIShadowRunner).GetMethod(
            "BuildSingleContractResult", BindingFlags.Static | BindingFlags.NonPublic);
        if (buildResult == null)
            throw new System.MissingMethodException("AIShadowRunner.BuildSingleContractResult");
        string text = (string)buildResult.Invoke(null, new object[]
        {
            endReason, true, won, promoted != null, promoted, 3, cognition,
            default(SingleContractCalibrationProgress)
        });
        text += "\n" + EchoRunPresentation.BuildSingleContractGateReview(attempt);
        Text title = GetUiField<Text>(manager, "_gameOverTitleText");
        string titleText = UIManager.GetSingleContractGameOverTitle(
            text, endReason, true, won);
        manager.PresentResultSummary(text, titleText, true);
        title.color = won ? EchoRunUITheme.Success : EchoRunUITheme.Danger;
        GetUiField<Text>(manager, "_gameOverStatsText").text =
            won ? "得分 2480 · 金币 42"
                : "得分 1280 · 金币 23";
        GetUiField<Button>(manager, "_restartBtn")
            .GetComponentInChildren<Text>(true).text =
            UIManager.GetSingleContractGameOverActionLabel(endReason,
                true, promoted != null, promoted != null ? 4 : 3, true);
    }

    private static ActiveEchoIdentity CaptureIdentity(int generation,
        int lane, float confidence, int evidence)
    {
        return new ActiveEchoIdentity
        {
            generation = generation,
            identityId = "capture-identity-" + generation,
            parentIdentityId = "capture-identity-" + (generation - 1),
            memoryContract = new EchoMemoryContract
            {
                preferredLane = lane, confidence = confidence,
                evidenceCount = evidence
            }
        };
    }

    private static void CaptureUiManagerState(string state, Transform root,
        Camera camera, int width, int height, string outputDirectory,
        StringBuilder report)
    {
        // Match the runtime's repeated geometry refresh after dynamic-font
        // atlas growth; do not replace Text with a capture-only UI renderer.
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
            {
                if (!text.gameObject.activeInHierarchy) continue;
                if (text.font != null)
                    text.font.RequestCharactersInTexture(text.text,
                        text.fontSize, text.fontStyle);
                text.SetLayoutDirty();
                text.SetVerticesDirty();
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
        }
        string path = Path.Combine(outputDirectory, width + "-" + state + ".png");
        SaveCameraImage(path, camera, width, height);
        report.AppendLine(path + " | UIManager injected state=" + state
            + " | actual production components | no gameplay/save lifecycle");
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
            if (text.gameObject.activeInHierarchy)
                report.AppendLine("  " + text.name + " | "
                    + text.text.Replace('\n', '|'));
        Debug.Log("RACING_FEEDBACK_CAPTURE " + path);
    }

    private static void InvokeUiBuilder(UIManager manager, string method)
    {
        MethodInfo builder = typeof(UIManager).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (builder == null) throw new System.MissingMethodException("UIManager." + method);
        builder.Invoke(manager, null);
    }

    private static T GetUiField<T>(UIManager manager, string field)
    {
        FieldInfo info = typeof(UIManager).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null) throw new System.MissingFieldException("UIManager." + field);
        return (T)info.GetValue(manager);
    }

    private static void SetUiField(UIManager manager, string field, object value)
    {
        FieldInfo info = typeof(UIManager).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null) throw new System.MissingFieldException("UIManager." + field);
        info.SetValue(manager, value);
    }

    private static void CaptureState(string phase, float elapsed,
        SingleContractHudInput input, EchoHudView view, CanvasGroup feedback,
        Camera camera, int width, int height, string outputDirectory,
        StringBuilder report)
    {
        SingleContractHudData data = EchoRunPresentation.BuildSingleContractHud(input);
        view.PresentSingleContract(data, data.openingMemory);
        view.SetStats(1280, 480f);
        view.ShowTimedFeedback(data.instantFeedback, EchoRunUITheme.HudSuccessText,
            elapsed, !string.IsNullOrEmpty(data.instantFeedback),
            EchoRunAccessibility.ReducedMotion);
        EchoRunAccessibility.ApplyToHierarchy(view.transform);
        // New font sizes can grow the dynamic atlas. Refresh the production
        // Text geometry after atlas growth before reading the rendered frame.
        for (int pass = 0; pass < 3; pass++)
        {
            foreach (Text text in view.GetComponentsInChildren<Text>(true))
            {
                if (!text.gameObject.activeInHierarchy) continue;
                if (text.font != null)
                    text.font.RequestCharactersInTexture(text.text,
                        text.fontSize, text.fontStyle);
                text.SetLayoutDirty();
                text.SetVerticesDirty();
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
        }

        string path = Path.Combine(outputDirectory, width + "-" + phase + ".png");
        SaveCameraImage(path, camera, width, height);
        report.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "{0} | elapsed={1:0.000}s | groupAlpha={2:0.000} | active={3} | text={4}",
            path, elapsed, feedback.alpha, feedback.gameObject.activeInHierarchy,
            data.instantFeedback));
        report.AppendLine("  injected viewport=" + width + "x" + height
            + " | largeText=" + EchoRunAccessibility.LargeText
            + " | highContrast=" + EchoRunAccessibility.HighContrast
            + " | reducedMotion=" + EchoRunAccessibility.ReducedMotion
            + " | injuries=" + data.injuriesText);
        Debug.Log("RACING_FEEDBACK_CAPTURE " + path);
    }

    private static void SaveCameraImage(string path, Camera camera,
        int width, int height)
    {
        RenderTexture previous = RenderTexture.active;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = camera.targetTexture;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(image);
        }
    }
}
