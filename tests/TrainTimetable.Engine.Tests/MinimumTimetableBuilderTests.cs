using TrainTimetable.Configuration;
using TrainTimetable.Domain;
using Xunit;

namespace TrainTimetable.Engine.Tests;

public class MinimumTimetableBuilderTests
{
    private readonly MinimumTimetableBuilder _builder = new(new StopRules(), new RunningTimeRules());
    private readonly RailwayNetwork _network = TestNetworkFixture.BuildFiveStationLine();

    // Hanh trinh: 1 -> 2 (through) -> 3 (passenger stop) -> 4 (through) -> 5 (destination).
    // Inbound running time: 30, 25, 40, 35. Tay tinh (xem TestNetworkFixture):
    //   KG1-2: 30 + accel(+2) = 32          -> Arrival(2)=392
    //   Ga2: through, Departure(2)=392
    //   KG2-3: 25 + decel(+1, dung o ga3)=26 -> Arrival(3)=418, Departure(3)=418+3(passenger)=421
    //   KG3-4: 40 + accel(+2, xuat phat sau khi dung)=42 -> Arrival(4)=463
    //   Ga4: through, Departure(4)=463
    //   KG4-5: 35 + decel(+1, la destination)=36 -> Arrival(5)=499
    private TrainService BuildServiceWithPassengerStopAtStation3(int journeyTimeMinutes = 160) => new(
        serviceId: "SE1",
        trainCode: "SE1",
        direction: Direction.Inbound,
        originStationSequence: 1,
        destinationStationSequence: 5,
        fixedDepartureTimeOfDayMinutes: 360,
        journeyTimeMinutes: journeyTimeMinutes,
        priority: 1,
        stopRequirements: new List<TrainStopRequirement>
        {
            new() { StationSequence = 3, RequiresPassengerStop = true }
        });

    [Fact]
    public void Build_ThroughStation_HasNoAccelerationOrDecelerationAndNoStopDuration()
    {
        var trajectory = _builder.Build(BuildServiceWithPassengerStopAtStation3(), _network);

        var station2 = trajectory.GetEntry(2);
        Assert.Equal(StopType.Through, station2.StopType);
        Assert.Equal(0, station2.StopDurationMinutes);
        Assert.False(station2.DecelerationApplied); // ga 2 khong yeu cau dung -> khong giam toc de dung o day
        Assert.Equal(392, station2.ArrivalTimeMinutes);
        Assert.Equal(392, station2.DepartureTimeMinutes);
    }

    [Fact]
    public void Build_FirstSectionFromOrigin_AlwaysAppliesAccelerationPenalty()
    {
        var trajectory = _builder.Build(BuildServiceWithPassengerStopAtStation3(), _network);

        var station2 = trajectory.GetEntry(2);
        Assert.True(station2.AccelerationApplied);
        Assert.Equal(32, station2.RunningTimeFromPrevMinutes); // 30 (min) + 2 (accel)
    }

    [Fact]
    public void Build_SectionEndingAtStopStation_AppliesDecelerationPenalty()
    {
        var trajectory = _builder.Build(BuildServiceWithPassengerStopAtStation3(), _network);

        var station3 = trajectory.GetEntry(3);
        Assert.True(station3.DecelerationApplied);
        Assert.False(station3.AccelerationApplied); // truoc do ga2 la through, khong xuat phat tu trang thai dung
        Assert.Equal(26, station3.RunningTimeFromPrevMinutes); // 25 (min) + 1 (decel)
        Assert.Equal(418, station3.ArrivalTimeMinutes);
    }

    [Fact]
    public void Build_SectionAfterStopStation_AppliesAccelerationPenaltyOnDeparture()
    {
        var trajectory = _builder.Build(BuildServiceWithPassengerStopAtStation3(), _network);

        var station4 = trajectory.GetEntry(4);
        Assert.True(station4.AccelerationApplied); // xuat phat tu ga3 sau khi da dung
        Assert.False(station4.DecelerationApplied);
        Assert.Equal(42, station4.RunningTimeFromPrevMinutes); // 40 (min) + 2 (accel)
        Assert.Equal(463, station4.ArrivalTimeMinutes);
    }

    [Fact]
    public void Build_Destination_AlwaysAppliesDecelerationPenaltyEvenWithoutExplicitStopRequirement()
    {
        var trajectory = _builder.Build(BuildServiceWithPassengerStopAtStation3(), _network);

        var destination = trajectory.Last;
        Assert.Equal(5, destination.StationSequence);
        Assert.True(destination.DecelerationApplied);
        Assert.False(destination.AccelerationApplied); // ga4 truoc do la through
        Assert.Equal(36, destination.RunningTimeFromPrevMinutes); // 35 (min) + 1 (decel)
        Assert.Equal(499, destination.ArrivalTimeMinutes);
        Assert.Null(destination.DepartureTimeMinutes);
    }

    [Fact]
    public void Build_PassengerStop_UsesConfiguredDurationOfThreeMinutes()
    {
        var trajectory = _builder.Build(BuildServiceWithPassengerStopAtStation3(), _network);

        var station3 = trajectory.GetEntry(3);
        Assert.Equal(StopType.Passenger, station3.StopType);
        Assert.Equal(3, station3.StopDurationMinutes);
        Assert.Equal(421, station3.DepartureTimeMinutes); // 418 + 3
    }

    [Fact]
    public void Build_TechnicalStop_UsesConfiguredDurationOfTwentyMinutes()
    {
        var service = new TrainService(
            serviceId: "SE2", trainCode: "SE2", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 5,
            fixedDepartureTimeOfDayMinutes: 360, journeyTimeMinutes: 200, priority: 1,
            stopRequirements: new List<TrainStopRequirement>
            {
                new() { StationSequence = 3, RequiresTechnicalStop = true }
            });

        var trajectory = _builder.Build(service, _network);

        var station3 = trajectory.GetEntry(3);
        Assert.Equal(StopType.Technical, station3.StopType);
        Assert.Equal(20, station3.StopDurationMinutes);
    }

    [Theory]
    [InlineData(StopTimeCombineMode.Max, 20)]
    [InlineData(StopTimeCombineMode.Sum, 23)]
    public void Build_PassengerAndTechnicalStopAtSameStation_CombinesAccordingToConfiguredRule(
        StopTimeCombineMode combineMode, int expectedStopMinutes)
    {
        var stopRules = new StopRules { CombineMode = combineMode };
        var builder = new MinimumTimetableBuilder(stopRules, new RunningTimeRules());
        var service = new TrainService(
            serviceId: "SE3", trainCode: "SE3", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 5,
            fixedDepartureTimeOfDayMinutes: 360, journeyTimeMinutes: 200, priority: 1,
            stopRequirements: new List<TrainStopRequirement>
            {
                new() { StationSequence = 3, RequiresPassengerStop = true, RequiresTechnicalStop = true }
            });

        var trajectory = builder.Build(service, _network);

        var station3 = trajectory.GetEntry(3);
        Assert.Equal(StopType.PassengerAndTechnical, station3.StopType);
        Assert.Equal(expectedStopMinutes, station3.StopDurationMinutes);
    }

    [Fact]
    public void Build_JourneyLongerThan24Hours_ComputesArrivalAsPlainAbsoluteMinutesWithoutWrapping()
    {
        // Hanh trinh HN-SG thuc te dai ~30-34h -> JourneyTime co the > 1440 phut (muc 0 thiet ke).
        var service = new TrainService(
            serviceId: "SE1", trainCode: "SE1", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 5,
            fixedDepartureTimeOfDayMinutes: 1200, // 20:00
            journeyTimeMinutes: 1950,
            priority: 1,
            stopRequirements: new List<TrainStopRequirement>
            {
                new() { StationSequence = 3, RequiresPassengerStop = true }
            });

        var trajectory = _builder.Build(service, _network);

        Assert.Equal(3150, service.FixedArrivalTimeMinutes); // 1200 + 1950, KHONG mod 1440
        Assert.Equal(1200 + 139, trajectory.Last.ArrivalTimeMinutes); // giong tay tinh o tren, chi doi moc xuat phat
    }

    // Test F (thiet ke muc 15.9): tau BYPASS mot ga nhanh - khu gian giua co MinRunningTime=0 (dong
    // logic van ton tai trong JourneySequence nhung khong ranh vao nhanh that). MinimumJourneyTime
    // KHONG duoc cong them bat ky accel/decel/stop nao cho ga nhanh do - phai dung bang tong 2 khu gian
    // thuc (10+10=20), khong phai 20 + accel/decel "gia" cho ga giua.
    [Fact]
    public void Build_ZeroRunningTimeThroughStation_AddsNoAccelerationDecelerationOrStopTime()
    {
        var stations = new List<Station>
        {
            BypassStation(1), BypassStation(2), BypassStation(3), BypassStation(4)
        };
        var sections = new List<Section>
        {
            BypassSection(1, 2, running: 10),
            BypassSection(2, 3, running: 0), // "ga nhanh ao" - tau nay khong re vao
            BypassSection(3, 4, running: 10)
        };
        var network = new RailwayNetwork(stations, sections);

        var service = new TrainService(
            serviceId: "BYPASS", trainCode: "BYPASS", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 4,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 25, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var trajectory = _builder.Build(service, network);

        var station2 = trajectory.GetEntry(2);
        var station3 = trajectory.GetEntry(3);
        // Diem mau chot: KHONG co accel/decel "gia" quanh chinh khu gian bypass (KG2-3) - station2
        // khong giam toc de "dung" o day (no khong dung), station3 khong tang toc tu mot lan dung
        // truoc do (khong co lan dung nao ca). Day la 2 assertion true chung minh bypass dung.
        Assert.False(station2.DecelerationApplied);
        Assert.False(station3.AccelerationApplied);
        Assert.Equal(0, station3.StopDurationMinutes);
        Assert.Equal(0, station3.RunningTimeFromPrevMinutes); // dung bang MinRunningTime=0, khong bi cong gi them

        // MinimumJourneyTime = 10+accel(2, xuat phat) [KG1-2] + 0 [KG2-3, bypass, KHONG accel/decel] +
        // 10+decel(1, la destination) [KG3-4] = 23. Accel o KG1-2 va decel o KG3-4 la penalty CHUAN cho
        // moi hanh trinh (xuat phat/den), khong lien quan gi den bypass - neu bypass sai, con so nay se
        // LON HON 23 (vd 25 neu bi cong nham +2 hoac +1 quanh ga nhanh ao).
        Assert.Equal(23, trajectory.Last.ArrivalTimeMinutes);
    }

    private static Station BypassStation(int seq) => new()
    {
        StationId = $"BP{seq}", Code = $"BP{seq}", Name = $"BP{seq}", Sequence = seq,
        Tracks = new List<StationTrack> { new($"BP{seq}-T1", TrackType.MainThrough, false, false) }
    };

    private static Section BypassSection(int from, int to, int running) => new()
    {
        SectionId = $"BPKG{from}-{to}",
        FromStationSequence = from,
        ToStationSequence = to,
        MinRunningTimeMinutes = new Dictionary<Direction, int> { [Direction.Inbound] = running, [Direction.Outbound] = running }
    };
}
