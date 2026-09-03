using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class InstallColdWhiteMemoryFortress
{
    public const string ModelRoot =
        "Assets/Art/Environment/ColdWhiteMemoryFortress/Models/";
    public const string PrefabRoot =
        "Assets/Resources/Art/Environment/ColdWhiteMemoryFortress/";
    public const string PhaseAccentMaterialPath =
        PrefabRoot + "Materials/ColdWhiteFortress_PhaseAccent.mat";
    public const string ScenePath = "Assets/Scenes/SampleScene.scene";

    private const string MaterialRoot = PrefabRoot + "Materials/";
    private const string RoadMaterialPath =
        "Assets/Resources/Materials/EchoRoad.mat";
    private const float RoadBoundsTolerance = 0.20f;
    private static readonly string[] ModelNames =
    {
        "CantileverSlab_A",
        "MemorySilo_A",
        "ArchiveTower_A",
        "ScanRing_A",
        "BrokenOverpass_A",
        "MechanicalFacility_A",
        "MechanicalFacility_B",
        "RoadStraight_A",
        "RoadTurnRight_A"
    };

    private struct Placement
    {
        public string asset;
        public string instance;
        public Vector3 position;
        public Vector3 euler;

        public Placement(string asset, string instance, Vector3 position,
            Vector3 euler)
        {
            this.asset = asset;
            this.instance = instance;
            this.position = position;
            this.euler = euler;
        }
    }

    private static Material _concrete;
    private static Material _ceramic;
    private static Material _metal;
    private static Material _void;
    private static Material _phaseAccent;
    private static Material _road;
    private static Material _roadInset;

    [MenuItem("Tools/EchoRun/Art/Install Cold White Memory Fortress")]
    public static void InstallAndBake()
    {
        InstallAssets();

        Scene scene = EditorSceneManager.OpenScene(
            ScenePath, OpenSceneMode.Single);
        BuildScene.FixTurnRoadJoins();
        BuildScene.EnsureFormalTrackManagerBindings();
        BuildScene.BakeEnvironmentVariants();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException(
                "Could not save SampleScene after installing the fortress.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateInstalledAssets();
        Debug.Log("COLD_WHITE_MEMORY_FORTRESS_INSTALL_OK atoms=9 composites=3");
    }

    public static void InstallAssets()
    {
        EnsureFolder(PrefabRoot.TrimEnd('/'));
        EnsureFolder(MaterialRoot.TrimEnd('/'));
        CreateOrUpdateMaterials();

        for (int index = 0; index < ModelNames.Length; index++)
        {
            string modelName = ModelNames[index];
            string modelPath = ModelRoot + modelName + ".fbx";
            ConfigureModelImporter(modelPath);
            CreateAtomicPrefab(modelName, modelPath);
        }

        CreateCompositePrefabs();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static void ValidateInstalledAssets()
    {
        for (int index = 0; index < ModelNames.Length; index++)
        {
            GameObject atom = AssetDatabase.LoadAssetAtPath<GameObject>(
                AtomicPrefabPath(ModelNames[index]));
            if (atom == null)
                throw new InvalidOperationException(
                    "Missing fortress atom: " + ModelNames[index]);
            ValidateColliderFree(atom, ModelNames[index]);
        }

        ValidateCompositeAsset("Sample_Straight_00", "RoadStraight_A");
        ValidateCompositeAsset("Sample_TurnRight_20", "RoadTurnRight_A");
        ValidateCompositeAsset("Sample_Straight_40", "RoadStraight_A");
        ValidateFormalTrackVisualIsolation();

        TrackManager manager = UnityEngine.Object.FindObjectOfType<TrackManager>();
        if (manager == null || manager.trackSegmentPrefab == null
            || manager.turnLeftPrefab == null || manager.turnRightPrefab == null
            || manager.coinPrefab == null || manager.obstaclePrefabs == null
            || manager.obstaclePrefabs.Length < 3)
        {
            throw new InvalidOperationException(
                "Production TrackManager is not bound to the formal prefab set.");
        }
    }

    private static void ConfigureModelImporter(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
            AssetDatabase.ImportAsset(path,
                ImportAssetOptions.ForceSynchronousImport
                | ImportAssetOptions.ForceUpdate);

        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException("Missing authored model: " + path);

        importer.globalScale = 1f;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.meshCompression = ModelImporterMeshCompression.Medium;
        importer.isReadable = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
    }

    private static void CreateAtomicPrefab(string modelName, string modelPath)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
            throw new InvalidOperationException("Model import failed: " + modelPath);

        GameObject root = new GameObject(modelName);
        try
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (visual == null)
                throw new InvalidOperationException(
                    "Could not instantiate authored model: " + modelPath);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            visual.transform.localScale = Vector3.one;

            RemoveColliders(root);
            RemapMaterials(root, modelName);
            ValidateAtomic(root, modelName);
            PrefabUtility.SaveAsPrefabAsset(root, AtomicPrefabPath(modelName));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateCompositePrefabs()
    {
        CreateComposite("Sample_Straight_00", new[]
        {
            Place("RoadStraight_A", "RoadVisual", new Vector3(0f, 0.025f, 0f)),
            Place("MechanicalFacility_A", "LeftFacility",
                new Vector3(-13.0f, 0f, -3.5f), new Vector3(0f, 90f, 0f)),
            Place("ArchiveTower_A", "RightArchiveTower",
                new Vector3(13.8f, 0f, 1.5f), new Vector3(0f, -90f, 0f))
        });

        CreateComposite("Sample_TurnRight_20", new[]
        {
            Place("RoadTurnRight_A", "RoadVisual",
                new Vector3(0f, 0.025f, 0f)),
            Place("CantileverSlab_A", "OverhangTail",
                new Vector3(0f, 0f, -0.5f)),
            Place("MemorySilo_A", "OuterMemorySilo",
                new Vector3(-TrackGeometryStandards
                    .TurnNearDecorationCenterOffset, 0f, 10.2f),
                new Vector3(0f, 18f, 0f)),
            Place("MechanicalFacility_B", "InnerMechanicalFacility",
                new Vector3(12.5f, 0f, 19.0f), new Vector3(0f, -90f, 0f))
        });

        CreateComposite("Sample_Straight_40", new[]
        {
            Place("RoadStraight_A", "RoadVisual", new Vector3(0f, 0.025f, 0f)),
            Place("MechanicalFacility_B", "LeftFacility",
                new Vector3(-13.2f, 0f, -3.0f), new Vector3(0f, 90f, 0f)),
            Place("ArchiveTower_A", "RightArchiveTower",
                new Vector3(14.2f, 0f, 2.5f), new Vector3(0f, -78f, 0f)),
            Place("ScanRing_A", "FinalScanRing", new Vector3(0f, 0f, 6.5f))
        });
    }

    private static Placement Place(string asset, string instance,
        Vector3 position, Vector3 euler = default)
    {
        return new Placement(asset, instance, position, euler);
    }

    private static void CreateComposite(string name, Placement[] placements)
    {
        GameObject root = new GameObject(name);
        try
        {
            for (int index = 0; index < placements.Length; index++)
            {
                Placement placement = placements[index];
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AtomicPrefabPath(placement.asset));
                if (prefab == null)
                    throw new InvalidOperationException(
                        "Missing fortress atom for composite: " + placement.asset);

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    prefab, root.transform);
                instance.name = placement.instance;
                instance.transform.localPosition = placement.position;
                instance.transform.localRotation = Quaternion.Euler(
                    placement.euler);
                instance.transform.localScale = Vector3.one;
                if (placement.asset == "RoadStraight_A")
                    AlignStraightRoadVisual(instance);
                else if (placement.asset == "RoadTurnRight_A")
                    AlignRightTurnRoadVisual(instance);
            }

            ValidateColliderFree(root, name);
            if (root.GetComponentsInChildren<Renderer>(true).Length == 0)
                throw new InvalidOperationException(
                    "Fortress composite has no renderers: " + name);
            PrefabUtility.SaveAsPrefabAsset(root, CompositePrefabPath(name));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CreateOrUpdateMaterials()
    {
        _concrete = CreateOrUpdateMaterial("ColdWhiteFortress_Concrete",
            new Color(0.80f, 0.84f, 0.85f), Color.black, 0.03f, 0.28f);
        _ceramic = CreateOrUpdateMaterial("ColdWhiteFortress_Ceramic",
            new Color(0.94f, 0.96f, 0.96f), Color.black, 0.08f, 0.62f);
        _metal = CreateOrUpdateMaterial("ColdWhiteFortress_Metal",
            new Color(0.045f, 0.058f, 0.064f), Color.black, 0.68f, 0.27f);
        _void = CreateOrUpdateMaterial("ColdWhiteFortress_Void",
            new Color(0.018f, 0.024f, 0.030f), Color.black, 0.18f, 0.18f);
        _phaseAccent = CreateOrUpdateMaterial(
            WorldStyler.ColdWhiteFortressPhaseAccentMaterialName,
            new Color(0.16f, 0.72f, 0.90f),
            new Color(0.04f, 0.38f, 0.62f), 0.18f, 0.66f);
        _roadInset = CreateOrUpdateMaterial(
            "ColdWhiteFortress_RoadInset",
            new Color(0.16f, 0.18f, 0.19f), Color.black, 0.20f, 0.44f);
        _road = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
        if (_road == null)
            throw new InvalidOperationException(
                "Missing formal road material: " + RoadMaterialPath);
    }

    private static Material CreateOrUpdateMaterial(string name, Color color,
        Color emission, float metallic, float smoothness)
    {
        string path = MaterialRoot + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = GraphicsSettings.currentRenderPipeline != null
            ? Shader.Find("Universal Render Pipeline/Lit")
            : Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Mobile/Diffuse");
        if (shader == null)
            throw new InvalidOperationException(
                "No supported shader is available for fortress materials.");

        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", smoothness);
        if (material.HasProperty("_EmissionColor"))
        {
            if (emission.maxColorComponent > 0f)
                material.EnableKeyword("_EMISSION");
            else
                material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void RemapMaterials(GameObject root, string modelName)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException(
                "Fortress FBX contains no renderers: " + modelName);

        for (int rendererIndex = 0; rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length;
                 materialIndex++)
            {
                string sourceName = materials[materialIndex] != null
                    ? materials[materialIndex].name : "";
                materials[materialIndex] = ResolveMaterial(
                    sourceName, modelName);
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        }
    }

    private static Material ResolveMaterial(string sourceName,
        string modelName)
    {
        if (Contains(sourceName, "Phase")
            || Contains(sourceName, "Signal")
            || Contains(sourceName, "Emitter")
            || Contains(sourceName, "Emission"))
            return _phaseAccent;
        if (Contains(sourceName, "RoadEdge")) return _ceramic;
        if (Contains(sourceName, "RoadInset")) return _roadInset;
        if (Contains(sourceName, "RoadGraphite")
            || Contains(sourceName, "Graphite"))
            return _road;
        if (Contains(sourceName, "Ceramic")) return _ceramic;
        if (Contains(sourceName, "Metal")) return _metal;
        if (Contains(sourceName, "Void") || Contains(sourceName, "Black")
            || Contains(sourceName, "Depth"))
            return _void;
        if (modelName.StartsWith("Road", StringComparison.OrdinalIgnoreCase))
            return _road;
        return _concrete;
    }

    private static bool Contains(string value, string expected)
    {
        return value.IndexOf(expected,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void RemoveColliders(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int index = colliders.Length - 1; index >= 0; index--)
            UnityEngine.Object.DestroyImmediate(colliders[index]);
    }

    private static void ValidateAtomic(GameObject root, string modelName)
    {
        ValidateColliderFree(root, modelName);
        if (root.transform.localScale != Vector3.one)
            throw new InvalidOperationException(
                "Fortress atom root scale changed: " + modelName);

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0 || renderers.Length > 24)
            throw new InvalidOperationException(
                "Unexpected fortress renderer count for " + modelName
                + ": " + renderers.Length);

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        float maximumExtent = Mathf.Max(bounds.size.x,
            Mathf.Max(bounds.size.y, bounds.size.z));
        if (maximumExtent < 0.25f || maximumExtent > 160f)
            throw new InvalidOperationException(
                "Unexpected fortress bounds for " + modelName + ": "
                + bounds.size.ToString("F3"));

        if (modelName == "RoadStraight_A"
            && (bounds.size.x < 10f || bounds.size.x > 12f
                || bounds.size.z < 19f || bounds.size.z > 21f))
        {
            throw new InvalidOperationException(
                "RoadStraight_A must preserve the 11 x 20 metre visual envelope: "
                + bounds.size.ToString("F3"));
        }
    }

    private static void ValidateCompositeAsset(string name,
        string requiredRoadAtom)
    {
        string path = CompositePrefabPath(name);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            throw new InvalidOperationException(
                "Missing fortress composite: " + name);

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            ValidateColliderFree(root, name);
            Transform road = FindDescendant(root.transform, "RoadVisual");
            if (road == null)
            {
                throw new InvalidOperationException(
                    name + " is missing its formal " + requiredRoadAtom
                    + " road visual.");
            }

            if (requiredRoadAtom == "RoadTurnRight_A")
                ValidateRightTurnRoadBounds(road.gameObject, name);
            else
                ValidateStraightRoadBounds(road.gameObject, name);
            ValidateFormalRoadMaterial(road.gameObject, name);

            Transform overhang = FindDescendant(root.transform,
                "OverhangLead");
            if (overhang != null)
                ValidateFirstSegmentOverhang(overhang.gameObject);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ValidateFormalTrackVisualIsolation()
    {
        GameObject straight = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/TrackSegment.prefab");
        GameObject right = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/TurnSegment_Right.prefab");
        GameObject left = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/TurnSegment_Left.prefab");
        ValidateLegacyRendererHidden(straight, "GroundPlane", true);
        ValidateLegacyRendererHidden(straight, "LaneLine_L", false);
        ValidateLegacyRendererHidden(straight, "LaneLine_R", false);
        int seamCount = 0;
        for (int index = 0; index < straight.transform.childCount; index++)
        {
            Transform child = straight.transform.GetChild(index);
            if (!child.name.StartsWith("DataSeam",
                    StringComparison.Ordinal)) continue;
            seamCount++;
            Renderer renderer = child.GetComponent<Renderer>();
            if (renderer == null || renderer.enabled)
                throw new InvalidOperationException(
                    "All legacy DataSeam renderers must be hidden.");
        }
        if (seamCount == 0)
            throw new InvalidOperationException(
                "Formal straight prefab lost its legacy DataSeam objects.");
        ValidateLegacyRendererHidden(right, "EntryStrip", true);
        ValidateLegacyRendererHidden(right, "ExitStrip", true);
        ValidateLegacyRendererHidden(right,
            TrackManager.TurnInnerCornerCapName, false);
        ValidateLegacyRendererHidden(right, "LaneLine_L", false);
        ValidateLegacyRendererHidden(right, "LaneLine_R", false);

        Transform leftEntry = left != null
            ? left.transform.Find("EntryStrip") : null;
        Renderer leftRenderer = leftEntry != null
            ? leftEntry.GetComponent<Renderer>() : null;
        if (leftRenderer == null || !leftRenderer.enabled)
        {
            throw new InvalidOperationException(
                "TurnLeft must retain its legacy visual until it has an authored variant.");
        }
        ValidateCornerSupport(left, -1, true);
        ValidateCornerSupport(right, 1, false);
    }

    private static void ValidateCornerSupport(GameObject prefab,
        int turnDirection, bool capMustBeVisible)
    {
        if (prefab == null)
            throw new InvalidOperationException(
                "Missing turn prefab while validating corner support.");

        Transform cap = prefab.transform.Find(
            TrackManager.TurnInnerCornerCapName);
        Renderer capRenderer = cap != null ? cap.GetComponent<Renderer>() : null;
        if (cap == null || capRenderer == null
            || capRenderer.enabled != capMustBeVisible
            || cap.GetComponent<Collider>() != null)
        {
            throw new InvalidOperationException(prefab.name
                + " must keep a collider-free inner-corner visual cap with the expected visibility.");
        }

        Vector3 expectedCap = TrackGeometryStandards.TurnInnerCornerCenter(
            TrackGeometryStandards.StandardSegmentLength, turnDirection);
        RequireNear(cap.localPosition.x, expectedCap.x, 0.001f,
            prefab.name + " corner cap x");
        RequireNear(cap.localPosition.z, expectedCap.z, 0.001f,
            prefab.name + " corner cap z");

        Transform bridge = prefab.transform.Find(
            TrackManager.TurnWalkableBridgeName);
        BoxCollider collider = bridge != null
            ? bridge.GetComponent<BoxCollider>() : null;
        if (bridge == null || collider == null || !collider.enabled
            || bridge.GetComponent<Renderer>() != null)
        {
            throw new InvalidOperationException(prefab.name
                + " must keep an invisible enabled walkable bridge at the turn join.");
        }

        Vector3 expectedBridge = TrackGeometryStandards.TurnWalkableBridgeCenter(
            TrackGeometryStandards.StandardSegmentLength, turnDirection);
        RequireNear(bridge.localPosition.x, expectedBridge.x, 0.001f,
            prefab.name + " bridge x");
        RequireNear(bridge.localPosition.z, expectedBridge.z, 0.001f,
            prefab.name + " bridge z");
        RequireNear(collider.size.x,
            TrackGeometryStandards.TurnWalkableBridgeWidth, 0.001f,
            prefab.name + " bridge width");
        RequireNear(collider.size.z,
            TrackGeometryStandards.WalkableWidth, 0.001f,
            prefab.name + " bridge length");
    }

    private static void ValidateLegacyRendererHidden(GameObject prefab,
        string childName, bool requiresCollider)
    {
        if (prefab == null)
            throw new InvalidOperationException(
                "Missing formal track prefab while validating " + childName);
        Transform child = prefab.transform.Find(childName);
        Renderer renderer = child != null ? child.GetComponent<Renderer>() : null;
        if (child == null || renderer == null || renderer.enabled)
            throw new InvalidOperationException(prefab.name + "/" + childName
                + " must keep its object but hide its legacy renderer.");
        if (requiresCollider && child.GetComponent<Collider>() == null)
            throw new InvalidOperationException(prefab.name + "/" + childName
                + " must retain the formal collision component.");
    }

    private static void AlignStraightRoadVisual(GameObject road)
    {
        Bounds bounds = RendererBounds(road);
        Renderer graphite = GraphiteRenderer(road);
        Vector3 delta = new Vector3(-bounds.center.x,
            TrackGeometryStandards.AuthoredRoadSurfaceTopY
            - graphite.bounds.max.y, -bounds.center.z);
        road.transform.position += delta;
        ValidateStraightRoadBounds(road, road.name);
    }

    private static void AlignRightTurnRoadVisual(GameObject road)
    {
        float segmentLength = TrackGeometryStandards.StandardSegmentLength;
        float targetMinX = -TrackGeometryStandards.VisualRoadHalfWidth;
        float targetMaxX = segmentLength * 0.5f;
        float turnEnvelope = TrackGeometryStandards.TurnEntrySurfaceLength(
            segmentLength);
        Vector3 targetCenter = new Vector3(
            (targetMinX + targetMaxX) * 0.5f, 0f,
            turnEnvelope * 0.5f);
        Bounds bounds = RendererBounds(road);
        Renderer graphite = GraphiteRenderer(road);
        Vector3 delta = new Vector3(
            targetCenter.x - bounds.center.x,
            TrackGeometryStandards.AuthoredRoadSurfaceTopY
            - graphite.bounds.max.y,
            targetCenter.z - bounds.center.z);
        road.transform.position += delta;
        ValidateRightTurnRoadBounds(road, road.name);
    }

    private static void ValidateStraightRoadBounds(GameObject road,
        string label)
    {
        Bounds bounds = RendererBounds(road);
        float halfLength = TrackGeometryStandards.StandardSegmentLength * 0.5f;
        RequireNear(bounds.center.x, 0f, RoadBoundsTolerance,
            label + " road center x");
        RequireNear(bounds.center.z, 0f, RoadBoundsTolerance,
            label + " road center z");
        RequireNear(bounds.min.z, -halfLength, RoadBoundsTolerance,
            label + " road near edge");
        RequireNear(bounds.max.z, halfLength, RoadBoundsTolerance,
            label + " road far edge");
        RequireNear(bounds.size.x, TrackGeometryStandards.VisualRoadWidth,
            RoadBoundsTolerance, label + " road visual width");
        ValidateRoadSurfaceHeight(road, bounds, label);
    }

    private static void ValidateRightTurnRoadBounds(GameObject road,
        string label)
    {
        Bounds bounds = RendererBounds(road);
        float segmentLength = TrackGeometryStandards.StandardSegmentLength;
        float turnEnvelope = TrackGeometryStandards.TurnEntrySurfaceLength(
            segmentLength);
        RequireNear(bounds.min.x,
            -TrackGeometryStandards.VisualRoadHalfWidth,
            RoadBoundsTolerance, label + " turn entry outer edge");
        RequireNear(bounds.max.x, segmentLength * 0.5f,
            RoadBoundsTolerance, label + " turn exit end");
        RequireNear(bounds.min.z, 0f, RoadBoundsTolerance,
            label + " turn entry join");
        RequireNear(bounds.max.z, turnEnvelope, RoadBoundsTolerance,
            label + " turn corner envelope");
        ValidateRightTurnInnerCornerCoverage(road, label);
        ValidateRoadSurfaceHeight(road, bounds, label);
    }

    private static void ValidateRightTurnInnerCornerCoverage(GameObject road,
        string label)
    {
        Renderer graphite = GraphiteRenderer(road);
        MeshFilter filter = graphite.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            throw new InvalidOperationException(
                label + " graphite renderer has no mesh.");

        Transform coordinateRoot = road.transform.parent;
        if (coordinateRoot == null) coordinateRoot = road.transform;
        Vector3 localPoint = TrackGeometryStandards.TurnInnerCornerCenter(
            TrackGeometryStandards.StandardSegmentLength, 1);
        Vector3 worldPoint = coordinateRoot.TransformPoint(localPoint);
        if (!MeshCoversPointXZ(filter, worldPoint))
        {
            throw new InvalidOperationException(label
                + " still has an uncovered inner corner at "
                + localPoint.ToString("F3") + ".");
        }
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
        float area = CrossXZ(a, b, c);
        if (Mathf.Abs(area) < 0.00001f) return false;
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

    private static void ValidateFirstSegmentOverhang(GameObject overhang)
    {
        Bounds bounds = RendererBounds(overhang);
        float halfLength = TrackGeometryStandards.StandardSegmentLength * 0.5f;
        if (bounds.min.z < -halfLength - RoadBoundsTolerance
            || bounds.max.z > halfLength + RoadBoundsTolerance)
        {
            throw new InvalidOperationException(
                "OverhangLead must stay inside the pooled 20 m segment: "
                + bounds.ToString());
        }
    }

    private static void ValidateFormalRoadMaterial(GameObject road,
        string label)
    {
        bool foundGraphite = false;
        Renderer[] renderers = road.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            if (!Contains(renderers[index].name, "RoadGraphite")) continue;
            foundGraphite = true;
            Material material = renderers[index].sharedMaterial;
            if (material == null
                || AssetDatabase.GetAssetPath(material) != RoadMaterialPath)
            {
                throw new InvalidOperationException(label
                    + " graphite renderer must use " + RoadMaterialPath + ".");
            }
        }

        if (!foundGraphite)
            throw new InvalidOperationException(
                label + " is missing its MF_RoadGraphite renderer.");
    }

    private static void ValidateRoadSurfaceHeight(GameObject road,
        Bounds bounds, string label)
    {
        RequireNear(GraphiteRenderer(road).bounds.max.y,
            TrackGeometryStandards.AuthoredRoadSurfaceTopY,
            0.04f, label + " graphite surface top");
        if (bounds.max.y > 0.36f || bounds.min.y < -0.50f)
        {
            throw new InvalidOperationException(label
                + " road vertical envelope is not near the track plane: "
                + bounds.ToString());
        }
    }

    private static Renderer GraphiteRenderer(GameObject road)
    {
        Renderer[] renderers = road.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            if (Contains(renderers[index].name, "RoadGraphite"))
                return renderers[index];
        }
        throw new InvalidOperationException(
            road.name + " is missing its MF_RoadGraphite renderer.");
    }

    private static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException(
                root.name + " has no renderer bounds.");
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static void RequireNear(float actual, float expected,
        float tolerance, string label)
    {
        if (Mathf.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException(label + " expected "
                + expected.ToString("F3") + " +/- "
                + tolerance.ToString("F3") + ", got "
                + actual.ToString("F3") + ".");
        }
    }

    private static void ValidateColliderFree(GameObject root, string label)
    {
        int count = root.GetComponentsInChildren<Collider>(true).Length;
        if (count != 0)
            throw new InvalidOperationException(
                label + " must remain visual-only; colliders=" + count);
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            if (descendants[index].name == name) return descendants[index];
        }
        return null;
    }

    private static string AtomicPrefabPath(string modelName)
    {
        return PrefabRoot + modelName + ".prefab";
    }

    private static string CompositePrefabPath(string name)
    {
        return PrefabRoot + name + ".prefab";
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }
}
