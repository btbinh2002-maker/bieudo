using TrainTimetable.Configuration;
using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>
/// Phat hien MEET/HEADWAY tren toan bo TrainService duoc truyen vao, chay o "che do cyclic" ngay tu dau
/// (moi Section duoc kiem tra tren occupation cua moi CycleIndex trong cua so [-K,K], muc 5.1/11.3) -
/// KHONG co phien ban "chi trong ngay" tach rieng. Chua sinh OVERTAKE (viec cua ConflictAnalyzer, Phase
/// 6, muc 5.4) va chua resolve gi ca - day thuan la detection.
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
        IReadOnlyList<(TrainService Service, TrainServiceTrajectory Trajectory)> services,
        RailwayNetwork network)
    {
        if (services.Count == 0)
        {
            return Array.Empty<Conflict>();
        }

        var cyclicRadius = ComputeCyclicRadius(services);
        var sectionsById = network.Sections.ToDictionary(s => s.SectionId);

        var occupationsBySection = new Dictionary<string, List<SectionOccupation>>();
        foreach (var (service, trajectory) in services)
        {
            for (var cycleIndex = -cyclicRadius; cycleIndex <= cyclicRadius; cycleIndex++)
            {
                foreach (var occupation in BuildOccupationsForCycle(service, trajectory, network, cycleIndex))
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
            var section = sectionsById[sectionId];
            conflicts.AddRange(DetectWithinSection(section, occupations));
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
    private IEnumerable<Conflict> DetectWithinSection(Section section, List<SectionOccupation> occupations)
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

                if (section.NumberOfTracks >= 2 && earlier.Direction != later.Direction)
                {
                    continue; // ngoai le double-track, muc 5.3 - chua co track assignment nen bo qua MEET
                }

                var actualGap = later.EntryTimeMinutes - earlier.ExitTimeMinutes;
                if (actualGap >= headway)
                {
                    continue;
                }

                yield return BuildConflict(section.SectionId, earlier, later, actualGap, headway);
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

    private static IEnumerable<SectionOccupation> BuildOccupationsForCycle(
        TrainService service, TrainServiceTrajectory trajectory, RailwayNetwork network, int cycleIndex)
    {
        var shift = cycleIndex * TrainService.CycleLengthMinutes;
        for (var i = 0; i < trajectory.Entries.Count - 1; i++)
        {
            var from = trajectory.Entries[i];
            var to = trajectory.Entries[i + 1];
            var section = network.GetSectionBetween(from.StationSequence, to.StationSequence);

            yield return new SectionOccupation
            {
                SectionId = section.SectionId,
                ServiceId = service.ServiceId,
                CycleIndex = cycleIndex,
                Direction = service.Direction,
                EntryTimeMinutes = from.DepartureTimeMinutes!.Value + shift,
                ExitTimeMinutes = to.ArrivalTimeMinutes!.Value + shift
            };
        }
    }

    private static int ComputeCyclicRadius(
        IReadOnlyList<(TrainService Service, TrainServiceTrajectory Trajectory)> services)
    {
        var maxJourneyTime = services.Max(s => s.Service.JourneyTimeMinutes);
        return 1 + (int)Math.Ceiling(maxJourneyTime / (double)TrainService.CycleLengthMinutes);
    }
}
