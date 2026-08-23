using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>
/// Doc thoi diem Entry/Exit THUC TE cua MOT service tai mot SectionId cho truoc - dung chung boi
/// RequiredShiftCalculator (muc 7.2) thay vi moi cong thuc tu viet lai phep tim cap TimetableEntry giap
/// Section (da co san logic nay o SectionOccupationBuilder, muc 1.6/15.13 - chi loc ket qua co san,
/// khong duplicate).
///
/// `trajectory` truyen vao co the la mot ban PREVIEW (muc 7.2) thay vi trajectory that cua service - cung
/// mot resolver dung duoc cho ca 2 truong hop, vi no chi doc du lieu tu dung `trajectory` duoc truyen,
/// khong tu y lay trajectory "chinh thuc" cua service tu dau khac.
/// </summary>
public static class SectionTimingResolver
{
    public static int GetEntryTime(
        TrainService service, TrainServiceTrajectory trajectory, RailwayNetwork network, string sectionId, int cycleIndex) =>
        SectionOccupationBuilder.BuildForCycle(service, trajectory, network, cycleIndex)
            .Single(o => o.SectionId == sectionId)
            .EntryTimeMinutes;

    public static int GetExitTime(
        TrainService service, TrainServiceTrajectory trajectory, RailwayNetwork network, string sectionId, int cycleIndex) =>
        SectionOccupationBuilder.BuildForCycle(service, trajectory, network, cycleIndex)
            .Single(o => o.SectionId == sectionId)
            .ExitTimeMinutes;

    /// <summary>
    /// Local index cua ga VAO (entry side) cua SectionId tren chinh route nay - dung cho
    /// RequiredShiftCalculator de xac dinh doan [S, EntrySection] khi tinh RecoveryRemainingBetween
    /// (muc 7.2). Duyet truc tiep cap TimetableEntry ke nhau (khong qua SectionOccupationBuilder, vi
    /// muc dich la tim VI TRI trong mang, khong phai occupation - Section cua chinh conflict nay luon co
    /// physical traversal that tren route cua tau da sinh ra occupation, muc 1.6).
    /// </summary>
    public static int GetEntryIndex(TrainServiceTrajectory trajectory, RailwayNetwork network, string sectionId)
    {
        for (var i = 0; i < trajectory.Entries.Count - 1; i++)
        {
            var section = network.GetSectionBetween(trajectory.Entries[i].StationSequence, trajectory.Entries[i + 1].StationSequence);
            if (section.SectionId == sectionId)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"SectionId '{sectionId}' khong xuat hien tren route nay.");
    }

    /// <summary>
    /// Doc Arrival/Departure THUC (cong dung CycleIndex x 1440) tai MOT local index cho truoc - khac
    /// GetEntryTime/GetExitTime (doc tai ranh gioi 1 Section). Dung cho MEET (muc 7.1): candidate station S
    /// khong nhat thiet la ranh gioi Section, ma la 1 ga cu the tren route. KHONG fallback sang truong con
    /// lai neu null - Domain.TimetableEntry.ArrivalTimeMinutes/DepartureTimeMinutes = null dung nghia la
    /// "khong co su kien nay" (ga xuat phat/ga den), CandidateGenerator da dam bao invariant nay truoc khi
    /// goi toi day (muc 6.1 buoc 5) nen o day khong can/khong duoc kiem tra lai.
    /// </summary>
    public static int GetArrivalAtLocalIndex(TrainServiceTrajectory trajectory, int localIndex, int cycleIndex) =>
        trajectory.Entries[localIndex].ArrivalTimeMinutes!.Value + cycleIndex * TrainService.CycleLengthMinutes;

    public static int GetDepartureAtLocalIndex(TrainServiceTrajectory trajectory, int localIndex, int cycleIndex) =>
        trajectory.Entries[localIndex].DepartureTimeMinutes!.Value + cycleIndex * TrainService.CycleLengthMinutes;
}
