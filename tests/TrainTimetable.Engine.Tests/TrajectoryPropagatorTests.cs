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
