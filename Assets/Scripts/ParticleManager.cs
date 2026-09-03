using UnityEngine;

public class ParticleManager : MonoBehaviour
{
    public static ParticleManager Instance { get; private set; }

    private ParticleSystem _coinPS;
    private ParticleSystem _coinAbsorbPS;
    private ParticleSystem _dustPS;
    private ParticleSystem _deathPS;
    private ParticleSystem _trailPS;
    private ParticleSystem _contactLinePS;
    private ParticleSystem _contactEchoPS;

    private Material _defaultMat;
    private float _lastSlideSustainTime = float.NegativeInfinity;

    private static readonly Color ContactWhite =
        new Color(0.78f, 0.96f, 1f, 1f);
    private static readonly Color EchoCyan =
        new Color(0.0f, 0.86f, 0.92f, 0.68f);
    private static readonly Color LandingCyan =
        new Color(0.02f, 0.74f, 0.82f, 0.46f);
    private static readonly Color DangerCoral =
        new Color(1f, 0.34f, 0.30f, 1f);
    private static readonly Color CounterGold =
        new Color(1f, 0.76f, 0.24f, 1f);

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Shader s = Shader.Find("Particles/Standard Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Mobile/Particles/Additive");
        _defaultMat = s != null ? new Material(s) : null;

        _coinPS  = CreateParticleSystem("MemoryFragmentFX",
            new Color(0.0f, 0.86f, 0.92f), 0.16f, 2.2f, 18);
        _coinAbsorbPS = CreateParticleSystem("MemoryAbsorbFX",
            new Color(0.72f, 0.98f, 1f), 0.16f, 0f, 4);
        var absorbMain = _coinAbsorbPS.main;
        absorbMain.startSize = 0.16f;
        ParticleSystemRenderer absorbRenderer =
            _coinAbsorbPS.GetComponent<ParticleSystemRenderer>();
        absorbRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        absorbRenderer.lengthScale = ResolveCoinAbsorbLengthScale();
        absorbRenderer.velocityScale = ResolveCoinAbsorbVelocityScale();
        absorbRenderer.cameraVelocityScale = 0f;
        _dustPS  = CreateParticleSystem("DustFX",  new Color(0.16f, 0.32f, 0.42f), 0.25f, 1.5f, 5);
        _deathPS = CreateParticleSystem("DeathFX", new Color(1f, 0.34f, 0.30f), 0.5f, 4f, 30);
        _trailPS = CreateParticleSystem("TrailFX", new Color(0.12f, 0.76f, 1f), 0.62f, 1f, 12);
        _contactLinePS = CreateContactSystem("ActionContactLineFX",
            ContactWhite, 34);
        _contactEchoPS = CreateContactSystem("ActionContactEchoFX",
            EchoCyan, 18);
        VisualQualityController.Changed += ApplyQuality;
        ApplyQuality(VisualQualityController.Current);
    }

    ParticleSystem CreateParticleSystem(string name, Color color, float lifetime, float speed, int maxParticles)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = 0.18f;
        main.startColor = color;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = false;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.25f;

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, 0f);

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(
            new Color(color.r, color.g, color.b, 1f),
            new Color(color.r, color.g, color.b, 0f));

        if (_defaultMat != null)
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _defaultMat;

        return ps;
    }

    private ParticleSystem CreateContactSystem(string name, Color color,
        int maxParticles)
    {
        ParticleSystem ps = CreateParticleSystem(name, color, 0.14f, 0f,
            maxParticles);
        var main = ps.main;
        main.startSize = 0.06f;
        main.gravityModifier = 0.04f;

        var shape = ps.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer =
            ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 3.2f;
        renderer.velocityScale = 0.34f;
        renderer.cameraVelocityScale = 0f;
        return ps;
    }

    public void EmitCoin(Vector3 pos)
    {
        EmitCoin(pos, pos + Vector3.up * 0.7f);
    }

    public static float ResolveCoinAbsorbLengthScale()
    {
        return 2.4f;
    }

    public static float ResolveCoinAbsorbVelocityScale()
    {
        // The particle already reaches its target through its velocity. Letting
        // that velocity also scale the renderer turns airborne pickups into a
        // long rectangular beam instead of the intended short absorb streak.
        return 0f;
    }

    public void EmitCoin(Vector3 pos, Vector3 target)
    {
        if (_coinPS == null || _coinAbsorbPS == null) return;

        for (int index = 0; index < 5; index++)
        {
            Vector3 radial = Random.onUnitSphere;
            radial.y = Mathf.Abs(radial.y) * 0.65f + 0.15f;
            var fragment = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = radial.normalized * Random.Range(1.8f, 3.0f),
                startLifetime = Random.Range(0.12f, 0.19f),
                startSize = Random.Range(0.055f, 0.09f),
                startColor = index == 0
                    ? new Color(1f, 0.42f, 0.08f, 1f)
                    : new Color(0.0f, 0.86f, 0.92f, 1f)
            };
            _coinPS.Emit(fragment, 1);
        }

        const float absorbLifetime = 0.16f;
        var absorb = new ParticleSystem.EmitParams
        {
            position = pos,
            velocity = (target - pos) / absorbLifetime,
            startLifetime = absorbLifetime,
            startSize = 0.17f,
            startColor = new Color(0.72f, 0.98f, 1f, 1f)
        };
        _coinAbsorbPS.Emit(absorb, 1);
    }
    public void EmitDust(Vector3 pos)    { _dustPS.transform.position = pos;  _dustPS.Emit(2); }
    public void EmitTrail(Vector3 pos)
    {
        if (VisualQualityController.Current != VisualQuality.High) return;
        _trailPS.transform.position = pos;
        _trailPS.Emit(1);
    }
    public void EmitDeath(Vector3 pos)   { _deathPS.transform.position = pos; _deathPS.Emit(20); }

    public void EmitSlideStart(Vector3 pos)
    {
        EmitSlideStart(pos, Vector3.forward);
    }

    public void EmitSlideStart(Vector3 pos, Vector3 forward)
    {
        _lastSlideSustainTime = Time.time;
        EmitSlideContact(pos, forward, 1f, 3.8f, 0.13f);
    }

    public void EmitSlideSustain(Vector3 pos, float slide01)
    {
        EmitSlideSustain(pos, Vector3.forward, slide01);
    }

    public void EmitSlideSustain(Vector3 pos, Vector3 forward, float slide01)
    {
        VisualQuality quality = VisualQualityController.Current;
        float interval = ResolveSlideSustainInterval(quality);
        if (!CanEmitSlideSustain(Time.time, _lastSlideSustainTime, interval))
            return;

        _lastSlideSustainTime = Time.time;
        EmitSlideContact(pos, forward, ResolveSlideContactStrength(slide01),
            3.15f, 0.11f);
    }

    public void EmitSlideEnd(Vector3 pos)
    {
        EmitSlideEnd(pos, Vector3.forward);
    }

    public void EmitSlideEnd(Vector3 pos, Vector3 forward)
    {
        EmitSlideContact(pos, forward, 0.72f, 2.5f, 0.1f);
        _lastSlideSustainTime = float.NegativeInfinity;
    }

    public void EmitTakeoff(Vector3 pos)
    {
        EmitTakeoff(pos, Vector3.forward);
    }

    public void EmitTakeoff(Vector3 pos, Vector3 forward)
    {
        Vector3 backward = ResolveBackward(forward);
        EmitContact(_contactLinePS, pos, backward * 2.2f + Vector3.up * 0.12f,
            ContactWhite, 0.1f, 0.065f);
        if (ShouldEmitContactEcho(VisualQualityController.Current))
        {
            EmitContact(_contactEchoPS, pos + backward * 0.08f,
                backward * 1.65f, EchoCyan, 0.14f, 0.05f);
        }
    }

    public void EmitLand(Vector3 pos, float intensity01 = 1f)
    {
        EmitLand(pos, Vector3.forward, intensity01);
    }

    public void EmitLand(Vector3 pos, Vector3 forward,
        float intensity01 = 1f)
    {
        float strength = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(intensity01));
        VisualQuality quality = VisualQualityController.Current;
        int streakCount = ResolveLandingGroundStreakCount(quality);
        Vector3 backward = ResolveBackward(forward);
        Vector3 right = ResolveRight(forward);

        // Keep landing energy on the road and behind the feet. The previous
        // pair of opposing white stretch particles crossed at the origin and
        // read as a small white "+" instead of a grounded impact.
        for (int index = 0; index < streakCount; index++)
        {
            float sideOffset = ResolveLandingGroundStreakOffset(
                index, streakCount);
            Vector3 direction = ResolveLandingGroundStreakDirection(
                forward, sideOffset);
            Vector3 origin = pos
                             + right * sideOffset
                             + backward * 0.05f
                             + Vector3.up * 0.012f;
            EmitContact(_contactLinePS, origin,
                direction * (1.6f * strength),
                ResolveLandingGroundColor(), 0.13f,
                0.04f * strength);
        }
    }

    public void EmitImpactResult(Vector3 pos, bool fatal)
    {
        EmitImpactResult(pos, Vector3.back, fatal);
    }

    public void EmitImpactResult(Vector3 pos, Vector3 surfaceNormal,
        bool fatal)
    {
        VisualQuality quality = VisualQualityController.Current;
        int count = ResolveImpactLineCount(fatal, quality);
        Vector3 away = surfaceNormal.sqrMagnitude > 0.0001f
            ? surfaceNormal.normalized
            : Vector3.back;
        away.y = Mathf.Max(0.08f, away.y);
        Color color = fatal ? DangerCoral : ContactWhite;

        for (int index = 0; index < count; index++)
        {
            float angle = count <= 1
                ? 0f
                : Mathf.Lerp(-34f, 34f, index / (float)(count - 1));
            Vector3 velocity = Quaternion.AngleAxis(angle, Vector3.up)
                               * away * (fatal ? 3.7f : 2.45f);
            EmitContact(_contactLinePS, pos, velocity, color,
                fatal ? 0.2f : 0.13f, fatal ? 0.085f : 0.06f);
        }

        if (!fatal && ShouldEmitContactEcho(quality))
        {
            EmitContact(_contactEchoPS, pos + away * 0.06f,
                away * 1.8f, EchoCyan, 0.17f, 0.055f);
        }
    }

    public void EmitCounterSuccess(Vector3 pos)
    {
        EmitCounterSuccess(pos, Vector3.forward);
    }

    public void EmitCounterSuccess(Vector3 pos, Vector3 forward)
    {
        VisualQuality quality = VisualQualityController.Current;
        int fractureCount = ResolveCounterSuccessLineCount(quality);
        Vector3 backward = ResolveBackward(forward);
        Vector3 right = ResolveRight(forward);
        for (int index = 0; index < fractureCount; index++)
        {
            float side = fractureCount <= 1
                ? 0f
                : Mathf.Lerp(-1f, 1f,
                    index / (float)(fractureCount - 1));
            Vector3 fractureVelocity = (right * side
                                        + backward * 0.34f
                                        + Vector3.up * 0.14f).normalized
                                       * 3.25f;
            EmitContact(_contactLinePS, pos, fractureVelocity,
                DangerCoral, 0.14f, 0.058f);
        }

        int rewardCount = quality == VisualQuality.High ? 4 : 2;
        Vector3 travel = -backward;
        for (int index = 0; index < rewardCount; index++)
        {
            float side = rewardCount <= 1
                ? 0f
                : Mathf.Lerp(-0.38f, 0.38f,
                    index / (float)(rewardCount - 1));
            Vector3 rewardVelocity = (travel
                                      + right * side
                                      + Vector3.up * 0.42f).normalized
                                     * 2.7f;
            EmitContact(_contactLinePS, pos + Vector3.up * 0.08f,
                rewardVelocity, CounterGold, 0.2f, 0.052f);
        }
    }

    public static int ResolveSlideContactLineCount(VisualQuality quality)
    {
        return quality == VisualQuality.High ? 2 : 1;
    }

    public static bool ShouldEmitContactEcho(VisualQuality quality)
    {
        return quality == VisualQuality.High;
    }

    public static int ResolveLandingGroundStreakCount(VisualQuality quality)
    {
        return quality == VisualQuality.High ? 2 : 1;
    }

    public static float ResolveLandingGroundStreakOffset(int index,
        int streakCount)
    {
        if (streakCount <= 1) return 0f;
        return Mathf.Lerp(-0.12f, 0.12f,
            Mathf.Clamp01(index / (float)(streakCount - 1)));
    }

    public static Vector3 ResolveLandingGroundStreakDirection(
        Vector3 forward, float sideOffset)
    {
        Vector3 backward = ResolveBackward(forward);
        Vector3 right = ResolveRight(forward);
        return (backward + right * (sideOffset * 2.2f)).normalized;
    }

    public static Color ResolveLandingGroundColor()
    {
        return LandingCyan;
    }

    public static float ResolveSlideSustainInterval(VisualQuality quality)
    {
        return quality == VisualQuality.High ? 0.055f : 0.085f;
    }

    public static bool CanEmitSlideSustain(float now, float lastEmission,
        float interval)
    {
        return now - lastEmission >= Mathf.Max(0f, interval);
    }

    public static float ResolveSlideContactStrength(float slide01)
    {
        float normalized = Mathf.Clamp01(slide01);
        float middle = 1f - Mathf.Abs(normalized * 2f - 1f);
        return Mathf.Lerp(0.55f, 1f,
            Mathf.SmoothStep(0f, 1f, middle));
    }

    public static int ResolveImpactLineCount(bool fatal,
        VisualQuality quality)
    {
        if (fatal) return quality == VisualQuality.High ? 6 : 4;
        return quality == VisualQuality.High ? 4 : 2;
    }

    public static int ResolveCounterSuccessLineCount(VisualQuality quality)
    {
        return quality == VisualQuality.High ? 4 : 2;
    }

    private void EmitSlideContact(Vector3 pos, Vector3 forward,
        float strength, float speed, float lifetime)
    {
        VisualQuality quality = VisualQualityController.Current;
        int lineCount = ResolveSlideContactLineCount(quality);
        Vector3 backward = ResolveBackward(forward);
        Vector3 right = ResolveRight(forward);

        for (int index = 0; index < lineCount; index++)
        {
            float offset = ResolveContactLineOffset(index, lineCount);
            EmitContact(_contactLinePS, pos + right * offset,
                backward * (speed * strength), ContactWhite, lifetime,
                0.055f * strength);
        }

        if (ShouldEmitContactEcho(quality))
        {
            EmitContact(_contactEchoPS, pos + backward * 0.1f,
                backward * (speed * 0.72f * strength), EchoCyan,
                lifetime + 0.045f, 0.045f * strength);
        }
    }

    public static float ResolveContactLineOffset(int index, int lineCount)
    {
        if (lineCount <= 1) return 0f;
        return Mathf.Lerp(-0.24f, 0.24f,
            Mathf.Clamp01(index / (float)(lineCount - 1)));
    }

    private static Vector3 ResolveBackward(Vector3 forward)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        return -forward.normalized;
    }

    private static Vector3 ResolveRight(Vector3 forward)
    {
        Vector3 backward = ResolveBackward(forward);
        return Vector3.Cross(backward, Vector3.up).normalized;
    }

    private static void EmitContact(ParticleSystem system, Vector3 pos,
        Vector3 velocity, Color color, float lifetime, float size)
    {
        if (system == null) return;
        var emission = new ParticleSystem.EmitParams
        {
            position = pos,
            velocity = velocity,
            startColor = color,
            startLifetime = lifetime,
            startSize = size
        };
        system.Emit(emission, 1);
    }

    private void ApplyQuality(VisualQuality quality)
    {
        bool high = quality == VisualQuality.High;
        if (_trailPS != null)
        {
            var emission = _trailPS.emission;
            emission.enabled = high;
            var main = _trailPS.main;
            main.startLifetime = high ? 0.62f : 0.25f;
        }
        if (!high && _contactEchoPS != null)
        {
            _contactEchoPS.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void OnDestroy()
    {
        VisualQualityController.Changed -= ApplyQuality;
        if (_defaultMat != null) Destroy(_defaultMat);
        if (Instance == this) Instance = null;
    }
}
