using UnityEngine;

public enum EchoHudTransitionKind
{
    None,
    Scan,
    Activate,
    Fracture,
    Release
}

public struct EchoHudSkin
{
    public Color panel;
    public Color panelRaised;
    public Color ink;
    public Color mutedInk;
    public Color rule;
    public Color accent;
    public Color accentSoft;
    public EchoHudTransitionKind transition;
}

public static class EchoRunUITheme
{
    public static readonly Color Backdrop = new Color32(20, 24, 31, 255);
    public static readonly Color Surface = new Color32(30, 36, 45, 255);
    public static readonly Color SurfaceRaised = new Color32(40, 48, 59, 255);
    public static readonly Color SurfaceSelected = new Color32(50, 63, 80, 255);
    // Keep the existing UI token names for callers; neutral blue-white now
    // carries ordinary emphasis. Track and character materials are separate.
    public static readonly Color RouteCyan = new Color32(193, 214, 239, 255);
    public static readonly Color RouteCyanDark = new Color32(71, 90, 115, 255);
    public static readonly Color ActionAccent = new Color32(238, 143, 76, 255);
    public static readonly Color ActionAccentDark = new Color32(156, 79, 37, 255);
    public static readonly Color Reward = new Color32(230, 169, 112, 255);
    public static readonly Color Danger = new Color32(255, 103, 90, 255);
    public static readonly Color Success = new Color32(195, 216, 237, 255);
    public static readonly Color TextPrimary = new Color32(235, 241, 249, 255);
    public static readonly Color TextMuted = new Color32(156, 171, 190, 255);
    public static readonly Color Ink = new Color32(16, 21, 29, 255);

    // The in-run HUD is one restrained floating instrument layer. Information
    // shares a dark translucent rail instead of becoming a stack of white
    // cards. Stage identity still belongs to the sparse accent geometry.
    public static readonly Color HudPanel = new Color32(17, 22, 29, 224);
    public static readonly Color HudPanelRaised = new Color32(25, 32, 42, 204);
    public static readonly Color HudMessageVeil = new Color32(0, 0, 0, 0);
    public static readonly Color HudPredictionVeil = new Color32(17, 22, 29, 220);
    public static readonly Color HudInk = new Color32(235, 242, 251, 255);
    public static readonly Color HudInkMuted = new Color32(166, 183, 204, 245);
    public static readonly Color HudRule = new Color32(183, 204, 230, 32);
    public static readonly Color HudTextShadow = new Color32(0, 0, 0, 190);
    public static readonly Color HudDangerText = new Color32(255, 117, 104, 255);
    public static readonly Color HudRewardText = new Color32(241, 165, 106, 255);
    public static readonly Color HudSuccessText = new Color32(191, 216, 243, 255);

    public static readonly Color HudCalibrationAccent =
        new Color32(133, 164, 199, 255);
    public static readonly Color HudChallengeAccent =
        new Color32(193, 214, 239, 255);
    public static readonly Color HudRelearnAccent =
        new Color32(238, 143, 76, 255);
    public static readonly Color HudFinaleAccent =
        new Color32(228, 178, 129, 255);

    public static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    public static EchoHudSkin HudSkinFor(SingleContractVisualState state)
    {
        switch (state)
        {
            case SingleContractVisualState.Challenge:
                return MakeHudSkin(HudChallengeAccent,
                    EchoHudTransitionKind.Activate);
            case SingleContractVisualState.RelearnPulse:
                return MakeHudSkin(HudRelearnAccent,
                    EchoHudTransitionKind.Fracture);
            case SingleContractVisualState.Finale:
                return MakeHudSkin(HudFinaleAccent,
                    EchoHudTransitionKind.Release);
            default:
                return MakeHudSkin(HudCalibrationAccent,
                    EchoHudTransitionKind.Scan);
        }
    }

    public static EchoHudSkin HudSkinFor(EchoHudMode mode)
    {
        switch (mode)
        {
            case EchoHudMode.Counterattack:
            case EchoHudMode.Rewrite:
            case EchoHudMode.FinaleFailed:
                return HudSkinFor(SingleContractVisualState.RelearnPulse);
            case EchoHudMode.FinaleClean:
            case EchoHudMode.FinaleContract:
                return HudSkinFor(SingleContractVisualState.Finale);
            case EchoHudMode.Reveal:
            case EchoHudMode.Resistance:
                return HudSkinFor(SingleContractVisualState.Challenge);
            default:
                return HudSkinFor(SingleContractVisualState.Calibration);
        }
    }

    private static EchoHudSkin MakeHudSkin(Color accent,
        EchoHudTransitionKind transition)
    {
        return new EchoHudSkin
        {
            panel = HudPanel,
            panelRaised = HudPanelRaised,
            ink = HudInk,
            mutedInk = HudInkMuted,
            rule = HudRule,
            accent = accent,
            accentSoft = WithAlpha(accent, 0.14f),
            transition = transition
        };
    }
}
