namespace TrainTimetable.Domain;

public enum TrackType
{
    MainThrough,
    Siding,
    Platform
}

public sealed record StationTrack(
    string TrackId,
    TrackType TrackType,
    bool UsableForMeet,
    bool UsableForOvertake);
