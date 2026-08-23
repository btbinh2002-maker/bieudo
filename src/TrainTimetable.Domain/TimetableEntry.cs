namespace TrainTimetable.Domain;

public sealed record TimetableEntry
{
    public required int StationSequence { get; init; }
    public int? ArrivalTimeMinutes { get; init; }
    public int? DepartureTimeMinutes { get; init; }
    public required StopType StopType { get; init; }
    public int StopDurationMinutes { get; init; }
    public int RunningTimeFromPrevMinutes { get; init; }
    public bool AccelerationApplied { get; init; }
    public bool DecelerationApplied { get; init; }
    public int RecoveryTimeFromPrevMinutes { get; init; }

    /// <summary>
    /// Tong so phut delay da tung duoc chen (InsertDelay) tinh don den ga nay - KHONG phai luong
    /// buffer/recovery thuc te da tieu (phan do co the da duoc RecoveryTimeFromPrev hap thu roi).
    /// Dung de audit "da co bao nhieu quyet dinh delay ap len tau nay", khong dung de suy ra buffer
    /// con lai - muon biet buffer con lai hay dung BufferCalculator.ComputeForwardSlackMinutes.
    /// </summary>
    public int CumulativeInsertedDelayMinutes { get; init; }
}
