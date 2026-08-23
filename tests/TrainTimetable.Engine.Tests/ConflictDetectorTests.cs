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
        return detector.Detect(new[]
        {
            (a.Service, a.Trajectory, network),
            (b.Service, b.Trajectory, network)
        });
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
        var conflicts = detector.Detect(new[]
        {
            (a.Service, a.Trajectory, network),
            (sameServiceCloseBehind.Service, sameServiceCloseBehind.Trajectory, network)
        });

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
        var conflicts = detector.Detect(new[]
        {
            (a.Service, a.Trajectory, network),
            (b.Service, b.Trajectory, network)
        });

        Assert.Contains(conflicts, c =>
            c.ServiceIdA == "A" && c.CycleIndexA == 0 &&
            c.ServiceIdB == "B" && c.CycleIndexB == 1 &&
            c.ActualGapMinutes == 2);

        // Filter muc 11.3(a): moi Conflict tra ve phai co it nhat 1 ben CycleIndex == 0.
        Assert.All(conflicts, c => Assert.True(c.CycleIndexA == 0 || c.CycleIndexB == 0));
    }

    // ----- Ngoai le ga nhanh/terminal (Qui Nhon/Phan Thiet, thiet ke muc 15.9) -----

    private static RailwayNetwork BuildFourStationNetwork()
    {
        var stations = new List<Station> { Station(1), Station(2), Station(3), Station(4) };
        var sections = new List<Section>
        {
            SectionBetween(1, 2), SectionBetween(2, 3), SectionBetween(3, 4)
        };
        return new RailwayNetwork(stations, sections);
    }

    private static Section SectionBetween(int from, int to) => new()
    {
        SectionId = $"KG{from}-{to}",
        FromStationSequence = from,
        ToStationSequence = to,
        MinRunningTimeMinutes = new Dictionary<Direction, int> { [Direction.Inbound] = 1, [Direction.Outbound] = 1 }
    };

    [Fact]
    public void BypassTrainAndBranchTerminalTrain_DoNotConflictOnBranchSection()
    {
        // Test E (thiet ke muc 15.9): tau A chay xuyen qua ga nhanh (Entry==Exit tai dong logic seq=3,
        // khong ranh vao KG2-3 thuc su); tau B thuc su di vao/ket thuc tai KG2-3 voi thoi gian [9,11] -
        // trung hoan toan vao "khoang thoi gian" cua diem ao [10,10] ben A. Neu SectionOccupationBuilder
        // van sinh occupation "ao" cho A tai KG2-3, day se bi bao MEET/HEADWAY sai. Dung thi KHONG duoc
        // co Conflict nao tren KG2-3 giua A va B.
        var bypass = (
            Service: new TrainService(
                serviceId: "A", trainCode: "A", direction: Direction.Inbound,
                originStationSequence: 1, destinationStationSequence: 4,
                fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 20, priority: 1,
                stopRequirements: new List<TrainStopRequirement>()),
            Trajectory: new TrainServiceTrajectory
            {
                ServiceId = "A",
                Entries = new List<TimetableEntry>
                {
                    Entry(1, null, 0),
                    Entry(2, 10, 10),
                    Entry(3, 10, 10), // ga nhanh ao - khong ranh vao
                    Entry(4, 20, null)
                }
            });

        var branchTerminal = (
            Service: new TrainService(
                serviceId: "B", trainCode: "B", direction: Direction.Inbound,
                originStationSequence: 2, destinationStationSequence: 3,
                fixedDepartureTimeOfDayMinutes: 9, journeyTimeMinutes: 2, priority: 1,
                stopRequirements: new List<TrainStopRequirement>()),
            Trajectory: new TrainServiceTrajectory
            {
                ServiceId = "B",
                Entries = new List<TimetableEntry>
                {
                    Entry(2, null, 9),
                    Entry(3, 11, null)
                }
            });

        var detector = new ConflictDetector(_headwayRules);
        var network = BuildFourStationNetwork();
        var conflicts = detector.Detect(new[]
        {
            (bypass.Service, bypass.Trajectory, network),
            (branchTerminal.Service, branchTerminal.Trajectory, network)
        });

        Assert.DoesNotContain(conflicts, c => c.SectionId == "KG2-3");
    }

    // ----- RailwayNetwork per-service, KHONG dung chung (muc 1.4/15.14) -----

    private static RailwayNetwork BuildTwoStationNetworkWithSectionId(
        int fromSeq, int toSeq, string sectionId, int numberOfTracks = 1)
    {
        var stations = new List<Station> { Station(fromSeq), Station(toSeq) };
        var section = new Section
        {
            SectionId = sectionId,
            FromStationSequence = fromSeq,
            ToStationSequence = toSeq,
            MinRunningTimeMinutes = new Dictionary<Direction, int> { [Direction.Inbound] = 1, [Direction.Outbound] = 1 },
            NumberOfTracks = numberOfTracks
        };
        return new RailwayNetwork(stations, new List<Section> { section });
    }

    [Fact]
    public void DifferentTrainsWithCollidingLocalJourneySequence_DoNotShareSectionIdentity()
    {
        // Bo sung theo yeu cau review kien truc (muc 1.4/15.14): Train A va Train B CUNG dung local
        // JourneySequence 10->11 (trung so), nhung StationCode vat ly hoan toan khac nhau
        // (A: 100->200, B: 300->200) - moi network gan SectionId KHAC NHAU cho dung 2 physical resource
        // khac nhau. TUYET DOI khong duoc coi day la 1 Section chi vi trung Sequence cuc bo.
        var networkA = BuildTwoStationNetworkWithSectionId(fromSeq: 10, toSeq: 11, sectionId: "SEC-100-200");
        var networkB = BuildTwoStationNetworkWithSectionId(fromSeq: 10, toSeq: 11, sectionId: "SEC-300-200");

        var serviceA = new TrainService(
            serviceId: "A", trainCode: "A", direction: Direction.Inbound,
            originStationSequence: 10, destinationStationSequence: 11,
            fixedDepartureTimeOfDayMinutes: 90, journeyTimeMinutes: 10, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());
        var trajectoryA = new TrainServiceTrajectory
        {
            ServiceId = "A",
            Entries = new List<TimetableEntry> { Entry(10, null, 90), Entry(11, 100, null) }
        };

        var serviceB = new TrainService(
            serviceId: "B", trainCode: "B", direction: Direction.Inbound,
            originStationSequence: 10, destinationStationSequence: 11,
            fixedDepartureTimeOfDayMinutes: 98, journeyTimeMinutes: 10, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());
        var trajectoryB = new TrainServiceTrajectory
        {
            ServiceId = "B",
            Entries = new List<TimetableEntry> { Entry(10, null, 98), Entry(11, 108, null) }
        };

        var detector = new ConflictDetector(_headwayRules);
        var conflicts = detector.Detect(new[]
        {
            (serviceA, trajectoryA, networkA),
            (serviceB, trajectoryB, networkB)
        });

        // A:[90,100] va B:[98,108] chac chan se bi bao HEADWAY neu bi gop nham vao 1 Section (gap=-2) -
        // dung thi KHONG duoc co conflict nao, vi day la 2 physical resource khac nhau hoan toan.
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DifferentLocalSequenceSchemes_SameSectionId_StillDetectsConflict()
    {
        // Chieu nguoc lai cua test tren: 2 tau dung local JourneySequence HOAN TOAN khac nhau (5->6 vs
        // 20->21 - dung nhu bypass vs terminal train gan Qui Nhon/Phan Thiet co the co so cuc bo khac
        // nhau, muc 15.13.5) nhung CUNG mot physical section that (SectionId dung chung, suy tu
        // StationCode that). ConflictDetector PHAI van so sanh dung duoc qua SectionId, khong phu
        // thuoc Sequence cuc bo co trung nhau hay khong.
        var networkX = BuildTwoStationNetworkWithSectionId(fromSeq: 5, toSeq: 6, sectionId: "SEC-500-600");
        var networkY = BuildTwoStationNetworkWithSectionId(fromSeq: 20, toSeq: 21, sectionId: "SEC-500-600");

        var serviceX = new TrainService(
            serviceId: "X", trainCode: "X", direction: Direction.Inbound,
            originStationSequence: 5, destinationStationSequence: 6,
            fixedDepartureTimeOfDayMinutes: 90, journeyTimeMinutes: 10, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());
        var trajectoryX = new TrainServiceTrajectory
        {
            ServiceId = "X",
            Entries = new List<TimetableEntry> { Entry(5, null, 90), Entry(6, 100, null) }
        };

        var serviceY = new TrainService(
            serviceId: "Y", trainCode: "Y", direction: Direction.Inbound,
            originStationSequence: 20, destinationStationSequence: 21,
            fixedDepartureTimeOfDayMinutes: 98, journeyTimeMinutes: 10, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());
        var trajectoryY = new TrainServiceTrajectory
        {
            ServiceId = "Y",
            Entries = new List<TimetableEntry> { Entry(20, null, 98), Entry(21, 108, null) }
        };

        var detector = new ConflictDetector(_headwayRules);
        var conflicts = detector.Detect(new[]
        {
            (serviceX, trajectoryX, networkX),
            (serviceY, trajectoryY, networkY)
        });

        var conflict = Assert.Single(conflicts);
        Assert.Equal("SEC-500-600", conflict.SectionId);
        Assert.Equal(ConflictType.HEADWAY, conflict.Type);
    }
}
