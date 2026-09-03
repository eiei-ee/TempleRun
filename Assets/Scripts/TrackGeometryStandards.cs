using UnityEngine;

public static class TrackGeometryStandards
{
    public const float StandardSegmentLength = 20f;
    public const float LaneSpacing = 3f;
    public const float WalkableWidth = 9f;
    public const float VisualRoadWidth = 11f;
    public const float VisualRoadHalfWidth = VisualRoadWidth * 0.5f;
    public const float AuthoredRoadSurfaceTopY = 0.10f;
    public const float EdgeRailInset = 0.15f;
    public const float EdgeRailOffset = VisualRoadHalfWidth - EdgeRailInset;
    public const float TurnWalkableBridgeWidth =
        VisualRoadHalfWidth - WalkableWidth * 0.5f;
    public const float TurnWalkableBridgeCenterOffset =
        WalkableWidth * 0.5f + TurnWalkableBridgeWidth * 0.5f;
    // The follow camera sits roughly eight metres behind the player and
    // sweeps across the outside-rear quadrant while a 90 degree turn settles.
    // Large turn landmarks need a wider centre offset than straight dressing
    // so their visible shell never intersects that camera path.
    public const float TurnNearDecorationCenterOffset = 16.5f;
    public const float TurnCameraShellClearance = 3.0f;

    public static float GetLaneCenter(int lane)
    {
        return (Mathf.Clamp(lane, 0, 2) - 1) * LaneSpacing;
    }

    public static float TurnEntrySurfaceLength(float segmentLength)
    {
        return Mathf.Max(0f, segmentLength * 0.5f) + VisualRoadHalfWidth;
    }

    public static float TurnEntrySurfaceCenter(float segmentLength)
    {
        return TurnEntrySurfaceLength(segmentLength) * 0.5f;
    }

    public static float TurnExitSurfaceLength(float segmentLength)
    {
        return Mathf.Max(0.01f,
            segmentLength * 0.5f - VisualRoadHalfWidth);
    }

    public static float TurnExitSurfaceCenter(float segmentLength)
    {
        return VisualRoadHalfWidth
               + TurnExitSurfaceLength(segmentLength) * 0.5f;
    }

    public static float TurnInnerCornerSize(float segmentLength)
    {
        return TurnExitSurfaceLength(segmentLength);
    }

    public static Vector3 TurnInnerCornerCenter(float segmentLength,
        int turnDirection)
    {
        float size = TurnInnerCornerSize(segmentLength);
        return new Vector3(
            Mathf.Sign(turnDirection) * (VisualRoadHalfWidth + size * 0.5f),
            0f, size * 0.5f);
    }

    public static Vector3 TurnWalkableBridgeCenter(float segmentLength,
        int turnDirection)
    {
        return new Vector3(
            Mathf.Sign(turnDirection) * TurnWalkableBridgeCenterOffset,
            0f, Mathf.Max(0f, segmentLength) * 0.5f);
    }
}
