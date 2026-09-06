using NUnit.Framework;

public sealed class EchoVisualCaptureProbeTests
{
    [TestCase("-echo-qa-offscreen-capture", true)]
    [TestCase("-ECHO-QA-OFFSCREEN-CAPTURE", true)]
    [TestCase("-echo-qa-offscreen-capture=false", false)]
    [TestCase("-echo-qa-capture-distances=22", false)]
    [TestCase(null, false)]
    public void OffscreenRenderingRequiresTheExactExplicitFlag(
        string argument, bool expected)
    {
        Assert.AreEqual(expected,
            EchoVisualCaptureProbe.UsesOffscreenCapture(new[] { argument }));
        Assert.IsFalse(EchoVisualCaptureProbe.UsesOffscreenCapture(null));
        Assert.IsFalse(EchoVisualCaptureProbe.UsesOffscreenCapture(new string[0]));
    }

    [Test]
    public void ParserRemainsDisabledWithoutTheExplicitArgument()
    {
        CollectionAssert.IsEmpty(
            EchoVisualCaptureProbe.ParseCaptureDistances(null));
        CollectionAssert.IsEmpty(
            EchoVisualCaptureProbe.ParseCaptureDistances(new string[0]));
        CollectionAssert.IsEmpty(
            EchoVisualCaptureProbe.ParseCaptureDistances(new[]
            {
                "EchoRun.exe",
                "-echo-single-contract-validation"
            }));
    }

    [Test]
    public void ParserIgnoresInvalidAndUnsafeDistanceTokens()
    {
        float[] distances = EchoVisualCaptureProbe.ParseCaptureDistances(
            new[]
            {
                "-echo-qa-capture-distances=invalid,-1,NaN,Infinity,,22"
            });

        CollectionAssert.AreEqual(new[] { 22f }, distances);
        CollectionAssert.IsEmpty(
            EchoVisualCaptureProbe.ParseCaptureDistances(new[]
            {
                "-echo-qa-capture-distances="
            }));
    }

    [Test]
    public void ParserMergesSortsAndDeduplicatesExplicitDistances()
    {
        float[] distances = EchoVisualCaptureProbe.ParseCaptureDistances(
            new[]
            {
                "-ECHO-QA-CAPTURE-DISTANCES=51,22,8,22",
                "-echo-qa-capture-distances=36,8.5,0"
            });

        CollectionAssert.AreEqual(
            new[] { 0f, 8f, 8.5f, 22f, 36f, 51f }, distances);
    }
}
