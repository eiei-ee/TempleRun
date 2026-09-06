using UnityEngine;

public static class UILayoutRules
{
    private static readonly Vector2 LandscapeReference = new Vector2(1920f, 1080f);
    private static readonly Vector2 PortraitReference = new Vector2(1080f, 1920f);

    public static bool ShouldShowLandscapeGuard(
        int width, int height, bool touchLayout)
    {
        return ShouldShowLandscapeGuard(width, height, touchLayout, false);
    }

    public static bool ShouldShowLandscapeGuard(
        int width, int height, bool touchLayout, bool allowPortrait)
    {
        return !allowPortrait && touchLayout && width > 0 && height > width;
    }

    public static bool IsCompactPortrait(int width, int height)
    {
        return width > 0 && height > width;
    }

    public static Vector2 GetReferenceResolution(int width, int height)
    {
        return IsCompactPortrait(width, height)
            ? PortraitReference
            : LandscapeReference;
    }

    public static Vector2 EnsureTouchButtonSize(
        Vector2 requested, bool touchLayout, bool portrait)
    {
        if (touchLayout || portrait)
            requested.y = Mathf.Max(requested.y, 104f);
        return requested;
    }

    public static Vector2 EnsureTouchSliderSize(
        Vector2 requested, bool touchLayout, bool portrait)
    {
        if (touchLayout || portrait)
            requested.y = Mathf.Max(requested.y, 72f);
        return requested;
    }

    public static Vector2 GetPrimaryActionSize(
        int width, int height, bool touchLayout)
    {
        bool portrait = IsCompactPortrait(width, height);
        return EnsureTouchButtonSize(portrait
            ? new Vector2(760f, 104f)
            : new Vector2(520f, 78f), touchLayout || portrait, portrait);
    }

    public static Vector2 GetHomeNavigationAnchor(int index, bool portrait)
    {
        int safeIndex = Mathf.Clamp(index, 0, 2);
        return portrait
            ? new Vector2(0.22f + safeIndex * 0.28f, 0.095f)
            : new Vector2(0.085f + safeIndex * 0.105f, 0.095f);
    }

    public static Vector2 GetHomeNavigationSize(bool portrait,
        bool touchLayout)
    {
        return EnsureTouchButtonSize(portrait
            ? new Vector2(260f, 104f)
            : new Vector2(180f, 56f), touchLayout || portrait, portrait);
    }

    public static Vector2 GetRestartButtonSize(
        int width, int height, bool touchLayout)
    {
        bool portrait = IsCompactPortrait(width, height);
        return EnsureTouchButtonSize(portrait
            ? new Vector2(520f, 104f)
            : new Vector2(380f, 76f), touchLayout || portrait, portrait);
    }

    public static Vector2 GetMenuButtonSize(
        int width, int height, bool touchLayout)
    {
        bool portrait = IsCompactPortrait(width, height);
        return EnsureTouchButtonSize(portrait
            ? new Vector2(420f, 104f)
            : new Vector2(280f, 60f), touchLayout || portrait, portrait);
    }

    public static Vector2 GetResultTextSize(int width, int height)
    {
        return IsCompactPortrait(width, height)
            ? new Vector2(900f, 360f)
            : new Vector2(1160f, 260f);
    }

    public static Rect NormalizeSafeArea(Rect reported, int width, int height)
    {
        Rect fullScreen = new Rect(0f, 0f,
            Mathf.Max(0, width), Mathf.Max(0, height));
        if (width <= 0 || height <= 0
            || !IsFinite(reported.x) || !IsFinite(reported.y)
            || !IsFinite(reported.width) || !IsFinite(reported.height))
            return fullScreen;

        const float tolerance = 0.5f;
        bool insideCanvas = reported.xMin >= -tolerance
                            && reported.yMin >= -tolerance
                            && reported.xMax <= width + tolerance
                            && reported.yMax <= height + tolerance;
        bool usable = reported.width >= width * 0.5f
                      && reported.height >= height * 0.5f;
        if (!insideCanvas || !usable) return fullScreen;

        float xMin = Mathf.Clamp(reported.xMin, 0f, width);
        float yMin = Mathf.Clamp(reported.yMin, 0f, height);
        float xMax = Mathf.Clamp(reported.xMax, xMin, width);
        float yMax = Mathf.Clamp(reported.yMax, yMin, height);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
