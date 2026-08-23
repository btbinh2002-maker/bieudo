namespace TrainTimetable.Domain;

public sealed record TrainServiceTrajectory
{
    public required string ServiceId { get; init; }
    public required IReadOnlyList<TimetableEntry> Entries { get; init; }

    public TimetableEntry First => Entries[0];
    public TimetableEntry Last => Entries[^1];

    public TimetableEntry GetEntry(int stationSequence) =>
        Entries.FirstOrDefault(e => e.StationSequence == stationSequence)
        ?? throw new ArgumentException($"Khong tim thay ga {stationSequence} trong trajectory cua {ServiceId}.");

    public int IndexOf(int stationSequence)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].StationSequence == stationSequence)
            {
                return i;
            }
        }

        throw new ArgumentException($"Khong tim thay ga {stationSequence} trong trajectory cua {ServiceId}.");
    }
}
