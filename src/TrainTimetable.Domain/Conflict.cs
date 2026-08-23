namespace TrainTimetable.Domain;

/// <summary>
/// A = occupation co EntryTimeMinutes nho hon ("Earlier"), B = occupation con lai ("Later") - quy uoc
/// co dinh de cac cong thuc ActualGapMinutes/HeadwayDeficitMinutes khong phu thuoc thu tu tham so.
/// </summary>
public sealed record Conflict
{
    public required string ConflictId { get; init; }
    public required ConflictType Type { get; init; }
    public required ConstraintKind ConstraintKind { get; init; }
    public required string ServiceIdA { get; init; }
    public required int CycleIndexA { get; init; }
    public required string ServiceIdB { get; init; }
    public required int CycleIndexB { get; init; }
    public required string SectionId { get; init; }
    public required int ConflictStartTimeMinutes { get; init; }
    public required int ConflictEndTimeMinutes { get; init; }
    public required int RequiredHeadwayMinutes { get; init; }
    public required int ActualGapMinutes { get; init; }
    public required int HeadwayDeficitMinutes { get; init; }
}
