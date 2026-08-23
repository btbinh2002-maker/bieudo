namespace TrainTimetable.Domain;

/// <summary>
/// Su kien chiem dung 1 Section cua 1 TrainInstance (ServiceId + CycleIndex, muc 1.4/1.6) - suy dien tu
/// TrainServiceTrajectory, khong luu tru doc lap.
/// </summary>
public sealed record SectionOccupation
{
    public required string SectionId { get; init; }
    public required string ServiceId { get; init; }
    public required int CycleIndex { get; init; }
    public required Direction Direction { get; init; }
    public required int EntryTimeMinutes { get; init; }
    public required int ExitTimeMinutes { get; init; }
}
