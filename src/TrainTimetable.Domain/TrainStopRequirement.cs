namespace TrainTimetable.Domain;

public sealed record TrainStopRequirement
{
    public required int StationSequence { get; init; }
    public bool RequiresPassengerStop { get; init; }
    public bool RequiresTechnicalStop { get; init; }
    public int? StopDurationOverrideMinutes { get; init; }
}
