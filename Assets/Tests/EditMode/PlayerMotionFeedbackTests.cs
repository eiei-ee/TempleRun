using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class PlayerMotionFeedbackTests
{
    private readonly List<GameObject> _objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1f;
        foreach (GameObject go in _objects)
            if (go != null)
                Object.DestroyImmediate(go);
        _objects.Clear();
    }

    [Test]
    public void SnapshotIsAPureProjectionOfAuthoritativeMotionState()
    {
        Vector3 sourceForward = new Vector3(3f, 7f, 4f);

        PlayerMotionSnapshot first = PlayerMotionFeedback.Project(
            2, true, 0.45f, 0.9f, false, 3f, 0.8f,
            -6f, 17f, 10f, 24f, sourceForward, 2.25f);
        PlayerMotionSnapshot second = PlayerMotionFeedback.Project(
            2, true, 0.45f, 0.9f, false, 3f, 0.8f,
            -6f, 17f, 10f, 24f, sourceForward, 2.25f);

        Assert.AreEqual(2, first.Lane);
        Assert.IsTrue(first.IsJumping);
        Assert.IsFalse(first.IsSliding);
        Assert.AreEqual(0.5f, first.Jump01, 0.0001f);
        Assert.AreEqual(0f, first.Slide01, 0.0001f,
            "Inactive action timers must not leak into the snapshot.");
        Assert.AreEqual(-6f, first.LateralVelocity, 0.0001f);
        Assert.AreEqual(0.5f, first.Speed01, 0.0001f);
        Assert.AreEqual(new Vector3(0.6f, 0f, 0.8f), first.Forward);
        Assert.AreEqual(2.25f, first.LateralOffset, 0.0001f);
        Assert.AreEqual(first.Jump01, second.Jump01);
        Assert.AreEqual(first.Speed01, second.Speed01);
        Assert.AreEqual(first.Forward, second.Forward);
        Assert.AreEqual(new Vector3(3f, 7f, 4f), sourceForward,
            "Projection must not mutate its inputs or gameplay state.");
    }

    [Test]
    public void SlideEdgesWrapColliderMutationAndEndOnlyOnce()
    {
        PlayerController player = CreatePlayer(out CapsuleCollider capsule,
            out _);
        capsule.center = new Vector3(0f, 1.1f, 0f);
        capsule.height = 2.2f;
        capsule.radius = 0.4f;
        player.slideColliderHeight = 0.9f;
        SetPrivateField(player, "_originalColliderHeight", capsule.height);
        SetPrivateField(player, "_originalColliderCenter", capsule.center);
        var signals = new List<PlayerActionSignal>();
        float heightAtStart = -1f;
        float heightAtEnd = -1f;
        player.ActionRaised += signal =>
        {
            signals.Add(signal);
            if (signal.Edge == PlayerActionEdge.SlideStarted)
                heightAtStart = capsule.height;
            if (signal.Edge == PlayerActionEdge.SlideEnded)
                heightAtEnd = capsule.height;
        };

        InvokePrivate(player, "BeginSlide");
        InvokePrivate(player, "CompleteSlide");
        InvokePrivate(player, "CompleteSlide");

        Assert.AreEqual(2, signals.Count);
        Assert.AreEqual(PlayerActionEdge.SlideStarted, signals[0].Edge);
        Assert.AreEqual(PlayerActionEdge.SlideEnded, signals[1].Edge);
        Assert.AreEqual(0.9f, heightAtStart, 0.0001f,
            "SlideStarted must observe the lowered collider.");
        Assert.AreEqual(2.2f, heightAtEnd, 0.0001f,
            "SlideEnded must observe the restored collider.");
        Assert.AreEqual(signals[0].ActionId, signals[1].ActionId);
        Assert.Less(signals[0].Sequence, signals[1].Sequence);
        Assert.IsTrue(signals[0].Motion.IsSliding);
        Assert.IsFalse(signals[1].Motion.IsSliding);
    }

    [Test]
    public void LandedFiresOnceAtTheAuthoritativeJumpEnd()
    {
        PlayerController player = CreatePlayer(out _, out _);
        var signals = new List<PlayerActionSignal>();
        player.ActionRaised += signals.Add;

        InvokePrivate(player, "BeginJump");
        SetPrivateField(player, "_jumpTimer", 0.87f);
        InvokePrivate(player, "UpdateJumpVelocity", Vector3.zero, 0.02f);
        Assert.AreEqual(1, signals.Count,
            "The jump must remain active before its 0.9 second endpoint.");

        InvokePrivate(player, "UpdateJumpVelocity", Vector3.zero, 0.02f);
        InvokePrivate(player, "UpdateJumpVelocity", Vector3.zero, 0.02f);

        Assert.AreEqual(2, signals.Count);
        Assert.AreEqual(PlayerActionEdge.JumpStarted, signals[0].Edge);
        Assert.AreEqual(PlayerActionEdge.Landed, signals[1].Edge);
        Assert.AreEqual(signals[0].ActionId, signals[1].ActionId);
        Assert.AreEqual(0.9f, signals[1].Duration, 0.0001f);
        Assert.IsFalse(signals[1].Motion.IsJumping);
    }

    [Test]
    public void LaneRedirectionCompletesOnlyTheLatestActionId()
    {
        PlayerController player = CreatePlayer(out _, out _);
        var signals = new List<PlayerActionSignal>();
        player.ActionRaised += signals.Add;

        SetPrivateField(player, "<CurrentLane>k__BackingField", 0);
        InvokePrivate(player, "BeginLaneChange", 1, 0);
        SetPrivateField(player, "<CurrentLane>k__BackingField", 1);
        InvokePrivate(player, "BeginLaneChange", 0, 1);
        InvokePrivate(player, "CompleteLaneChange");
        InvokePrivate(player, "CompleteLaneChange");

        Assert.AreEqual(3, signals.Count);
        Assert.AreEqual(PlayerActionEdge.LaneChangeStarted, signals[0].Edge);
        Assert.AreEqual(PlayerActionEdge.LaneChangeStarted, signals[1].Edge);
        Assert.AreEqual(PlayerActionEdge.LaneChangeCompleted, signals[2].Edge);
        Assert.AreNotEqual(signals[0].ActionId, signals[1].ActionId);
        Assert.AreEqual(signals[1].ActionId, signals[2].ActionId,
            "A redirect supersedes the unfinished lane action.");
        Assert.AreEqual(0, signals[2].FromLane);
        Assert.AreEqual(1, signals[2].ToLane);
    }

    [Test]
    public void EachResolvedCollisionRaisesExactlyOneOutcomeEdge()
    {
        GameManager manager = Create<GameManager>("GameManager");
        SetPrivateField(manager, "<State>k__BackingField",
            GameState.Playing);
        PlayerController player = CreatePlayer(out _, out Rigidbody body);
        body.isKinematic = false;
        body.useGravity = false;
        SetPrivateField(player, "_gm", manager);
        GameObject powerUpObject = new GameObject("Power Up Controller Test");
        powerUpObject.SetActive(false);
        _objects.Add(powerUpObject);
        PowerUpController powerUp =
            powerUpObject.AddComponent<PowerUpController>();
        SetPrivateField(powerUp, "<ActivePowerUp>k__BackingField",
            PowerUpId.Shield);
        SetPrivateField(powerUp, "_shieldCharges", 1);
        PowerUpController previousPowerUp = PowerUpController.Instance;
        SetPrivateStaticField(typeof(PowerUpController),
            "<Instance>k__BackingField", powerUp);
        var signals = new List<PlayerActionSignal>();
        player.ActionRaised += signals.Add;

        try
        {
            GameObject shieldBarrier = CreateBarrier("shield", 1f);
            InvokeObstacleContact(player, shieldBarrier);
            InvokeObstacleContact(player, shieldBarrier);
            InvokeObstacleContact(player, CreateBarrier("recovery", 2f));
            // A distinct fatal impact occurs after the recovery protection
            // expires; contacts inside that window remain recoverable.
            InvokePrivate(manager, "AdvanceRunSpeed",
                manager.CollisionRecoveryDuration);
            InvokeObstacleContact(player, CreateBarrier("fatal", 3f));

            Assert.AreEqual(3, signals.Count,
                "Duplicate physics settlement must not emit another outcome.");
            Assert.AreEqual(PlayerActionEdge.ImpactAbsorbed, signals[0].Edge);
            Assert.AreEqual(PlayerActionEdge.ImpactRecovered, signals[1].Edge);
            Assert.AreEqual(PlayerActionEdge.FatalImpact, signals[2].Edge);
            Assert.Less(signals[0].Sequence, signals[1].Sequence);
            Assert.Less(signals[1].Sequence, signals[2].Sequence);
            Assert.AreNotEqual(signals[0].ActionId, signals[1].ActionId);
            Assert.AreNotEqual(signals[1].ActionId, signals[2].ActionId);
            Assert.AreEqual(0.5f, signals[0].Position.z, 0.0001f,
                "The contact point must be captured before the obstacle is pooled.");
            Assert.AreEqual(1, player.DuplicateObstacleContactCount);
        }
        finally
        {
            SetPrivateStaticField(typeof(PowerUpController),
                "<Instance>k__BackingField", previousPowerUp);
        }
    }

    private PlayerController CreatePlayer(out CapsuleCollider capsule,
        out Rigidbody body)
    {
        GameObject playerObject = new GameObject("player");
        _objects.Add(playerObject);
        body = playerObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        capsule = playerObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 1f, 0f);
        capsule.height = 2f;
        capsule.radius = 0.4f;
        PlayerController player = playerObject.AddComponent<PlayerController>();
        SetPrivateField(player, "_rb", body);
        SetPrivateField(player, "_capsuleCollider", capsule);
        SetPrivateField(player, "_originalColliderHeight", capsule.height);
        SetPrivateField(player, "_originalColliderCenter", capsule.center);
        return player;
    }

    private GameObject CreateBarrier(string name, float z)
    {
        GameObject obstacleObject = new GameObject(name);
        _objects.Add(obstacleObject);
        obstacleObject.transform.position = new Vector3(0f, 0f, z);
        obstacleObject.AddComponent<Obstacle>().type = ObstacleType.Barrier;
        BoxCollider collider = obstacleObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, 1f, 0f);
        collider.size = new Vector3(1f, 2f, 1f);
        Physics.SyncTransforms();
        return obstacleObject;
    }

    private static void InvokeObstacleContact(PlayerController player,
        GameObject obstacleObject)
    {
        InvokePrivate(player, "HandleObstacleContact",
            obstacleObject.GetComponent<BoxCollider>(),
            obstacleObject.GetComponent<Obstacle>(),
            ObstacleContactSource.Trigger);
    }

    private T Create<T>(string name) where T : Component
    {
        GameObject go = new GameObject(name);
        _objects.Add(go);
        return go.AddComponent<T>();
    }

    private static void SetPrivateField(object target, string name,
        object value)
    {
        FieldInfo field = target.GetType().GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing private field: " + name);
        field.SetValue(target, value);
    }

    private static void SetPrivateStaticField(System.Type type, string name,
        object value)
    {
        FieldInfo field = type.GetField(name,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Missing private static field: " + name);
        field.SetValue(null, value);
    }

    private static object InvokePrivate(object target, string name,
        params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "Missing private method: " + name);
        return method.Invoke(target, args);
    }
}
