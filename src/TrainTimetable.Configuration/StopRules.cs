namespace TrainTimetable.Configuration;

public enum StopTimeCombineMode
{
    Max,
    Sum
}

public interface IStopRules
{
    int PassengerStopMinutes { get; }
    int TechnicalStopMinutes { get; }
    StopTimeCombineMode CombineMode { get; }

    int ResolveStopMinutes(bool requiresPassengerStop, bool requiresTechnicalStop, int? stopDurationOverrideMinutes);
}

public sealed class StopRules : IStopRules
{
    public int PassengerStopMinutes { get; init; } = 3;
    public int TechnicalStopMinutes { get; init; } = 20;
    public StopTimeCombineMode CombineMode { get; init; } = StopTimeCombineMode.Max;

    public int ResolveStopMinutes(bool requiresPassengerStop, bool requiresTechnicalStop, int? stopDurationOverrideMinutes)
    {
        if (stopDurationOverrideMinutes is { } overrideMinutes)
        {
            return overrideMinutes;
        }

        if (!requiresPassengerStop && !requiresTechnicalStop)
        {
            return 0;
        }

        if (requiresPassengerStop && requiresTechnicalStop)
        {
            return CombineMode switch
            {
                StopTimeCombineMode.Max => Math.Max(PassengerStopMinutes, TechnicalStopMinutes),
                StopTimeCombineMode.Sum => PassengerStopMinutes + TechnicalStopMinutes,
                _ => throw new NotSupportedException($"CombineMode khong duoc ho tro: {CombineMode}")
            };
        }

        return requiresPassengerStop ? PassengerStopMinutes : TechnicalStopMinutes;
    }
}
