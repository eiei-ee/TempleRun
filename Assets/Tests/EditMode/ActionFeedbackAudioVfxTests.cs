using NUnit.Framework;
using UnityEngine;

public sealed class ActionFeedbackAudioVfxTests
{
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void FootstepsRequireRunRequestWithoutActionPause(bool requested,
        bool paused, bool expected)
    {
        Assert.AreEqual(expected,
            AudioManager.ShouldEmitFootsteps(requested, paused));
    }

    [Test]
    public void SlideLoopDoesNotRestartWhenRequestedClipIsAlreadyPlaying()
    {
        Assert.IsFalse(AudioManager.ShouldStartSlideLoop(true, true));
        Assert.IsTrue(AudioManager.ShouldStartSlideLoop(false, true));
        Assert.IsTrue(AudioManager.ShouldStartSlideLoop(true, false));
    }

    [Test]
    public void LandVolumeClampsToRestrainedRange()
    {
        Assert.AreEqual(0.42f,
            AudioManager.ResolveLandVolumeScale(-1f), 0.0001f);
        Assert.AreEqual(0.6f,
            AudioManager.ResolveLandVolumeScale(0.5f), 0.0001f);
        Assert.AreEqual(0.78f,
            AudioManager.ResolveLandVolumeScale(2f), 0.0001f);
    }

    [Test]
    public void FatalImpactCanReplaceRecoverableImpactInSameFrame()
    {
        Assert.IsTrue(AudioManager.ShouldPlayImpact(true, false, true));
        Assert.IsTrue(AudioManager.ShouldReplaceImpact(true, false, true));
        Assert.IsFalse(AudioManager.ShouldPlayImpact(true, true, false));
        Assert.IsFalse(AudioManager.ShouldReplaceImpact(true, true, false));
    }

    [Test]
    public void RepeatedImpactOfSameSeverityIsIgnoredWithinFrame()
    {
        Assert.IsFalse(AudioManager.ShouldPlayImpact(true, false, false));
        Assert.IsFalse(AudioManager.ShouldPlayImpact(true, true, true));
        Assert.IsTrue(AudioManager.ShouldPlayImpact(false, true, false));
    }

    [Test]
    public void SpeedFeedbackLifecycleRequiresAnActiveRun()
    {
        Assert.AreEqual(0f,
            AudioManager.ResolveStoredSpeedFeedback(false, 0.8f));
        Assert.AreEqual(0.8f,
            AudioManager.ResolveStoredSpeedFeedback(true, 0.8f), 0.0001f);
        Assert.AreEqual(1f,
            AudioManager.ResolveStoredSpeedFeedback(true, 2f), 0.0001f);
    }

    [Test]
    public void FootstepIntervalShortensContinuouslyWithoutCrossingFloor()
    {
        float slow = AudioManager.ResolveTargetFootstepInterval(
            0.35f, 0.2f, 0f);
        float medium = AudioManager.ResolveTargetFootstepInterval(
            0.35f, 0.2f, 0.5f);
        float fast = AudioManager.ResolveTargetFootstepInterval(
            0.35f, 0.2f, 1f);

        Assert.AreEqual(0.35f, slow, 0.0001f);
        Assert.Less(medium, slow);
        Assert.Greater(medium, fast);
        Assert.AreEqual(0.2f, fast, 0.0001f);
        Assert.GreaterOrEqual(AudioManager.ResolveTargetFootstepInterval(
            0.35f, 0.05f, 1f), 0.12f);
    }

    [Test]
    public void FootstepIntervalSmoothingDoesNotJumpOrOvershoot()
    {
        float unchanged = AudioManager.SmoothFootstepInterval(
            0.35f, 0.2f, 7f, 0f);
        float next = AudioManager.SmoothFootstepInterval(
            0.35f, 0.2f, 7f, 1f / 60f);

        Assert.AreEqual(0.35f, unchanged, 0.0001f);
        Assert.Less(next, 0.35f);
        Assert.Greater(next, 0.2f);
    }

    [Test]
    public void ActionPauseDoesNotInterruptWorldSpeedWind()
    {
        Assert.IsFalse(AudioManager.ShouldPlaySpeedWind(false, false, 0.8f));
        Assert.IsFalse(AudioManager.ShouldPlaySpeedWind(true, false, 0f));
        Assert.IsTrue(AudioManager.ShouldPlaySpeedWind(true, false, 0.8f));
        Assert.IsTrue(AudioManager.ShouldPlaySpeedWind(true, true, 0.8f));
    }

    [Test]
    public void SpeedWindRemainsQuietAndScalesContinuously()
    {
        float stopped = AudioManager.ResolveSpeedWindVolumeScale(0f, 0.12f);
        float medium = AudioManager.ResolveSpeedWindVolumeScale(0.5f, 0.12f);
        float fast = AudioManager.ResolveSpeedWindVolumeScale(1f, 0.12f);

        Assert.AreEqual(0f, stopped, 0.0001f);
        Assert.Greater(medium, stopped);
        Assert.Less(medium, fast);
        Assert.AreEqual(0.12f, fast, 0.0001f);
        Assert.LessOrEqual(
            AudioManager.ResolveSpeedWindVolumeScale(1f, 1f), 0.2f);
    }

    [Test]
    public void SpeedWindPitchStaysInRestrainedRange()
    {
        Assert.AreEqual(0.86f,
            AudioManager.ResolveSpeedWindPitch(-1f), 0.0001f);
        Assert.AreEqual(1.16f,
            AudioManager.ResolveSpeedWindPitch(2f), 0.0001f);
    }

    [Test]
    public void LowQualityKeepsOneCoreSlideLineWithoutEcho()
    {
        Assert.AreEqual(1,
            ParticleManager.ResolveSlideContactLineCount(VisualQuality.Low));
        Assert.IsFalse(
            ParticleManager.ShouldEmitContactEcho(VisualQuality.Low));
    }

    [Test]
    public void HighQualityAddsSecondSlideLineAndCyanEcho()
    {
        Assert.AreEqual(2,
            ParticleManager.ResolveSlideContactLineCount(VisualQuality.High));
        Assert.IsTrue(
            ParticleManager.ShouldEmitContactEcho(VisualQuality.High));
    }

    [Test]
    public void CoinAbsorbLengthDoesNotGrowWithAirbornePickupVelocity()
    {
        Assert.AreEqual(2.4f,
            ParticleManager.ResolveCoinAbsorbLengthScale(), 0.0001f);
        Assert.AreEqual(0f,
            ParticleManager.ResolveCoinAbsorbVelocityScale(), 0.0001f);
    }

    [Test]
    public void LandingUsesLowCyanBackwardStreaksInsteadOfCrossingWhiteBurst()
    {
        Assert.AreEqual(1,
            ParticleManager.ResolveLandingGroundStreakCount(
                VisualQuality.Low));
        Assert.AreEqual(2,
            ParticleManager.ResolveLandingGroundStreakCount(
                VisualQuality.High));

        float leftOffset = ParticleManager.ResolveLandingGroundStreakOffset(
            0, 2);
        float rightOffset = ParticleManager.ResolveLandingGroundStreakOffset(
            1, 2);
        Assert.Less(leftOffset, 0f);
        Assert.Greater(rightOffset, 0f);
        Assert.AreEqual(-leftOffset, rightOffset, 0.0001f);

        Vector3 leftDirection =
            ParticleManager.ResolveLandingGroundStreakDirection(
                Vector3.forward, leftOffset);
        Vector3 rightDirection =
            ParticleManager.ResolveLandingGroundStreakDirection(
                Vector3.forward, rightOffset);
        Assert.Greater(Vector3.Dot(leftDirection, Vector3.back), 0.9f);
        Assert.Greater(Vector3.Dot(rightDirection, Vector3.back), 0.9f);
        Assert.Less(leftDirection.x, 0f);
        Assert.Greater(rightDirection.x, 0f);
        Assert.AreEqual(0f, leftDirection.y, 0.0001f);
        Assert.AreEqual(0f, rightDirection.y, 0.0001f);

        Color color = ParticleManager.ResolveLandingGroundColor();
        Assert.Greater(color.g, color.r);
        Assert.Greater(color.b, color.r);
        Assert.Less(color.a, 0.6f);
    }

    [Test]
    public void SlideSustainEmissionIsRateLimitedPerQualityTier()
    {
        float lowInterval = ParticleManager.ResolveSlideSustainInterval(
            VisualQuality.Low);
        float highInterval = ParticleManager.ResolveSlideSustainInterval(
            VisualQuality.High);

        Assert.Greater(lowInterval, highInterval);
        Assert.IsFalse(ParticleManager.CanEmitSlideSustain(
            1f + lowInterval * 0.5f, 1f, lowInterval));
        Assert.IsTrue(ParticleManager.CanEmitSlideSustain(
            1f + lowInterval, 1f, lowInterval));
    }

    [Test]
    public void SlideContactIsStrongestDuringStableHold()
    {
        float enter = ParticleManager.ResolveSlideContactStrength(0f);
        float hold = ParticleManager.ResolveSlideContactStrength(0.5f);
        float exit = ParticleManager.ResolveSlideContactStrength(1f);

        Assert.Greater(hold, enter);
        Assert.AreEqual(enter, exit, 0.0001f);
    }

    [Test]
    public void HighQualitySlideLinesStaySymmetricAroundPlayerRoot()
    {
        float left = ParticleManager.ResolveContactLineOffset(0, 2);
        float right = ParticleManager.ResolveContactLineOffset(1, 2);

        Assert.Less(left, 0f);
        Assert.Greater(right, 0f);
        Assert.AreEqual(-left, right, 0.0001f);
        Assert.AreEqual(0f,
            ParticleManager.ResolveContactLineOffset(0, 1), 0.0001f);
    }

    [Test]
    public void ImpactLinesRemainBoundedAndScaleByResultAndQuality()
    {
        int recoverLow = ParticleManager.ResolveImpactLineCount(false,
            VisualQuality.Low);
        int recoverHigh = ParticleManager.ResolveImpactLineCount(false,
            VisualQuality.High);
        int fatalHigh = ParticleManager.ResolveImpactLineCount(true,
            VisualQuality.High);

        Assert.AreEqual(2, recoverLow);
        Assert.AreEqual(4, recoverHigh);
        Assert.AreEqual(6, fatalHigh);
        Assert.LessOrEqual(fatalHigh, 6);
    }

    [Test]
    public void CounterSuccessKeepsAReadableFractureAtBothQualityTiers()
    {
        Assert.AreEqual(2,
            ParticleManager.ResolveCounterSuccessLineCount(
                VisualQuality.Low));
        Assert.AreEqual(4,
            ParticleManager.ResolveCounterSuccessLineCount(
                VisualQuality.High));
    }
}
