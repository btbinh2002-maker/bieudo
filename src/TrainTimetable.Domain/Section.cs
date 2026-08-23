namespace TrainTimetable.Domain;

public sealed record Section
{
    public required string SectionId { get; init; }
    public required int FromStationSequence { get; init; }
    public required int ToStationSequence { get; init; }
    public required IReadOnlyDictionary<Direction, int> MinRunningTimeMinutes { get; init; }
    public int NumberOfTracks { get; init; } = 1;
    public bool Passable { get; init; } = true;

    public int GetMinRunningTimeMinutes(Direction direction) => MinRunningTimeMinutes[direction];
}
