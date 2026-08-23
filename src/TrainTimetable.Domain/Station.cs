namespace TrainTimetable.Domain;

public sealed record Station
{
    public required string StationId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required int Sequence { get; init; }
    public required IReadOnlyList<StationTrack> Tracks { get; init; }

    public bool CanMeet => Tracks.Count(t => t.UsableForMeet) >= 2;
    public bool CanOvertake => Tracks.Count(t => t.UsableForOvertake) >= 2;
    public int MaxSimultaneousTrains => Tracks.Count;
}
