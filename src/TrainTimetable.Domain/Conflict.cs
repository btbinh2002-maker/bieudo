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

    /// <summary>
    /// HEADWAY only (Earlier=Leader, Later=Follower - muc 1.7). Throw cho MEET/OVERTAKE thay vi tra ve
    /// mot gia tri "hop ly nhung sai" - Leader/Follower khong co y nghia ngoai HEADWAY.
    /// </summary>
    public string LeaderServiceId => Type == ConflictType.HEADWAY
        ? ServiceIdA
        : throw new InvalidOperationException($"{nameof(LeaderServiceId)} chi hop le cho Type=HEADWAY, khong phai {Type}");

    public int LeaderCycleIndex => Type == ConflictType.HEADWAY
        ? CycleIndexA
        : throw new InvalidOperationException($"{nameof(LeaderCycleIndex)} chi hop le cho Type=HEADWAY, khong phai {Type}");

    public string FollowerServiceId => Type == ConflictType.HEADWAY
        ? ServiceIdB
        : throw new InvalidOperationException($"{nameof(FollowerServiceId)} chi hop le cho Type=HEADWAY, khong phai {Type}");

    public int FollowerCycleIndex => Type == ConflictType.HEADWAY
        ? CycleIndexB
        : throw new InvalidOperationException($"{nameof(FollowerCycleIndex)} chi hop le cho Type=HEADWAY, khong phai {Type}");

    /// <summary>
    /// Dung cho MOI Type (khac Leader/Follower - chi HEADWAY): tra ve ben CON LAI cua serviceId truyen
    /// vao. Giu DUY NHAT 1 noi lam phep tra cuu A/B, tranh ternary serviceId == ServiceIdA rai rac o
    /// tung call-site (muc 1.7, review lan 5).
    /// </summary>
    public string OtherServiceId(string serviceId) => serviceId switch
    {
        _ when serviceId == ServiceIdA => ServiceIdB,
        _ when serviceId == ServiceIdB => ServiceIdA,
        _ => throw new InvalidOperationException($"{serviceId} khong phai ServiceIdA/ServiceIdB cua Conflict nay")
    };

    public int CycleIndexOf(string serviceId) => serviceId switch
    {
        _ when serviceId == ServiceIdA => CycleIndexA,
        _ when serviceId == ServiceIdB => CycleIndexB,
        _ => throw new InvalidOperationException($"{serviceId} khong phai ServiceIdA/ServiceIdB cua Conflict nay")
    };
}
