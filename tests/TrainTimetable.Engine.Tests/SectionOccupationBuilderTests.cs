using TrainTimetable.Domain;
using Xunit;

namespace TrainTimetable.Engine.Tests;

/// <summary>
/// Kiem chung dung 1 dieu quan trong nhat sau review "ngoai le Qui Nhon/Phan Thiet" (thiet ke muc
/// 1.6/5.1/15.9): mot cap TimetableEntry lien tiep CHI sinh SectionOccupation neu co physical
/// traversal that (ExitTime > EntryTime). Khong hard-code ten ga nao - chi dua vao thoi luong thuc te.
/// </summary>
public class SectionOccupationBuilderTests
{
    private static RailwayNetwork BuildFourStationNetwork() => new(
        new List<Station>
        {
            Station(1), Station(2), Station(3), Station(4)
        },
        new List<Section>
        {
            SectionBetween(1, 2), SectionBetween(2, 3), SectionBetween(3, 4)
        });

    private static Station Station(int seq) => new()
    {
        StationId = $"S{seq}", Code = $"S{seq}", Name = $"S{seq}", Sequence = seq,
        Tracks = new List<StationTrack> { new($"S{seq}-T1", TrackType.MainThrough, false, false) }
    };

    private static Section SectionBetween(int from, int to) => new()
    {
        SectionId = $"KG{from}-{to}",
        FromStationSequence = from,
        ToStationSequence = to,
        MinRunningTimeMinutes = new Dictionary<Direction, int> { [Direction.Inbound] = 1, [Direction.Outbound] = 1 }
    };

    private static TimetableEntry Entry(int seq, int? arrival, int? departure) => new()
    {
        StationSequence = seq,
        ArrivalTimeMinutes = arrival,
        DepartureTimeMinutes = departure,
        StopType = StopType.Through,
        StopDurationMinutes = 0,
        RunningTimeFromPrevMinutes = 0,
        RecoveryTimeFromPrevMinutes = 0,
        CumulativeInsertedDelayMinutes = 0
    };

    [Fact]
    public void BuildForCycle_ZeroDurationHopInMiddle_SkipsOccupationOnlyForThatHop()
    {
        // Test A (thiet ke muc 15.9): mo phong tau BYPASS mot ga nhanh - dong logic tai ga 3 van ton
        // tai (JourneySequence khong doi) nhung Entry==Exit (khong co physical traversal). Section
        // 1->2 va 3->4 co running time thuc, phai VAN sinh occupation binh thuong.
        var service = new TrainService(
            serviceId: "BYPASS", trainCode: "BYPASS", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 4,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 20, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var trajectory = new TrainServiceTrajectory
        {
            ServiceId = "BYPASS",
            Entries = new List<TimetableEntry>
            {
                Entry(1, null, 0),
                Entry(2, 10, 10),
                Entry(3, 10, 10),   // ga nhanh "ao" - Entry==Exit=10, khong ranh vao nhanh
                Entry(4, 20, null)
            }
        };

        var network = BuildFourStationNetwork();

        var occupations = SectionOccupationBuilder.BuildForCycle(service, trajectory, network, cycleIndex: 0).ToList();

        Assert.Equal(2, occupations.Count); // CHI 1->2 va 3->4, KHONG co 2->3
        Assert.Contains(occupations, o => o.SectionId == "KG1-2");
        Assert.Contains(occupations, o => o.SectionId == "KG3-4");
        Assert.DoesNotContain(occupations, o => o.SectionId == "KG2-3");
    }

    [Fact]
    public void BuildForCycle_RealBranchTraversal_GeneratesOccupationWithCorrectDuration()
    {
        // Test B (thiet ke muc 15.9): tau THAT SU ket thuc tai ga nhanh - khu gian cuoi co running
        // time thuc (10 phut) => phai sinh occupation binh thuong, dung do dai.
        var service = new TrainService(
            serviceId: "TERMINAL", trainCode: "TERMINAL", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 3,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 20, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var trajectory = new TrainServiceTrajectory
        {
            ServiceId = "TERMINAL",
            Entries = new List<TimetableEntry>
            {
                Entry(1, null, 0),
                Entry(2, 10, 10),
                Entry(3, 20, null)   // khu gian 2->3 (vao nhanh) = 10 phut that
            }
        };

        var network = BuildFourStationNetwork();

        var occupations = SectionOccupationBuilder.BuildForCycle(service, trajectory, network, cycleIndex: 0).ToList();

        Assert.Equal(2, occupations.Count);
        var branchOccupation = occupations.Single(o => o.SectionId == "KG2-3");
        Assert.Equal(10, branchOccupation.EntryTimeMinutes);
        Assert.Equal(20, branchOccupation.ExitTimeMinutes);
    }

    [Fact]
    public void BuildForCycle_RealBranchTraversalFromOrigin_GeneratesOccupationInOppositeDirection()
    {
        // Test C (thiet ke muc 15.9): tau XUAT PHAT tai ga nhanh, chieu Outbound - van sinh occupation
        // binh thuong cho khu gian dau tien (running time thuc), dung Direction cua service.
        var service = new TrainService(
            serviceId: "FROMBRANCH", trainCode: "FROMBRANCH", direction: Direction.Outbound,
            originStationSequence: 3, destinationStationSequence: 1,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 20, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var trajectory = new TrainServiceTrajectory
        {
            ServiceId = "FROMBRANCH",
            Entries = new List<TimetableEntry>
            {
                Entry(3, null, 0),
                Entry(2, 10, 10),
                Entry(1, 20, null)
            }
        };

        var network = BuildFourStationNetwork();

        var occupations = SectionOccupationBuilder.BuildForCycle(service, trajectory, network, cycleIndex: 0).ToList();

        Assert.Equal(2, occupations.Count);
        var branchOccupation = occupations.Single(o => o.SectionId == "KG2-3");
        Assert.Equal(Direction.Outbound, branchOccupation.Direction);
        Assert.Equal(0, branchOccupation.EntryTimeMinutes);
        Assert.Equal(10, branchOccupation.ExitTimeMinutes);
    }

    [Fact]
    public void BuildForCycle_NegativeDuration_ThrowsInsteadOfSilentlySkipping()
    {
        // Bo sung sau review (muc 15.13.7): ExitTime < EntryTime KHONG THE la mot bypass hop le (bypass
        // hop le chi cho ExitTime == EntryTime, da qua validate o tang input) - day la invariant
        // violation, phai fail fast bang exception, KHONG duoc coi nhu truong hop <= 0 roi skip.
        var service = new TrainService(
            serviceId: "BROKEN", trainCode: "BROKEN", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 2,
            fixedDepartureTimeOfDayMinutes: 10, journeyTimeMinutes: 5, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var trajectory = new TrainServiceTrajectory
        {
            ServiceId = "BROKEN",
            Entries = new List<TimetableEntry>
            {
                Entry(1, null, 10),
                Entry(2, 5, null) // ArrivalTime(5) < DepartureTime cua ga truoc (10) - vo ly
            }
        };

        var network = BuildFourStationNetwork();

        Assert.Throws<InvalidOperationException>(() =>
            SectionOccupationBuilder.BuildForCycle(service, trajectory, network, cycleIndex: 0).ToList());
    }
}
