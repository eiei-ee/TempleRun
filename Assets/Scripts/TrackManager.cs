using System.Collections.Generic;
using UnityEngine;

public struct EchoEncounterLaneChoice
{
    public int lane;
    public int minCoinCount;
    public int maxCoinCount;
    public bool echoContractMarker;
}

public class TrackManager : MonoBehaviour
{
    public const string ColdWhiteFortressSampleArgument =
        "-echo-cold-white-fortress-sample";
    public const string ColdWhiteFortressLeftSampleArgument =
        "-echo-cold-white-fortress-sample-left";
    public const float ColdWhiteFortressSampleLength =
        TrackGeometryStandards.StandardSegmentLength * 3f;
    public const float ChallengeSettlementMargin = 7f;
    public const float PredictionGateMinimumObstacleClearance = 12f;
    public const float PredictionGateRibbonWidth = 1.15f;
    public const float PredictionGateRibbonLength = 2.4f;
    public const float PredictionGateDecisionBandWidth = 1.45f;
    public const float PredictionGateSurfaceClearance = 0.01f;
    public const string PredictionGateVisualRootName =
        "PredictionGateVisual";
    public const string TurnInnerCornerCapName = "TurnInnerCornerCap";
    public const string TurnWalkableBridgeName = "TurnWalkableBridge";
    public static TrackManager Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeInstance()
    {
        if (FindObjectOfType<TrackManager>() != null) return;
        new GameObject("TrackManager_Runtime").AddComponent<TrackManager>();
    }

    [Header("Track")]
    public GameObject trackSegmentPrefab;
    public float segmentLength = 20f;
    public int poolSize = 10;

    [Header("Turns")]
    public GameObject turnLeftPrefab;
    public GameObject turnRightPrefab;
    [Range(0, 1)] public float turnChance = 0.15f;
    public int minStraightBeforeTurn = 4;

    [Header("Lanes")]
    public float laneDistance = TrackGeometryStandards.LaneSpacing;

    [Header("Obstacles & Coins")]
    public GameObject[] obstaclePrefabs;
    public GameObject coinPrefab;
    [Range(0, 1)] public float obstacleChance = 0.4f;
    [Range(0, 1)] public float coinChance = 0.6f;
    [Min(1)] public int maxConsecutiveObstacleFreeStraights = 2;

    [Header("AI Track Director")]
    public bool useAITrackDirector = true;

    private Queue<GameObject> _straightPool = new Queue<GameObject>();
    private Queue<GameObject> _turnLeftPool = new Queue<GameObject>();
    private Queue<GameObject> _turnRightPool = new Queue<GameObject>();
    private List<GameObject> _activeSegments = new List<GameObject>();
    private readonly Dictionary<GameObject, Queue<GameObject>> _dynamicPools =
        new Dictionary<GameObject, Queue<GameObject>>();
    private readonly List<DynamicEntry> _dynamicObjects = new List<DynamicEntry>();

    private class DynamicEntry
    {
        public GameObject instance;
        public GameObject prefab;
        public GameObject ownerSegment;
    }

    private struct CoinTrailPlan
    {
        public int lane;
        public float startZ;
        public int count;
        public bool echoContractMarker;
        public int challengeStepId;
    }

    private struct SpawnedObstacleInfo
    {
        public int lane;
        public float z;
        public ObstacleType type;
        public EchoChallengeObstacleRole challengeRole;
    }

    private struct ChallengeRowRuntime
    {
        public int stepId;
        public float routeDistance;
    }

    private Vector3 _spawnPosition;
   private float _spawnAngle;
    private int _lastSafeLane = 1;
    private int _obstacleFreeSegments;
    private int _straightSegmentsSpawned;
    private int _rhythmContractRowsSpawned;
    private int _counterattackRowsSpawned;
    private readonly int[] _laneObstacleDrought = new int[3];
    private readonly List<ChallengeRowRuntime> _challengeRows =
        new List<ChallengeRowRuntime>();
    private float _lastObstacleRouteDistance = float.NegativeInfinity;
    private int _straightSegmentsSinceLastTurn;
    private float _plannedDistance;
    private float _contentPreparedDistance;
    private Transform _player;
    private AITrackDirector _aiDirector;
    private GameObject _finishMarker;
    private Material _finishMarkerMaterial;
    private Material _predictionGateMaterial;
    private Light[] _finishMarkerLights;
    private bool _usesColdWhiteFortressSample;
    private bool _usesColdWhiteFortressLeftSample;

    public int ObstacleRowsSpawned { get; private set; }
    public int ObstacleRowsRejectedForSpacing { get; private set; }
    public int ObstacleRowsSkippedByChance { get; private set; }
    public int JumpOpportunityRows { get; private set; }
    public int SlideOpportunityRows { get; private set; }
    public int ChallengeRowsSpawned { get; private set; }
    public int ChallengeRowsMissed { get; private set; }
    public int PredictionGateRowsSpawned { get; private set; }
    public bool UsesColdWhiteFortressSample =>
        _usesColdWhiteFortressSample;
    public bool UsesColdWhiteFortressLeftSample =>
        _usesColdWhiteFortressLeftSample;
    public float LongestObstacleRowGap { get; private set; }
    public float MinimumChallengeWarningSeconds { get; private set; }
        = float.PositiveInfinity;

    private const float SEGMENT_CHECK_MULT = 1.5f;
    private const float SEGMENT_RECYCLE_MULT = 5f;

    public TrackSegmentData CurrentTurnSegment { get; private set; }
    public int ActiveSegmentCount => _activeSegments.Count;
    public float PlannedRouteDistance => Mathf.Max(0f, _plannedDistance);
    public float ContentPreparedRouteDistance =>
        Mathf.Max(0f, _contentPreparedDistance);
    public Vector3 ForwardDirection =>
        Quaternion.Euler(0, _spawnAngle, 0) * Vector3.forward;

    public float GetNextRouteBoundary(float playerRouteDistance)
    {
        return NextRouteBoundary(playerRouteDistance, segmentLength);
    }

    public float GetPreparedPhaseBoundary(float playerRouteDistance)
    {
        return PreparedPhaseBoundary(playerRouteDistance,
            ContentPreparedRouteDistance, segmentLength);
    }

    public static float NextRouteBoundary(float playerRouteDistance,
        float routeSegmentLength)
    {
        float length = Mathf.Max(1f, routeSegmentLength);
        float distance = Mathf.Max(0f, playerRouteDistance);
        return (Mathf.Floor(distance / length) + 1f) * length;
    }

    public static float PreparedPhaseBoundary(float playerRouteDistance,
        float plannedRouteDistance, float routeSegmentLength)
    {
        float length = Mathf.Max(1f, routeSegmentLength);
        float next = NextRouteBoundary(playerRouteDistance, length);
        float prepared = Mathf.Ceil(
            Mathf.Max(0f, plannedRouteDistance) / length) * length;
        return Mathf.Max(next, prepared);
    }

    public static int PlanningLookaheadPoolSize(int configuredPoolSize,
        EchoDuelPhase phase)
    {
        // Phase timing may change the content prepared for future segments,
        // but it must never collapse the visible road shell around the player.
        return Mathf.Max(12, configuredPoolSize);
    }

    public static float ContentLookaheadDistance(float routeSegmentLength,
        int planningPoolSize = 12)
    {
        float safeLength = Mathf.Max(1f, routeSegmentLength);
        return safeLength * Mathf.Max(2, planningPoolSize / 2);
    }

    public static bool ShouldPrepareSegmentContent(float segmentRouteDistance,
        float playerRouteDistance, float routeSegmentLength,
        int planningPoolSize = 12)
    {
        return segmentRouteDistance - playerRouteDistance
               < ContentLookaheadDistance(routeSegmentLength,
                   planningPoolSize);
    }

    public static bool HasColdWhiteFortressSampleArgument(string[] arguments)
    {
        if (arguments == null) return false;
        for (int index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index],
                    ColdWhiteFortressSampleArgument,
                    System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(arguments[index],
                    ColdWhiteFortressLeftSampleArgument,
                    System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool TryGetColdWhiteFortressSampleSegment(
        float routeDistance, bool sampleEnabled,
        out TrackSegmentType segmentType)
    {
        return TryGetColdWhiteFortressSampleSegment(routeDistance,
            sampleEnabled, false, out segmentType);
    }

    public static bool TryGetColdWhiteFortressSampleSegment(
        float routeDistance, bool sampleEnabled, bool leftTurn,
        out TrackSegmentType segmentType)
    {
        segmentType = TrackSegmentType.Straight;
        if (!sampleEnabled || routeDistance < 0f
            || routeDistance >= ColdWhiteFortressSampleLength)
            return false;

        int segmentIndex = Mathf.FloorToInt(routeDistance
            / TrackGeometryStandards.StandardSegmentLength);
        segmentType = segmentIndex == 1
            ? (leftTurn
                ? TrackSegmentType.TurnLeft
                : TrackSegmentType.TurnRight)
            : TrackSegmentType.Straight;
        return true;
    }

    public static bool HasColdWhiteFortressLeftSampleArgument(
        string[] arguments)
    {
        if (arguments == null) return false;
        for (int index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index],
                    ColdWhiteFortressLeftSampleArgument,
                    System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static int ColdWhiteFortressVisualVariantIndex(
        TrackSegmentType segmentType, float routeDistance,
        bool sampleEnabled)
    {
        if (sampleEnabled && segmentType == TrackSegmentType.Straight
            && routeDistance >= ColdWhiteFortressSampleLength
            && routeDistance < ColdWhiteFortressSampleLength
                               + TrackGeometryStandards.StandardSegmentLength
                                 * 2f)
        {
            int openIndex = Mathf.FloorToInt(routeDistance
                / TrackGeometryStandards.StandardSegmentLength);
            return openIndex % 2;
        }

        if (!TryGetColdWhiteFortressSampleSegment(routeDistance,
                sampleEnabled, out TrackSegmentType authoredType)
            || authoredType != segmentType)
            return -1;

        if (segmentType != TrackSegmentType.Straight) return 0;
        return routeDistance >= TrackGeometryStandards.StandardSegmentLength
                                * 2f
            ? 2 : 0;
    }

    public static Vector3 ColdWhiteFortressInitialSpawnOffset(
        bool sampleEnabled, float routeSegmentLength,
        Vector3 forwardDirection)
    {
        if (!sampleEnabled) return Vector3.zero;
        return forwardDirection.normalized
               * Mathf.Max(1f, routeSegmentLength) * 0.5f;
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _usesColdWhiteFortressSample =
            HasColdWhiteFortressSampleArgument(
                System.Environment.GetCommandLineArgs());
        _usesColdWhiteFortressLeftSample =
            HasColdWhiteFortressLeftSampleArgument(
                System.Environment.GetCommandLineArgs());
        TrackBalance balance = GameBalanceConfig.Current.track;
        obstacleChance = balance.obstacleChance;
        coinChance = balance.coinChance;
        turnChance = balance.turnChance;

        if (useAITrackDirector)
        {
            _aiDirector = GetComponent<AITrackDirector>();
            if (_aiDirector == null) _aiDirector = gameObject.AddComponent<AITrackDirector>();
        }

        if (GetComponent<AIShadowRunner>() == null)
            gameObject.AddComponent<AIShadowRunner>();
    }

    void Start()
    {
        _player = GameObject.Find("player")?.transform;
        _spawnAngle = 0f;
        _spawnPosition = _player != null
            ? new Vector3(_player.position.x, 0f, _player.position.z)
            : Vector3.zero;
        _spawnPosition += ColdWhiteFortressInitialSpawnOffset(
            _usesColdWhiteFortressSample, segmentLength, ForwardDirection);
        _straightSegmentsSinceLastTurn = 0;
        InitializePools();
    }

    void Update()
    {
        if (GameManager.Instance == null
            || GameManager.Instance.State != GameState.Playing)
        {
            SetFinishMarkerActive(false);
            return;
        }
        if (_player == null) return;
        if (trackSegmentPrefab == null) return;

        float playerRouteDistance = GameManager.Instance.Distance;
        bool singleContractRun = IsSingleContractRun();
        if (!singleContractRun) UpdateChallengeRows(playerRouteDistance);
        AIShadowRunner shadow = AIShadowRunner.Instance;
        // Scene-reload auto-start can enter Playing before AIShadowRunner.Start
        // subscribes to the state event. Do not author any content until its
        // immutable gate plan exists, otherwise the first gate window can be
        // permanently marked as ordinary content.
        if (singleContractRun
            && (shadow == null || shadow.SingleContractRuntime == null))
            return;
        EchoDuelPhase planningPhase = !singleContractRun && shadow != null
                                      && shadow.DuelTransitionPending
            ? shadow.PendingDuelPhase
            : !singleContractRun && shadow != null
                ? shadow.DuelPhase : EchoDuelPhase.None;
        int lookaheadPoolSize = PlanningLookaheadPoolSize(
            poolSize, planningPhase);
        int spawnBudget = Mathf.Max(1, lookaheadPoolSize);
        while (spawnBudget-- > 0 && TrackSpawnRules.NeedsSegment(
                   _plannedDistance, playerRouteDistance, segmentLength,
                   lookaheadPoolSize))
        {
            SpawnSegment();
        }
        PopulatePreparedSegmentContent(playerRouteDistance,
            lookaheadPoolSize);
        _aiDirector?.ActivatePlanForDistance(playerRouteDistance);

        while (_activeSegments.Count > 0)
        {
            GameObject seg = _activeSegments[0];
            TrackSegmentData data = seg.GetComponent<TrackSegmentData>();
            if (data == null || !TrackSpawnRules.CanRecycleSegment(
                    data.routeDistance, playerRouteDistance, segmentLength,
                    SEGMENT_RECYCLE_MULT)) break;
            RecycleSegment(seg);
        }

        UpdateFinishMarker();
    }

    float XZSqrDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    public TrackSegmentData FindTurnAtPosition(Vector3 worldPos)
    {
        // Check cached turn first
        if (CurrentTurnSegment != null && CurrentTurnSegment.gameObject.activeSelf)
        {
            float sqrDist = XZSqrDistance(worldPos, CurrentTurnSegment.transform.position);
            float checkSqr = (segmentLength * SEGMENT_CHECK_MULT);
            checkSqr *= checkSqr;
            if (sqrDist < checkSqr)
                return CurrentTurnSegment;
        }

        CurrentTurnSegment = null;

        float checkDistSqr = (segmentLength * SEGMENT_CHECK_MULT);
        checkDistSqr *= checkDistSqr;

        for (int i = _activeSegments.Count - 1; i >= 0; i--)
        {
            GameObject seg = _activeSegments[i];
            if (!seg.activeSelf) continue;

            TrackSegmentData data = seg.GetComponent<TrackSegmentData>();
            if (data == null || data.segmentType == TrackSegmentType.Straight) continue;

            if (XZSqrDistance(worldPos, seg.transform.position) < checkDistSqr)
            {
                CurrentTurnSegment = data;
                return data;
            }
        }
       return null;
   }

    public bool IsInsideTurnTransition(Vector3 worldPosition,
        float transitionDistance = 3f)
    {
        float distance = Mathf.Max(0.1f, transitionDistance);
        float maxRadius = distance + laneDistance;
        float maxRadiusSqr = maxRadius * maxRadius;

        for (int i = _activeSegments.Count - 1; i >= 0; i--)
        {
            GameObject segment = _activeSegments[i];
            if (segment == null || !segment.activeInHierarchy) continue;

            TrackSegmentData data = segment.GetComponent<TrackSegmentData>();
            if (data == null || data.segmentType == TrackSegmentType.Straight)
                continue;

            Vector3 fromTurn = worldPosition - data.turnPointWorld;
            fromTurn.y = 0f;
            if (fromTurn.sqrMagnitude > maxRadiusSqr) continue;

            Vector3 entryDirection = data.entryDirection.normalized;
            Vector3 exitDirection = data.exitDirection.normalized;
            float entryProgress = Vector3.Dot(fromTurn, entryDirection);
            float exitProgress = Vector3.Dot(fromTurn, exitDirection);
            if (entryProgress >= -distance && exitProgress <= distance)
                return true;
        }

        return false;
    }

    public bool TryGetUpcomingObstacle(Vector3 playerPosition, Vector3 forward,
        int currentLane, out int obstacleLane, out float obstacleDistance,
        out ObstacleType obstacleType, out int obstacleId)
    {
        return TryGetUpcomingObstacleInternal(playerPosition, forward, currentLane,
            false, null, out obstacleLane, out obstacleDistance,
            out obstacleType, out obstacleId);
    }

    public bool TryGetUpcomingObstacleInLane(Vector3 position, Vector3 forward,
        int lane, ISet<int> ignoredObstacleIds, out float obstacleDistance,
        out ObstacleType obstacleType, out int obstacleId)
    {
        return TryGetUpcomingObstacleInternal(position, forward, lane,
            true, ignoredObstacleIds, out _, out obstacleDistance,
            out obstacleType, out obstacleId);
    }

    private bool TryGetUpcomingObstacleInternal(Vector3 playerPosition, Vector3 forward,
        int currentLane, bool currentLaneOnly, ISet<int> ignoredObstacleIds,
        out int obstacleLane, out float obstacleDistance,
        out ObstacleType obstacleType, out int obstacleId)
    {
        obstacleLane = currentLane;
        obstacleDistance = float.MaxValue;
        obstacleType = ObstacleType.Low;
        obstacleId = 0;

        Vector3 normalizedForward = forward.sqrMagnitude > 0.001f
            ? forward.normalized
            : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, normalizedForward).normalized;
        bool found = false;

        for (int i = 0; i < _dynamicObjects.Count; i++)
        {
            DynamicEntry entry = _dynamicObjects[i];
            if (entry.instance == null || !entry.instance.activeInHierarchy) continue;

            Obstacle obstacle = entry.instance.GetComponent<Obstacle>();
            if (obstacle == null) continue;

            Vector3 offset = entry.instance.transform.position - playerPosition;
            float forwardDistance = Vector3.Dot(offset, normalizedForward);
            if (forwardDistance <= 0.5f || forwardDistance > 24f
                || forwardDistance >= obstacleDistance)
                continue;

            float laneDelta = Vector3.Dot(offset, right) / Mathf.Max(0.1f, laneDistance);
            int candidateLane = Mathf.Clamp(
                currentLane + Mathf.RoundToInt(laneDelta), 0, 2);
            if (currentLaneOnly && candidateLane != currentLane) continue;

            int candidateId = GetObstacleTrackingId(entry.instance);
            if (ignoredObstacleIds != null && ignoredObstacleIds.Contains(candidateId))
                continue;

            obstacleLane = candidateLane;
            obstacleDistance = forwardDistance;
            obstacleType = obstacle.type;
            obstacleId = candidateId;
            found = true;
        }

        return found;
    }

    public static int GetObstacleTrackingId(GameObject obstacleInstance)
    {
        if (obstacleInstance == null) return 0;
        Vector3 position = obstacleInstance.transform.position;
        return obstacleInstance.GetInstanceID()
               ^ Mathf.RoundToInt(position.x * 17f)
               ^ Mathf.RoundToInt(position.z * 31f);
    }

    public EchoChallengeObstacleBinding GetChallengeBinding(int obstacleId)
    {
        if (obstacleId == 0) return default;
        for (int i = 0; i < _dynamicObjects.Count; i++)
        {
            GameObject instance = _dynamicObjects[i].instance;
            if (instance == null || !instance.activeInHierarchy
                || GetObstacleTrackingId(instance) != obstacleId)
                continue;
            EchoChallengeObstacleTag tag =
                instance.GetComponent<EchoChallengeObstacleTag>();
            return tag != null ? tag.Binding : default;
        }
        return default;
    }

    public PredictionGateObstacleBinding GetPredictionGateBinding(
        int obstacleId)
    {
        if (obstacleId == 0) return default;
        for (int i = 0; i < _dynamicObjects.Count; i++)
        {
            GameObject instance = _dynamicObjects[i].instance;
            if (instance == null || !instance.activeInHierarchy
                || GetObstacleTrackingId(instance) != obstacleId)
                continue;
            PredictionGateObstacleTag tag =
                instance.GetComponent<PredictionGateObstacleTag>();
            return tag != null ? tag.Binding : default;
        }
        return default;
    }

    public bool TryGetUpcomingChallengeObstacle(Vector3 position,
        Vector3 forward, int challengeStepId, out float obstacleDistance)
    {
        obstacleDistance = float.MaxValue;
        if (challengeStepId <= 0) return false;

        Vector3 normalizedForward = forward.sqrMagnitude > 0.001f
            ? forward.normalized
            : Vector3.forward;
        bool found = false;
        for (int i = 0; i < _dynamicObjects.Count; i++)
        {
            GameObject instance = _dynamicObjects[i].instance;
            if (instance == null || !instance.activeInHierarchy
                || instance.GetComponent<Obstacle>() == null)
                continue;
            EchoChallengeObstacleTag tag =
                instance.GetComponent<EchoChallengeObstacleTag>();
            EchoChallengeObstacleBinding binding = tag != null
                ? tag.Binding : default;
            if (!binding.IsBound || binding.stepId != challengeStepId)
                continue;

            float distance = Vector3.Dot(
                instance.transform.position - position, normalizedForward);
            if (distance <= 0.5f || distance >= obstacleDistance)
                continue;
            obstacleDistance = distance;
            found = true;
        }
        return found;
    }

    public void GetTrackPoseAhead(Vector3 playerPosition, Vector3 playerForward,
        int playerLane, float targetLane, float distanceAhead,
        out Vector3 trackPosition, out Vector3 trackForward)
    {
        GetTrackPoseAhead(playerPosition, playerForward,
            (playerLane - 1) * laneDistance, targetLane, distanceAhead,
            out trackPosition, out trackForward);
    }

    public void GetTrackPoseAhead(Vector3 playerPosition, Vector3 playerForward,
        float playerLateralOffset, float targetLane, float distanceAhead,
        out Vector3 trackPosition, out Vector3 trackForward)
    {
        Vector3 forward = playerForward.sqrMagnitude > 0.001f
            ? playerForward.normalized
            : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 trackCenter = playerPosition
                              - right * playerLateralOffset;
        trackForward = forward;

        if (distanceAhead > 0f)
        {
            TrackSegmentData nextTurn = null;
            float nearestTurnDistance = float.MaxValue;
            for (int i = 0; i < _activeSegments.Count; i++)
            {
                GameObject segment = _activeSegments[i];
                if (segment == null || !segment.activeInHierarchy) continue;

                TrackSegmentData data = segment.GetComponent<TrackSegmentData>();
                if (data == null || data.segmentType == TrackSegmentType.Straight
                    || Vector3.Dot(data.entryDirection, forward) < 0.9f)
                    continue;

                Vector3 toTurn = data.turnPointWorld - trackCenter;
                float forwardDistance = Vector3.Dot(toTurn, forward);
                float lateralDistance = Mathf.Abs(Vector3.Dot(toTurn, right));
                const float cornerBlendDistance = 2.5f;
                if (forwardDistance <= 0.1f
                    || forwardDistance > distanceAhead + cornerBlendDistance
                    || lateralDistance > laneDistance * 1.5f
                    || forwardDistance >= nearestTurnDistance)
                    continue;

                nextTurn = data;
                nearestTurnDistance = forwardDistance;
            }

            if (nextTurn != null)
            {
                const float cornerBlendDistance = 2.5f;
                float distanceFromTurn = distanceAhead - nearestTurnDistance;
                if (distanceFromTurn <= -cornerBlendDistance)
                {
                    trackCenter += forward * distanceAhead;
                }
                else if (distanceFromTurn >= cornerBlendDistance)
                {
                    trackCenter = nextTurn.turnPointWorld
                                  + nextTurn.exitDirection * distanceFromTurn;
                    trackForward = nextTurn.exitDirection;
                }
                else
                {
                    float t = Mathf.InverseLerp(-cornerBlendDistance,
                        cornerBlendDistance, distanceFromTurn);
                    Vector3 entryPoint = nextTurn.turnPointWorld
                                         - forward * cornerBlendDistance;
                    Vector3 exitPoint = nextTurn.turnPointWorld
                                        + nextTurn.exitDirection * cornerBlendDistance;
                    float oneMinusT = 1f - t;
                    trackCenter = oneMinusT * oneMinusT * entryPoint
                                  + 2f * oneMinusT * t * nextTurn.turnPointWorld
                                  + t * t * exitPoint;
                    trackForward = Vector3.Slerp(
                        forward, nextTurn.exitDirection, t).normalized;
                }
            }
            else
            {
                trackCenter += forward * distanceAhead;
            }
        }
        else
        {
            trackCenter += forward * distanceAhead;
        }

        Vector3 targetRight = Vector3.Cross(Vector3.up, trackForward).normalized;
        trackPosition = trackCenter
                        + targetRight * ((Mathf.Clamp(targetLane, 0f, 2f) - 1f)
                                         * laneDistance);
    }

   void InitializePools()
   {
       EnsureProceduralAssets();
       EchoRoadVisualController roadVisuals = EchoRoadVisualController.Instance;

       if (trackSegmentPrefab != null)
        {
            int straightPoolSize = Mathf.Max(1, poolSize - 4);
            for (int i = 0; i < straightPoolSize; i++)
            {
                GameObject seg = Instantiate(trackSegmentPrefab, Vector3.zero, Quaternion.identity, transform);
                roadVisuals.ApplyToTrackSegment(seg, RoadSurfaceRole.Main);
                seg.SetActive(false);
                _straightPool.Enqueue(seg);
            }
        }
        if (turnLeftPrefab != null)
        {
            for (int i = 0; i < 2; i++)
            {
                GameObject seg = Instantiate(turnLeftPrefab, Vector3.zero, Quaternion.identity, transform);
                roadVisuals.ApplyToTrackSegment(seg, RoadSurfaceRole.Turn);
                seg.SetActive(false);
                _turnLeftPool.Enqueue(seg);
            }
        }
        if (turnRightPrefab != null)
        {
            for (int i = 0; i < 2; i++)
            {
                GameObject seg = Instantiate(turnRightPrefab, Vector3.zero, Quaternion.identity, transform);
                roadVisuals.ApplyToTrackSegment(seg, RoadSurfaceRole.Turn);
                seg.SetActive(false);
                _turnRightPool.Enqueue(seg);
            }
        }
    }

    void SpawnSegment()
    {
        float baseDifficulty = CalculateBaseDifficulty();
        bool reservedSingleContractStraight = IsSingleContractRun()
            && IsSingleContractProtectedSegment(
                _plannedDistance, _plannedDistance + segmentLength);
        bool canTurn = _straightSegmentsSinceLastTurn >= minStraightBeforeTurn
                       && turnLeftPrefab != null && turnRightPrefab != null
                       && !reservedSingleContractStraight;
        AITrackPlan plan = _aiDirector != null && _aiDirector.useAI
            ? _aiDirector.CreatePlan(baseDifficulty, obstacleChance, coinChance,
                turnChance, _lastSafeLane, canTurn, _plannedDistance,
                _plannedDistance + segmentLength)
            : CreateFallbackPlan(baseDifficulty, canTurn);
        RunDifficultyLevel runDifficulty = ActiveRunDifficulty();
        plan.difficulty = RunDifficultySettings.AdjustDifficulty(
            plan.difficulty, runDifficulty);
        plan.obstacleChance = RunDifficultySettings.AdjustObstacleChance(
            plan.obstacleChance, runDifficulty);
        if (runDifficulty == RunDifficultyLevel.Relaxed)
            plan.maxBlockedLanes = 1;
        if (_aiDirector == null || !_aiDirector.useAI)
            plan.echoEncounterStep = Mathf.RoundToInt(
                _plannedDistance / Mathf.Max(1f, segmentLength));
        float courseDistance = GameManager.Instance != null
            ? GameManager.Instance.CourseDistance
            : 0f;
        bool isFinishSegment = courseDistance > _plannedDistance
                               && courseDistance <= _plannedDistance + segmentLength;
        bool shouldTurn = ShouldSpawnTurn(canTurn, plan.shouldTurn,
                               _straightSegmentsSinceLastTurn)
                           && !isFinishSegment
                           && !reservedSingleContractStraight;
        bool usesSampleSegment = TryGetColdWhiteFortressSampleSegment(
            _plannedDistance, _usesColdWhiteFortressSample,
            _usesColdWhiteFortressLeftSample,
            out TrackSegmentType sampleSegmentType);
        if (usesSampleSegment)
            shouldTurn = sampleSegmentType != TrackSegmentType.Straight;

        GameObject prefab;
        TrackSegmentType segType;
        float angleDelta = 0f;
        Queue<GameObject> pool;

        if (shouldTurn)
        {
            bool turnRight = usesSampleSegment
                ? sampleSegmentType == TrackSegmentType.TurnRight
                : AIRunRandom.Value < 0.5f;
            prefab = turnRight ? turnRightPrefab : turnLeftPrefab;
            segType = turnRight ? TrackSegmentType.TurnRight : TrackSegmentType.TurnLeft;
            angleDelta = turnRight ? 90f : -90f;
            pool = turnRight ? _turnRightPool : _turnLeftPool;
        }
        else
        {
            prefab = trackSegmentPrefab;
            segType = TrackSegmentType.Straight;
            pool = _straightPool;
        }

        GameObject segment = pool.Count > 0
            ? pool.Dequeue()
            : Instantiate(prefab, Vector3.zero, Quaternion.identity, transform);

        segment.transform.position = _spawnPosition;
        segment.transform.rotation = Quaternion.Euler(0, _spawnAngle, 0);
        EchoRoadVisualController.Instance.ApplyToTrackSegment(segment,
            segType == TrackSegmentType.Straight
                ? RoadSurfaceRole.Main
                : RoadSurfaceRole.Turn);
        segment.SetActive(true);

        TrackSegmentData data = segment.GetComponent<TrackSegmentData>();
        if (data == null) data = segment.AddComponent<TrackSegmentData>();
        data.segmentType = segType;
        data.routeDistance = _plannedDistance;
        data.trackPlan = plan;
        data.contentSpawned = false;
        data.isFinishSegment = isFinishSegment;

        _activeSegments.Add(segment);

        if (segType == TrackSegmentType.Straight)
        {
            data.entryDirection = ForwardDirection;
            _spawnPosition += ForwardDirection * segmentLength;
            _straightSegmentsSinceLastTurn++;
        }
        else
        {
            Vector3 entryDir = ForwardDirection;
            Vector3 cornerPos = _spawnPosition;

            // Shift turn back half a segment so entry strip bridges the gap
            // from previous straight's visual end to the corner
            Vector3 turnPlacePos = _spawnPosition - entryDir * (segmentLength * 0.5f);
            segment.transform.position = turnPlacePos;
            segment.transform.rotation = Quaternion.Euler(0, _spawnAngle, 0);

            EnsureTurnCoverage(segment, angleDelta > 0f ? 1 : -1);

            // Advance spawn: full segment in exit direction from corner
            // (entry half was consumed by the shifted-back placement)
            _spawnAngle += angleDelta;
            _spawnPosition += ForwardDirection * segmentLength;

            data.entryDirection = entryDir;
            data.exitDirection = ForwardDirection;
            data.turnPointWorld = cornerPos;
            _straightSegmentsSinceLastTurn = 0;

            // Cache the newly spawned turn for fast lookup
            CurrentTurnSegment = data;
        }

        WorldStyler.Instance?.DecorateSegment(segment, segType);
        AIRunTelemetry.RecordEvent("track_segment", (int)segType,
            plan.safeLane, plan.difficulty, plan.obstacleChance);
        _plannedDistance += segmentLength;
    }

    private void PopulatePreparedSegmentContent(float playerRouteDistance,
        int lookaheadPoolSize)
    {
        bool singleContractRun = IsSingleContractRun();
        for (int segmentIndex = 0;
             segmentIndex < _activeSegments.Count; segmentIndex++)
        {
            GameObject segment = _activeSegments[segmentIndex];
            if (segment == null || !segment.activeSelf) continue;
            TrackSegmentData data = segment.GetComponent<TrackSegmentData>();
            if (data == null || data.contentSpawned) continue;
            if (!ShouldPrepareContentForRun(data.routeDistance,
                    playerRouteDistance, singleContractRun,
                    lookaheadPoolSize))
                break;

            if (singleContractRun
                && TryPopulateSingleContractSegment(segment, data))
            {
                _contentPreparedDistance = Mathf.Max(
                    _contentPreparedDistance,
                    data.routeDistance + segmentLength);
                continue;
            }

            AITrackPlan contentPlan = RefreshPlanForPreparedContent(
                data.trackPlan, data.routeDistance);
            if (ShouldDeferChallengeContent(contentPlan))
                break;
            data.trackPlan = contentPlan;
            data.contentSpawned = true;
            if (!data.isFinishSegment)
                SpawnObstaclesAndCoins(segment, data.segmentType, contentPlan);
            _contentPreparedDistance = Mathf.Max(_contentPreparedDistance,
                data.routeDistance + segmentLength);
        }
    }

    private bool ShouldPrepareContentForRun(float segmentRouteDistance,
        float playerRouteDistance, bool singleContractRun,
        int lookaheadPoolSize)
    {
        float lookahead = ContentLookaheadDistance(segmentLength,
            lookaheadPoolSize);
        if (singleContractRun && GameManager.Instance != null)
        {
            lookahead = Mathf.Max(lookahead,
                GameManager.Instance.maxSpeed * 2.5f);
        }
        return segmentRouteDistance - playerRouteDistance < lookahead;
    }

    private bool TryPopulateSingleContractSegment(GameObject segment,
        TrackSegmentData data)
    {
        float segmentStart = data.routeDistance;
        float segmentEnd = segmentStart + segmentLength;
        if (segmentStart < SingleContractOpeningMemoryDistance())
        {
            data.contentSpawned = true;
            return true;
        }

        if (!TryGetSingleContractGateForSegment(
                segmentStart, segmentEnd,
                out PredictionGateDefinition gate,
                out bool containsGateContent))
            return false;

        if (!containsGateContent)
        {
            data.contentSpawned = true;
            return true;
        }
        // Materialize the frozen gate while its road shell is prewarmed.
        // The flow still enters Presented only at presentationDistance; these
        // are separate concerns. Waiting here makes obstacles visibly pop in
        // after a turn reveals the next straight.
        data.contentSpawned = true;
        SpawnPredictionGateContent(segment, gate);
        return true;
    }

    private bool IsSingleContractProtectedSegment(float segmentStart,
        float segmentEnd)
    {
        if (segmentStart < SingleContractOpeningMemoryDistance())
            return true;
        return TryGetSingleContractGateForSegment(
            segmentStart, segmentEnd, out _, out _);
    }

    private bool TryGetSingleContractGateForSegment(float segmentStart,
        float segmentEnd, out PredictionGateDefinition definition,
        out bool containsGateContent)
    {
        definition = null;
        containsGateContent = false;
        AIShadowRunner shadow = AIShadowRunner.Instance;
        SingleContractFlow flow = shadow != null
            ? shadow.SingleContractRuntime : null;
        if (flow == null) return false;

        for (int index = 0; index < flow.GateCount; index++)
        {
            PredictionGateDefinition candidate =
                flow.GetGate(index).Definition;
            bool overlaps = segmentStart < candidate.exitDistance - 0.01f
                            && segmentEnd
                            > candidate.presentationDistance + 0.01f;
            if (!overlaps) continue;
            definition = candidate;
            containsGateContent = candidate.resolveDistance
                                  >= segmentStart - 0.01f
                                  && candidate.resolveDistance
                                  < segmentEnd - 0.01f;
            return true;
        }
        return false;
    }

    private float SingleContractOpeningMemoryDistance()
    {
        GameManager manager = GameManager.Instance;
        if (manager == null) return 0f;
        float start = Mathf.Max(1f, manager.CurrentSpeed > 0f
            ? manager.CurrentSpeed : manager.startSpeed);
        return EchoTimeRules.DistanceForAcceleratingRun(
            start, Mathf.Max(start, manager.maxSpeed),
            manager.speedIncreaseRate,
            SingleContractFlow.OpeningMemoryDurationSeconds);
    }

    private void SpawnPredictionGateContent(GameObject segment,
        PredictionGateDefinition gate)
    {
        if (segment == null || gate == null || gate.lanes == null)
            return;
        TrackSegmentData data = segment.GetComponent<TrackSegmentData>();
        float segmentStart = data != null ? data.routeDistance : 0f;
        SpawnPredictionGateVisual(segment, gate, segmentStart);
        float obstacleZ = Mathf.Clamp(
            gate.resolveDistance - segmentStart, 4f, segmentLength - 4f);
        var spawnedObstacles = new List<SpawnedObstacleInfo>(1);

        for (int laneIndex = 0;
             laneIndex < gate.lanes.Length; laneIndex++)
        {
            PredictionGateLane lane = gate.lanes[laneIndex];
            if (!lane.obstacle.isRequired) continue;
            PredictionGateObstacleBinding binding =
                new PredictionGateObstacleBinding
                {
                    runId = gate.runId,
                    gateId = gate.gateId,
                    physicalLane = lane.physicalLane,
                    obstacleType = lane.obstacle.obstacleType
                };
            if (SpawnPredictionGateObstacle(segment,
                    lane.physicalLane, obstacleZ, binding,
                    lane.obstacle.prefabIndex) == null)
                continue;
            spawnedObstacles.Add(new SpawnedObstacleInfo
            {
                lane = lane.physicalLane,
                z = obstacleZ,
                type = lane.obstacle.obstacleType,
                challengeRole = EchoChallengeObstacleRole.None
            });
        }

        PlayerController playerController = _player != null
            ? _player.GetComponent<PlayerController>() : null;
        float jumpHeight = playerController != null
            ? playerController.jumpHeight : 3f;
        for (int laneIndex = 0;
             laneIndex < gate.lanes.Length; laneIndex++)
        {
            PredictionGateLane lane = gate.lanes[laneIndex];
            int count = Mathf.Max(0, lane.coinCount);
            float span = Mathf.Max(0f, count - 1)
                         * TrackSpawnRules.CoinSpacing;
            float startZ = lane.obstacle.isRequired
                ? obstacleZ - span * 0.5f
                : TrackSpawnRules.CoinSegmentMargin;
            startZ = Mathf.Clamp(startZ,
                TrackSpawnRules.CoinSegmentMargin,
                Mathf.Max(TrackSpawnRules.CoinSegmentMargin,
                    segmentLength - TrackSpawnRules.CoinSegmentMargin
                    - span));
            SpawnCoinTrail(segment, lane.physicalLane, startZ, count,
                spawnedObstacles, jumpHeight);
        }

        PredictionGateRowsSpawned++;
        AIRunTelemetry.RecordEvent("prediction_gate_spawned",
            gate.gateId, gate.sequence, gate.resolveDistance,
            gate.isFinal ? 1f : 0f);
    }

    private void SpawnPredictionGateVisual(GameObject segment,
        PredictionGateDefinition gate, float segmentStart)
    {
        if (segment == null || gate?.lanes == null) return;
        ClearPredictionGateVisual(segment);
        EnsurePredictionGateMaterial();

        var root = new GameObject(PredictionGateVisualRootName);
        root.transform.SetParent(segment.transform, false);
        // The authored graphite road is above the physics plane. Rest each
        // marker on a shared visible surface instead of burying it underneath
        // the opaque road mesh.
        root.transform.localPosition = new Vector3(0f,
            TrackGeometryStandards.AuthoredRoadSurfaceTopY
            + PredictionGateSurfaceClearance,
            PredictionGateVisualLocalZ(gate.commitDistance,
                gate.resolveDistance, segmentStart, segmentLength));

        float finalScale = gate.isFinal ? 1.12f : 1f;
        for (int index = 0; index < gate.lanes.Length; index++)
        {
            PredictionGateLane lane = gate.lanes[index];
            var laneRoot = new GameObject(
                "Lane_" + lane.physicalLane + "_" + lane.role);
            laneRoot.transform.SetParent(root.transform, false);
            laneRoot.transform.localPosition = new Vector3(
                (lane.physicalLane - 1) * laneDistance, 0f, 0f);
            laneRoot.transform.localScale = Vector3.one * finalScale;
            Color color = PredictionGateRoleColor(lane.role);
            const float ribbonHeight = 0.045f;
            const float decisionBandHeight = 0.065f;
            CreatePredictionGatePart(laneRoot.transform, "ApproachRibbon",
                new Vector3(0f, ribbonHeight * 0.5f,
                    -PredictionGateRibbonLength * 0.5f),
                new Vector3(PredictionGateRibbonWidth, ribbonHeight,
                    PredictionGateRibbonLength), color);
            CreatePredictionGatePart(laneRoot.transform, "DecisionBand",
                new Vector3(0f, decisionBandHeight * 0.5f, 0f),
                new Vector3(PredictionGateDecisionBandWidth,
                    decisionBandHeight,
                    0.24f), color);
            CreatePredictionGateSymbol(laneRoot.transform, lane.role, color);
        }
    }

    // Ground symbols carry the same roles as the colors. Keep them flat and
    // ahead of the decision band, within the existing obstacle clearance.
    private void CreatePredictionGateSymbol(Transform lane, PredictionGateRole role,
        Color color)
    {
        var symbol = new GameObject("RoleSymbol");
        symbol.transform.SetParent(lane, false);
        symbol.transform.localPosition = new Vector3(0f, 0f, 1.6f);
        if (role == PredictionGateRole.Predicted)
        {
            PredictionGateStroke(symbol.transform, "FrameLeft", -0.65f, -0.65f, -0.65f, 0.65f, color);
            PredictionGateStroke(symbol.transform, "FrameRight", 0.65f, -0.65f, 0.65f, 0.65f, color);
            PredictionGateStroke(symbol.transform, "FrameNear", -0.65f, -0.65f, 0.65f, -0.65f, color);
            PredictionGateStroke(symbol.transform, "FrameFar", -0.65f, 0.65f, 0.65f, 0.65f, color);
        }
        else if (role == PredictionGateRole.Counter)
        {
            PredictionGateStroke(symbol.transform, "TriangleLeft", -0.65f, -0.55f, 0f, 0.65f, color);
            PredictionGateStroke(symbol.transform, "TriangleRight", 0.65f, -0.55f, 0f, 0.65f, color);
            PredictionGateStroke(symbol.transform, "TriangleBase", -0.65f, -0.55f, 0.65f, -0.55f, color);
        }
        else
        {
            PredictionGateStroke(symbol.transform, "SafeNear", -0.65f, -0.30f, 0.65f, -0.30f, color);
            PredictionGateStroke(symbol.transform, "SafeFar", -0.65f, 0.30f, 0.65f, 0.30f, color);
        }
    }

    private void PredictionGateStroke(Transform parent, string name,
        float fromX, float fromZ, float toX, float toZ, Color color)
    {
        Vector3 from = new Vector3(fromX, 0.04f, fromZ);
        Vector3 to = new Vector3(toX, 0.04f, toZ);
        Vector3 direction = to - from;
        CreatePredictionGatePart(parent, name, (from + to) * 0.5f,
            new Vector3(0.18f, 0.08f, direction.magnitude), color,
            Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
    }

    public static float PredictionGateVisualLocalZ(float commitDistance,
        float resolveDistance, float segmentStart, float routeSegmentLength)
    {
        float safeLength = Mathf.Max(8f, routeSegmentLength);
        float obstacleLocalZ = Mathf.Clamp(resolveDistance - segmentStart,
            4f, safeLength - 4f);
        float authoredCommitLocalZ = commitDistance - segmentStart;
        return Mathf.Min(authoredCommitLocalZ,
            obstacleLocalZ - PredictionGateMinimumObstacleClearance);
    }

    public static Color PredictionGateRoleColor(PredictionGateRole role)
    {
        switch (role)
        {
            case PredictionGateRole.Predicted:
                return new Color(1f, 0.12f, 0.08f, 1f);
            case PredictionGateRole.Counter:
                return new Color(0.05f, 0.92f, 1f, 1f);
            default:
                return new Color(0.92f, 0.96f, 1f, 1f);
        }
    }

    private void EnsurePredictionGateMaterial()
    {
        if (_predictionGateMaterial != null) return;
        Shader shader = Shader.Find("EchoRun/FinishGate");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Mobile/Diffuse");
        if (shader == null) return;
        _predictionGateMaterial = new Material(shader)
        {
            name = "EchoPredictionGate_Runtime"
        };
    }

    private void CreatePredictionGatePart(Transform parent, string name,
        Vector3 localPosition, Vector3 localScale, Color color, float yaw = 0f)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            DestroyRuntimeObject(collider);
        }
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer == null || _predictionGateMaterial == null) return;
        renderer.sharedMaterial = _predictionGateMaterial;
        var properties = new MaterialPropertyBlock();
        properties.SetFloat("_GateRole", 2f);
        properties.SetFloat("_GateProgress", 1f);
        properties.SetColor("_CoreColor", color);
        properties.SetColor("_SignalColor", color);
        properties.SetColor("_StructureColor", color * 0.45f);
        properties.SetColor("_Color", color);
        renderer.SetPropertyBlock(properties);
    }

    private static void ClearPredictionGateVisual(GameObject segment)
    {
        if (segment == null) return;
        Transform existing = segment.transform.Find(
            PredictionGateVisualRootName);
        if (existing == null) return;
        existing.SetParent(null, false);
        DestroyRuntimeObject(existing.gameObject);
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    public static bool ShouldDeferChallengeContent(AITrackPlan plan)
    {
        return plan.echoEncounterKind == EchoEncounterKind.CounterTest
               && plan.echoChallengeStepId < 0;
    }

    private void UpdateChallengeRows(float playerRouteDistance)
    {
        AIShadowRunner shadow = AIShadowRunner.Instance;
        EchoChallengeStep activeStep = shadow != null
            ? shadow.ActiveChallengeStep : default;
        for (int i = _challengeRows.Count - 1; i >= 0; i--)
        {
            ChallengeRowRuntime row = _challengeRows[i];
            if (playerRouteDistance
                <= row.routeDistance + ChallengeSettlementMargin)
                continue;

            _challengeRows.RemoveAt(i);
            if (!ShouldMarkChallengeRowMissed(activeStep, row.stepId))
                continue;
            PlayerController player = _player != null
                ? _player.GetComponent<PlayerController>() : null;
            int playerLane = player != null ? player.CurrentLane : 1;
            if (shadow.ResolveChallengeStepAtGate(row.stepId, playerLane))
            {
                AIRunTelemetry.RecordEvent("echo_challenge_gate_resolved",
                    row.stepId, playerLane, row.routeDistance,
                    playerRouteDistance);
                continue;
            }

            ChallengeRowsMissed++;
            shadow.RecordChallengeStepMissed(row.stepId);
            AIRunTelemetry.RecordEvent("echo_challenge_cancelled", row.stepId,
                playerLane, row.routeDistance, playerRouteDistance);
        }
    }

    public static bool ShouldMarkChallengeRowMissed(
        EchoChallengeStep activeStep, int rowStepId)
    {
        return activeStep.IsActive && activeStep.stepId == rowStepId;
    }

    public static float RowsPer100Meters(int rowCount, float routeDistance)
    {
        return Mathf.Max(0, rowCount) * 100f
               / Mathf.Max(1f, routeDistance);
    }

    private AITrackPlan RefreshPlanForPreparedContent(AITrackPlan plan,
        float segmentRouteDistance)
    {
        if (IsSingleContractRun()) return plan;
        AIShadowRunner shadow = AIShadowRunner.Instance;
        EchoContractData contract = shadow != null
            ? shadow.ActiveContract : null;
        if (contract == null || contract.type == EchoContractType.None)
            return plan;

        EchoDuelPhase phaseOverride = EchoDuelPhase.None;
        if (_aiDirector != null
            && _aiDirector.ScheduledEchoPhase != EchoDuelPhase.None
            && segmentRouteDistance + 0.01f
            >= _aiDirector.ScheduledEchoBoundary)
        {
            phaseOverride = _aiDirector.ScheduledEchoPhase;
        }
        else if (shadow != null)
        {
            phaseOverride = shadow.DuelPhase;
        }

        int encounterStep = _aiDirector != null
            ? _aiDirector.ResolveEchoEncounterStepForRoute(
                phaseOverride, segmentRouteDistance, segmentLength,
                plan.echoEncounterStep)
            : plan.echoEncounterStep;
        EchoChallengeStep challengeStep = shadow != null
            ? shadow.ActiveChallengeStep : default;
        return AITrackDirector.ApplyEchoContract(plan, contract,
            encounterStep, phaseOverride, challengeStep);
    }

    public static bool ShouldSpawnTurn(bool canTurn, bool planShouldTurn,
        int straightSegmentsSinceLastTurn)
    {
        const int maxStraightSegments = 7;
        return canTurn && (planShouldTurn
                           || straightSegmentsSinceLastTurn >= maxStraightSegments);
    }

    private static bool IsSingleContractRun()
    {
        GameManager manager = GameManager.Instance;
        return manager != null
               && manager.ActiveGameplayFlowMode
               == GameplayFlowMode.SingleContract;
    }

    private static RunDifficultyLevel ActiveRunDifficulty()
    {
        GameManager manager = GameManager.Instance;
        return manager != null && manager.State != GameState.Menu
            ? manager.ActiveRunDifficulty
            : RunDifficultySettings.Current;
    }

    private void UpdateFinishMarker()
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null || _player == null)
        {
            SetFinishMarkerActive(false);
            return;
        }

        float remaining = gameManager.RemainingDistance;
        float visibleDistance = Mathf.Max(24f, segmentLength * 1.5f);
        if (remaining <= 0f || remaining > visibleDistance)
        {
            SetFinishMarkerActive(false);
            return;
        }

        EnsureFinishMarker();
        if (_finishMarkerMaterial != null
            && _finishMarkerMaterial.HasProperty("_GateProgress"))
            _finishMarkerMaterial.SetFloat("_GateProgress",
                Mathf.Clamp01(1f - remaining / visibleDistance));
        bool enableFinishLights = remaining <= 12f
                                  && VisualQualityController.Current
                                  == VisualQuality.High
                                  && PostFxController.SupportsHighFx(
                                      Application.platform);
        if (_finishMarkerLights != null)
        {
            for (int i = 0; i < _finishMarkerLights.Length; i++)
                if (_finishMarkerLights[i] != null)
                    _finishMarkerLights[i].enabled = enableFinishLights;
        }
        PlayerController controller = _player.GetComponent<PlayerController>();
        Vector3 forward = controller != null
            ? controller.ForwardDirection
            : _player.forward;
        float lateralOffset = controller != null
            ? controller.RenderedLateralOffset : 0f;
        GetTrackPoseAhead(_player.position, forward, lateralOffset, 1f, remaining,
            out Vector3 position, out Vector3 trackForward);
        position.y = 0f;
        _finishMarker.transform.SetPositionAndRotation(position,
            Quaternion.LookRotation(trackForward, Vector3.up));
        SetFinishMarkerActive(true);
    }

    private void EnsureFinishMarker()
    {
        if (_finishMarker != null) return;

        _finishMarker = new GameObject("FinishMarker");
        _finishMarker.transform.SetParent(transform, false);
        Shader shader = Shader.Find("EchoRun/FinishGate");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Mobile/Diffuse");
        if (shader != null)
        {
            _finishMarkerMaterial = new Material(shader)
            {
                name = "EchoFinishMarker_Runtime"
            };
            if (_finishMarkerMaterial.HasProperty("_Color"))
                _finishMarkerMaterial.color = new Color(0.05f, 0.95f, 1f, 1f);
        }

        CreateFinishMarkerPart("LeftPillar", PrimitiveType.Cube,
            new Vector3(-5.1f, 2.1f, 0f), new Vector3(0.42f, 4.2f, 0.58f), 0f);
        CreateFinishMarkerPart("RightPillar", PrimitiveType.Cube,
            new Vector3(5.1f, 2.1f, 0f), new Vector3(0.42f, 4.2f, 0.58f), 0f);
        CreateFinishMarkerPart("TopBeam", PrimitiveType.Cube,
            new Vector3(0f, 4.2f, 0f), new Vector3(10.55f, 0.42f, 0.58f), 0f);
        CreateFinishMarkerPart("LeftSignal", PrimitiveType.Cube,
            new Vector3(-4.92f, 2.1f, -0.34f), new Vector3(0.10f, 3.3f, 0.08f), 1f);
        CreateFinishMarkerPart("RightSignal", PrimitiveType.Cube,
            new Vector3(4.92f, 2.1f, -0.34f), new Vector3(0.10f, 3.3f, 0.08f), 1f);
        CreateFinishMarkerPart("LaneSignalLeft", PrimitiveType.Cube,
            new Vector3(-3f, 3.42f, 0f), new Vector3(2.6f, 0.16f, 0.18f), 1f);
        CreateFinishMarkerPart("LaneSignalCenter", PrimitiveType.Cube,
            new Vector3(0f, 3.42f, 0f), new Vector3(2.6f, 0.16f, 0.18f), 1f);
        CreateFinishMarkerPart("LaneSignalRight", PrimitiveType.Cube,
            new Vector3(3f, 3.42f, 0f), new Vector3(2.6f, 0.16f, 0.18f), 1f);
        CreateFinishMarkerPart("ProtocolCore", PrimitiveType.Sphere,
            new Vector3(0f, 4.18f, -0.42f), new Vector3(1.15f, 1.15f, 0.42f), 2f);
        CreateFinishMarkerPart("CoreSpine", PrimitiveType.Cube,
            new Vector3(0f, 3.18f, -0.20f), new Vector3(0.16f, 1.45f, 0.16f), 2f);
        _finishMarkerLights = new[]
        {
            CreateFinishPointLight("FinishLightLeft",
                new Vector3(-3.6f, 2.4f, -0.6f)),
            CreateFinishPointLight("FinishLightRight",
                new Vector3(3.6f, 2.4f, -0.6f))
        };
        _finishMarker.SetActive(false);
    }

    private Light CreateFinishPointLight(string name, Vector3 localPosition)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(_finishMarker.transform, false);
        lightObject.transform.localPosition = localPosition;
        Light point = lightObject.AddComponent<Light>();
        point.type = LightType.Point;
        point.range = 5.5f;
        point.intensity = 0.58f;
        point.color = new Color(0.18f, 0.82f, 1f);
        point.shadows = LightShadows.None;
        point.enabled = false;
        return point;
    }

    private void CreateFinishMarkerPart(string name, PrimitiveType primitive,
        Vector3 localPosition, Vector3 localScale, float role)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = name;
        part.transform.SetParent(_finishMarker.transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null && _finishMarkerMaterial != null)
        {
            renderer.sharedMaterial = _finishMarkerMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_GateRole", role);
            renderer.SetPropertyBlock(properties);
        }
    }

    private void SetFinishMarkerActive(bool active)
    {
        if (_finishMarker != null && _finishMarker.activeSelf != active)
            _finishMarker.SetActive(active);
    }

    void RecycleSegment(GameObject segment)
    {
        TrackSegmentData data = segment.GetComponent<TrackSegmentData>();

        // Clear cached turn if this is the one being recycled
        if (data != null && data == CurrentTurnSegment)
            CurrentTurnSegment = null;
        ClearPredictionGateVisual(segment);

        // Return dynamic objects owned by this segment to their pools.
        for (int i = _dynamicObjects.Count - 1; i >= 0; i--)
        {
            DynamicEntry entry = _dynamicObjects[i];
            if (entry.instance == null)
            {
                _dynamicObjects.RemoveAt(i);
                continue;
            }
            if (entry.ownerSegment == segment)
            {
                ReturnDynamicToPool(entry);
                _dynamicObjects.RemoveAt(i);
            }
        }

        segment.SetActive(false);
        _activeSegments.RemoveAt(0);

        if (data != null)
        {
            switch (data.segmentType)
            {
                case TrackSegmentType.TurnLeft:
                    _turnLeftPool.Enqueue(segment);
                    break;
                case TrackSegmentType.TurnRight:
                    _turnRightPool.Enqueue(segment);
                    break;
                default:
                    _straightPool.Enqueue(segment);
                    break;
            }
        }
        else
        {
            _straightPool.Enqueue(segment);
        }
    }

   void SpawnObstaclesAndCoins(GameObject segment, TrackSegmentType segType, AITrackPlan plan)
   {
       if ((obstaclePrefabs == null || obstaclePrefabs.Length == 0)
           && coinPrefab == null) return;

       float buffer = 4f;
       float end    = segmentLength - 2f;

       if (segType != TrackSegmentType.Straight)
       {
           SpawnTurnCoinGuide(segment, segType);
           return;
       }

        // Warmup: first few segments have no obstacles
        const int warmupSegments = 1;
        _straightSegmentsSpawned++;
        _obstacleFreeSegments++;
        for (int lane = 0; lane < _laneObstacleDrought.Length; lane++)
            _laneObstacleDrought[lane]++;
        plan = BalanceCounterEncounterLanes(plan, _laneObstacleDrought);

        float diff = Mathf.Clamp01(plan.difficulty);

        // Preserve route continuity while rotating protection away from lanes
        // that have gone too long without an obstacle.
        int safeLane = plan.echoEncounterKind != EchoEncounterKind.None
            ? Mathf.Clamp(plan.echoSafeChoiceLane, 0, 2)
            : ChooseContractSafeLane(
                plan.echoContractType, plan.safeLane, _lastSafeLane,
                _laneObstacleDrought, plan.echoChallengeLane);
        _lastSafeLane = safeLane;

        // Determine coin Z first so obstacles can avoid it
        float coinZ = AIRunRandom.Range(buffer + 2f, end - 4f);

        // Plan rewards first, then instantiate them after obstacle types and
        // positions are known. This keeps jump rewards aligned with the actual
        // player jump instead of leaving ground coins inside a jump obstacle.
        var coinTrails = new List<CoinTrailPlan>(3);

        EchoEncounterLaneChoice[] encounterChoices =
            BuildEchoEncounterLaneChoices(plan);
        if (encounterChoices.Length > 0)
        {
            for (int choiceIndex = 0;
                 choiceIndex < encounterChoices.Length; choiceIndex++)
            {
                EchoEncounterLaneChoice choice =
                    encounterChoices[choiceIndex];
                int choiceMin = Mathf.Max(1, choice.minCoinCount);
                int choiceMax = Mathf.Max(choiceMin, choice.maxCoinCount);
                coinTrails.Add(new CoinTrailPlan
                {
                    lane = choice.lane,
                    startZ = coinZ + choiceIndex * 0.35f,
                    count = AIRunRandom.Range(choiceMin, choiceMax + 1),
                    echoContractMarker = choice.echoContractMarker,
                    challengeStepId = plan.echoEncounterKind
                                      == EchoEncounterKind.CounterTest
                                      && plan.echoEncounterContractType
                                      == EchoContractType.BreakLaneHabit
                        ? Mathf.Max(0, plan.echoChallengeStepId) : 0
                });
            }
        }
        else
        {
            int minCoins = Mathf.Max(2, plan.minCoinCount);
            int maxCoins = Mathf.Max(minCoins + 1, plan.maxCoinCount);
            coinTrails.Add(new CoinTrailPlan
            {
                lane = safeLane,
                startZ = coinZ,
                count = AIRunRandom.Range(minCoins, maxCoins),
                echoContractMarker = plan.echoContractType
                                     == EchoContractType.BreakLaneHabit
            });
        }
        int echoChallengeLane = plan.echoChallengeLane;
        if (encounterChoices.Length == 0
            && (plan.echoContractType == EchoContractType.ChangeVerticalHabit
                || plan.echoContractType == EchoContractType.DisruptRhythm))
        {
            if (echoChallengeLane < 0 || echoChallengeLane > 2
                || echoChallengeLane == safeLane)
                echoChallengeLane = (safeLane + 1) % 3;
            // The contract route carries visibly denser rewards but remains
            // optional: the untouched safe lane preserves route solvability.
            coinTrails.Add(new CoinTrailPlan
            {
                lane = echoChallengeLane,
                startZ = coinZ + 0.6f,
                count = AIRunRandom.Range(7, 10)
            });
        }
        // Sometimes add sparse coins on an adjacent lane
        if (encounterChoices.Length == 0
            && AIRunRandom.Value < plan.coinChance)
        {
            int altLane = (safeLane + (AIRunRandom.Value < 0.5f ? -1 : 1) + 3) % 3;
            float altStartZ = coinZ + AIRunRandom.Range(-1f, 1f);
            int altCount = AIRunRandom.Range(2, 5);
            if (altLane != echoChallengeLane)
            {
                coinTrails.Add(new CoinTrailPlan
                {
                    lane = altLane,
                    startZ = altStartZ,
                    count = altCount
                });
            }
        }

        var spawnedObstacles = new List<SpawnedObstacleInfo>(2);
        float currentSpeed = GameManager.Instance != null
            ? GameManager.Instance.CurrentSpeed
            : 10f;
        PlayerController playerController = _player != null
            ? _player.GetComponent<PlayerController>()
            : null;
        float jumpDuration = playerController != null
            ? playerController.jumpDuration
            : 0.6f;
        float jumpHeight = playerController != null
            ? playerController.jumpHeight
            : 3f;
        TrackSegmentData segmentData = segment.GetComponent<TrackSegmentData>();
        float segmentRouteDistance = segmentData != null
            ? segmentData.routeDistance
            : _plannedDistance;
        bool challengeObstacleReady = false;
        float challengeRouteDistance = segmentRouteDistance + coinZ;
        bool prefabsReady = obstaclePrefabs != null && obstaclePrefabs.Length >= 3;
        bool guaranteedContractRow = RequiresGuaranteedEchoEncounterRow(plan);
        bool shouldSpawnObstacles = prefabsReady
            && (guaranteedContractRow
                ? _straightSegmentsSpawned > warmupSegments
                : ShouldSpawnObstacleRow(
                    _straightSegmentsSpawned, _obstacleFreeSegments,
                    warmupSegments,
                    RunDifficultySettings.ResolveMaxFreeSegments(
                        maxConsecutiveObstacleFreeStraights,
                        ActiveRunDifficulty()),
                    plan.obstacleChance, AIRunRandom.Value));
        if (shouldSpawnObstacles)
        {
            plan = PrepareCounterObstacleRowPlan(
                plan, _counterattackRowsSpawned);
            // Counterattack rows deliberately move between early, middle and
            // late positions. Ordinary rows keep their coin-aware placement.
            float obsZ;
            if (plan.echoEncounterKind == EchoEncounterKind.CounterTest)
            {
                obsZ = EchoObstacleRowZ(plan, buffer + 1f, end - 1f,
                    AIRunRandom.Value);
            }
            else
            {
                obsZ = coinZ + 3f + AIRunRandom.Range(0f, 3f);
                if (obsZ > end - 1f)
                    obsZ = coinZ - 3f - AIRunRandom.Range(0f, 3f);
                obsZ = Mathf.Clamp(obsZ, buffer + 1f, end - 1f);
            }
            float firstObstacleRouteDistance = segmentRouteDistance + obsZ
                + EchoObstacleMinimumLaneOffset(plan);
            float minimumSpacing = TrackSpawnRules.MinimumObstacleRowSpacing(
                currentSpeed, jumpDuration, segmentLength,
                RunDifficultySettings.ObstacleRecoverySeconds(
                    ActiveRunDifficulty()))
                * EchoObstacleSpacingMultiplier(plan);
            if (TrackSpawnRules.CanSpawnObstacleRow(
                    firstObstacleRouteDistance, _lastObstacleRouteDistance,
                    minimumSpacing))
            {
                int spawned = SpawnObstacleRow(
                    segment, obsZ, diff, safeLane, plan.maxBlockedLanes,
                    plan, echoChallengeLane, spawnedObstacles);
                if (spawned > 0)
                {
                    if (plan.echoContractType == EchoContractType.DisruptRhythm)
                        _rhythmContractRowsSpawned++;
                    if (plan.echoEncounterKind == EchoEncounterKind.CounterTest)
                        _counterattackRowsSpawned++;
                    _obstacleFreeSegments = 0;
                    ObstacleRowsSpawned++;
                    float obstacleGap = float.IsNegativeInfinity(
                        _lastObstacleRouteDistance)
                        ? 0f
                        : firstObstacleRouteDistance
                          - _lastObstacleRouteDistance;
                    LongestObstacleRowGap = Mathf.Max(LongestObstacleRowGap,
                        obstacleGap);
                    AIRunTelemetry.RecordEvent("obstacle_row_spawned",
                        (int)plan.echoEncounterKind, plan.echoChallengeStepId,
                        firstObstacleRouteDistance, obstacleGap);
                    float lastObstacleZ = spawnedObstacles[0].z;
                    for (int obstacleIndex = 1;
                         obstacleIndex < spawnedObstacles.Count; obstacleIndex++)
                        lastObstacleZ = Mathf.Max(lastObstacleZ,
                            spawnedObstacles[obstacleIndex].z);
                    _lastObstacleRouteDistance = segmentRouteDistance
                                                 + lastObstacleZ;
                    bool hasJumpOpportunity = false;
                    bool hasSlideOpportunity = false;
                    for (int obstacleIndex = 0;
                         obstacleIndex < spawnedObstacles.Count; obstacleIndex++)
                    {
                        SpawnedObstacleInfo info = spawnedObstacles[obstacleIndex];
                        if (info.type == ObstacleType.High)
                            hasJumpOpportunity = true;
                        else if (info.type == ObstacleType.Low)
                            hasSlideOpportunity = true;
                        if (info.challengeRole
                            != EchoChallengeObstacleRole.Required)
                            continue;
                        challengeObstacleReady = true;
                        challengeRouteDistance = segmentRouteDistance + info.z;
                    }
                    if (hasJumpOpportunity) JumpOpportunityRows++;
                    if (hasSlideOpportunity) SlideOpportunityRows++;
                    AIRunTelemetry.RecordEvent("obstacle_row_actions",
                        (hasJumpOpportunity ? 1 : 0)
                        | (hasSlideOpportunity ? 2 : 0),
                        plan.echoChallengeLane,
                        RowsPer100Meters(ObstacleRowsSpawned,
                            firstObstacleRouteDistance),
                        plan.echoChallengeStepId);
                }
            }
            else
            {
                ObstacleRowsRejectedForSpacing++;
                AIRunTelemetry.RecordEvent("obstacle_row_spacing_rejected",
                    (int)plan.echoEncounterKind, plan.echoChallengeStepId,
                    firstObstacleRouteDistance, minimumSpacing);
            }
        }
        else if (prefabsReady && _straightSegmentsSpawned > warmupSegments)
        {
            ObstacleRowsSkippedByChance++;
        }

        for (int i = 0; i < coinTrails.Count; i++)
        {
            CoinTrailPlan trail = coinTrails[i];
            SpawnCoinTrail(segment, trail.lane, trail.startZ, trail.count,
                spawnedObstacles, jumpHeight, trail.echoContractMarker,
                trail.challengeStepId);
        }

        bool laneChallengeReady = plan.echoChallengeStepId > 0
                                  && plan.echoEncounterKind
                                  == EchoEncounterKind.CounterTest
                                  && plan.echoEncounterContractType
                                  == EchoContractType.BreakLaneHabit
                                  && coinPrefab != null;
        if (laneChallengeReady || challengeObstacleReady)
        {
            RegisterChallengeRow(plan, challengeRouteDistance);
        }
   }

    private void RegisterChallengeRow(AITrackPlan plan, float routeDistance)
    {
        if (plan.echoChallengeStepId <= 0) return;
        AIShadowRunner shadow = AIShadowRunner.Instance;
        if (shadow == null || !shadow.BindChallengeStep(
                plan.echoChallengeStepId, plan.echoPredictedLane,
                plan.echoRiskChoiceLane, plan.echoSafeChoiceLane,
                routeDistance))
            return;

        _challengeRows.Add(new ChallengeRowRuntime
        {
            stepId = plan.echoChallengeStepId,
            routeDistance = routeDistance
        });
        ChallengeRowsSpawned++;
        GameManager gameManager = GameManager.Instance;
        float playerDistance = gameManager != null ? gameManager.Distance : 0f;
        float currentSpeed = gameManager != null
            ? gameManager.CurrentSpeed : 10f;
        float warningSeconds = Mathf.Max(0f, routeDistance - playerDistance)
                               / Mathf.Max(1f, currentSpeed);
        MinimumChallengeWarningSeconds = Mathf.Min(
            MinimumChallengeWarningSeconds, warningSeconds);
        AIRunTelemetry.RecordEvent("echo_challenge_spawned",
            plan.echoChallengeStepId, plan.echoRiskChoiceLane,
            routeDistance, (float)plan.echoTargetAction);
        AIRunTelemetry.RecordEvent("echo_challenge_warning",
            plan.echoChallengeStepId, plan.echoRiskChoiceLane,
            warningSeconds, (float)plan.echoTargetAction);
    }

    float CalculateBaseDifficulty()
    {
        float speedFactor = GameManager.Instance != null
            ? Mathf.InverseLerp(GameManager.Instance.startSpeed,
                GameManager.Instance.maxSpeed, GameManager.Instance.CurrentSpeed)
            : 0f;
        float segmentFactor = Mathf.Clamp01(_straightSegmentsSpawned / 15f);
        return Mathf.Max(speedFactor, segmentFactor);
    }

    AITrackPlan CreateFallbackPlan(float difficulty, bool canTurn)
    {
        int safeLane = Mathf.Clamp(
            _lastSafeLane + AIRunRandom.Range(-1, 2), 0, 2);
        return new AITrackPlan
        {
            intent = AIDirectorIntent.Observe,
            difficulty = difficulty,
            obstacleChance = Mathf.Lerp(
                obstacleChance, Mathf.Clamp01(obstacleChance + 0.3f), difficulty),
            coinChance = coinChance,
            minCoinCount = 5,
            maxCoinCount = 8,
            maxBlockedLanes = difficulty > 0.5f ? 2 : 1,
            safeLane = safeLane,
            shouldTurn = canTurn && AIRunRandom.Value < turnChance,
            echoContractType = EchoContractType.None,
            echoChallengeLane = -1,
            echoTargetAction = ShadowAction.Keep,
            echoEncounterKind = EchoEncounterKind.None,
            echoEncounterContractType = EchoContractType.None,
            echoChallengeStepId = 0,
            echoPredictedLane = -1,
            echoSafeChoiceLane = -1,
            echoRiskChoiceLane = -1,
            echoPredictedAction = ShadowAction.Keep,
            echoObstaclePattern = EchoObstaclePattern.Standard,
            echoObstacleSpacingBand = 0,
            echoObstacleLayoutStep = 0
        };
    }

    // ---- coin patterns ----

    void SpawnCoinTrail(GameObject segment, int lane, float startZ, int count,
        List<SpawnedObstacleInfo> obstacles, float jumpHeight,
        bool echoContractMarker = false, int challengeStepId = 0)
    {
        if (coinPrefab == null) return;
        if (TryGetOverlappingJumpObstacle(
                lane, startZ, count, obstacles, out float obstacleZ))
        {
            SpawnJumpCoinArc(segment, lane, obstacleZ, jumpHeight,
                echoContractMarker, challengeStepId);
            return;
        }

        float x = (lane - 1) * laneDistance;
        Quaternion routeRotation = TrackSpawnRules.CoinRouteRotation(
            segment.transform.rotation, Vector3.forward);
        for (int c = 0; c < count; c++)
        {
            Vector3 lp = new Vector3(x, TrackSpawnRules.GroundCoinHeight,
                startZ + c * TrackSpawnRules.CoinSpacing);
            if (lp.z > segmentLength - 1f) break;
            Vector3 wp = segment.transform.TransformPoint(lp);
            SpawnCoinInstance(segment, wp, routeRotation, echoContractMarker,
                challengeStepId);
        }
    }

    bool TryGetOverlappingJumpObstacle(int lane, float startZ, int count,
        List<SpawnedObstacleInfo> obstacles, out float obstacleZ)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            SpawnedObstacleInfo obstacle = obstacles[i];
            if (obstacle.lane != lane || obstacle.type != ObstacleType.High)
                continue;
            float halfDepth = ObstacleGeometryRules.ColliderSize(obstacle.type).z
                              * 0.5f;
            if (!TrackSpawnRules.CoinTrailOverlapsObstacle(
                    startZ, count, TrackSpawnRules.CoinSpacing,
                    obstacle.z, halfDepth))
                continue;
            obstacleZ = obstacle.z;
            return true;
        }

        obstacleZ = 0f;
        return false;
    }

    void SpawnJumpCoinArc(GameObject segment, int lane, float obstacleZ,
        float jumpHeight, bool echoContractMarker = false,
        int challengeStepId = 0)
    {
        int count = TrackSpawnRules.JumpRewardCoinCount;
        float spacing = TrackSpawnRules.CoinSpacing;
        float halfSpan = (count - 1) * spacing * 0.5f;
        float startZ = obstacleZ - halfSpan;
        float x = (lane - 1) * laneDistance;
        Quaternion routeRotation = TrackSpawnRules.CoinRouteRotation(
            segment.transform.rotation, Vector3.forward);
        for (int c = 0; c < count; c++)
        {
            float progress = count > 1 ? (float)c / (count - 1) : 0.5f;
            float z = startZ + c * spacing;
            if (z < TrackSpawnRules.CoinSegmentMargin
                || z > segmentLength - TrackSpawnRules.CoinSegmentMargin)
                continue;
            float y = TrackSpawnRules.JumpCoinHeight(progress,
                TrackSpawnRules.GroundCoinHeight, jumpHeight);
            Vector3 wp = segment.transform.TransformPoint(
                new Vector3(x, y, z));
            SpawnCoinInstance(segment, wp, routeRotation, echoContractMarker,
                challengeStepId);
        }
    }

   void SpawnTurnCoinGuide(GameObject segment, TrackSegmentType segmentType)
    {
        if (coinPrefab == null) return;
        int turnDirection = segmentType == TrackSegmentType.TurnRight ? 1 : -1;
        for (int index = 0;
             index < TrackSpawnRules.TurnGuideCoinCount; index++)
        {
            Vector3 localPosition = TrackSpawnRules.TurnGuideCoinLocalPosition(
                segmentLength, turnDirection, index);
            Vector3 worldPosition = segment.transform.TransformPoint(
                localPosition);
            Vector3 localTangent = TrackSpawnRules.TurnGuideCoinLocalTangent(
                segmentLength, turnDirection, index);
            Quaternion routeRotation = TrackSpawnRules.CoinRouteRotation(
                segment.transform.rotation, localTangent);
            SpawnCoinInstance(segment, worldPosition, routeRotation, false);
        }
    }

   // ---- obstacle patterns ----

    // Guarantees at least 1 lane is always open
    int SpawnObstacleRow(GameObject segment, float obsZ, float difficulty, int safeLane,
        int maxBlockedLanes, AITrackPlan plan, int echoChallengeLane,
        List<SpawnedObstacleInfo> spawnedObstacles)
    {
       if (obstaclePrefabs == null || obstaclePrefabs.Length < 3) return 0;

       // How many lanes to block (1 or 2, never 3)
        int blocked = difficulty > 0.5f ? 2 : 1;
        blocked = Mathf.Clamp(blocked, 1, Mathf.Clamp(maxBlockedLanes, 1, 2));
        int[] lanes = plan.echoEncounterKind != EchoEncounterKind.None
            ? SelectEchoEncounterBlockedLanes(plan, _laneObstacleDrought)
            : SelectContractBlockedLanes(
                safeLane, blocked, _laneObstacleDrought,
                plan.echoContractType, echoChallengeLane);
        int spawned = 0;

       for (int i = 0; i < lanes.Length; i++)
       {
           int lane = lanes[i];

            // Full-height barriers were visually ambiguous and could create
            // jump sequences with no recoverable timing window. Lane changes
            // remain meaningful because every row still preserves a safe lane.
            int type = SelectEchoEncounterObstaclePrefabIndex(
                plan, lane,
                (plan.echoEncounterContractType == EchoContractType.DisruptRhythm
                 || plan.echoContractType == EchoContractType.DisruptRhythm)
                    ? _rhythmContractRowsSpawned : _straightSegmentsSpawned,
                difficulty, AIRunRandom.Value);

            float obstacleZ = obsZ
                + (plan.echoEncounterKind == EchoEncounterKind.CounterTest
                    ? EchoObstacleLaneOffset(plan, lane)
                    : AIRunRandom.Range(-0.8f, 0.8f));
            obstacleZ = Mathf.Clamp(obstacleZ, 1f, segmentLength - 1f);
            Obstacle obstacleData = obstaclePrefabs[type].GetComponent<Obstacle>();
            ObstacleType obstacleType = obstacleData != null
                ? obstacleData.type
                : (ObstacleType)Mathf.Clamp(type, 0, 2);
            if (obstacleType == ObstacleType.High)
            {
                obstacleZ = TrackSpawnRules.ClampJumpRewardCenter(
                    obstacleZ, segmentLength,
                    TrackSpawnRules.JumpRewardCoinCount,
                    TrackSpawnRules.CoinSpacing,
                    TrackSpawnRules.CoinSegmentMargin);
            }

            EchoChallengeObstacleRole challengeRole =
                EchoChallengeObstacleRole.None;
            if (plan.echoChallengeStepId > 0
                && plan.echoEncounterKind == EchoEncounterKind.CounterTest)
            {
                if (lane == plan.echoRiskChoiceLane)
                    challengeRole = EchoChallengeObstacleRole.Required;
                else if (lane == plan.echoPredictedLane)
                    challengeRole = EchoChallengeObstacleRole.Predicted;
            }
            EchoChallengeObstacleBinding binding = new EchoChallengeObstacleBinding
            {
                stepId = challengeRole == EchoChallengeObstacleRole.None
                    ? 0 : plan.echoChallengeStepId,
                role = challengeRole,
                action = obstacleType == ObstacleType.High
                    ? ShadowAction.Jump : obstacleType == ObstacleType.Low
                        ? ShadowAction.Slide : ShadowAction.Keep,
                lane = lane
            };

            if (SpawnObstacleAtWithBinding(
                    segment, lane, obstacleZ, type, binding) != null)
            {
                _laneObstacleDrought[lane] = 0;
                spawnedObstacles.Add(new SpawnedObstacleInfo
                {
                    lane = lane,
                    z = obstacleZ,
                    type = obstacleType,
                    challengeRole = challengeRole
                });
                spawned++;
            }
       }
       return spawned;
    }

    bool SpawnObstacleAt(GameObject segment, int lane, float z,
        int prefabIndex)
    {
        return SpawnObstacleAtWithBinding(
            segment, lane, z, prefabIndex, default) != null;
    }

    GameObject SpawnObstacleAtWithBinding(GameObject segment, int lane,
        float z, int prefabIndex, EchoChallengeObstacleBinding binding)
    {
        if (obstaclePrefabs == null || prefabIndex < 0
            || prefabIndex >= obstaclePrefabs.Length || obstaclePrefabs[prefabIndex] == null)
            return null;

        float x = (lane - 1) * laneDistance;
        Vector3 lp = new Vector3(x, 1f, z);
        Vector3 wp = segment.transform.TransformPoint(lp);
        Quaternion rot = segment.transform.rotation;
        GameObject instance = SpawnDynamic(
            obstaclePrefabs[prefabIndex], segment, wp, rot);
        if (instance == null) return null;
        instance.GetComponent<PredictionGateObstacleTag>()?.Clear();
        EchoChallengeObstacleTag tag = instance.GetComponent<
            EchoChallengeObstacleTag>();
        if (binding.IsBound)
        {
            if (tag == null) tag = instance.AddComponent<EchoChallengeObstacleTag>();
            tag.Configure(binding);
        }
        else
        {
            tag?.Clear();
        }
        return instance;
    }

    private GameObject SpawnPredictionGateObstacle(GameObject segment,
        int lane, float z, PredictionGateObstacleBinding binding,
        int prefabIndex)
    {
        GameObject instance = SpawnObstacleAtWithBinding(
            segment, lane, z, prefabIndex, default);
        if (instance == null) return null;
        PredictionGateObstacleTag tag =
            instance.GetComponent<PredictionGateObstacleTag>();
        if (tag == null)
            tag = instance.AddComponent<PredictionGateObstacleTag>();
        tag.Configure(binding);
        return instance;
    }

    public static bool ShouldSpawnObstacleRow(int straightSegmentsSpawned,
        int obstacleFreeSegments, int warmupSegments, int maxFreeSegments,
        float chance, float chanceRoll)
    {
        return TrackSpawnRules.ShouldSpawnObstacleRow(straightSegmentsSpawned,
            obstacleFreeSegments, warmupSegments, maxFreeSegments, chance,
            chanceRoll);
    }

    public static int ChooseFairSafeLane(int proposedLane, int previousSafeLane,
        int[] laneObstacleDrought)
    {
        return TrackSpawnRules.ChooseFairSafeLane(
            proposedLane, previousSafeLane, laneObstacleDrought);
    }

    public static int[] SelectBlockedLanes(int safeLane, int blockedLaneCount,
        int[] laneObstacleDrought)
    {
        return TrackSpawnRules.SelectBlockedLanes(
            safeLane, blockedLaneCount, laneObstacleDrought);
    }

    public static int ChooseContractSafeLane(EchoContractType contractType,
        int proposedLane, int previousSafeLane, int[] laneObstacleDrought,
        int challengeLane = -1)
    {
        if (contractType == EchoContractType.BreakLaneHabit)
            return Mathf.Clamp(proposedLane, 0, 2);

        int safe = ChooseFairSafeLane(
            proposedLane, previousSafeLane, laneObstacleDrought);
        if ((contractType != EchoContractType.ChangeVerticalHabit
             && contractType != EchoContractType.DisruptRhythm)
            || safe != challengeLane)
            return safe;

        int previous = Mathf.Clamp(previousSafeLane, 0, 2);
        for (int offset = 1; offset <= 2; offset++)
        {
            int left = previous - offset;
            if (left >= 0 && left != challengeLane) return left;
            int right = previous + offset;
            if (right <= 2 && right != challengeLane) return right;
        }
        return (Mathf.Clamp(challengeLane, 0, 2) + 1) % 3;
    }

    public static bool RequiresGuaranteedContractRow(
        EchoContractType contractType)
    {
        return contractType == EchoContractType.ChangeVerticalHabit
               || contractType == EchoContractType.DisruptRhythm;
    }

    public static bool RequiresGuaranteedEchoEncounterRow(AITrackPlan plan)
    {
        if (plan.echoEncounterKind == EchoEncounterKind.None)
            return RequiresGuaranteedContractRow(plan.echoContractType);
        if (plan.echoEncounterKind == EchoEncounterKind.CounterTest
            && plan.echoChallengeStepId < 0)
            return false;
        if (plan.echoEncounterKind == EchoEncounterKind.DetectionEvidence)
            return plan.echoEncounterStep % 2 == 0;
        return true;
    }

    public static EchoEncounterLaneChoice[] BuildEchoEncounterLaneChoices(
        AITrackPlan plan)
    {
        if (plan.echoEncounterKind == EchoEncounterKind.None)
            return new EchoEncounterLaneChoice[0];

        bool laneContract = plan.echoEncounterContractType
                            == EchoContractType.BreakLaneHabit;
        if (plan.echoEncounterKind == EchoEncounterKind.DetectionEvidence)
        {
            return new[]
            {
                new EchoEncounterLaneChoice
                {
                    lane = Mathf.Clamp(plan.echoPredictedLane, 0, 2),
                    minCoinCount = 5,
                    maxCoinCount = 7,
                    echoContractMarker = true
                },
                new EchoEncounterLaneChoice
                {
                    lane = Mathf.Clamp(plan.echoSafeChoiceLane, 0, 2),
                    minCoinCount = 5,
                    maxCoinCount = 7,
                    echoContractMarker = true
                },
                new EchoEncounterLaneChoice
                {
                    lane = Mathf.Clamp(plan.echoRiskChoiceLane, 0, 2),
                    minCoinCount = 5,
                    maxCoinCount = 7,
                    echoContractMarker = true
                }
            };
        }

        bool isScoredChoice = plan.echoEncounterKind
                              == EchoEncounterKind.RevealChoice
                              || plan.echoEncounterKind
                              == EchoEncounterKind.ResistanceTest
                              || plan.echoEncounterKind
                              == EchoEncounterKind.CounterTest
                              || plan.echoEncounterKind
                              == EchoEncounterKind.FinaleOldHabit
                              || plan.echoEncounterKind
                              == EchoEncounterKind.FinaleCounterHabit
                              || plan.echoEncounterKind
                              == EchoEncounterKind.FinaleFreeChoice;
        bool marker = laneContract
                      && (isScoredChoice
                          || plan.echoEncounterKind
                          == EchoEncounterKind.RewriteChoice);
        int predictedMin = 8;
        int predictedMax = 10;
        int safeMin = 3;
        int safeMax = 4;
        int riskMin = 8;
        int riskMax = 10;
        switch (plan.echoEncounterKind)
        {
            case EchoEncounterKind.RewriteChoice:
                predictedMin = 3;
                predictedMax = 4;
                safeMin = 3;
                safeMax = 4;
                riskMin = 6;
                riskMax = 8;
                break;
            case EchoEncounterKind.FinaleOldHabit:
                predictedMin = 9;
                predictedMax = 11;
                safeMin = 4;
                safeMax = 5;
                riskMin = 7;
                riskMax = 9;
                break;
            case EchoEncounterKind.FinaleCounterHabit:
                predictedMin = 7;
                predictedMax = 9;
                riskMin = 10;
                riskMax = 12;
                break;
            case EchoEncounterKind.FinaleFreeChoice:
                predictedMin = 6;
                predictedMax = 8;
                safeMin = 4;
                safeMax = 5;
                riskMin = 10;
                riskMax = 12;
                break;
        }
        return new[]
        {
            new EchoEncounterLaneChoice
            {
                lane = Mathf.Clamp(plan.echoPredictedLane, 0, 2),
                minCoinCount = predictedMin,
                maxCoinCount = predictedMax,
                echoContractMarker = marker
            },
            new EchoEncounterLaneChoice
            {
                lane = Mathf.Clamp(plan.echoSafeChoiceLane, 0, 2),
                minCoinCount = safeMin,
                maxCoinCount = safeMax,
                echoContractMarker = marker
            },
            new EchoEncounterLaneChoice
            {
                lane = Mathf.Clamp(plan.echoRiskChoiceLane, 0, 2),
                minCoinCount = riskMin,
                maxCoinCount = riskMax,
                echoContractMarker = marker
            }
        };
    }

    public static int[] SelectEchoEncounterBlockedLanes(AITrackPlan plan,
        int[] laneObstacleDrought)
    {
        if (plan.echoEncounterKind == EchoEncounterKind.None)
        {
            return SelectContractBlockedLanes(
                plan.safeLane, plan.maxBlockedLanes, laneObstacleDrought,
                plan.echoContractType, plan.echoChallengeLane);
        }

        int safe = Mathf.Clamp(plan.echoSafeChoiceLane, 0, 2);
        bool oldActionHabit = plan.echoEncounterKind
                              == EchoEncounterKind.FinaleOldHabit
                              && plan.echoEncounterContractType
                              != EchoContractType.BreakLaneHabit;
        int primary = plan.echoEncounterKind
                      == EchoEncounterKind.DetectionEvidence
                      || oldActionHabit
            ? Mathf.Clamp(plan.echoPredictedLane, 0, 2)
            : Mathf.Clamp(plan.echoRiskChoiceLane, 0, 2);
        if (primary == safe) primary = (safe + 1) % 3;
        int count = Mathf.Clamp(plan.maxBlockedLanes, 1, 2);
        if (count == 1) return new[] { primary };

        int secondary = Mathf.Clamp(plan.echoPredictedLane, 0, 2);
        if (secondary == safe || secondary == primary)
        {
            for (int lane = 0; lane < 3; lane++)
            {
                if (lane == safe || lane == primary) continue;
                secondary = lane;
                break;
            }
        }

        if (plan.echoEncounterKind == EchoEncounterKind.CounterTest
            && plan.echoObstaclePattern == EchoObstaclePattern.RiskOnly
            && LaneObstacleDrought(laneObstacleDrought, secondary) < 2)
            return new[] { primary };

        return new[] { primary, secondary };
    }

    public static AITrackPlan BalanceCounterEncounterLanes(AITrackPlan plan,
        int[] laneObstacleDrought)
    {
        if (plan.echoEncounterKind != EchoEncounterKind.CounterTest)
            return plan;

        int predicted = Mathf.Clamp(plan.echoPredictedLane, 0, 2);
        int first = (predicted + 1) % 3;
        int second = (predicted + 2) % 3;
        int firstDrought = LaneObstacleDrought(laneObstacleDrought, first);
        int secondDrought = LaneObstacleDrought(laneObstacleDrought, second);
        int risk;
        if (firstDrought > secondDrought)
            risk = first;
        else if (secondDrought > firstDrought)
            risk = second;
        else
            risk = plan.echoRiskChoiceLane == second ? second : first;

        plan.echoRiskChoiceLane = risk;
        plan.echoSafeChoiceLane = risk == first ? second : first;
        plan.safeLane = plan.echoSafeChoiceLane;
        plan.echoChallengeLane = risk;
        return plan;
    }

    public static float EchoObstacleSpacingMultiplier(AITrackPlan plan)
    {
        // Layout bands still vary row position and lane order, but no longer
        // stretch the recovery-safe floor during the time-limited counterattack.
        return 1f;
    }

    public static AITrackPlan PrepareCounterObstacleRowPlan(
        AITrackPlan plan, int acceptedRowIndex)
    {
        if (plan.echoEncounterKind != EchoEncounterKind.CounterTest)
            return plan;

        int step = Mathf.Max(0, acceptedRowIndex);
        plan.echoObstaclePattern =
            EchoObstaclePatternRules.PatternForStep(step);
        plan.echoObstacleSpacingBand =
            EchoObstaclePatternRules.SpacingBandForStep(step);
        plan.echoObstacleLayoutStep = step;
        return plan;
    }

    public static float EchoObstacleRowZ(AITrackPlan plan, float minimumZ,
        float maximumZ, float variationRoll)
    {
        float minimum = Mathf.Min(minimumZ, maximumZ);
        float maximum = Mathf.Max(minimumZ, maximumZ);
        if (plan.echoEncounterKind != EchoEncounterKind.CounterTest)
            return Mathf.Lerp(minimum, maximum,
                Mathf.Clamp01(variationRoll));

        float position;
        switch ((plan.echoObstacleLayoutStep % 5 + 5) % 5)
        {
            case 0: position = 0.24f; break;
            case 1: position = 0.66f; break;
            case 2: position = 0.40f; break;
            case 3: position = 0.78f; break;
            default: position = 0.52f; break;
        }
        float jitter = (Mathf.Clamp01(variationRoll) - 0.5f) * 0.08f;
        return Mathf.Lerp(minimum, maximum,
            Mathf.Clamp01(position + jitter));
    }

    public static float EchoObstacleLaneOffset(AITrackPlan plan, int lane)
    {
        bool predicted = lane == plan.echoPredictedLane;
        bool risk = lane == plan.echoRiskChoiceLane;
        switch (plan.echoObstaclePattern)
        {
            case EchoObstaclePattern.PredictedThenRisk:
                if (predicted) return -2.4f;
                if (risk) return 2.4f;
                break;
            case EchoObstaclePattern.RiskThenPredicted:
                if (risk) return -2.4f;
                if (predicted) return 2.4f;
                break;
        }
        return 0f;
    }

    private static float EchoObstacleMinimumLaneOffset(AITrackPlan plan)
    {
        if (plan.echoEncounterKind != EchoEncounterKind.CounterTest)
            return -0.8f;
        return plan.echoObstaclePattern == EchoObstaclePattern.PredictedThenRisk
               || plan.echoObstaclePattern
               == EchoObstaclePattern.RiskThenPredicted
            ? -2.4f : 0f;
    }

    private static int LaneObstacleDrought(int[] laneObstacleDrought, int lane)
    {
        return laneObstacleDrought != null && lane >= 0
               && lane < laneObstacleDrought.Length
            ? Mathf.Max(0, laneObstacleDrought[lane])
            : 0;
    }

    public static int SelectEchoEncounterObstaclePrefabIndex(
        AITrackPlan plan, int lane, int straightSegmentIndex,
        float difficulty, float typeRoll)
    {
        EchoContractType type = plan.echoEncounterKind != EchoEncounterKind.None
            ? plan.echoEncounterContractType : plan.echoContractType;
        if (plan.echoEncounterKind != EchoEncounterKind.None
            && (type == EchoContractType.ChangeVerticalHabit
                || type == EchoContractType.DisruptRhythm))
        {
            ShadowAction action = lane == plan.echoRiskChoiceLane
                ? plan.echoTargetAction : plan.echoPredictedAction;
            return action == ShadowAction.Jump ? 1 : 0;
        }
        return SelectContractObstaclePrefabIndex(type, plan.echoTargetAction,
            straightSegmentIndex, difficulty, typeRoll);
    }

    public static int[] SelectContractBlockedLanes(int safeLane,
        int blockedLaneCount, int[] laneObstacleDrought,
        EchoContractType contractType, int challengeLane)
    {
        int[] normal = SelectBlockedLanes(
            safeLane, blockedLaneCount, laneObstacleDrought);
        bool usesChallengeLane = contractType == EchoContractType.ChangeVerticalHabit
                                 || contractType == EchoContractType.DisruptRhythm;
        int safe = Mathf.Clamp(safeLane, 0, 2);
        if (!usesChallengeLane || challengeLane < 0 || challengeLane > 2
            || challengeLane == safe || normal.Length == 0)
            return normal;

        normal[0] = challengeLane;
        if (normal.Length > 1 && normal[1] == challengeLane)
        {
            for (int lane = 0; lane < 3; lane++)
            {
                if (lane == safe || lane == challengeLane) continue;
                normal[1] = lane;
                break;
            }
        }
        return normal;
    }

    public static int SelectContractObstaclePrefabIndex(
        EchoContractType contractType, ShadowAction targetAction,
        int straightSegmentIndex, float difficulty, float typeRoll)
    {
        if (contractType == EchoContractType.ChangeVerticalHabit)
            return targetAction == ShadowAction.Jump ? 1 : 0;
        if (contractType == EchoContractType.DisruptRhythm)
            return Mathf.Abs(straightSegmentIndex) % 2 == 0 ? 1 : 0;
        return TrackSpawnRules.SelectObstaclePrefabIndex(difficulty, typeRoll);
    }

   GameObject SpawnDynamic(GameObject prefab, GameObject ownerSegment,
       Vector3 position, Quaternion rotation)
   {
       if (!_dynamicPools.TryGetValue(prefab, out Queue<GameObject> pool))
       {
           pool = new Queue<GameObject>();
           _dynamicPools.Add(prefab, pool);
       }

       GameObject instance = pool.Count > 0
           ? pool.Dequeue()
           : Instantiate(prefab);

       instance.SetActive(false);
       if (prefab == coinPrefab)
           Coin.EnsureRuntimeContract(instance);
       instance.transform.SetParent(ownerSegment.transform, true);
       instance.transform.SetPositionAndRotation(position, rotation);
       instance.SetActive(true);
       if (WorldStyler.Instance != null)
       {
           if (instance.GetComponent<Coin>() != null)
               WorldStyler.Instance.StyleCoin(instance);
           else if (instance.GetComponent<Obstacle>() != null)
               WorldStyler.Instance.StyleObstacle(instance);
       }
       _dynamicObjects.Add(new DynamicEntry
       {
           instance = instance,
           prefab = prefab,
           ownerSegment = ownerSegment
       });
       return instance;
   }

    private GameObject SpawnCoinInstance(GameObject ownerSegment,
        Vector3 position, Quaternion routeRotation, bool echoContractMarker,
        int challengeStepId = 0)
    {
        GameObject instance = SpawnDynamic(
            coinPrefab, ownerSegment, position, routeRotation);
       Coin coin = instance != null ? instance.GetComponent<Coin>() : null;
        coin?.ConfigureEchoContractMarker(echoContractMarker, challengeStepId);
       return instance;
   }

   public void ReleaseDynamic(GameObject instance)
   {
       for (int i = _dynamicObjects.Count - 1; i >= 0; i--)
       {
           DynamicEntry entry = _dynamicObjects[i];
           if (entry.instance != instance) continue;
           ReturnDynamicToPool(entry);
           _dynamicObjects.RemoveAt(i);
           return;
       }

        if (instance != null)
        {
            instance.GetComponent<EchoChallengeObstacleTag>()?.Clear();
            PredictionGateObstacleTag predictionTag =
                instance.GetComponent<PredictionGateObstacleTag>();
            PredictionGateObstacleBinding predictionBinding =
                predictionTag != null ? predictionTag.Binding : default;
            if (predictionBinding.IsBound)
            {
                AIShadowRunner.Instance?.SingleContractRuntime
                    ?.RecycleGate(predictionBinding.gateId);
            }
            predictionTag?.Clear();
            instance.GetComponent<Coin>()?.ConfigureEchoContractMarker(false);
            instance.SetActive(false);
        }
   }

   void ReturnDynamicToPool(DynamicEntry entry)
   {
       if (entry.instance == null || entry.prefab == null) return;
        if (_player != null)
            _player.GetComponent<PlayerController>()
                ?.ForgetResolvedObstacle(entry.instance);
        entry.instance.GetComponent<EchoChallengeObstacleTag>()?.Clear();
        PredictionGateObstacleTag predictionTag =
            entry.instance.GetComponent<PredictionGateObstacleTag>();
        PredictionGateObstacleBinding predictionBinding =
            predictionTag != null ? predictionTag.Binding : default;
        if (predictionBinding.IsBound)
        {
            AIShadowRunner.Instance?.SingleContractRuntime
                ?.RecycleGate(predictionBinding.gateId);
        }
        predictionTag?.Clear();
        entry.instance.GetComponent<Coin>()?.ConfigureEchoContractMarker(false);
        entry.instance.SetActive(false);
       entry.instance.transform.SetParent(transform, false);

       if (!_dynamicPools.TryGetValue(entry.prefab, out Queue<GameObject> pool))
       {
           pool = new Queue<GameObject>();
           _dynamicPools.Add(entry.prefab, pool);
       }
       pool.Enqueue(entry.instance);
   }

   void EnsureProceduralAssets()
   {
       int groundLayer = LayerMask.NameToLayer("Ground");
       if (groundLayer < 0) groundLayer = 0;

       if (trackSegmentPrefab == null)
            trackSegmentPrefab = CreateProcStraight(groundLayer);
       if (turnLeftPrefab == null)
            turnLeftPrefab = CreateProcTurn(groundLayer, -1);
       if (turnRightPrefab == null)
            turnRightPrefab = CreateProcTurn(groundLayer, 1);
       if (coinPrefab == null)
           coinPrefab = CreateProcCoin();
       bool missingObstaclePrefab = obstaclePrefabs == null || obstaclePrefabs.Length < 3;
       if (!missingObstaclePrefab)
       {
           for (int i = 0; i < 3; i++)
           {
               if (obstaclePrefabs[i] == null)
               {
                   missingObstaclePrefab = true;
                   break;
               }
           }
       }
       if (missingObstaclePrefab)
           obstaclePrefabs = CreateProcObstacles();
   }

    GameObject CreateProcStraight(int layer)
    {
       return CreateProcTrackRoot("ProcStraight", layer);
    }

    GameObject CreateProcTurn(int layer, int turnDirection)
    {
        GameObject root = new GameObject(
            turnDirection > 0 ? "ProcTurnRight" : "ProcTurnLeft");
        root.layer = layer;
        EnsureTurnCoverage(root, turnDirection);
        root.SetActive(false);
        root.transform.SetParent(transform);
        return root;
    }

    void EnsureTurnCoverage(GameObject segment, int turnDirection)
    {
        if (segment == null)
            return;

        Transform entryStrip = segment.transform.Find("EntryStrip");
        Transform exitStrip = segment.transform.Find("ExitStrip");
        bool hasAuthoredSurfaces = entryStrip != null && exitStrip != null;
        int layer = LayerMask.NameToLayer("Ground");
        if (layer < 0) layer = segment.layer;

        if (hasAuthoredSurfaces)
        {
            ConfigureAuthoredTurnSurface(entryStrip, true, turnDirection);
            ConfigureAuthoredTurnSurface(exitStrip, false, turnDirection);
        }
        else if (segment.transform.Find("RuntimeTurnCoverage") == null)
        {
            GameObject coverage = new GameObject("RuntimeTurnCoverage");
            coverage.layer = layer;
            coverage.transform.SetParent(segment.transform, false);

            CreateTurnSurface("EntryCoverage", coverage.transform,
                new Vector3(0f, -0.15f,
                    TrackGeometryStandards.TurnEntrySurfaceCenter(segmentLength)),
                Quaternion.identity,
                new Vector3(TrackGeometryStandards.VisualRoadWidth, 0.3f,
                    TrackGeometryStandards.TurnEntrySurfaceLength(segmentLength)),
                layer);
            // Exit coverage ends at the following straight. The separate inner
            // corner cap closes the visible square without lengthening either
            // road arm into a coplanar overlap.
            float exitLength = TrackGeometryStandards.TurnExitSurfaceLength(
                segmentLength);
            float exitCenterX = TrackGeometryStandards.TurnExitSurfaceCenter(
                segmentLength);
            CreateTurnSurface("ExitCoverage", coverage.transform,
                new Vector3(turnDirection * exitCenterX, -0.15f,
                    segmentLength * 0.5f),
                Quaternion.Euler(0f, 90f, 0f),
                new Vector3(TrackGeometryStandards.VisualRoadWidth, 0.3f,
                    exitLength), layer);
        }

        EnsureTurnInnerCornerCap(segment.transform, turnDirection, layer,
            hasAuthoredSurfaces);
        EnsureTurnWalkableBridge(segment.transform, turnDirection, layer,
            hasAuthoredSurfaces);
    }

    void EnsureTurnInnerCornerCap(Transform root, int turnDirection,
        int layer, bool authoredSurface)
    {
        Transform existing = root.Find(TurnInnerCornerCapName);
        GameObject cap;
        if (existing != null)
        {
            cap = existing.gameObject;
        }
        else
        {
            cap = GameObject.CreatePrimitive(authoredSurface
                ? PrimitiveType.Plane : PrimitiveType.Cube);
            cap.name = TurnInnerCornerCapName;
            cap.transform.SetParent(root, false);
        }

        Collider[] capColliders = cap.GetComponents<Collider>();
        for (int index = 0; index < capColliders.Length; index++)
        {
            capColliders[index].enabled = false;
            if (Application.isPlaying) Destroy(capColliders[index]);
            else DestroyImmediate(capColliders[index]);
        }

        float size = TrackGeometryStandards.TurnInnerCornerSize(segmentLength);
        Vector3 center = TrackGeometryStandards.TurnInnerCornerCenter(
            segmentLength, turnDirection);
        center.y = authoredSurface ? 0.051f : -0.15f;
        cap.layer = layer;
        cap.transform.localPosition = center;
        cap.transform.localRotation = Quaternion.identity;
        cap.transform.localScale = authoredSurface
            ? new Vector3(size / 10f, 1f, size / 10f)
            : new Vector3(size, 0.3f, size);
        Renderer renderer = cap.GetComponent<Renderer>();
        if (renderer != null)
            EchoRoadVisualController.Instance.ApplyTo(renderer,
                RoadSurfaceRole.RuntimeFallback);
    }

    void EnsureTurnWalkableBridge(Transform root, int turnDirection,
        int layer, bool authoredSurface)
    {
        Transform existing = root.Find(TurnWalkableBridgeName);
        GameObject bridge = existing != null
            ? existing.gameObject : new GameObject(TurnWalkableBridgeName);
        if (existing == null) bridge.transform.SetParent(root, false);

        Vector3 center = TrackGeometryStandards.TurnWalkableBridgeCenter(
            segmentLength, turnDirection);
        center.y = authoredSurface ? 0.05f : -0.15f;
        bridge.layer = layer;
        bridge.transform.localPosition = center;
        bridge.transform.localRotation = Quaternion.identity;
        bridge.transform.localScale = Vector3.one;
        BoxCollider collider = bridge.GetComponent<BoxCollider>();
        if (collider == null) collider = bridge.AddComponent<BoxCollider>();
        collider.enabled = true;
        collider.center = Vector3.zero;
        collider.size = new Vector3(
            TrackGeometryStandards.TurnWalkableBridgeWidth, 0.3f,
            TrackGeometryStandards.WalkableWidth);
    }

    void ConfigureAuthoredTurnSurface(Transform surface, bool entry,
        int turnDirection)
    {
        float length = entry
            ? TrackGeometryStandards.TurnEntrySurfaceLength(segmentLength)
            : TrackGeometryStandards.TurnExitSurfaceLength(segmentLength);
        Vector3 position = surface.localPosition;
        Vector3 scale = surface.localScale;
        scale.x = TrackGeometryStandards.VisualRoadWidth / 10f;
        scale.z = length / 10f;

        if (entry)
        {
            position.x = 0f;
            position.z = TrackGeometryStandards.TurnEntrySurfaceCenter(
                segmentLength);
            surface.localRotation = Quaternion.identity;
        }
        else
        {
            position.x = turnDirection
                         * TrackGeometryStandards.TurnExitSurfaceCenter(
                             segmentLength);
            position.z = segmentLength * 0.5f;
            surface.localRotation = Quaternion.Euler(0f, 90f, 0f);
        }

        surface.localPosition = position;
        surface.localScale = scale;
        BoxCollider collider = surface.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.size = new Vector3(
                TrackGeometryStandards.WalkableWidth / scale.x,
                collider.size.y, 10f);
        }
    }

    static void CreateTurnSurface(string name, Transform parent, Vector3 localPosition,
        Quaternion localRotation, Vector3 localScale, int layer)
    {
        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.name = name;
        surface.layer = layer;
        surface.transform.SetParent(parent, false);
        surface.transform.localPosition = localPosition;
        surface.transform.localRotation = localRotation;
        surface.transform.localScale = localScale;
        BoxCollider collider = surface.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.size = new Vector3(
                TrackGeometryStandards.WalkableWidth / localScale.x, 1f, 1f);
        }
        EchoRoadVisualController.Instance.ApplyTo(
            surface.GetComponent<MeshRenderer>(),
            RoadSurfaceRole.RuntimeFallback);
    }

    GameObject CreateProcTrackRoot(string name, int layer)
    {
        GameObject root = new GameObject(name);
        root.layer = layer;

        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Cube);
        surface.name = "Surface";
        surface.layer = layer;
        surface.transform.SetParent(root.transform, false);
        surface.transform.localPosition = new Vector3(0f, -0.15f, 0f);
        surface.transform.localScale = new Vector3(
            TrackGeometryStandards.VisualRoadWidth, 0.3f, segmentLength);
        BoxCollider collider = surface.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.size = new Vector3(
                TrackGeometryStandards.WalkableWidth
                / TrackGeometryStandards.VisualRoadWidth, 1f, 1f);
        }
        EchoRoadVisualController.Instance.ApplyTo(
            surface.GetComponent<MeshRenderer>(),
            RoadSurfaceRole.RuntimeFallback);

        root.SetActive(false);
        root.transform.SetParent(transform);
        return root;
    }

   GameObject CreateProcCoin()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "ProcCoin";
        go.transform.localScale = new Vector3(0.6f, 0.15f, 0.6f);
        Collider coinCollider = go.GetComponent<Collider>();
        if (coinCollider == null) coinCollider = go.AddComponent<BoxCollider>();
        coinCollider.isTrigger = true;
        go.AddComponent<Coin>();
        go.SetActive(false); go.transform.SetParent(transform);
        return go;
    }

    GameObject[] CreateProcObstacles()
    {
        ObstacleType[] types = { ObstacleType.Low, ObstacleType.High, ObstacleType.Barrier };
        Color[] colors    = { new Color(1f, 0.45f, 0.1f), new Color(0.85f, 0.15f, 0.05f), new Color(0.9f, 0.25f, 0.15f) };
        Shader sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");

        GameObject[] obs = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            obs[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obs[i].name = "ProcObstacle_" + i;
            if (sh != null) obs[i].GetComponent<MeshRenderer>().material = new Material(sh) { color = colors[i] };
            BoxCollider bc = obs[i].GetComponent<BoxCollider>();
            if (bc == null) bc = obs[i].AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = ObstacleGeometryRules.ColliderSize(types[i]);
            bc.center = ObstacleGeometryRules.ColliderCenter(types[i]);
            Obstacle o = obs[i].AddComponent<Obstacle>();
            o.type = types[i];
            obs[i].SetActive(false); obs[i].transform.SetParent(transform);
        }
        return obs;
    }

    void OnDestroy()
    {
        DestroyRuntimeObject(_finishMarkerMaterial);
        DestroyRuntimeObject(_predictionGateMaterial);
        if (Instance == this) Instance = null;
    }
}
