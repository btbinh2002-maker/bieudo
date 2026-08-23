namespace TrainTimetable.Configuration;

public interface IHeadwayRules
{
    int SectionReleaseHeadwayMinutes { get; }
    int OvertakeHeadway { get; }
}

/// <summary>
/// SectionReleaseHeadwayMinutes dung chung cho ca MEET (nguoc chieu) va HEADWAY (cung chieu) tren cung
/// Section - dung mot quy tac duy nhat, khong tach MeetHeadway/SameDirectionHeadway (xem thiet ke muc
/// 5.2/5.6). OvertakeHeadway la khai niem rieng, dung o RequiredShiftCalculator (muc 7.2), chua ra soat
/// lai trong dot sua nay.
/// </summary>
public sealed class HeadwayRules : IHeadwayRules
{
    public int SectionReleaseHeadwayMinutes { get; init; } = 3;
    public int OvertakeHeadway { get; init; } = 3;
}
