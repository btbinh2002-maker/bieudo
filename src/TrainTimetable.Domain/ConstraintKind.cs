namespace TrainTimetable.Domain;

/// <summary>
/// Doc lap voi ConflictType (muc 5.3): ConstraintKind chi phu thuoc dau cua ActualGapMinutes, KHONG phu
/// thuoc chieu di chuyen - HEADWAY (cung chieu) van co the la SectionOverlap neu gap am.
/// </summary>
public enum ConstraintKind
{
    SectionOverlap,
    SectionReleaseHeadway,
    OrderReversal
}
