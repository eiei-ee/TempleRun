using System;

public enum GameplayFlowMode
{
    SixPhaseLegacy,
    SingleContract
}

[Serializable]
public sealed class SingleContractValidationConfig
{
    public bool enabled;
    public int fixedSeed = 1337;
    public bool freezeDirector;
    public bool disablePowerUps;
    public bool forceStandardDifficulty;
    public bool autoStart;
    public bool useFixedIdentity;

    public SingleContractValidationConfig Clone()
    {
        return new SingleContractValidationConfig
        {
            enabled = enabled,
            fixedSeed = fixedSeed,
            freezeDirector = freezeDirector,
            disablePowerUps = disablePowerUps,
            forceStandardDifficulty = forceStandardDifficulty,
            autoStart = autoStart,
            useFixedIdentity = useFixedIdentity
        };
    }

    public static SingleContractValidationConfig CopyOf(
        SingleContractValidationConfig source)
    {
        return source != null
            ? source.Clone()
            : new SingleContractValidationConfig();
    }
}

public static class SingleContractValidationLaunchOptions
{
    public const string EnableArgument =
        "-echo-single-contract-validation";
    public const string SeedArgumentPrefix =
        "-echo-single-contract-seed=";
    public const string AutoStartArgument =
        "-echo-single-contract-autostart";
    public const string FixedIdentityArgument =
        "-echo-single-contract-fixed-identity";

    public static bool TryParse(string[] arguments,
        out SingleContractValidationConfig validation, out string error)
    {
        validation = null;
        error = "";
        int fixedSeed = 1337;
        if (arguments == null) return false;

        bool enabled = false;
        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index] ?? "";
            if (string.Equals(argument, EnableArgument,
                    StringComparison.OrdinalIgnoreCase))
                enabled = true;
        }
        if (!enabled) return false;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index] ?? "";
            if (!argument.StartsWith(SeedArgumentPrefix,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            string value = argument.Substring(SeedArgumentPrefix.Length);
            if (!int.TryParse(value, out fixedSeed) || fixedSeed <= 0)
            {
                error = "Single-contract validation seed must be a positive integer.";
                return false;
            }
        }

        validation = new SingleContractValidationConfig
        {
            enabled = true,
            fixedSeed = fixedSeed,
            freezeDirector = true,
            disablePowerUps = true,
            forceStandardDifficulty = true,
            autoStart = HasArgument(arguments, AutoStartArgument),
            useFixedIdentity = HasArgument(arguments,
                FixedIdentityArgument)
        };
        return true;
    }

    private static bool HasArgument(string[] arguments, string expected)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], expected,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

public struct EchoRunContext
{
    public GameplayFlowMode mode;
    public int runSequence;
    public int runSeed;
    public int generation;
    public bool hasOpponent;
    public float courseDuration;
    public float courseDistance;
    public SingleContractValidationConfig validation;
}

public struct EchoRunFrame
{
    public float deltaTime;
    public float elapsedTime;
    public float currentSpeed;
    public float playerDistance;
    public float remainingDistance;
    public int playerLane;
    public bool hasLateralEvidence;
    public float lateralOffset;
    public bool laneChangeInProgress;
}

public struct GateChoice
{
    public int gateId;
    public int physicalLane;
    public float routeDistance;
    public float reactionTime;
    public bool hasLateralEvidence;
    public float lateralOffset;
    public bool laneChangeInProgress;
}

public struct GateObstacleEvent
{
    public int gateId;
    public int obstacleId;
    public int physicalLane;
    public ObstacleType obstacleType;
    public float routeDistance;
}

public struct RunSettlement
{
    public RunEndReason reason;
    public bool reachedFinish;
    public bool playerWon;
    public float playerLeadMeters;
}

public interface IEchoGameplayFlowRuntime
{
    GameplayFlowMode Mode { get; }

    bool OwnsSpecialEncounters { get; }
    bool OwnsLeadSettlement { get; }
    bool OwnsFinishSchedule { get; }

    void BeginRun(EchoRunContext context);
    void Tick(EchoRunFrame frame);

    void OnGateChoiceCommitted(GateChoice choice);
    void OnObstaclePassed(GateObstacleEvent obstacleEvent);
    void OnObstacleHit(GateObstacleEvent obstacleEvent);

    RunSettlement FinishRun(RunEndReason reason);
}
