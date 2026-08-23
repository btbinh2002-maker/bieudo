namespace TrainTimetable.Configuration;

public interface IRunningTimeRules
{
    int AccelerationPenaltyMinutes { get; }
    int DecelerationPenaltyMinutes { get; }
}

public sealed class RunningTimeRules : IRunningTimeRules
{
    public int AccelerationPenaltyMinutes { get; init; } = 2;
    public int DecelerationPenaltyMinutes { get; init; } = 1;
}
