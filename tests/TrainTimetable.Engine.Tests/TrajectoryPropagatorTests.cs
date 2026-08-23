using TrainTimetable.Domain;
using Xunit;

namespace TrainTimetable.Engine.Tests;

public class TrajectoryPropagatorTests
{
    private readonly BufferCalculator _bufferCalculator = new();
    private readonly TrajectoryPropagator _propagator;

    public TrajectoryPropagatorTests()
    {
        _propagator = new TrajectoryPropagator(_bufferCalculator);
    }

    // Trajectory tong hop (khong qua MinimumTimetableBuilder) de kiem soat truc tiep RecoveryTimeFromPrev:
    // Ga1(origin, dep=0) -> Ga2(arr=50,dep=50, recovery=0) -> Ga3(arr=100,dep=100, recovery=10, da
    // cay san 10' buffer trong khu gian 2-3) -> Ga4(arr=160,dep=160, recovery=0) -> Ga5(dest, arr=210).
    // FixedArrivalTime = 220 -> ForwardSlack tai Ga2 = 220 - 50 - (40+60+50) = 20.
    private static (TrainService Service, TrainServiceTrajectory Trajectory) BuildScenario()
    {
        var service = new TrainService(
            serviceId: "SE1", trainCode: "SE1", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 5,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 220, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var entries = new List<TimetableEntry>
        {
            Entry(1, null, 0, running: 0, recovery: 0),
            Entry(2, 50, 50, running: 50, recovery: 0),
            Entry(3, 100, 100, running: 50, recovery: 10),
            Entry(4, 160, 160, running: 60, recovery: 0),
            Entry(5, 210, null, running: 50, recovery: 0)
        };

        var trajectory = new TrainServiceTrajectory { ServiceId = "SE1", Entries = entries };
        return (service, trajectory);
    }

    private static TimetableEntry Entry(int stationSeq, int? arrival, int? departure, int running, int recovery) => new()
    {
        StationSequence = stationSeq,
        ArrivalTimeMinutes = arrival,
        DepartureTimeMinutes = departure,
        StopType = StopType.Through,
        StopDurationMinutes = 0,
        RunningTimeFromPrevMinutes = running,
        RecoveryTimeFromPrevMinutes = recovery,
        CumulativeInsertedDelayMinutes = 0
    };

    [Fact]
    public void InsertDelay_WithinForwardSlack_AbsorbsPartiallyIntoDownstreamRecoveryAndReachesDestinationOnTime()
    {
        var (service, trajectory) = BuildScenario();

        var result = _propagator.InsertDelay(service, trajectory, stationSequence: 2, delayMinutes: 12);

        Assert.True(result.IsFeasible);
        Assert.Equal(0, result.ResidualDelayMinutes);
        var newTrajectory = result.NewTrajectory!;

        var station1 = newTrajectory.GetEntry(1);
        Assert.Equal(0, station1.DepartureTimeMinutes); // truoc diem chen - khong doi

        var station2 = newTrajectory.GetEntry(2);
        Assert.Equal(62, station2.DepartureTimeMinutes); // 50 + 12

        var station3 = newTrajectory.GetEntry(3);
        Assert.Equal(102, station3.ArrivalTimeMinutes); // carry 12 - absorbed 10 (het recovery) = 2
        Assert.Equal(0, station3.RecoveryTimeFromPrevMinutes); // recovery 10 da dung het de hap thu
        Assert.Equal(40, station3.RunningTimeFromPrevMinutes); // 50 - 10 (giam dung bang phan da hap thu)

        var station4 = newTrajectory.GetEntry(4);
        Assert.Equal(162, station4.ArrivalTimeMinutes); // khong con recovery de hap thu -> carry 2 giu nguyen

        var destination = newTrajectory.Last;
        Assert.Equal(212, destination.ArrivalTimeMinutes); // 210 + 2, <= FixedArrivalTime (220)
        Assert.True(destination.ArrivalTimeMinutes <= service.FixedArrivalTimeMinutes);
    }

    [Fact]
    public void InsertDelay_WithinForwardSlack_IncreasesCumulativeInsertedDelayUniformlyFromInsertionPointOnward()
    {
        var (service, trajectory) = BuildScenario();

        var result = _propagator.InsertDelay(service, trajectory, stationSequence: 2, delayMinutes: 12);

        var newTrajectory = result.NewTrajectory!;
        Assert.Equal(0, newTrajectory.GetEntry(1).CumulativeInsertedDelayMinutes);
        Assert.Equal(12, newTrajectory.GetEntry(2).CumulativeInsertedDelayMinutes);
        Assert.Equal(12, newTrajectory.GetEntry(3).CumulativeInsertedDelayMinutes);
        Assert.Equal(12, newTrajectory.GetEntry(4).CumulativeInsertedDelayMinutes);
        Assert.Equal(12, newTrajectory.Last.CumulativeInsertedDelayMinutes);
    }

    [Fact]
    public void InsertDelay_ExceedingForwardSlack_IsInfeasibleAndReportsResidual()
    {
        var (service, trajectory) = BuildScenario();

        var result = _propagator.InsertDelay(service, trajectory, stationSequence: 2, delayMinutes: 25);

        Assert.False(result.IsFeasible);
        Assert.Null(result.NewTrajectory);
        Assert.Equal(5, result.ResidualDelayMinutes); // ForwardSlack tai Ga2 = 20 -> vuot 5 phut
    }

    [Fact]
    public void InsertDelay_ExactlyEqualToForwardSlack_IsFeasibleAndArrivesExactlyOnFixedArrivalTime()
    {
        var (service, trajectory) = BuildScenario();

        var result = _propagator.InsertDelay(service, trajectory, stationSequence: 2, delayMinutes: 20);

        Assert.True(result.IsFeasible);
        Assert.Equal(0, result.ResidualDelayMinutes);
        Assert.Equal(service.FixedArrivalTimeMinutes, result.NewTrajectory!.Last.ArrivalTimeMinutes);

        // Bien tren cua invariant: khi delay dung bang ForwardSlack, RecoveryTimeFromPrev khong bao gio
        // am o bat ky ga nao (Math.Min dam bao khong "muon" qua so con lai).
        Assert.All(result.NewTrajectory.Entries, e => Assert.True(e.RecoveryTimeFromPrevMinutes >= 0));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(20)]
    public void InsertDelay_Feasible_ConservesDelayAsConsumedRecoveryPlusResidualArrivalShift(int delayMinutes)
    {
        // Invariant tong quat cua propagation (muc 8 thiet ke): moi phut delay chen vao phai duoc
        // "hach toan" chinh xac mot lan - hoac bi mot ga phia sau "an" (RecoveryTimeFromPrev giam
        // dung bang do), hoac day lui Arrival(destination) dung bang phan con lai chua duoc an - khong
        // duoc mat di, cung khong duoc dem hai lan. FinalArrival == FixedArrivalTime CHI dung khi
        // delay == ForwardSlack (bien tren, xem test rieng o tren); truong hop chung chi dam bao <=.
        var (service, trajectory) = BuildScenario();

        var result = _propagator.InsertDelay(service, trajectory, stationSequence: 2, delayMinutes);

        Assert.True(result.IsFeasible);
        Assert.Equal(0, result.ResidualDelayMinutes);

        var newTrajectory = result.NewTrajectory!;
        var insertIndex = trajectory.IndexOf(2);

        var totalRecoveryConsumed = 0;
        for (var i = insertIndex + 1; i < trajectory.Entries.Count; i++)
        {
            var oldEntry = trajectory.Entries[i];
            var newEntry = newTrajectory.Entries[i];

            Assert.True(newEntry.RecoveryTimeFromPrevMinutes >= 0);
            totalRecoveryConsumed += oldEntry.RecoveryTimeFromPrevMinutes - newEntry.RecoveryTimeFromPrevMinutes;
        }

        var residualArrivalShift = newTrajectory.Last.ArrivalTimeMinutes!.Value - trajectory.Last.ArrivalTimeMinutes!.Value;

        Assert.Equal(delayMinutes, totalRecoveryConsumed + residualArrivalShift);
        Assert.True(newTrajectory.Last.ArrivalTimeMinutes <= service.FixedArrivalTimeMinutes);
    }

    // Trajectory KHONG co recovery nao duoc cay san (RecoveryTimeFromPrev = 0 khap noi) - dung mo phong
    // dung y "TotalBuffer chua phan bo" tren MinimumTimetableBuilder that (xem thiet ke muc 15.6/15.10).
    // Ga1(origin,dep=0) -> Ga2(arr=50,dep=50) -> Ga3(arr=100,dep=100) -> Ga4(arr=160,dep=160)
    //                   -> Ga5(dest,arr=210). MinimumJourneyTime=50+50+60+50=210, JourneyTime=230
    //                   -> TotalBuffer=20.
    private static (TrainService Service, TrainServiceTrajectory Trajectory) BuildZeroRecoveryScenarioWithTotalBufferTwenty()
    {
        var service = new TrainService(
            serviceId: "SE2", trainCode: "SE2", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 5,
            fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 230, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var entries = new List<TimetableEntry>
        {
            Entry(1, null, 0, running: 0, recovery: 0),
            Entry(2, 50, 50, running: 50, recovery: 0),
            Entry(3, 100, 100, running: 50, recovery: 0),
            Entry(4, 160, 160, running: 60, recovery: 0),
            Entry(5, 210, null, running: 50, recovery: 0)
        };

        var trajectory = new TrainServiceTrajectory { ServiceId = "SE2", Entries = entries };
        return (service, trajectory);
    }

    [Fact]
    public void InsertDelay_OnZeroRecoveryTrajectory_ConsumesUnallocatedBufferAcrossSequentialInsertsAndBlocksOverflow()
    {
        // Test bat buoc (yeu cau review): chung minh bang so lieu that, khong chi bang chung minh dai
        // so, rang mot trajectory KHONG co recovery nao cay san van hap thu dung dan nhieu lan
        // InsertDelay lien tiep, luon giu Arrival(destination) <= FixedArrivalTime, va UnallocatedBuffer
        // (mucc 15.10, BufferCalculator.ComputeBufferState) giam dung theo tung lan chen - khong can
        // BufferAllocator cay RecoveryTimeFromPrev truoc.
        var (service, trajectory) = BuildZeroRecoveryScenarioWithTotalBufferTwenty();

        var initialState = _bufferCalculator.ComputeBufferState(service, trajectory);
        Assert.Equal(20, initialState.TotalBufferMinutes);
        Assert.Equal(0, initialState.AllocatedRecoveryMinutes);
        Assert.Equal(0, initialState.ConsumedBufferMinutes);
        Assert.Equal(20, initialState.UnallocatedBufferMinutes);

        // Chen 5 phut tai ga giua (Ga2) -> con lai 15.
        var result1 = _propagator.InsertDelay(service, trajectory, stationSequence: 2, delayMinutes: 5);
        Assert.True(result1.IsFeasible);
        Assert.Equal(0, result1.ResidualDelayMinutes);
        Assert.True(result1.NewTrajectory!.Last.ArrivalTimeMinutes <= service.FixedArrivalTimeMinutes);

        var state1 = _bufferCalculator.ComputeBufferState(service, result1.NewTrajectory);
        Assert.Equal(20, state1.TotalBufferMinutes); // hang so, khong doi qua InsertDelay
        Assert.Equal(0, state1.AllocatedRecoveryMinutes); // khong co recovery nao de tieu
        Assert.Equal(5, state1.ConsumedBufferMinutes);
        Assert.Equal(15, state1.UnallocatedBufferMinutes);

        // Chen them 12 phut (cong don 17) tai cung Ga2, tren trajectory MOI -> con lai 3.
        var result2 = _propagator.InsertDelay(service, result1.NewTrajectory, stationSequence: 2, delayMinutes: 12);
        Assert.True(result2.IsFeasible);
        Assert.Equal(0, result2.ResidualDelayMinutes);
        Assert.True(result2.NewTrajectory!.Last.ArrivalTimeMinutes <= service.FixedArrivalTimeMinutes);

        var state2 = _bufferCalculator.ComputeBufferState(service, result2.NewTrajectory);
        Assert.Equal(20, state2.TotalBufferMinutes);
        Assert.Equal(0, state2.AllocatedRecoveryMinutes);
        Assert.Equal(17, state2.ConsumedBufferMinutes);
        Assert.Equal(3, state2.UnallocatedBufferMinutes);

        // Chen them 4 phut nua (se vuot qua 3 phut con lai) -> PHAI infeasible, KHONG duoc coi la thanh
        // cong roi lam Arrival(destination) tre qua FixedArrivalTime.
        var result3 = _propagator.InsertDelay(service, result2.NewTrajectory, stationSequence: 2, delayMinutes: 4);
        Assert.False(result3.IsFeasible);
        Assert.Null(result3.NewTrajectory);
        Assert.Equal(1, result3.ResidualDelayMinutes); // con lai 3, vuot 1 phut

        // Trajectory tra ve tu lan chen truoc (result2) khong bi anh huong boi lan chen that bai - van
        // dung hen dung gio.
        Assert.Equal(service.FixedArrivalTimeMinutes, result2.NewTrajectory.Last.ArrivalTimeMinutes + 3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BufferState_And_ForwardSlack_SatisfyRemainingBufferIdentity_AtVariousStations(int stationSequence)
    {
        // Chung minh identity muc 4.3 (RemainingBuffer = ForwardSlack(k) + RedistributableSlack(k)) tren
        // chinh trajectory da co san recovery khong deu (station3 recovery=10) - dam bao AllocatedRecovery
        // KHONG bi tinh nham thanh da tieu (RemainingBuffer phai tinh tu ConsumedBuffer, khong phai
        // TotalBuffer - AllocatedRecovery - ConsumedBuffer).
        var (service, trajectory) = BuildScenario();

        var state = _bufferCalculator.ComputeBufferState(service, trajectory);
        Assert.Equal(
            state.TotalBufferMinutes,
            state.AllocatedRecoveryMinutes + state.ConsumedBufferMinutes + state.UnallocatedBufferMinutes);

        var remainingBuffer = state.TotalBufferMinutes - state.ConsumedBufferMinutes;
        Assert.Equal(state.AllocatedRecoveryMinutes + state.UnallocatedBufferMinutes, remainingBuffer);

        var forwardSlack = _bufferCalculator.ComputeForwardSlackMinutes(service, trajectory, stationSequence);

        // RedistributableSlack(T,k): recovery cua moi ga m voi StationSequence(m) <= k - BAO GOM ca
        // RecoveryTimeFromPrev(k) chinh no (khu gian dan VAO k), dung bien nhu mo ta o muc 4.2.
        var redistributableSlack = trajectory.Entries
            .Where(e => e.StationSequence <= stationSequence)
            .Sum(e => e.RecoveryTimeFromPrevMinutes);

        Assert.Equal(remainingBuffer, forwardSlack + redistributableSlack);
    }

    [Fact]
    public void InsertDelay_AtOriginStation_ThrowsBecauseFixedDepartureTimeIsImmutable()
    {
        var (service, trajectory) = BuildScenario();

        Assert.Throws<ArgumentException>(() =>
            _propagator.InsertDelay(service, trajectory, stationSequence: 1, delayMinutes: 5));
    }

    [Fact]
    public void InsertDelay_WithZeroMinutes_ReturnsOriginalTrajectoryUnchanged()
    {
        var (service, trajectory) = BuildScenario();

        var result = _propagator.InsertDelay(service, trajectory, stationSequence: 2, delayMinutes: 0);

        Assert.True(result.IsFeasible);
        Assert.Same(trajectory, result.NewTrajectory);
    }
}
