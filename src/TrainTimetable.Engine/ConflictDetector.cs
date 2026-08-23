using TrainTimetable.Configuration;
using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>
/// Phat hien MEET/HEADWAY tren toan bo TrainService duoc truyen vao, chay o "che do cyclic" ngay tu dau
/// (moi Section duoc kiem tra tren occupation cua moi CycleIndex trong cua so [-K,K], muc 5.1/11.3) -
/// KHONG co phien ban "chi trong ngay" tach rieng. Chua sinh OVERTAKE (viec cua ConflictAnalyzer, Phase
/// 6, muc 5.4) va chua resolve gi ca - day thuan la detection.
///
/// KHONG tu sinh SectionOccupation - nhan tu SectionOccupationBuilder (da loc dung, chi chua physical
/// traversal). Lop nay vi vay KHONG can biet ga nao la ga nhanh/terminal (Qui Nhon, Phan Thiet...) -
/// toan bo semantic do nam o SectionOccupationBuilder, dung mot cho duy nhat (muc 15.9).
///
/// QUAN TRONG (sua sau review - muc 1.4/15.14): moi TrainService nhan RailwayNetwork CUA RIENG NO khi
/// goi Detect - KHONG dung chung 1 network cho moi service. Ly do: StationSequence chi co y nghia CUC
/// BO trong route cua chinh 1 TrainService (muc 1.4) - 2 service co the dung cung mot so Sequence de
/// tro toi 2 StationCode vat ly hoan toan khac nhau. Danh tinh dung chung giua cac tau duy nhat la
/// Section.SectionId (phai duoc nguoi xay network suy tu cap StationCode that, KHONG tu Sequence, KHONG
/// chua TrainCode - muc 1.4) - ConflictDetector chi so sanh occupation theo dung SectionId nay, khong
/// bao gio tu minh tra Section qua mot network "dung chung".
/// </summary>
public sealed class ConflictDetector
{
    private readonly IHeadwayRules _headwayRules;
    private readonly bool _includeSelfServiceConflicts;

    public ConflictDetector(IHeadwayRules headwayRules, bool includeSelfServiceConflicts = false)
    {
        _headwayRules = headwayRules;
        _includeSelfServiceConflicts = includeSelfServiceConflicts;
    }

    public IReadOnlyList<Conflict> Detect(
        IReadOnlyList<(TrainService Service, TrainServiceTrajectory Trajectory, RailwayNetwork Network)> services)
    {
        if (services.Count == 0)
        {
            return Array.Empty<Conflict>();
        }

        var cyclicRadius = ComputeCyclicRadius(services);

        var occupationsBySection = new Dictionary<string, List<SectionOccupation>>();
        foreach (var (service, trajectory, network) in services)
        {
            for (var cycleIndex = -cyclicRadius; cycleIndex <= cyclicRadius; cycleIndex++)
            {
                foreach (var occupation in SectionOccupationBuilder.BuildForCycle(service, trajectory, network, cycleIndex))
                {
                    if (!occupationsBySection.TryGetValue(occupation.SectionId, out var list))
                    {
                        list = new List<SectionOccupation>();
                        occupationsBySection[occupation.SectionId] = list;
                    }

                    list.Add(occupation);
                }
            }
        }

        var conflicts = new List<Conflict>();
        foreach (var (sectionId, occupations) in occupationsBySection)
        {
            conflicts.AddRange(DetectWithinSection(sectionId, occupations));
        }

        return conflicts
            .Where(c => c.CycleIndexA == 0 || c.CycleIndexB == 0)
            .ToList();
    }

    /// <summary>
    /// Sweep theo EntryTime, giu mot "active set" cac occupation chua duoc "release" du headway - dung
    /// cho ca truong hop long nhau (occupation C nam gon trong occupation A dai hon), khong chi so cap
    /// ke nhau theo EntryTime (muc 5.4/5.5). Do phuc tap: O(n log n) sort + O(n x |active|) so sanh, voi
    /// |active| trong thuc te nho vi headway chi 3 phut.
    /// </summary>
    private IEnumerable<Conflict> DetectWithinSection(string sectionId, List<SectionOccupation> occupations)
    {
        occupations.Sort((x, y) => x.EntryTimeMinutes.CompareTo(y.EntryTimeMinutes));
        var headway = _headwayRules.SectionReleaseHeadwayMinutes;
        var active = new List<SectionOccupation>();

        foreach (var later in occupations)
        {
            active.RemoveAll(earlier => earlier.ExitTimeMinutes + headway <= later.EntryTimeMinutes);

            foreach (var earlier in active)
            {
                if (!_includeSelfServiceConflicts && earlier.ServiceId == later.ServiceId)
                {
                    continue;
                }

                // Ngoai le double-track (muc 5.3): CA HAI occupation phai dong y day la double-track -
                // neu lech nhau (vd network cua 1 service bao sai), mac dinh AN TOAN la coi nhu single
                // track (van bao MEET) thay vi bo qua nham mot conflict that.
                if (Math.Min(earlier.NumberOfTracks, later.NumberOfTracks) >= 2
                    && earlier.Direction != later.Direction)
                {
                    continue; // chua co track assignment nen bo qua MEET ngang chieu tren double-track
                }

                var actualGap = later.EntryTimeMinutes - earlier.ExitTimeMinutes;
                if (actualGap >= headway)
                {
                    continue;
                }

                yield return BuildConflict(sectionId, earlier, later, actualGap, headway);
            }

            active.Add(later);
        }
    }

    private static Conflict BuildConflict(
        string sectionId, SectionOccupation earlier, SectionOccupation later, int actualGap, int headway)
    {
        var constraintKind = actualGap < 0 ? ConstraintKind.SectionOverlap : ConstraintKind.SectionReleaseHeadway;
        var type = earlier.Direction != later.Direction ? ConflictType.MEET : ConflictType.HEADWAY;

        return new Conflict
        {
            ConflictId = $"{sectionId}|{earlier.ServiceId}/{earlier.CycleIndex}|{later.ServiceId}/{later.CycleIndex}",
            Type = type,
            ConstraintKind = constraintKind,
            ServiceIdA = earlier.ServiceId,
            CycleIndexA = earlier.CycleIndex,
            ServiceIdB = later.ServiceId,
            CycleIndexB = later.CycleIndex,
            SectionId = sectionId,
            ConflictStartTimeMinutes = later.EntryTimeMinutes,
            ConflictEndTimeMinutes = earlier.ExitTimeMinutes + headway,
            RequiredHeadwayMinutes = headway,
            ActualGapMinutes = actualGap,
            HeadwayDeficitMinutes = headway - actualGap
        };
    }

    private static int ComputeCyclicRadius(
        IReadOnlyList<(TrainService Service, TrainServiceTrajectory Trajectory, RailwayNetwork Network)> services)
    {
        var maxJourneyTime = services.Max(s => s.Service.JourneyTimeMinutes);
        return 1 + (int)Math.Ceiling(maxJourneyTime / (double)TrainService.CycleLengthMinutes);
    }
}
