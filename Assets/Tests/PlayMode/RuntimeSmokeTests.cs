using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class RuntimeSmokeTests
{
    [UnityTest]
    public IEnumerator BundledAudioAndBalanceLoadInPlayer()
    {
        yield return null;

        Assert.IsNotNull(Resources.Load<AudioClip>("Audio/bgm_transit"));
        Assert.IsNotNull(Resources.Load<AudioClip>("Audio/footstep_01"));
        Assert.IsNotNull(Resources.Load<AudioClip>("Audio/collision"));
        Assert.IsNotNull(Resources.Load<AudioClip>("Audio/coin"));
        Assert.IsNotNull(Resources.Load<AudioClip>("Audio/ui_click"));
        Material shaderRetention = Resources.Load<Material>(
            "Materials/EchoKitVertexColor");
        Assert.IsNotNull(shaderRetention,
            "The runtime vertex-color shader must be retained by a bundled material.");
        Assert.AreEqual("EchoRun/VertexColor", shaderRetention.shader.name);
        Assert.AreEqual(4, GameBalanceConfig.Current.powerUps.Length);
    }

    [UnityTest]
    public IEnumerator RuntimeManagersBootstrapWithoutExceptions()
    {
        for (int frame = 0; frame < 120 && GameManager.Instance == null; frame++)
            yield return null;

        Assert.IsNotNull(GameManager.Instance);
        Assert.IsNotNull(TrackManager.Instance);
        Assert.IsNotNull(PowerUpController.Instance);
        Assert.IsNotNull(AudioManager.Instance);
    }

    [UnityTest]
    public IEnumerator StyledGameplayPropsKeepTheirGameplayColliders()
    {
        for (int frame = 0; frame < 120 && WorldStyler.Instance == null; frame++)
            yield return null;

        Assert.IsNotNull(WorldStyler.Instance);
        GameObject coin = null;
        var obstacles = new GameObject[3];

        try
        {
            coin = new GameObject("VisualTestCoin");
            BoxCollider coinCollider = coin.AddComponent<BoxCollider>();
            coinCollider.isTrigger = true;
            WorldStyler.Instance.StyleCoin(coin);

            Transform coinVisual = coin.transform.Find("StreamlinedVisual");
            Assert.IsNotNull(coinVisual);
            Assert.IsNotNull(coinVisual.GetComponent<EchoCoinVisual>());
            Assert.AreEqual(1,
                coinVisual.GetComponentsInChildren<Renderer>(true).Length,
                "Coin trails must use one combined renderer.");
            MeshFilter coinMesh = coinVisual.GetComponent<MeshFilter>();
            Assert.IsNotNull(coinMesh);
            Assert.IsNotNull(coinMesh.sharedMesh);
            Assert.AreSame(coinCollider, coin.GetComponent<BoxCollider>());
            Assert.IsTrue(coinCollider.isTrigger);

            string[] signatureParts =
            {
                "SlideDroneBody",
                "JumpBlockBody",
                "LaneBulkheadBody"
            };
            Vector3[] colliderSizes =
            {
                new Vector3(3.1f, 0.82f, 1.2f),
                new Vector3(3.2f, 0.9f, 0.7f),
                new Vector3(3.4f, 2.7f, 0.9f)
            };
            Vector3[] colliderCenters =
            {
                new Vector3(0f, 0.95f, 0f),
                new Vector3(0f, -0.45f, 0f),
                new Vector3(0f, 0.25f, 0f)
            };
            for (int i = 0; i < obstacles.Length; i++)
            {
                GameObject obstacle = new GameObject("VisualTestObstacle_" + i);
                obstacles[i] = obstacle;
                BoxCollider gameplayCollider = obstacle.AddComponent<BoxCollider>();
                gameplayCollider.isTrigger = true;
                gameplayCollider.size = colliderSizes[i];
                gameplayCollider.center = colliderCenters[i];
                Obstacle data = obstacle.AddComponent<Obstacle>();
                data.type = (ObstacleType)i;

                WorldStyler.Instance.StyleObstacle(obstacle);

                Transform visual = obstacle.transform.Find("StreamlinedVisual");
                Assert.IsNotNull(visual);
                Assert.IsNotNull(visual.Find(signatureParts[i]));
                Assert.AreSame(gameplayCollider,
                    obstacle.GetComponent<BoxCollider>());
                Assert.IsTrue(gameplayCollider.isTrigger);

                Bounds visualBounds = CombinedRendererBounds(visual);
                Assert.GreaterOrEqual(visualBounds.size.x,
                    gameplayCollider.bounds.size.x * 0.9f,
                    data.type + " visual must visibly block its collider width.");
                Assert.LessOrEqual(visualBounds.size.x,
                    gameplayCollider.bounds.size.x * 1.05f,
                    data.type + " visual must not extend beyond its collider.");
                if (data.type == ObstacleType.Low)
                {
                    Assert.IsNull(visual.Find("SlideShutterBody"),
                        "The old slide shutter must not return.");
                    Assert.LessOrEqual(visualBounds.size.x, 3.15f,
                        "The slide drone must remain lane-sized.");
                    Assert.AreEqual(gameplayCollider.bounds.min.y,
                        visualBounds.min.y, 0.06f,
                        "The slide drone must visibly match its collider bottom.");
                }
                AssertNoPointLikeObstacleParts(visual);
            }

            yield return null;
        }
        finally
        {
            if (coin != null) Object.Destroy(coin);
            foreach (GameObject obstacle in obstacles)
            {
                if (obstacle != null) Object.Destroy(obstacle);
            }
        }
    }

    [UnityTest]
    public IEnumerator CoinPoolRepairsBindingAndKeepsRouteAlignedTrigger()
    {
        for (int frame = 0; frame < 120
             && (TrackManager.Instance == null || WorldStyler.Instance == null);
             frame++)
            yield return null;

        TrackManager track = TrackManager.Instance;
        Assert.IsNotNull(track);
        Assert.IsNotNull(WorldStyler.Instance);

        MethodInfo spawnCoin = typeof(TrackManager).GetMethod(
            "SpawnCoinInstance", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(spawnCoin);

        GameObject originalPrefab = track.coinPrefab;
        GameObject legacyPrefab = new GameObject("LegacyCoinPrefab_Test");
        BoxCollider legacyTrigger = legacyPrefab.AddComponent<BoxCollider>();
        legacyTrigger.size = new Vector3(1f, 1f, 0.4f);
        legacyTrigger.isTrigger = true;
        legacyPrefab.SetActive(false);
        Assert.IsNull(legacyPrefab.GetComponent<Coin>(),
            "The test template must reproduce the missing Coin binding.");
        track.coinPrefab = legacyPrefab;

        GameObject owner = new GameObject("CoinPoolRuntimeOwner");
        owner.transform.SetPositionAndRotation(
            new Vector3(1000f, 0f, 1000f),
            Quaternion.Euler(0f, 90f, 0f));
        GameObject spawned = null;
        try
        {
            Quaternion firstRoute = TrackSpawnRules.CoinRouteRotation(
                owner.transform.rotation, Vector3.forward);
            spawned = spawnCoin.Invoke(track, new object[]
            {
                owner,
                owner.transform.position + Vector3.up,
                firstRoute,
                true,
                17
            }) as GameObject;
            Assert.IsNotNull(spawned);
            yield return null;

            Assert.IsTrue(spawned.activeInHierarchy);
            Assert.AreSame(owner.transform, spawned.transform.parent);
            Assert.AreEqual(1, spawned.GetComponents<Coin>().Length,
                "A pooled pickup must never accumulate Coin components.");
            BoxCollider trigger = spawned.GetComponent<BoxCollider>();
            Assert.IsNotNull(trigger);
            Assert.IsTrue(trigger.isTrigger);
            Assert.AreEqual(new Vector3(1f, 1f, 0.4f), trigger.size);
            Assert.Less(Vector3.Angle(spawned.transform.forward,
                    firstRoute * Vector3.forward), 0.01f,
                "The root trigger must follow the route, not the camera.");

            EchoCoinVisual visual =
                spawned.GetComponentInChildren<EchoCoinVisual>(true);
            Assert.IsNotNull(visual,
                "World styling must attach the single rendered coin visual.");
            Renderer renderer = visual.GetComponent<Renderer>();
            Assert.IsNotNull(renderer);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.AreEqual(1f,
                properties.GetFloat(Shader.PropertyToID("_ContractMarker")),
                0.001f);

            GameObject firstSpawn = spawned;
            track.ReleaseDynamic(firstSpawn);
            Assert.IsFalse(firstSpawn.activeSelf);

            Quaternion secondRoute = TrackSpawnRules.CoinRouteRotation(
                Quaternion.Euler(0f, -90f, 0f), Vector3.forward);
            GameObject reused = spawnCoin.Invoke(track, new object[]
            {
                owner,
                owner.transform.position + Vector3.up * 2f,
                secondRoute,
                false,
                0
            }) as GameObject;
            spawned = reused;
            Assert.AreSame(firstSpawn, reused,
                "The test must exercise the actual pooled reuse path.");
            yield return null;

            Assert.AreEqual(1, reused.GetComponents<Coin>().Length);
            Assert.Less(Vector3.Angle(reused.transform.forward,
                    secondRoute * Vector3.forward), 0.01f);
            renderer.GetPropertyBlock(properties);
            Assert.AreEqual(0f,
                properties.GetFloat(Shader.PropertyToID("_ContractMarker")),
                0.001f,
                "Reused normal coins must clear the contract marker.");
        }
        finally
        {
            if (spawned != null && spawned.activeSelf)
                track.ReleaseDynamic(spawned);
            track.coinPrefab = originalPrefab;
            Dictionary<GameObject, Queue<GameObject>> pools =
                GetPrivateField<Dictionary<GameObject, Queue<GameObject>>>(
                    track, "_dynamicPools");
            if (pools.TryGetValue(legacyPrefab,
                    out Queue<GameObject> testPool))
            {
                while (testPool.Count > 0)
                    Object.Destroy(testPool.Dequeue());
                pools.Remove(legacyPrefab);
            }
            Object.Destroy(legacyPrefab);
            Object.Destroy(owner);
        }
    }

    private static Bounds CombinedRendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        Assert.IsNotEmpty(renderers);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static void AssertNoPointLikeObstacleParts(Transform visual)
    {
        string[] forbiddenNames = { "Node", "Joint", "Hub", "Eye", "Dot" };
        Transform[] parts = visual.GetComponentsInChildren<Transform>();
        foreach (Transform part in parts)
        {
            foreach (string forbiddenName in forbiddenNames)
            {
                Assert.IsFalse(part.name.Contains(forbiddenName),
                    "Point-like obstacle decoration remains: " + part.name);
            }
        }
    }

    [UnityTest]
    public IEnumerator RestartedRunRecreatesTrackBeforeAutoStart()
    {
        GameManager bootstrapManager = GameManager.Instance;
        SceneManager.LoadScene(0);
        for (int frame = 0; frame < 120
             && (GameManager.Instance == null
                 || object.ReferenceEquals(GameManager.Instance, bootstrapManager));
             frame++)
            yield return null;

        Assert.IsNotNull(GameManager.Instance);
        GameManager firstRunManager = GameManager.Instance;

        firstRunManager.Restart();
        for (int frame = 0; frame < 120
             && (GameManager.Instance == null
                 || object.ReferenceEquals(GameManager.Instance, firstRunManager)
                 || GameManager.Instance.State != GameState.Playing);
             frame++)
            yield return null;

        Assert.IsNotNull(GameManager.Instance);
        Assert.AreEqual(GameState.Playing, GameManager.Instance.State);
        Assert.IsNotNull(TrackManager.Instance);

        yield return null;
        Assert.Greater(TrackManager.Instance.ActiveSegmentCount, 0,
            "The restarted run must generate track segments.");

        GameManager restartedManager = GameManager.Instance;
        restartedManager.ReturnToMenu();
        for (int frame = 0; frame < 120
             && (GameManager.Instance == null
                 || object.ReferenceEquals(GameManager.Instance, restartedManager)
                 || GameManager.Instance.State != GameState.Menu);
             frame++)
            yield return null;
        Assert.IsNotNull(GameManager.Instance);
        Assert.AreEqual(GameState.Menu, GameManager.Instance.State);
    }

    [UnityTest]
    public IEnumerator MenuProgressionSystemsBootstrapOnTheRuntimeCanvas()
    {
        yield return null;
        yield return null;

        Assert.IsNotNull(Object.FindObjectOfType<Canvas>());
        Assert.IsNotNull(Object.FindObjectOfType<PowerUpShopUI>());
        Assert.IsNotNull(Object.FindObjectOfType<AITrainingDashboardUI>());
    }

    [UnityTest]
    public IEnumerator HomePrimaryActionIsInsideSafeAreaAndReceivesPointerClick()
    {
        SceneManager.LoadScene("SampleScene");
        yield return null;
        UIManager ui = null;
        for (int frame = 0; frame < 180
             && (GameManager.Instance == null || ui == null); frame++)
        {
            ui = Object.FindObjectOfType<UIManager>();
            yield return null;
        }

        Assert.IsNotNull(ui);
        Assert.AreEqual(GameState.Menu, GameManager.Instance.State);
        Button start = GetPrivateField<Button>(ui, "_startBtn");
        RectTransform safeArea = GetPrivateField<RectTransform>(
            ui, "_safeAreaRoot");
        AssertButtonVisibleAndRaycastable(start, safeArea);

        ExecuteEvents.Execute(start.gameObject,
            new PointerEventData(EventSystem.current),
            ExecuteEvents.pointerClickHandler);
        yield return null;
        Assert.AreEqual(GameState.Playing, GameManager.Instance.State,
            "The visible primary action must enter gameplay on pointer click.");

        GameManager activeManager = GameManager.Instance;
        activeManager.ReturnToMenu();
        for (int frame = 0; frame < 180
             && (GameManager.Instance == null
                 || ReferenceEquals(GameManager.Instance, activeManager)
                 || GameManager.Instance.State != GameState.Menu); frame++)
            yield return null;
        Assert.AreEqual(GameState.Menu, GameManager.Instance.State);
    }

    [UnityTest]
    public IEnumerator SettingsSoundSlidersKeepVisibleMeaningfulReadouts()
    {
        SceneManager.LoadScene("SampleScene");
        yield return null;
        UIManager ui = null;
        for (int frame = 0; frame < 180
             && (GameManager.Instance == null || ui == null); frame++)
        {
            ui = Object.FindObjectOfType<UIManager>();
            yield return null;
        }

        Assert.IsNotNull(ui);
        Button settings = GetPrivateField<Button>(ui, "_settingsBtn");
        ExecuteEvents.Execute(settings.gameObject,
            new PointerEventData(EventSystem.current),
            ExecuteEvents.pointerClickHandler);
        for (int frame = 0; frame < 5; frame++) yield return null;
        Canvas.ForceUpdateCanvases();

        Text master = GetPrivateField<Text>(ui, "_masterValueText");
        Text music = GetPrivateField<Text>(ui, "_bgmValueText");
        Text effects = GetPrivateField<Text>(ui, "_sfxValueText");
        AssertSoundReadout(master, "主音量");
        AssertSoundReadout(music, "音乐音量");
        AssertSoundReadout(effects, "音效音量");
        Button back = GetPrivateField<Button>(ui, "_settingsBackBtn");
        GameObject settingsPanel = GetPrivateField<GameObject>(
            ui, "_settingsPanel");
        RectTransform safeArea = GetPrivateField<RectTransform>(
            ui, "_safeAreaRoot");
        RectTransform backRect = back.GetComponent<RectTransform>();
        Assert.AreSame(settingsPanel.transform, back.transform.parent,
            "Settings back must stay outside the scroll content.");
        Assert.AreEqual(new Vector2(0f, 1f), backRect.anchorMin);
        Assert.AreEqual(new Vector2(0f, 1f), backRect.anchorMax);
        Assert.AreEqual(new Vector2(0f, 1f), backRect.pivot);
        Assert.AreEqual(new Vector2(24f, -24f), backRect.anchoredPosition);
        AssertButtonVisibleAndRaycastable(back, safeArea);
    }

    [UnityTest]
    public IEnumerator RepeatedRestartsRestoreRuntimeArtForEveryRun()
    {
        SceneManager.LoadScene("SampleScene");
        yield return null;
        yield return WaitForFreshRun(null, false);

        for (int run = 0; run < 4; run++)
        {
            AssertRuntimeArtIsReady(run + 1);

            GameManager previous = GameManager.Instance;
            previous.Restart();
            yield return WaitForFreshRun(previous, true);
            yield return null;
        }

        AssertRuntimeArtIsReady(5);
    }

    private static IEnumerator WaitForFreshRun(GameManager previous,
        bool requirePlaying)
    {
        for (int frame = 0; frame < 240; frame++)
        {
            bool managerReady = GameManager.Instance != null
                                && !ReferenceEquals(GameManager.Instance, previous);
            bool stateReady = !requirePlaying
                              || GameManager.Instance.State == GameState.Playing;
            bool artReady = WorldStyler.Instance != null
                            && TrackManager.Instance != null;
            if (managerReady && stateReady && artReady) yield break;
            yield return null;
        }

        Assert.Fail("The reloaded run did not finish bootstrapping in time.");
    }

    private static void AssertRuntimeArtIsReady(int runNumber)
    {
        string context = "Run " + runNumber + ": ";
        Assert.IsNotNull(WorldStyler.Instance,
            context + "WorldStyler is missing.");
        Assert.IsNotNull(RenderSettings.skybox,
            context + "the runtime skybox is missing.");
        Assert.IsNotNull(RenderSettings.skybox.shader,
            context + "the runtime skybox shader is missing.");
        Assert.AreEqual(WorldStyler.SeamlessSkyShaderName,
            RenderSettings.skybox.shader.name,
            context + "the authored skybox was not restored.");

        Camera camera = Camera.main;
        Assert.IsNotNull(camera, context + "the main camera is missing.");
        Assert.AreEqual(CameraClearFlags.Skybox, camera.clearFlags,
            context + "the camera is not rendering the skybox.");

        Light fill = GameObject.Find("EchoFillLight")?.GetComponent<Light>();
        Assert.IsNotNull(fill, context + "the authored fill light is missing.");

        if (GameManager.Instance == null
            || GameManager.Instance.State != GameState.Playing) return;

        TrackSegmentData[] segments = Object.FindObjectsOfType<TrackSegmentData>();
        Assert.IsNotEmpty(segments,
            context + "no active track segment was generated.");
        foreach (TrackSegmentData segment in segments)
        {
            Transform environment = segment.transform.Find("EchoEnvironment");
            Assert.IsNotNull(environment,
                context + segment.name + " has no authored environment.");
            Assert.IsTrue(environment.gameObject.activeInHierarchy,
                context + segment.name + " environment is inactive.");
        }

        foreach (Obstacle obstacle in Object.FindObjectsOfType<Obstacle>())
        {
            Assert.IsNotNull(obstacle.transform.Find("StreamlinedVisual"),
                context + obstacle.name + " kept its primitive obstacle art.");
        }

        foreach (Coin coin in Object.FindObjectsOfType<Coin>())
        {
            Assert.IsNotNull(coin.transform.Find("StreamlinedVisual"),
                context + coin.name + " kept its primitive coin art.");
        }
    }

    [UnityTest]
    public IEnumerator ResultTextAndActionsStayInsideSafeAreaAndRaycast()
    {
        SceneManager.LoadScene("SampleScene");
        yield return null;
        UIManager ui = null;
        for (int frame = 0; frame < 180
             && (GameManager.Instance == null || ui == null); frame++)
        {
            ui = Object.FindObjectOfType<UIManager>();
            yield return null;
        }

        Assert.IsNotNull(ui);
        FieldInfo stateField = typeof(GameManager).GetField(
            "<State>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(stateField);
        stateField.SetValue(GameManager.Instance, GameState.GameOver);
        GameManager.Instance.OnStateChanged.Invoke(GameState.GameOver);
        yield return null;
        yield return null;

        RectTransform safeArea = GetPrivateField<RectTransform>(
            ui, "_safeAreaRoot");
        Text result = GetPrivateField<Text>(ui, "_shadowResultText");
        Assert.IsTrue(result.gameObject.activeInHierarchy);
        AssertRectContained(result.rectTransform, safeArea);
        AssertButtonVisibleAndRaycastable(
            GetPrivateField<Button>(ui, "_restartBtn"), safeArea);
        AssertButtonVisibleAndRaycastable(
            GetPrivateField<Button>(ui, "_goToMenuBtn"), safeArea);
    }

    [UnityTest]
    public IEnumerator FinishMarkerAppearsAheadWithoutBlockingTheRunner()
    {
        SceneManager.LoadScene("SampleScene");
        yield return null;
        for (int frame = 0; frame < 180
             && (GameManager.Instance == null || TrackManager.Instance == null);
             frame++)
            yield return null;

        GameManager gameManager = GameManager.Instance;
        TrackManager track = TrackManager.Instance;
        Assert.IsNotNull(gameManager);
        Assert.IsNotNull(track);
        gameManager.StartGame();
        yield return null;
        yield return null;

        float nearFinish = gameManager.CourseDistance - 10f;
        typeof(GameManager).GetField("<Distance>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(
            gameManager, nearFinish);
        typeof(GameManager).GetField("_distanceTraveled",
            BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(
            gameManager, nearFinish);
        MethodInfo updateMarker = typeof(TrackManager).GetMethod(
            "UpdateFinishMarker",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(updateMarker);
        updateMarker.Invoke(track, null);

        Transform marker = track.transform.Find("FinishMarker");
        Assert.IsNotNull(marker);
        Assert.IsTrue(marker.gameObject.activeInHierarchy);
        Assert.GreaterOrEqual(marker.GetComponentsInChildren<Renderer>().Length, 10);
        Assert.IsNotNull(marker.Find("ProtocolCore"));
        Light[] finishLights = marker.GetComponentsInChildren<Light>(true);
        Assert.AreEqual(2, finishLights.Length);
        foreach (Light finishLight in finishLights)
        {
            Assert.LessOrEqual(finishLight.range, 6f);
            Assert.AreEqual(LightShadows.None, finishLight.shadows);
        }
        foreach (Collider collider in marker.GetComponentsInChildren<Collider>())
            Assert.IsFalse(collider.enabled,
                "The visual finish marker must never collide with the runner.");
        PlayerController player = Object.FindObjectOfType<PlayerController>();
        Assert.IsNotNull(player);
        Vector3 toMarker = marker.position - player.transform.position;
        Assert.Greater(Vector3.Dot(toMarker, player.ForwardDirection), 1f,
            "The finish marker must be visibly ahead of the runner.");
    }

    [UnityTest]
    public IEnumerator MenuReportLauncherStaysClickableAboveHomeSurface()
    {
        yield return null;
        yield return null;

        AITrainingDashboardUI training =
            Object.FindObjectOfType<AITrainingDashboardUI>();
        Assert.IsNotNull(training);
        GameObject launcher = GetPrivateObject(training, "_launcher");
        Assert.IsNotNull(launcher);
        Assert.IsTrue(launcher.activeInHierarchy);
        Transform home = launcher.transform.parent.Find("MenuPanel");
        Assert.IsNotNull(home);
        Assert.Greater(launcher.transform.GetSiblingIndex(), home.GetSiblingIndex(),
            "The report launcher must remain clickable above the home surface.");
    }

    [UnityTest]
    public IEnumerator SupplyLauncherJoinsTheLandscapeHomeActions()
    {
        yield return null;
        yield return null;

        if (UILayoutRules.IsCompactPortrait(Screen.width, Screen.height))
            Assert.Ignore("Landscape launcher placement is not used in portrait.");

        PowerUpShopUI shop = Object.FindObjectOfType<PowerUpShopUI>();
        Assert.IsNotNull(shop);
        GameObject launcher = GetPrivateObject(shop, "_launcher");
        Assert.IsNotNull(launcher);
        RectTransform rect = launcher.GetComponent<RectTransform>();
        Assert.AreEqual(0.19f, rect.anchorMin.x, 0.001f);
        Assert.AreEqual(0.095f, rect.anchorMin.y, 0.001f);
    }

    [UnityTest]
    public IEnumerator MenuPanelTextStaysInsideItsPanel()
    {
        yield return null;
        yield return null;

        PowerUpShopUI shop = Object.FindObjectOfType<PowerUpShopUI>();
        AITrainingDashboardUI training =
            Object.FindObjectOfType<AITrainingDashboardUI>();
        Assert.IsNotNull(shop);
        Assert.IsNotNull(training);

        GameObject shopPanel = GetPrivatePanel(shop);
        Assert.IsNotNull(shopPanel);
        AssertContainedHorizontally(
            shopPanel.transform.Find("Title") as RectTransform);
        AssertContainedHorizontally(
            shopPanel.transform.Find("Feedback") as RectTransform);
        for (int i = 0; i < 4; i++)
        {
            Transform row = shopPanel.transform.Find(
                "Item_" + (PowerUpId)i);
            Assert.IsNotNull(row);
            AssertContainedHorizontally(
                row.Find("Name") as RectTransform);
            AssertContainedHorizontally(
                row.Find("Description") as RectTransform);
        }

        GameObject trainingPanel = GetPrivatePanel(training);
        Assert.IsNotNull(trainingPanel);
        AssertContainedHorizontally(
            trainingPanel.transform.Find("Title") as RectTransform);
    }

    [UnityTest]
    public IEnumerator SupplyBuyAndEquipRemainIndependentActions()
    {
        yield return null;
        yield return null;

        SavePreferenceSnapshot saveBefore = CaptureSavePreferences();
        int managerCoinsBefore = GameManager.Instance != null
            ? GameManager.Instance.TotalCoins : 0;
        try
        {
            var isolated = new EchoRunSaveData
            {
                totalCoins = 500,
                powerUpInventory = new[] { 1, 1, 0, 0 },
                selectedPowerUp = (int)PowerUpId.Magnet
            };
            InstallIsolatedSave(isolated);

            PowerUpShopUI shop = Object.FindObjectOfType<PowerUpShopUI>();
            Assert.IsNotNull(shop);
            typeof(PowerUpShopUI).GetMethod("Refresh",
                BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(
                    shop, null);
            GameObject panel = GetPrivatePanel(shop);
            Transform shield = panel.transform.Find("Item_Shield");
            Assert.IsNotNull(shield);
            UnityEngine.UI.Button buy = shield.Find("Buy")
                .GetComponent<UnityEngine.UI.Button>();
            UnityEngine.UI.Button equip = shield.Find("Equip")
                .GetComponent<UnityEngine.UI.Button>();
            PowerUpBalance definition = GameBalanceConfig.GetPowerUp(
                PowerUpId.Shield);

            Assert.IsTrue(buy.interactable);
            Assert.IsTrue(equip.interactable);
            Transform scoreBoost = panel.transform.Find("Item_ScoreBoost");
            Assert.IsFalse(scoreBoost.Find("Equip")
                .GetComponent<UnityEngine.UI.Button>().interactable);
            buy.onClick.Invoke();
            Assert.AreEqual(2, EchoRunSaveSystem.GetPowerUpCount(
                PowerUpId.Shield));
            Assert.AreEqual(PowerUpId.Magnet,
                EchoRunSaveSystem.GetSelectedPowerUp());
            Assert.AreEqual(500 - definition.cost,
                EchoRunSaveSystem.TotalCoins);

            equip.onClick.Invoke();
            Assert.AreEqual(PowerUpId.Shield,
                EchoRunSaveSystem.GetSelectedPowerUp());
            Assert.AreEqual(500 - definition.cost,
                EchoRunSaveSystem.TotalCoins);
        }
        finally
        {
            RestoreSavePreferences(saveBefore);
            if (GameManager.Instance != null)
                typeof(GameManager).GetField("<TotalCoins>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(
                        GameManager.Instance, managerCoinsBefore);
        }
    }

    [TestCase(PowerUpId.Shield)]
    [TestCase(PowerUpId.Magnet)]
    [TestCase(PowerUpId.ScoreBoost)]
    [TestCase(PowerUpId.TurboStart)]
    public void PurchaseEquipAndConsumeActivatesEachPowerUp(PowerUpId id)
    {
        SavePreferenceSnapshot saveBefore = CaptureSavePreferences();

        try
        {
            var isolated = new EchoRunSaveData
            {
                totalCoins = 200,
                powerUpInventory = new int[4],
                selectedPowerUp = -1
            };
            InstallIsolatedSave(isolated);

            PowerUpBalance definition = GameBalanceConfig.GetPowerUp(id);
            Assert.IsNotNull(definition);
            Assert.IsTrue(EchoRunSaveSystem.TryPurchasePowerUp(id, definition.cost));
            Assert.AreEqual(200 - definition.cost, EchoRunSaveSystem.TotalCoins);
            Assert.AreEqual(1, EchoRunSaveSystem.GetPowerUpCount(id));
            Assert.IsTrue(EchoRunSaveSystem.SelectPowerUp(id));

            PowerUpController controller = PowerUpController.Instance;
            Assert.IsNotNull(controller);
            controller.BeginRun();

            Assert.AreEqual(id, controller.ActivePowerUp);
            Assert.AreEqual(0, EchoRunSaveSystem.GetPowerUpCount(id));
            Assert.AreEqual(PowerUpId.None,
                EchoRunSaveSystem.GetSelectedPowerUp());

            if (id == PowerUpId.Shield)
                Assert.IsTrue(controller.TryAbsorbCollision());
            else if (id == PowerUpId.Magnet)
                Assert.IsTrue(controller.HasMagnet);
            else if (id == PowerUpId.ScoreBoost)
                Assert.Greater(controller.ScoreMultiplier, 1f);
            else if (id == PowerUpId.TurboStart)
                Assert.Greater(controller.GetTurboStartBonus(), 0f);
        }
        finally
        {
            if (PowerUpController.Instance != null)
            {
                typeof(PowerUpController).GetMethod("ClearActive",
                    BindingFlags.Instance | BindingFlags.NonPublic)?
                    .Invoke(PowerUpController.Instance, null);
            }
            RestoreSavePreferences(saveBefore);
        }
    }

    private sealed class SavePreferenceSnapshot
    {
        public readonly Dictionary<string, string> strings =
            new Dictionary<string, string>();
        public readonly Dictionary<string, int> ints =
            new Dictionary<string, int>();
        public readonly Dictionary<string, float> floats =
            new Dictionary<string, float>();
    }

    private static readonly string[] SaveStringKeys =
    {
        EchoRunSaveSystem.SaveKey,
        EchoRunSaveSystem.SaveSlotAKey,
        EchoRunSaveSystem.SaveSlotBKey,
        EchoRunSaveSystem.TelemetryKey,
        "AIShadowProfileV1"
    };

    private static readonly string[] SaveIntKeys =
    {
        EchoRunSaveSystem.ActiveSaveSlotKey,
        "HighScore",
        "TotalCoins",
        "TargetFrameRate",
        "AudioMuted",
        "CharacterPreset"
    };

    private static readonly string[] SaveFloatKeys =
    {
        "MasterVolume",
        "MusicVolume",
        "SfxVolume"
    };

    private static SavePreferenceSnapshot CaptureSavePreferences()
    {
        var snapshot = new SavePreferenceSnapshot();
        foreach (string key in SaveStringKeys)
            if (PlayerPrefs.HasKey(key))
                snapshot.strings[key] = PlayerPrefs.GetString(key);
        foreach (string key in SaveIntKeys)
            if (PlayerPrefs.HasKey(key))
                snapshot.ints[key] = PlayerPrefs.GetInt(key);
        foreach (string key in SaveFloatKeys)
            if (PlayerPrefs.HasKey(key))
                snapshot.floats[key] = PlayerPrefs.GetFloat(key);
        return snapshot;
    }

    private static void InstallIsolatedSave(EchoRunSaveData data)
    {
        ClearSavePreferences();
        PlayerPrefs.SetString(EchoRunSaveSystem.SaveKey,
            JsonUtility.ToJson(data));
        PlayerPrefs.SetInt("TotalCoins", data.totalCoins);
        PlayerPrefs.Save();
        ResetSaveSystemCache();
    }

    private static void RestoreSavePreferences(SavePreferenceSnapshot snapshot)
    {
        ClearSavePreferences();
        foreach (KeyValuePair<string, string> pair in snapshot.strings)
            PlayerPrefs.SetString(pair.Key, pair.Value);
        foreach (KeyValuePair<string, int> pair in snapshot.ints)
            PlayerPrefs.SetInt(pair.Key, pair.Value);
        foreach (KeyValuePair<string, float> pair in snapshot.floats)
            PlayerPrefs.SetFloat(pair.Key, pair.Value);
        PlayerPrefs.Save();
        ResetSaveSystemCache();
    }

    private static void ClearSavePreferences()
    {
        foreach (string key in SaveStringKeys) PlayerPrefs.DeleteKey(key);
        foreach (string key in SaveIntKeys) PlayerPrefs.DeleteKey(key);
        foreach (string key in SaveFloatKeys) PlayerPrefs.DeleteKey(key);
    }

    private static void ResetSaveSystemCache()
    {
        typeof(EchoRunSaveSystem).GetField("_data",
            BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
        typeof(EchoRunSaveSystem).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, false);
        typeof(EchoRunSaveSystem).GetField("_activeSlot",
            BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, -1);
        typeof(EchoRunSaveSystem).GetField("_generation",
            BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, 0L);
    }

    private static GameObject GetPrivatePanel(object component)
    {
        return GetPrivateObject(component, "_panel");
    }

    private static GameObject GetPrivateObject(object component, string field)
    {
        return (GameObject)component.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(component);
    }

    private static T GetPrivateField<T>(object component, string field)
        where T : class
    {
        FieldInfo info = component.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, "Missing private field: " + field);
        return info.GetValue(component) as T;
    }

    private static void AssertSoundReadout(Text readout, string expectedLabel)
    {
        Assert.IsNotNull(readout);
        Assert.IsTrue(readout.gameObject.activeInHierarchy);
        Assert.IsTrue(readout.enabled);
        StringAssert.StartsWith(expectedLabel, readout.text);
        Assert.Greater(readout.color.a, 0.9f);
        Assert.Greater(readout.preferredWidth, 0f);
        Assert.Greater(readout.cachedTextGenerator.vertexCount, 0);
    }

    private static void AssertButtonVisibleAndRaycastable(
        Button button, RectTransform safeArea)
    {
        Assert.IsNotNull(button);
        Assert.IsTrue(button.gameObject.activeInHierarchy);
        Assert.IsTrue(button.IsInteractable());
        Assert.IsNotNull(button.targetGraphic);
        Assert.IsTrue(button.targetGraphic.enabled);
        Assert.IsTrue(button.targetGraphic.raycastTarget);
        Assert.Greater(button.targetGraphic.canvasRenderer.GetAlpha(), 0.01f);
        AssertRectContained(button.transform as RectTransform, safeArea);

        Canvas canvas = button.GetComponentInParent<Canvas>();
        Assert.IsNotNull(canvas);
        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        Assert.IsNotNull(raycaster);
        Assert.IsNotNull(EventSystem.current);
        Canvas.ForceUpdateCanvases();
        RectTransform rect = button.transform as RectTransform;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(
            canvas.worldCamera, rect.TransformPoint(rect.rect.center));
        var eventData = new PointerEventData(EventSystem.current)
        {
            position = screenPoint
        };
        var hits = new List<RaycastResult>();
        raycaster.Raycast(eventData, hits);
        Assert.IsTrue(hits.Exists(hit =>
                hit.gameObject == button.gameObject
                || hit.gameObject.transform.IsChildOf(button.transform)),
            button.name + " is not reachable by a UI raycast at its center.");
    }

    private static void AssertRectContained(
        RectTransform child, RectTransform container)
    {
        Assert.IsNotNull(child);
        Assert.IsNotNull(container);
        Canvas.ForceUpdateCanvases();
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
            container, child);
        Rect rect = container.rect;
        const float tolerance = 0.5f;
        Assert.GreaterOrEqual(bounds.min.x, rect.xMin - tolerance,
            child.name + " extends beyond the left SafeArea edge.");
        Assert.LessOrEqual(bounds.max.x, rect.xMax + tolerance,
            child.name + " extends beyond the right SafeArea edge.");
        Assert.GreaterOrEqual(bounds.min.y, rect.yMin - tolerance,
            child.name + " extends below the SafeArea edge.");
        Assert.LessOrEqual(bounds.max.y, rect.yMax + tolerance,
            child.name + " extends above the SafeArea edge.");
    }

    private static void AssertContainedHorizontally(RectTransform child)
    {
        Assert.IsNotNull(child);
        RectTransform parent = child.parent as RectTransform;
        Assert.IsNotNull(parent);

        float left = parent.rect.xMin
                     + child.anchorMin.x * parent.rect.width
                     + child.anchoredPosition.x
                     - child.pivot.x * child.rect.width;
        float right = left + child.rect.width;
        Assert.GreaterOrEqual(left, parent.rect.xMin - 0.01f,
            child.name + " extends beyond the left panel edge.");
        Assert.LessOrEqual(right, parent.rect.xMax + 0.01f,
            child.name + " extends beyond the right panel edge.");
    }
}
