using TrainTimetable.Configuration;
using TrainTimetable.Domain;
using Xunit;

namespace TrainTimetable.Engine.Tests;

public class BufferCalculatorTests
{
    private readonly MinimumTimetableBuilder _builder = new(new StopRules(), new RunningTimeRules());
    private readonly BufferCalculator _bufferCalculator = new();
    private readonly RailwayNetwork _network = TestNetworkFixture.BuildFiveStationLine();

    // MinimumJourneyTime cho tuyen 1->5 voi passenger stop tai ga 3 = 139 phut
    // (xem tay tinh trong MinimumTimetableBuilderTests).
    private TrainService BuildService(int journeyTimeMinutes) => new(
        serviceId: "SE1", trainCode: "SE1", direction: Direction.Inbound,
        originStationSequence: 1, destinationStationSequence: 5,
        fixedDepartureTimeOfDayMinutes: 360, journeyTimeMinutes: journeyTimeMinutes, priority: 1,
        stopRequirements: new List<TrainStopRequirement>
        {
            new() { StationSequence = 3, RequiresPassengerStop = true }
        });

    [Fact]
    public void Calculate_JourneyTimeExceedsMinimum_ProducesPositiveTotalBufferAndIsFeasible()
    {
        var service = BuildService(journeyTimeMinutes: 160);
        var trajectory = _builder.Build(service, _network);

        var result = _bufferCalculator.Calculate(service, trajectory);

        Assert.Equal(139, result.MinimumJourneyTimeMinutes);
        Assert.Equal(160, result.JourneyTimeMinutes);
        Assert.Equal(21, result.TotalBufferMinutes);
        Assert.True(result.IsFeasible);
    }

    [Fact]
    public void Calculate_JourneyTimeBelowMinimum_ProducesNegativeTotalBufferAndIsNotFeasible()
    {
        // Test 7 (thiet ke muc 13): giờ đi/đến cố định không đủ cho minimum running -> infeasible ngay
        // từ đầu, trước khi đưa vào solver.
        var service = BuildService(journeyTimeMinutes: 100);
        var trajectory = _builder.Build(service, _network);

        var result = _bufferCalculator.Calculate(service, trajectory);

        Assert.Equal(-39, result.TotalBufferMinutes);
        Assert.False(result.IsFeasible);
    }

    [Fact]
    public void Calculate_JourneyLongerThan24Hours_UsesPlainAbsoluteArithmetic()
    {
        var service = new TrainService(
            serviceId: "SE1", trainCode: "SE1", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 5,
            fixedDepartureTimeOfDayMinutes: 1200, journeyTimeMinutes: 1950, priority: 1,
            stopRequirements: new List<TrainStopRequirement>
            {
                new() { StationSequence = 3, RequiresPassengerStop = true }
            });
        var trajectory = _builder.Build(service, _network);

        var result = _bufferCalculator.Calculate(service, trajectory);

        Assert.Equal(139, result.MinimumJourneyTimeMinutes);
        Assert.Equal(1950, result.JourneyTimeMinutes);
        Assert.Equal(1811, result.TotalBufferMinutes);
        Assert.True(result.IsFeasible);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void ComputeForwardSlackMinutes_OnZeroRecoveryMinimumTrajectory_EqualsTotalBufferEverywhere(
        int stationSequence)
    {
        // Tren mot minimum trajectory (RecoveryTimeFromPrev = 0 khap noi), ForwardSlack(T,k) phai
        // BANG NHAU tai moi ga k - chua co gi "da tieu" nen toan bo TotalBuffer van con nguyen
        // (xem chung minh thiet ke muc 4.1).
        var service = BuildService(journeyTimeMinutes: 160);
        var trajectory = _builder.Build(service, _network);
        var totalBuffer = _bufferCalculator.Calculate(service, trajectory).TotalBufferMinutes;

        var forwardSlack = _bufferCalculator.ComputeForwardSlackMinutes(service, trajectory, stationSequence);

        Assert.Equal(totalBuffer, forwardSlack);
    }

    // Trajectory tong hop (khong qua MinimumTimetableBuilder) mo phong mot lich trinh KHONG con la
    // free-running: ga 3 co mandatory passenger stop (StopDuration=3), recovery da duoc "cay" khong
    // deu vao hai khu gian (2 phut o KG2-3, 0 phut o KG3-4, 5 phut o KG4-5). Muc dich: chung minh cong
    // thuc ForwardSlack tru dung phan RecoveryTimeFromPrev (khong dung nham thanh minimum) va cong
    // nguyen StopDuration (khong coi day la slack co the "muon").
    //
    // Ga1(origin,dep=0) -> Ga2(arr=30,dep=30, running=30,recovery=0)
    //                   -> Ga3(arr=57,dep=60, running=27,recovery=2, stop=3 mandatory passenger)
    //                   -> Ga4(arr=100,dep=100, running=40,recovery=0)
    //                   -> Ga5(dest,arr=140, running=40,recovery=5)
    // MinimumJourneyTime = 30+25+3+40+35 = 133. JourneyTime=150 -> TotalBuffer=17 (= 10 buffer "chua cay"
    // giu o cuoi [150-140] + 2 recovery KG2-3 + 5 recovery KG4-5).
    private static (TrainService Service, TrainServiceTrajectory Trajectory) BuildUnevenRecoveryScenarioWithMandatoryStop()
    {
        var service = new TrainService(
            serviceId: "SE4", trainCode: "SE4", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 5,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 150, priority: 1,
            stopRequirements: new List<TrainStopRequirement>
            {
                new() { StationSequence = 3, RequiresPassengerStop = true }
            });

        var entries = new List<TimetableEntry>
        {
            Entry(1, StopType.Through, null, 0, running: 0, recovery: 0, stopDuration: 0),
            Entry(2, StopType.Through, 30, 30, running: 30, recovery: 0, stopDuration: 0),
            Entry(3, StopType.Passenger, 57, 60, running: 27, recovery: 2, stopDuration: 3),
            Entry(4, StopType.Through, 100, 100, running: 40, recovery: 0, stopDuration: 0),
            Entry(5, StopType.Through, 140, null, running: 40, recovery: 5, stopDuration: 0)
        };

        var trajectory = new TrainServiceTrajectory { ServiceId = "SE4", Entries = entries };
        return (service, trajectory);
    }

    private static TimetableEntry Entry(
        int stationSeq, StopType stopType, int? arrival, int? departure, int running, int recovery, int stopDuration) => new()
    {
        StationSequence = stationSeq,
        ArrivalTimeMinutes = arrival,
        DepartureTimeMinutes = departure,
        StopType = stopType,
        StopDurationMinutes = stopDuration,
        RunningTimeFromPrevMinutes = running,
        RecoveryTimeFromPrevMinutes = recovery,
        CumulativeInsertedDelayMinutes = 0
    };

    [Fact]
    public void ComputeForwardSlackMinutes_BeforeAnyRecoveryConsumed_EqualsTotalBufferEvenWithMandatoryStopDownstream()
    {
        var (service, trajectory) = BuildUnevenRecoveryScenarioWithMandatoryStop();

        // Tai ga 2: chua co recovery nao trong doan da di qua (KG1-2 recovery=0) -> toan bo TotalBuffer
        // (ke ca 2 phut da "cay" trong KG2-3 va 3 phut StopDuration bat buoc o ga 3) van con nguyen ven.
        var forwardSlack = _bufferCalculator.ComputeForwardSlackMinutes(service, trajectory, stationSequence: 2);

        Assert.Equal(17, forwardSlack);
    }

    [Fact]
    public void ComputeForwardSlackMinutes_AfterMandatoryStopWithRecoveryAlreadyPassed_ExcludesConsumedRecoveryButNotStopDuration()
    {
        var (service, trajectory) = BuildUnevenRecoveryScenarioWithMandatoryStop();

        // Tai ga 4 (sau ga 3): 2 phut recovery cua KG2-3 va 3 phut StopDuration cua ga 3 da "o phia sau"
        // -> khong con la slack kha dung nua. Chi con 5 phut recovery cua KG4-5 + 10 phut buffer chua
        // cay = 15, dung bang TotalBuffer(17) - 2 (recovery da tieu, KHONG bao gom StopDuration vi no
        // chua bao gio la slack).
        var forwardSlack = _bufferCalculator.ComputeForwardSlackMinutes(service, trajectory, stationSequence: 4);

        Assert.Equal(15, forwardSlack);
    }

    [Fact]
    public void ComputeForwardSlackMinutes_ImmediatelyAfterMandatoryStop_UnaffectedByStopDurationItself()
    {
        var (service, trajectory) = BuildUnevenRecoveryScenarioWithMandatoryStop();

        // Tai ga 3 (ngay sau khi rơi khoi mandatory stop): StopDuration cua chinh ga 3 khong con nam
        // trong "minimum remaining" (vi vong lap chi tinh tu index+1 tro di), nhung no cung khong con
        // la slack kha dung nua (no da "tieu" giua Arrival(3) va Departure(3)) - ket qua phai giong het
        // ForwardSlack(4) vi khu gian 3-4 khong co recovery nao ca.
        var forwardSlackAt3 = _bufferCalculator.ComputeForwardSlackMinutes(service, trajectory, stationSequence: 3);
        var forwardSlackAt4 = _bufferCalculator.ComputeForwardSlackMinutes(service, trajectory, stationSequence: 4);

        Assert.Equal(15, forwardSlackAt3);
        Assert.Equal(forwardSlackAt4, forwardSlackAt3);
    }
}
