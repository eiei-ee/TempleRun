using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public sealed class EchoVisualCaptureProbe : MonoBehaviour
{
    public const string CaptureDistancesArgumentPrefix =
        "-echo-qa-capture-distances=";
    public const string OffscreenCaptureArgument = "-echo-qa-offscreen-capture";

    private const string CaptureDirectoryName = "VisualCaptures";
    private const float DistanceTolerance = 0.0001f;

    private float[] _targetDistances;
    private bool _offscreenCapture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateWhenExplicitlyRequested()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        float[] distances = ParseCaptureDistances(arguments);
        if (distances.Length == 0) return;

        GameObject host = new GameObject("EchoVisualCaptureProbe_Runtime");
        DontDestroyOnLoad(host);
        EchoVisualCaptureProbe probe =
            host.AddComponent<EchoVisualCaptureProbe>();
        probe._targetDistances = distances;
        probe._offscreenCapture = UsesOffscreenCapture(arguments);
    }

    private IEnumerator Start()
    {
        if (_targetDistances == null || _targetDistances.Length == 0)
        {
            yield break;
        }

        int targetIndex = 0;
        string outputDirectory = null;

        while (targetIndex < _targetDistances.Length)
        {
            GameManager gameManager = GameManager.Instance;
            float actualDistance = gameManager != null
                ? gameManager.Distance
                : 0f;
            GameState state = gameManager != null
                ? gameManager.State
                : GameState.Menu;
            float targetDistance = _targetDistances[targetIndex];

            Debug.Log("ECHO_VISUAL_CAPTURE_FRAME target="
                      + FormatDistance(targetDistance)
                      + " actual=" + FormatDistance(actualDistance)
                      + " state=" + state);

            if (gameManager == null || state != GameState.Playing
                || actualDistance + DistanceTolerance < targetDistance)
            {
                yield return null;
                continue;
            }

            yield return new WaitForEndOfFrame();

            gameManager = GameManager.Instance;
            actualDistance = gameManager != null
                ? gameManager.Distance
                : actualDistance;
            outputDirectory = outputDirectory ?? CaptureOutputDirectory();
            Directory.CreateDirectory(outputDirectory);

            string fileName = BuildCaptureFileName(targetDistance,
                actualDistance, Screen.width, Screen.height);
            string capturePath = Path.Combine(outputDirectory, fileName);
            if (_offscreenCapture)
                CaptureOffscreen(capturePath);
            else
                ScreenCapture.CaptureScreenshot(capturePath);
            targetIndex++;

            // Give Unity a frame to submit the screenshot request before
            // advancing to the next target or freezing the finished run.
            yield return null;
        }

        Debug.Log("ECHO_VISUAL_CAPTURE_COMPLETE count="
                  + _targetDistances.Length
                  + " directory=" + outputDirectory);
        Time.timeScale = 0f;
        enabled = false;
    }

    public static float[] ParseCaptureDistances(string[] arguments)
    {
        if (arguments == null || arguments.Length == 0)
            return Array.Empty<float>();

        SortedSet<float> parsed = new SortedSet<float>();
        for (int argumentIndex = 0;
             argumentIndex < arguments.Length;
             argumentIndex++)
        {
            string argument = arguments[argumentIndex];
            if (string.IsNullOrEmpty(argument)
                || !argument.StartsWith(CaptureDistancesArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            string values = argument.Substring(
                CaptureDistancesArgumentPrefix.Length);
            string[] tokens = values.Split(',');
            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                if (!float.TryParse(tokens[tokenIndex].Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float distance)
                    || float.IsNaN(distance)
                    || float.IsInfinity(distance)
                    || distance < 0f)
                    continue;

                parsed.Add(distance);
            }
        }

        if (parsed.Count == 0) return Array.Empty<float>();
        float[] result = new float[parsed.Count];
        parsed.CopyTo(result);
        return result;
    }

    public static bool UsesOffscreenCapture(string[] arguments)
    {
        if (arguments == null) return false;
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, OffscreenCaptureArgument,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void CaptureOffscreen(string path)
    {
        Camera camera = Camera.main;
        if (camera == null)
            throw new InvalidOperationException(
                "Offscreen visual capture requires the active gameplay camera.");

        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        int previousCullingMask = camera.cullingMask;
        var overlayStates = new List<OverlayCanvasState>();
        // Collect every state before changing a parent canvas, since a child
        // can report a different effective render mode after that change.
        foreach (Canvas canvas in FindObjectsOfType<Canvas>())
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                overlayStates.Add(new OverlayCanvasState(canvas));
        }

        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            target = RenderTexture.GetTemporary(width, height, 24,
                RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            foreach (OverlayCanvasState state in overlayStates)
            {
                Canvas canvas = state.canvas;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.01f;
                // Overlay UI does not normally depend on camera culling.
                foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
                    camera.cullingMask |= 1 << child.gameObject.layer;
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log("ECHO_VISUAL_CAPTURE_OFFSCREEN camera=" + camera.name
                      + " size=" + width + "x" + height + " path=" + path);
        }
        finally
        {
            for (int index = overlayStates.Count - 1; index >= 0; index--)
                overlayStates[index].Restore();
            camera.targetTexture = previousTarget;
            camera.cullingMask = previousCullingMask;
            RenderTexture.active = previousActive;
            if (image != null) Destroy(image);
            if (target != null) RenderTexture.ReleaseTemporary(target);
            Canvas.ForceUpdateCanvases();
        }
    }

    private readonly struct OverlayCanvasState
    {
        public readonly Canvas canvas;
        private readonly RenderMode _renderMode;
        private readonly Camera _worldCamera;
        private readonly float _planeDistance;

        public OverlayCanvasState(Canvas canvas)
        {
            this.canvas = canvas;
            _renderMode = canvas.renderMode;
            _worldCamera = canvas.worldCamera;
            _planeDistance = canvas.planeDistance;
        }

        public void Restore()
        {
            if (canvas == null) return;
            canvas.renderMode = _renderMode;
            canvas.worldCamera = _worldCamera;
            canvas.planeDistance = _planeDistance;
        }
    }

    private static string CaptureOutputDirectory()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..",
            "Builds", "Windows", CaptureDirectoryName));
#else
        string executableDirectory = Path.GetDirectoryName(
            Application.dataPath);
        return Path.GetFullPath(Path.Combine(
            string.IsNullOrEmpty(executableDirectory)
                ? Application.dataPath
                : executableDirectory,
            CaptureDirectoryName));
#endif
    }

    private static string BuildCaptureFileName(float targetDistance,
        float actualDistance, int width, int height)
    {
        return "target-" + FileSafeDistance(targetDistance)
               + "m_actual-" + FileSafeDistance(actualDistance)
               + "m_" + Mathf.Max(0, width)
               + "x" + Mathf.Max(0, height) + ".png";
    }

    private static string FileSafeDistance(float distance)
    {
        return FormatDistance(distance).Replace('.', 'p');
    }

    private static string FormatDistance(float distance)
    {
        return distance.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
