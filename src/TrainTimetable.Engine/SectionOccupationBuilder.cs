using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>
/// Chuyen mot TrainServiceTrajectory (logical - moi TimetableEntry la 1 "dong" trong hanh trinh, muc
/// 1.5) thanh danh sach SectionOccupation (physical - chi nhung khu gian tau THUC SU chiem dung, muc
/// 1.6).
///
/// QUAN TRONG (sua lai sau review - xem thiet ke muc 15.13.2/15.13.7): lop nay KHONG PHAI noi quyet
/// dinh "logical row nao la bypass" - viec do thuoc ve buoc validate input (BranchStationRule) TRUOC
/// khi trajectory duoc dung. Khi den day, trajectory da duoc gia dinh la HOP LE (MinRun=0 chi con lai
/// dung tai cac bypass link da qua validate). ExitTime > EntryTime o day chi la MOT DEFENSIVE INVARIANT
/// o tang occupation, khong phai co che chinh de "phat hien" bypass:
///
///     ExitTime > EntryTime   => sinh SectionOccupation binh thuong (co physical traversal)
///     ExitTime == EntryTime  => KHONG sinh occupation (bypass da duoc validate hop le tu truoc,
///                               khong chiem dung resource vat ly nao)
///     ExitTime <  EntryTime  => KHONG duoc silent-skip - day la invariant violation (thoi gian am),
///                               KHONG THE la mot bypass hop le => throw ngay (fail fast), vi day la
///                               dau hieu loi o tang truoc (validate/trajectory), khong phai case can
///                               "xu ly" o day.
///
/// `network` truyen vao la network CUA RIENG service nay (muc 1.4) - StationSequence trong do chi co y
/// nghia cuc bo cho service nay. SectionId (va NumberOfTracks) lay tu chinh Section cua network nay -
/// nguoi xay network phai dam bao SectionId duoc suy tu cap StationCode vat ly that (on dinh giua cac
/// service), KHONG tu StationSequence, de ConflictDetector so sanh dung giua nhieu service.
/// </summary>
public static class SectionOccupationBuilder
{
    public static IEnumerable<SectionOccupation> BuildForCycle(
        TrainService service, TrainServiceTrajectory trajectory, RailwayNetwork network, int cycleIndex)
    {
        var shift = cycleIndex * TrainService.CycleLengthMinutes;
        for (var i = 0; i < trajectory.Entries.Count - 1; i++)
        {
            var from = trajectory.Entries[i];
            var to = trajectory.Entries[i + 1];

            var entryTime = from.DepartureTimeMinutes!.Value + shift;
            var exitTime = to.ArrivalTimeMinutes!.Value + shift;
            var duration = exitTime - entryTime;

            if (duration < 0)
            {
                throw new InvalidOperationException(
                    $"SectionOccupation khong hop le cho ServiceId={service.ServiceId}: " +
                    $"ExitTime ({exitTime}) < EntryTime ({entryTime}) giua StationSequence " +
                    $"{from.StationSequence}->{to.StationSequence}. Day la invariant violation " +
                    "(thoi luong am khong the la bypass hop le) - kiem tra lai trajectory/validate input.");
            }

            if (duration == 0)
            {
                continue; // bypass da qua validate o tang input - khong chiem dung resource vat ly
            }

            var section = network.GetSectionBetween(from.StationSequence, to.StationSequence);

            yield return new SectionOccupation
            {
                SectionId = section.SectionId,
                ServiceId = service.ServiceId,
                CycleIndex = cycleIndex,
                Direction = service.Direction,
                EntryTimeMinutes = entryTime,
                ExitTimeMinutes = exitTime,
                NumberOfTracks = section.NumberOfTracks
            };
        }
    }
}
