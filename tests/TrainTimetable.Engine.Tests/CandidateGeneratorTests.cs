using TrainTimetable.Domain;
using Xunit;

namespace TrainTimetable.Engine.Tests;

/// <summary>
/// Kiem chung thiet ke muc 6 (review lan 2 + lan 3): candidate dinh danh bang StationCode (int, khong
/// phai local Sequence), MEET dung intersection 2 route + upstream doc lap tung ben, HEADWAY KHONG can
/// intersection (chi route cua Follower) va CHI sinh CandidateSolution cho Follower.
/// </summary>
public class CandidateGeneratorTests
{
    private static Station Station(int code, bool canMeet = false, bool canOvertake = false, bool canHold = true) => new()
    {
        StationId = $"S{code}",
        Code = code.ToString(),
        Name = $"S{code}",
        Sequence = code, // gia tri bat ky, chi can duy nhat trong network nay - khong dung de so sanh xuyen service
        Tracks = canHold
            ? new List<StationTrack> { new($"S{code}-T1", TrackType.MainThrough, canMeet, canOvertake) }
                .Concat(canMeet || canOvertake
                    ? new List<StationTrack> { new($"S{code}-T2", TrackType.Siding, canMeet, canOvertake) }
                    : Enumerable.Empty<StationTrack>())
                .ToList()
            : new List<StationTrack>()
    };

    private static PhysicalCandidateStation Capability(int code, bool canMeet = false, bool canOvertake = false, bool canHold = true) =>
        new() { StationCode = code, CanMeet = canMeet, CanOvertake = canOvertake, CanHold = canHold };

    /// <summary>Xay 1 network + 1 route (Service, Trajectory, Network) tu danh sach StationCode theo dung thu tu di chuyen.</summary>
    private static TrainServiceRoute BuildRoute(string serviceId, Direction direction, IReadOnlyList<int> codesInOrder, Func<int, int, string> sectionId)
    {
        var stations = codesInOrder.Select((code, i) => Station(code) with { Sequence = i + 1 }).ToList();
        var sections = new List<Section>();
        for (var i = 0; i < codesInOrder.Count - 1; i++)
        {
            sections.Add(new Section
            {
                SectionId = sectionId(codesInOrder[i], codesInOrder[i + 1]),
                FromStationSequence = i + 1,
                ToStationSequence = i + 2,
                MinRunningTimeMinutes = new Dictionary<Direction, int> { [Direction.Inbound] = 10, [Direction.Outbound] = 10 }
            });
        }

        var network = new RailwayNetwork(stations, sections);

        var entries = new List<TimetableEntry>();
        var t = 0;
        for (var i = 0; i < codesInOrder.Count; i++)
        {
            var isFirst = i == 0;
            var isLast = i == codesInOrder.Count - 1;
            entries.Add(new TimetableEntry
            {
                StationSequence = i + 1,
                ArrivalTimeMinutes = isFirst ? null : t,
                DepartureTimeMinutes = isLast ? null : t,
                StopType = StopType.Through,
                StopDurationMinutes = 0,
                RunningTimeFromPrevMinutes = isFirst ? 0 : 10,
                RecoveryTimeFromPrevMinutes = 0,
                CumulativeInsertedDelayMinutes = 0
            });
            t += 10;
        }

        var trajectory = new TrainServiceTrajectory { ServiceId = serviceId, Entries = entries };
        var service = new TrainService(
            serviceId: serviceId, trainCode: serviceId, direction: direction,
            originStationSequence: 1, destinationStationSequence: codesInOrder.Count,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: (codesInOrder.Count - 1) * 10 + 1, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        return new TrainServiceRoute { Service = service, Trajectory = trajectory, Network = network };
    }

    private static string SectionIdBetween(int a, int b) => $"SEC-{Math.Min(a, b)}-{Math.Max(a, b)}";

    private static Conflict Meet(string serviceIdA, string serviceIdB, string sectionId) => new()
    {
        ConflictId = "C1", Type = ConflictType.MEET, ConstraintKind = ConstraintKind.SectionReleaseHeadway,
        ServiceIdA = serviceIdA, CycleIndexA = 0, ServiceIdB = serviceIdB, CycleIndexB = 0,
        SectionId = sectionId, ConflictStartTimeMinutes = 0, ConflictEndTimeMinutes = 10,
        RequiredHeadwayMinutes = 3, ActualGapMinutes = 0, HeadwayDeficitMinutes = 3
    };

    private static Conflict Headway(string leaderServiceId, string followerServiceId, string sectionId) => new()
    {
        ConflictId = "C2", Type = ConflictType.HEADWAY, ConstraintKind = ConstraintKind.SectionReleaseHeadway,
        ServiceIdA = leaderServiceId, CycleIndexA = 0, ServiceIdB = followerServiceId, CycleIndexB = 0,
        SectionId = sectionId, ConflictStartTimeMinutes = 0, ConflictEndTimeMinutes = 10,
        RequiredHeadwayMinutes = 3, ActualGapMinutes = 0, HeadwayDeficitMinutes = 3
    };

    [Fact]
    public void Meet_SameStationCode_DifferentLocalIndexEachSide_EachSideGetsItsOwnUpstreamOnlyCandidate()
    {
        // A: 100-200-300-400-500 (Inbound). B: 600-400-300-100 (Outbound, di nguoc qua cung khu gian 300-400).
        var routeA = BuildRoute("A", Direction.Inbound, new[] { 100, 200, 300, 400, 500 }, SectionIdBetween);
        var routeB = BuildRoute("B", Direction.Outbound, new[] { 600, 400, 300, 100 }, SectionIdBetween);

        var conflict = Meet("A", "B", SectionIdBetween(300, 400));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (300, 400) });

        var generator = new CandidateGenerator(window: 1);
        var solutions = generator.GenerateCandidates(conflict, routeA, routeB, catalog,
            code => Capability(code, canMeet: true));

        // 300: entry cho A (idx2<=entryIdxA=2) nhung la EXIT cho B (idx2 > entryIdxB=1) -> chi A duoc emit.
        Assert.Contains(solutions, s => s.CandidateStationCode == 300 && s.TrainToWait == "A" && s.TrainToWaitLocalStationIndex == 2);
        Assert.DoesNotContain(solutions, s => s.CandidateStationCode == 300 && s.TrainToWait == "B");

        // 400: entry cho B (idx1<=entryIdxB=1) nhung la EXIT cho A (idx3 > entryIdxA=2) -> chi B duoc emit.
        Assert.Contains(solutions, s => s.CandidateStationCode == 400 && s.TrainToWait == "B" && s.TrainToWaitLocalStationIndex == 1);
        Assert.DoesNotContain(solutions, s => s.CandidateStationCode == 400 && s.TrainToWait == "A");
    }

    [Fact]
    public void Meet_ToCodeMissingOnOneRoute_ThrowsInvariantViolation_NoSilentFallback()
    {
        var routeA = BuildRoute("A", Direction.Inbound, new[] { 100, 200, 300, 400, 500 }, SectionIdBetween);
        // routeB khong he co StationCode 400 tren route - du lieu catalog/route khong khop.
        var routeB = BuildRoute("B", Direction.Outbound, new[] { 999, 300, 100 }, SectionIdBetween);

        var conflict = Meet("A", "B", SectionIdBetween(300, 400));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (300, 400) });

        var generator = new CandidateGenerator(window: 1);

        Assert.Throws<InvalidOperationException>(() =>
            generator.GenerateCandidates(conflict, routeA, routeB, catalog, code => Capability(code, canMeet: true)));
    }

    [Fact]
    public void Meet_StationCanMeetFalse_NoCandidateEvenThoughItSurvivesIntersection()
    {
        var routeA = BuildRoute("A", Direction.Inbound, new[] { 100, 200, 300, 400, 500 }, SectionIdBetween);
        var routeB = BuildRoute("B", Direction.Outbound, new[] { 600, 400, 300, 100 }, SectionIdBetween);

        var conflict = Meet("A", "B", SectionIdBetween(300, 400));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (300, 400) });

        var generator = new CandidateGenerator(window: 1);
        var solutions = generator.GenerateCandidates(conflict, routeA, routeB, catalog,
            code => Capability(code, canMeet: false)); // khong ga nao co track tranh/vuot

        Assert.Empty(solutions);
    }

    [Fact]
    public void Headway_CandidateOnlyOnFollowerRoute_LeaderNeverVisitsIt_StillGenerated()
    {
        // Follower: A-B-C-D-[conflict]; Leader: chi C-D-[conflict] (khong he co StationCode=B).
        var followerRoute = BuildRoute("FOLLOWER", Direction.Inbound, new[] { 10, 20, 30, 40 }, SectionIdBetween);
        var leaderRoute = BuildRoute("LEADER", Direction.Inbound, new[] { 30, 40 }, SectionIdBetween);

        var conflict = Headway(leaderServiceId: "LEADER", followerServiceId: "FOLLOWER", SectionIdBetween(30, 40));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (30, 40) });

        var generator = new CandidateGenerator(window: 3);
        var solutions = generator.GenerateCandidates(conflict, leaderRoute, followerRoute, catalog,
            code => Capability(code, canHold: true));

        // StationCode=20 ("B") chi co tren route cua Follower - Leader hoan toan khong "biet" toi no.
        Assert.Contains(solutions, s => s.CandidateStationCode == 20 && s.TrainToWait == "FOLLOWER");
    }

    [Fact]
    public void Headway_OnlyFollowerGetsCandidateSolution_NeverLeaderEvenIfLeaderPassesThrough()
    {
        var leaderRoute = BuildRoute("LEADER", Direction.Inbound, new[] { 10, 20, 30, 40 }, SectionIdBetween);
        var followerRoute = BuildRoute("FOLLOWER", Direction.Inbound, new[] { 10, 20, 30, 40 }, SectionIdBetween);

        var conflict = Headway(leaderServiceId: "LEADER", followerServiceId: "FOLLOWER", SectionIdBetween(30, 40));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (30, 40) });

        var generator = new CandidateGenerator(window: 3);
        var solutions = generator.GenerateCandidates(conflict, leaderRoute, followerRoute, catalog,
            code => Capability(code, canHold: true));

        Assert.All(solutions, s => Assert.Equal("FOLLOWER", s.TrainToWait));
        Assert.DoesNotContain(solutions, s => s.TrainToWait == "LEADER");
    }

    [Fact]
    public void Headway_CanHoldTrue_IgnoresCanMeetAndCanOvertake()
    {
        var leaderRoute = BuildRoute("LEADER", Direction.Inbound, new[] { 30, 40 }, SectionIdBetween);
        var followerRoute = BuildRoute("FOLLOWER", Direction.Inbound, new[] { 10, 30, 40 }, SectionIdBetween);

        var conflict = Headway(leaderServiceId: "LEADER", followerServiceId: "FOLLOWER", SectionIdBetween(30, 40));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (30, 40) });

        var generator = new CandidateGenerator(window: 3);
        var solutions = generator.GenerateCandidates(conflict, leaderRoute, followerRoute, catalog,
            code => Capability(code, canMeet: false, canOvertake: false, canHold: true));

        Assert.Contains(solutions, s => s.CandidateStationCode == 10 && s.TrainToWait == "FOLLOWER");
    }

    [Fact]
    public void Headway_CanHoldFalse_BlocksCandidate_RegardlessOfCanMeetCanOvertake()
    {
        var leaderRoute = BuildRoute("LEADER", Direction.Inbound, new[] { 30, 40 }, SectionIdBetween);
        var followerRoute = BuildRoute("FOLLOWER", Direction.Inbound, new[] { 10, 30, 40 }, SectionIdBetween);

        var conflict = Headway(leaderServiceId: "LEADER", followerServiceId: "FOLLOWER", SectionIdBetween(30, 40));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (30, 40) });

        var generator = new CandidateGenerator(window: 3);
        var solutions = generator.GenerateCandidates(conflict, leaderRoute, followerRoute, catalog,
            code => Capability(code, canMeet: true, canOvertake: true, canHold: false));

        Assert.Empty(solutions);
    }

    [Fact]
    public void Headway_DownstreamOfEntry_NotUpstream_Excluded()
    {
        // Follower: 10-30-40-50; conflict tren SEC-30-40; ga 50 nam SAU entry (idx cua 30) -> khong hop le.
        var leaderRoute = BuildRoute("LEADER", Direction.Inbound, new[] { 30, 40 }, SectionIdBetween);
        var followerRoute = BuildRoute("FOLLOWER", Direction.Inbound, new[] { 10, 30, 40, 50 }, SectionIdBetween);

        var conflict = Headway(leaderServiceId: "LEADER", followerServiceId: "FOLLOWER", SectionIdBetween(30, 40));
        var catalog = new PhysicalSectionCatalog(new Dictionary<string, (int, int)> { [conflict.SectionId] = (30, 40) });

        var generator = new CandidateGenerator(window: 3);
        var solutions = generator.GenerateCandidates(conflict, leaderRoute, followerRoute, catalog,
            code => Capability(code, canHold: true));

        Assert.DoesNotContain(solutions, s => s.CandidateStationCode == 50);
    }
}
