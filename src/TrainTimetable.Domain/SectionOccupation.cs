namespace TrainTimetable.Domain;

/// <summary>
/// Su kien chiem dung 1 Section cua 1 TrainInstance (ServiceId + CycleIndex, muc 1.4/1.6) - suy dien tu
/// TrainServiceTrajectory, khong luu tru doc lap.
///
/// SectionId la danh tinh DUNG CHUNG giua cac TrainService (phai suy tu cap StationCode vat ly that,
/// KHONG tu StationSequence cua rieng tung tau - muc 1.4) - day la truong DUY NHAT ConflictDetector
/// dung de nhom occupation cua nhieu tau lai voi nhau; no khong bao gio tra lai mot RailwayNetwork dung
/// chung de lam viec nay.
/// </summary>
public sealed record SectionOccupation
{
    public required string SectionId { get; init; }
    public required string ServiceId { get; init; }
    public required int CycleIndex { get; init; }
    public required Direction Direction { get; init; }
    public required int EntryTimeMinutes { get; init; }
    public required int ExitTimeMinutes { get; init; }
    public required int NumberOfTracks { get; init; }
}
