using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ColdWhiteFortressSampleTests
{
    private const string PrefabRoot =
        "Assets/Resources/Art/Environment/ColdWhiteMemoryFortress/";
    private const string ScenePath = "Assets/Scenes/SampleScene.scene";

    [Test]
    public void SampleLaunchArgumentRequiresExplicitOptIn()
    {
        Assert.IsFalse(TrackManager.HasColdWhiteFortressSampleArgument(null));
        Assert.IsFalse(TrackManager.HasColdWhiteFortressSampleArgument(
            new[] { "-echo-single-contract-validation" }));
        Assert.IsTrue(TrackManager.HasColdWhiteFortressSampleArgument(
            new[] { "-ECHO-COLD-WHITE-FORTRESS-SAMPLE" }));
        Assert.IsTrue(TrackManager.HasColdWhiteFortressSampleArgument(
            new[] { "-ECHO-COLD-WHITE-FORTRESS-SAMPLE-LEFT" }));
        Assert.IsTrue(TrackManager.HasColdWhiteFortressLeftSampleArgument(
            new[] { "-echo-cold-white-fortress-sample-left" }));
    }

    [Test]
    public void LeftSampleMirrorsOnlyTheTurnDirection()
    {
        Assert.IsTrue(TrackManager.TryGetColdWhiteFortressSampleSegment(
            20f, true, true, out TrackSegmentType turn));
        Assert.AreEqual(TrackSegmentType.TurnLeft, turn);
        Assert.IsTrue(TrackManager.TryGetColdWhiteFortressSampleSegment(
            40f, true, true, out TrackSegmentType straight));
        Assert.AreEqual(TrackSegmentType.Straight, straight);
    }

    [Test]
    public void SamplePlanUsesThreeStandardSegmentsThenReleasesControl()
    {
        Assert.AreEqual(60f, TrackManager.ColdWhiteFortressSampleLength,
            0.001f);
        AssertSampleSegment(0f, TrackSegmentType.Straight);
        AssertSampleSegment(19.999f, TrackSegmentType.Straight);
        AssertSampleSegment(20f, TrackSegmentType.TurnRight);
        AssertSampleSegment(39.999f, TrackSegmentType.TurnRight);
        AssertSampleSegment(40f, TrackSegmentType.Straight);
        AssertSampleSegment(59.999f, TrackSegmentType.Straight);

        Assert.IsFalse(TrackManager.TryGetColdWhiteFortressSampleSegment(
            -0.001f, true, out _));
        Assert.IsFalse(TrackManager.TryGetColdWhiteFortressSampleSegment(
            60f, true, out _));
        Assert.IsFalse(TrackManager.TryGetColdWhiteFortressSampleSegment(
            20f, false, out _),
            "Shipping track planning must remain unchanged without the QA flag.");
    }

    [Test]
    public void SampleVisualVariantsMatchTheZeroAndFortyMetreStraights()
    {
        Assert.AreEqual(0,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.Straight, 0f, true));
        Assert.AreEqual(0,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.TurnRight, 20f, true));
        Assert.AreEqual(2,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.Straight, 40f, true));
        Assert.AreEqual(1,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.Straight, 60f, true));
        Assert.AreEqual(0,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.Straight, 80f, true));
        Assert.AreEqual(-1,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.Straight, 100f, true));
        Assert.AreEqual(-1,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.Straight, 20f, true));
        Assert.AreEqual(-1,
            TrackManager.ColdWhiteFortressVisualVariantIndex(
                TrackSegmentType.Straight, 0f, false));
    }

    [Test]
    public void SampleInitialOffsetPlacesTheFirstCornerAtThirtyMetres()
    {
        Vector3 forward = new Vector3(0f, 0f, 4f);
        Vector3 offset = TrackManager.ColdWhiteFortressInitialSpawnOffset(
            true, TrackGeometryStandards.StandardSegmentLength, forward);

        Assert.AreEqual(new Vector3(0f, 0f, 10f), offset);
        Assert.AreEqual(Vector3.zero,
            TrackManager.ColdWhiteFortressInitialSpawnOffset(false,
                TrackGeometryStandards.StandardSegmentLength, forward));
    }

    [Test]
    public void VariantSetHonorsAValidExplicitSelectionAndRemainsExclusive()
    {
        GameObject root = new GameObject("FortressVariantSet_Test");
        try
        {
            EchoEnvironmentVariantSet set =
                root.AddComponent<EchoEnvironmentVariantSet>();
            GameObject open = new GameObject("Open");
            GameObject exit = new GameObject("Exit");
            GameObject high = new GameObject("HighQualityOnly");
            open.transform.SetParent(root.transform, false);
            exit.transform.SetParent(root.transform, false);
            high.transform.SetParent(root.transform, false);
            set.Initialize(new[] { open, exit }, high);

            set.SelectFor(424242, 0f, 1);

            Assert.AreEqual(1, set.ActiveVariantIndex);
            Assert.IsFalse(open.activeSelf);
            Assert.IsTrue(exit.activeSelf);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InstalledSampleCompositesAreVisualOnlyFormalPrefabs()
    {
        GameObject first = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabRoot + "Sample_Straight_00.prefab");
        if (first == null)
            Assert.Ignore("Fortress FBXs have not been installed yet.");

        AssertComposite(first, "RoadVisual", "RightArchiveTower", false,
            false);
        Assert.IsNull(FindDescendant(first.transform, "OverhangLead"),
            "The first 0-15 m view must remain open.");
        GameObject turn = LoadComposite("Sample_TurnRight_20");
        AssertComposite(turn,
            "RoadVisual", "OuterMemorySilo", true, false);
        Assert.NotNull(FindDescendant(turn.transform, "OverhangTail"),
            "The 15-30 m section keeps the authored shadow beat.");
        GameObject final = LoadComposite("Sample_Straight_40");
        AssertComposite(final,
            "RoadVisual", "FinalScanRing", false, false);
        Assert.IsNull(FindDescendant(final.transform, "BrokenOverpass"),
            "The 42-60 m release section must stay open around the final gate.");
    }

    [Test]
    public void TurnMechanicalDressingStaysOutsideTheRoadEnvelope()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabRoot + "Sample_TurnRight_20.prefab");
        if (prefab == null)
            Assert.Ignore("Fortress FBXs have not been installed yet.");

        GameObject instance = Object.Instantiate(prefab);
        try
        {
            Bounds road = RendererBounds(FindDescendant(instance.transform,
                "RoadVisual").gameObject);
            Bounds facility = RendererBounds(FindDescendant(instance.transform,
                "InnerMechanicalFacility").gameObject);
            Assert.GreaterOrEqual(facility.min.z, road.max.z + 0.75f,
                "Visual-only dressing must not read as an obstacle in a lane.");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void TurnCantileverPiersStayOutsideTheExitRoadEnvelope()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabRoot + "Sample_TurnRight_20.prefab");
        if (prefab == null)
            Assert.Ignore("Fortress FBXs have not been installed yet.");

        GameObject instance = Object.Instantiate(prefab);
        try
        {
            GameObject overhang = FindDescendant(instance.transform,
                "OverhangTail").gameObject;
            Renderer concretePiers = FindRendererUsingMaterial(overhang,
                "ColdWhiteFortress_Concrete");
            Assert.NotNull(concretePiers,
                "The cantilever concrete batch must contain its two piers.");
            float exitRoadInnerEdge = TrackGeometryStandards
                .StandardSegmentLength * 0.5f
                - TrackGeometryStandards.VisualRoadHalfWidth;
            Assert.LessOrEqual(concretePiers.bounds.max.z,
                exitRoadInnerEdge - 0.75f,
                "A cantilever pier enters the right-turn exit road envelope.");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void TurnLandmarksClearTheRearCameraShellOnBothSides()
    {
        GameObject rightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabRoot + "Sample_TurnRight_20.prefab");
        GameObject leftDistrictPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Art/Environment/EchoMegacityDistrictA.prefab");
        if (rightPrefab == null || leftDistrictPrefab == null)
            Assert.Ignore("Turn environment prefabs have not been installed yet.");

        Vector3 offset = WorldStyler.GetCameraOffset(false);
        float cornerZ = TrackGeometryStandards.StandardSegmentLength * 0.5f;

        GameObject right = Object.Instantiate(rightPrefab);
        GameObject left = Object.Instantiate(leftDistrictPrefab);
        try
        {
            Bounds silo = RendererBounds(FindDescendant(right.transform,
                "OuterMemorySilo").gameObject);
            Vector3 rightCamera = new Vector3(offset.z, offset.y, cornerZ);
            Assert.GreaterOrEqual(HorizontalDistance(rightCamera, silo),
                TrackGeometryStandards.TurnCameraShellClearance,
                "The right-turn memory silo intersects the camera shell.");

            left.transform.position = new Vector3(
                TrackGeometryStandards.TurnNearDecorationCenterOffset,
                -0.72f, cornerZ);
            left.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            left.transform.localScale = Vector3.one * 0.92f;
            Bounds district = RendererBounds(left);
            Vector3 leftCamera = new Vector3(-offset.z, offset.y, cornerZ);
            Assert.GreaterOrEqual(HorizontalDistance(leftCamera, district),
                TrackGeometryStandards.TurnCameraShellClearance,
                "The mirrored left-turn district intersects the camera shell.");
        }
        finally
        {
            Object.DestroyImmediate(right);
            Object.DestroyImmediate(left);
        }
    }

    [Test]
    public void InstalledSceneBindsTheFormalTrackPrefabs()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + "Sample_Straight_00.prefab") == null)
            Assert.Ignore("Fortress FBXs have not been installed yet.");

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
            scene = EditorSceneManager.OpenScene(ScenePath,
                OpenSceneMode.Additive);

        try
        {
            TrackManager manager = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length && manager == null; index++)
                manager = roots[index].GetComponentInChildren<TrackManager>(true);

            Assert.NotNull(manager, "SampleScene must serialize TrackManager.");
            Assert.NotNull(manager.trackSegmentPrefab);
            Assert.NotNull(manager.turnLeftPrefab);
            Assert.NotNull(manager.turnRightPrefab);
            Assert.NotNull(manager.coinPrefab);
            Assert.NotNull(manager.obstaclePrefabs);
            Assert.AreEqual(3, manager.obstaclePrefabs.Length);
            Assert.AreEqual(TrackGeometryStandards.StandardSegmentLength,
                manager.segmentLength, 0.001f);
            Assert.AreEqual(TrackGeometryStandards.LaneSpacing,
                manager.laneDistance, 0.001f);
        }
        finally
        {
            if (openedForTest)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void InstalledTrackPrefabsHideOnlyLegacyRoadRenderers()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabRoot + "Sample_Straight_00.prefab") == null)
            Assert.Ignore("Fortress FBXs have not been installed yet.");

        GameObject straight = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/TrackSegment.prefab");
        GameObject right = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/TurnSegment_Right.prefab");
        GameObject left = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/TurnSegment_Left.prefab");
        Assert.NotNull(straight);
        Assert.NotNull(right);
        Assert.NotNull(left);

        AssertLegacyRendererHidden(straight.transform.Find("GroundPlane"),
            true);
        AssertLegacyRendererHidden(straight.transform.Find("LaneLine_L"),
            false);
        AssertLegacyRendererHidden(straight.transform.Find("LaneLine_R"),
            false);
        for (int lane = 0; lane < 3; lane++)
            Assert.NotNull(straight.transform.Find("Lane_" + lane));
        int seamCount = 0;
        foreach (Transform child in straight.transform)
        {
            if (child.name.StartsWith("DataSeam"))
            {
                seamCount++;
                AssertLegacyRendererHidden(child, false);
            }
        }
        Assert.Greater(seamCount, 0);

        AssertLegacyRendererHidden(right.transform.Find("EntryStrip"), true);
        AssertLegacyRendererHidden(right.transform.Find("ExitStrip"), true);
        AssertLegacyRendererHidden(right.transform.Find(
            TrackManager.TurnInnerCornerCapName), false);
        AssertLegacyRendererHidden(right.transform.Find("LaneLine_L"), false);
        AssertLegacyRendererHidden(right.transform.Find("LaneLine_R"), false);

        Renderer leftEntry = left.transform.Find("EntryStrip")
            .GetComponent<Renderer>();
        Assert.IsTrue(leftEntry.enabled,
            "TurnLeft has no authored fortress road and keeps its legacy visual.");
        AssertTurnCornerSupport(left, -1, true);
        AssertTurnCornerSupport(right, 1, false);
    }

    private static void AssertSampleSegment(float routeDistance,
        TrackSegmentType expected)
    {
        Assert.IsTrue(TrackManager.TryGetColdWhiteFortressSampleSegment(
            routeDistance, true, out TrackSegmentType actual));
        Assert.AreEqual(expected, actual);
    }

    private static GameObject LoadComposite(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabRoot + name + ".prefab");
        Assert.NotNull(prefab, name);
        return prefab;
    }

    private static void AssertLegacyRendererHidden(Transform visual,
        bool requiresCollider)
    {
        Assert.NotNull(visual);
        Assert.IsTrue(visual.gameObject.activeSelf,
            visual.name + " object remains active for its non-visual contract.");
        Renderer renderer = visual.GetComponent<Renderer>();
        Assert.NotNull(renderer, visual.name);
        Assert.IsFalse(renderer.enabled,
            visual.name + " must not compete with the authored road skin.");
        if (requiresCollider)
        {
            Collider collider = visual.GetComponent<Collider>();
            Assert.NotNull(collider, visual.name + " collision must remain formal.");
            Assert.IsTrue(collider.enabled);
        }
    }

    private static void AssertComposite(GameObject prefab,
        string requiredA, string requiredB, bool rightTurn,
        bool validateOverhangEnvelope)
    {
        Assert.NotNull(prefab);
        Assert.AreEqual(Vector3.one, prefab.transform.localScale);
        Assert.AreEqual(0,
            prefab.GetComponentsInChildren<Collider>(true).Length);
        GameObject instance = Object.Instantiate(prefab);
        try
        {
            Transform road = FindDescendant(instance.transform, requiredA);
            Transform landmark = FindDescendant(instance.transform, requiredB);
            Assert.NotNull(road, requiredA);
            Assert.NotNull(landmark, requiredB);
            Assert.Greater(
                instance.GetComponentsInChildren<Renderer>(true).Length, 0);
            AssertRoadBounds(road.gameObject, rightTurn);
            AssertMaterialBoundary(instance);

            if (validateOverhangEnvelope)
            {
                Bounds overhang = RendererBounds(landmark.gameObject);
                float halfLength = TrackGeometryStandards
                    .StandardSegmentLength * 0.5f;
                Assert.GreaterOrEqual(overhang.min.z, -halfLength - 0.20f);
                Assert.LessOrEqual(overhang.max.z, halfLength + 0.20f);
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void AssertRoadBounds(GameObject road, bool rightTurn)
    {
        Bounds bounds = RendererBounds(road);
        Renderer graphite = FindGraphiteRenderer(road);
        Assert.AreEqual(TrackGeometryStandards.AuthoredRoadSurfaceTopY,
            graphite.bounds.max.y, 0.04f,
            "The authored graphite sits at the formal collision surface.");
        Assert.LessOrEqual(bounds.max.y, 0.36f);
        Assert.GreaterOrEqual(bounds.min.y, -0.50f);
        if (!rightTurn)
        {
            float halfLength = TrackGeometryStandards.StandardSegmentLength
                               * 0.5f;
            Assert.AreEqual(0f, bounds.center.x, 0.20f);
            Assert.AreEqual(0f, bounds.center.z, 0.20f);
            Assert.AreEqual(-halfLength, bounds.min.z, 0.20f);
            Assert.AreEqual(halfLength, bounds.max.z, 0.20f);
            Assert.AreEqual(TrackGeometryStandards.VisualRoadWidth,
                bounds.size.x, 0.20f);
            return;
        }

        float segmentLength = TrackGeometryStandards.StandardSegmentLength;
        Assert.AreEqual(-TrackGeometryStandards.VisualRoadHalfWidth,
            bounds.min.x, 0.20f);
        Assert.AreEqual(segmentLength * 0.5f,
            bounds.max.x, 0.20f);
        Assert.AreEqual(0f, bounds.min.z, 0.20f);
        Assert.AreEqual(TrackGeometryStandards.TurnEntrySurfaceLength(
            segmentLength), bounds.max.z, 0.20f);
        AssertGraphiteCoversInnerCorner(road);
    }

    private static void AssertTurnCornerSupport(GameObject prefab,
        int turnDirection, bool capMustBeVisible)
    {
        Transform cap = prefab.transform.Find(
            TrackManager.TurnInnerCornerCapName);
        Assert.NotNull(cap);
        Renderer capRenderer = cap.GetComponent<Renderer>();
        Assert.NotNull(capRenderer);
        Assert.AreEqual(capMustBeVisible, capRenderer.enabled);
        Assert.IsNull(cap.GetComponent<Collider>());

        Vector3 expectedCap = TrackGeometryStandards.TurnInnerCornerCenter(
            TrackGeometryStandards.StandardSegmentLength, turnDirection);
        Assert.AreEqual(expectedCap.x, cap.localPosition.x, 0.001f);
        Assert.AreEqual(expectedCap.z, cap.localPosition.z, 0.001f);

        Transform bridge = prefab.transform.Find(
            TrackManager.TurnWalkableBridgeName);
        Assert.NotNull(bridge);
        Assert.IsNull(bridge.GetComponent<Renderer>());
        BoxCollider bridgeCollider = bridge.GetComponent<BoxCollider>();
        Assert.NotNull(bridgeCollider);
        Assert.IsTrue(bridgeCollider.enabled);
        Assert.AreEqual(TrackGeometryStandards.TurnWalkableBridgeWidth,
            bridgeCollider.size.x, 0.001f);
        Assert.AreEqual(TrackGeometryStandards.WalkableWidth,
            bridgeCollider.size.z, 0.001f);
    }

    private static void AssertGraphiteCoversInnerCorner(GameObject road)
    {
        Renderer graphite = FindGraphiteRenderer(road);
        MeshFilter filter = graphite.GetComponent<MeshFilter>();
        Assert.NotNull(filter);
        Assert.NotNull(filter.sharedMesh);

        Transform coordinateRoot = road.transform.parent;
        if (coordinateRoot == null) coordinateRoot = road.transform;
        Vector3 localPoint = TrackGeometryStandards.TurnInnerCornerCenter(
            TrackGeometryStandards.StandardSegmentLength, 1);
        Vector3 worldPoint = coordinateRoot.TransformPoint(localPoint);
        Assert.IsTrue(MeshCoversPointXZ(filter, worldPoint),
            "The formal right-turn graphite mesh must cover the old 4.5 x 4.5 metre hole.");
    }

    private static bool MeshCoversPointXZ(MeshFilter filter,
        Vector3 worldPoint)
    {
        Mesh mesh = filter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        for (int index = 0; index + 2 < triangles.Length; index += 3)
        {
            Vector3 a = filter.transform.TransformPoint(
                vertices[triangles[index]]);
            Vector3 b = filter.transform.TransformPoint(
                vertices[triangles[index + 1]]);
            Vector3 c = filter.transform.TransformPoint(
                vertices[triangles[index + 2]]);
            if (PointInTriangleXZ(worldPoint, a, b, c)) return true;
        }
        return false;
    }

    private static bool PointInTriangleXZ(Vector3 point, Vector3 a,
        Vector3 b, Vector3 c)
    {
        if (Mathf.Abs(CrossXZ(a, b, c)) < 0.00001f) return false;
        float first = CrossXZ(point, a, b);
        float second = CrossXZ(point, b, c);
        float third = CrossXZ(point, c, a);
        bool hasNegative = first < -0.0001f || second < -0.0001f
                           || third < -0.0001f;
        bool hasPositive = first > 0.0001f || second > 0.0001f
                           || third > 0.0001f;
        return !(hasNegative && hasPositive);
    }

    private static float CrossXZ(Vector3 a, Vector3 b, Vector3 c)
    {
        return (b.x - a.x) * (c.z - a.z)
               - (b.z - a.z) * (c.x - a.x);
    }

    private static void AssertMaterialBoundary(GameObject instance)
    {
        bool foundPhaseAccent = false;
        bool foundNeutral = false;
        bool foundFormalRoad = false;
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Material[] materials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material = materials[materialIndex];
                Assert.NotNull(material);
                if (renderers[rendererIndex].name.IndexOf("RoadGraphite",
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Assert.AreEqual("EchoRoad", material.name,
                        "The opaque authored road must retain phase road MPBs.");
                    foundFormalRoad = true;
                    continue;
                }
                StringAssert.StartsWith("ColdWhiteFortress_", material.name,
                    "The sample must not inherit a phase-tinted legacy palette.");
                if (material.name == WorldStyler
                        .ColdWhiteFortressPhaseAccentMaterialName)
                    foundPhaseAccent = true;
                else
                    foundNeutral = true;
            }
        }

        Assert.IsTrue(foundPhaseAccent,
            "MF_PhaseEmitter must map to the dedicated phase accent.");
        Assert.IsTrue(foundNeutral,
            "Architecture and road inset/edge materials stay neutral.");
        Assert.IsTrue(foundFormalRoad,
            "MF_RoadGraphite must use the formal EchoRoad material.");
    }

    private static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Assert.Greater(renderers.Length, 0, root.name);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static float HorizontalDistance(Vector3 point, Bounds bounds)
    {
        float x = Mathf.Max(bounds.min.x - point.x, 0f,
            point.x - bounds.max.x);
        float z = Mathf.Max(bounds.min.z - point.z, 0f,
            point.z - bounds.max.z);
        return Mathf.Sqrt(x * x + z * z);
    }

    private static Renderer FindGraphiteRenderer(GameObject road)
    {
        Renderer[] renderers = road.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            if (renderers[index].name.IndexOf("RoadGraphite",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
                return renderers[index];
        }
        Assert.Fail(road.name + " is missing MF_RoadGraphite.");
        return null;
    }

    private static Renderer FindRendererUsingMaterial(GameObject root,
        string materialName)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Material[] materials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material != null && material.name == materialName)
                    return renderers[rendererIndex];
            }
        }
        return null;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
            if (descendants[index].name == name) return descendants[index];
        return null;
    }
}
