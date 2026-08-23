using TrainTimetable.Configuration;
using TrainTimetable.Domain;
using Xunit;

namespace TrainTimetable.Engine.Tests;

/// <summary>
/// Bo test dung 1-1 theo muc 5.7 thiet ke (Section Release Headway). Moi tau chi co dung 2 dong
/// (origin/destination) tren 1 Section duy nhat de kiem soat truc tiep EntryTime/ExitTime, khong qua
/// MinimumTimetableBuilder.
/// </summary>
public class ConflictDetectorTests
{
    private readonly IHeadwayRules _headwayRules = new HeadwayRules();

    private static RailwayNetwork BuildTwoStationNetwork(int numberOfTracks = 1)
    {
        var stations = new List<Station>
        {
            Station(1),
            Station(2)
        };

        var section = new Section
        {
            SectionId = "KG1-2",
            FromStationSequence = 1,
            ToStationSequence = 2,
            MinRunningTimeMinutes = new Dictionary<Direction, int>
            {
                [Direction.Inbound] = 1,
                [Direction.Outbound] = 1
            },
            NumberOfTracks = numberOfTracks
        };

        return new RailwayNetwork(stations, new List<Section> { section });
    }

    private static Station Station(int sequence) => new()
    {
        StationId = $"S{sequence}",
        Code = $"S{sequence}",
        Name = $"S{sequence}",
        Sequence = sequence,
        Tracks = new List<StationTrack> { new($"S{sequence}-T1", TrackType.MainThrough, false, false) }
    };

    private static (TrainService Service, TrainServiceTrajectory Trajectory) BuildOccupation(
        string serviceId, Direction direction, int entryTime, int exitTime)
    {
        var (originSeq, destSeq) = direction == Direction.Inbound ? (1, 2) : (2, 1);

        var service = new TrainService(
            serviceId: serviceId, trainCode: serviceId, direction: direction,
            originStationSequence: originSeq, destinationStationSequence: destSeq,
            fixedDepartureTimeOfDayMinutes: entryTime, journeyTimeMinutes: exitTime - entryTime,
            priority: 1, stopRequirements: new List<TrainStopRequirement>());

        var entries = new List<TimetableEntry>
        {
            Entry(originSeq, arrival: null, departure: entryTime),
            Entry(destSeq, arrival: exitTime, departure: null)
        };

        var trajectory = new TrainServiceTrajectory { ServiceId = serviceId, Entries = entries };
        return (service, trajectory);
    }

    private static TimetableEntry Entry(int stationSeq, int? arrival, int? departure) => new()
    {
        StationSequence = stationSeq,
        ArrivalTimeMinutes = arrival,
        DepartureTimeMinutes = departure,
        StopType = StopType.Through,
        StopDurationMinutes = 0,
        RunningTimeFromPrevMinutes = 0,
        RecoveryTimeFromPrevMinutes = 0,
        CumulativeInsertedDelayMinutes = 0
    };

    private IReadOnlyList<Conflict> DetectTwo(
        (TrainService Service, TrainServiceTrajectory Trajectory) a,
        (TrainService Service, TrainServiceTrajectory Trajectory) b,
        int numberOfTracks = 1)
    {
        var detector = new ConflictDetector(_headwayRules);
        var network = BuildTwoStationNetwork(numberOfTracks);
        return detector.Detect(new[] { a, b }, network);
    }

    // ----- Nguoc chieu (MEET) -----

    [Fact]
    public void OppositeDirection_GapExactlyThree_NoConflict()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Outbound, entryTime: 103, exitTime: 113);

        var conflicts = DetectTwo(a, b);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void OppositeDirection_GapTwo_IsMeetWithSectionReleaseHeadwayAndDeficitOne()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Outbound, entryTime: 102, exitTime: 112);

        var conflicts = DetectTwo(a, b);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictType.MEET, conflict.Type);
        Assert.Equal(ConstraintKind.SectionReleaseHeadway, conflict.ConstraintKind);
        Assert.Equal(2, conflict.ActualGapMinutes);
        Assert.Equal(1, conflict.HeadwayDeficitMinutes);
    }

    [Fact]
    public void OppositeDirection_GapZero_IsMeetWithSectionReleaseHeadwayAndDeficitThree()
    {
        // Bien quan trong (muc 5.2): ExitA == EntryB khong phai overlap toan hoc nhung van la Conflict.
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Outbound, entryTime: 100, exitTime: 110);

        var conflicts = DetectTwo(a, b);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictType.MEET, conflict.Type);
        Assert.Equal(ConstraintKind.SectionReleaseHeadway, conflict.ConstraintKind);
        Assert.Equal(0, conflict.ActualGapMinutes);
        Assert.Equal(3, conflict.HeadwayDeficitMinutes);
    }

    [Fact]
    public void OppositeDirection_PhysicalOverlap_IsMeetWithSectionOverlap()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Outbound, entryTime: 98, exitTime: 108);

        var conflicts = DetectTwo(a, b);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictType.MEET, conflict.Type);
        Assert.Equal(ConstraintKind.SectionOverlap, conflict.ConstraintKind);
        Assert.Equal(-2, conflict.ActualGapMinutes);
        Assert.Equal(5, conflict.HeadwayDeficitMinutes);
    }

    // ----- Cung chieu (HEADWAY) -----

    [Fact]
    public void SameDirection_GapExactlyThree_NoConflict()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Inbound, entryTime: 103, exitTime: 113);

        var conflicts = DetectTwo(a, b);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void SameDirection_GapTwo_IsHeadwayWithSectionReleaseHeadway()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Inbound, entryTime: 102, exitTime: 112);

        var conflicts = DetectTwo(a, b);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictType.HEADWAY, conflict.Type);
        Assert.Equal(ConstraintKind.SectionReleaseHeadway, conflict.ConstraintKind);
        Assert.Equal(2, conflict.ActualGapMinutes);
    }

    [Fact]
    public void SameDirection_GapZero_IsHeadwayWithSectionReleaseHeadway()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Inbound, entryTime: 100, exitTime: 110);

        var conflicts = DetectTwo(a, b);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictType.HEADWAY, conflict.Type);
        Assert.Equal(ConstraintKind.SectionReleaseHeadway, conflict.ConstraintKind);
        Assert.Equal(0, conflict.ActualGapMinutes);
    }

    [Fact]
    public void SameDirection_PhysicalOverlap_IsHeadwayWithSectionOverlap_NotMeet()
    {
        // Diem sua sau review lan 2 (muc 5.3): cung chieu VAN co the ActualGap < 0 (chong lan vat ly) -
        // Type van la HEADWAY (vi cung chieu), chi co ConstraintKind = SectionOverlap.
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Inbound, entryTime: 98, exitTime: 108);

        var conflicts = DetectTwo(a, b);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictType.HEADWAY, conflict.Type);
        Assert.Equal(ConstraintKind.SectionOverlap, conflict.ConstraintKind);
        Assert.Equal(-2, conflict.ActualGapMinutes);
        Assert.Equal(5, conflict.HeadwayDeficitMinutes);
    }

    // ----- Double-track (muc 5.3 ngoai le) -----

    [Fact]
    public void DoubleTrack_OppositeDirectionOverlap_DoesNotReportMeet()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Outbound, entryTime: 98, exitTime: 108);

        var conflicts = DetectTwo(a, b, numberOfTracks: 2);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void DoubleTrack_SameDirectionGapTwo_StillReportsHeadway()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var b = BuildOccupation("B", Direction.Inbound, entryTime: 102, exitTime: 112);

        var conflicts = DetectTwo(a, b, numberOfTracks: 2);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictType.HEADWAY, conflict.Type);
    }

    // ----- Self-service & filter -----

    [Fact]
    public void SameService_DoesNotReportSelfConflictByDefault()
    {
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 90, exitTime: 100);
        var sameServiceCloseBehind = BuildOccupation("A", Direction.Inbound, entryTime: 101, exitTime: 111);

        var detector = new ConflictDetector(_headwayRules);
        var network = BuildTwoStationNetwork();
        var conflicts = detector.Detect(new[] { a, sameServiceCloseBehind }, network);

        Assert.Empty(conflicts);
    }

    // ----- Cyclic (muc 11) -----

    [Fact]
    public void CrossesCycleBoundary_DetectsConflictBetweenDifferentCycleIndexes()
    {
        // A (CycleIndex=0) ExitTime=1439; B (CycleIndex=0) EntryTime=1, nhung khi dich +1 chu ky
        // (tuyet doi 1441) tao gap=2 voi A/0 - dung vi du muc 5.7 case 8.
        var a = BuildOccupation("A", Direction.Inbound, entryTime: 1400, exitTime: 1439);
        var b = BuildOccupation("B", Direction.Outbound, entryTime: 1, exitTime: 3);

        var detector = new ConflictDetector(_headwayRules);
        var network = BuildTwoStationNetwork();
        var conflicts = detector.Detect(new[] { a, b }, network);

        Assert.Contains(conflicts, c =>
            c.ServiceIdA == "A" && c.CycleIndexA == 0 &&
            c.ServiceIdB == "B" && c.CycleIndexB == 1 &&
            c.ActualGapMinutes == 2);

        // Filter muc 11.3(a): moi Conflict tra ve phai co it nhat 1 ben CycleIndex == 0.
        Assert.All(conflicts, c => Assert.True(c.CycleIndexA == 0 || c.CycleIndexB == 0));
    }
}
