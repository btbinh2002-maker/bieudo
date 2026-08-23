using TrainTimetable.Configuration;
using TrainTimetable.Domain;
using Xunit;

namespace TrainTimetable.Engine.Tests;

/// <summary>
/// Kiem chung cong thuc HEADWAY (muc 7.2, review lan 3 + lan 4): RequiredWaitingMinutes phai tinh tren
/// preview SAU KHI ap ForcedStop (neu co) - khong cong thang HeadwayDeficitMinutes goc - va
/// LeaderExitTime phai doc truc tiep tu occupation (SectionTimingResolver), khong suy tu
/// Conflict.ConflictEndTime/RequiredHeadway.
///
/// Route Follower dung chung cho moi test: Origin(seq1) - S(seq2, candidate) - E(seq3, entry cua Section
/// xung dot) - F(seq4, exit). RequiredHeadwayMinutes=3, LeaderExitTime=100 co dinh -> RequiredSafeEntry=103.
/// </summary>
public class RequiredShiftCalculatorTests
{
    private const string SectionId = "SEC-E-F";
    private const int LeaderExitTime = 100;
    private const int RequiredHeadway = 3;

    private static Station Station(int seq) => new()
    {
        StationId = $"S{seq}", Code = seq.ToString(), Name = $"S{seq}", Sequence = seq,
        Tracks = new List<StationTrack> { new($"S{seq}-T1", TrackType.MainThrough, false, false) }
    };

    private static RailwayNetwork BuildFollowerNetwork() => new(
        new List<Station> { Station(1), Station(2), Station(3), Station(4) },
        new List<Section>
        {
            new() { SectionId = "SEC-O-S", FromStationSequence = 1, ToStationSequence = 2, MinRunningTimeMinutes = Rt() },
            new() { SectionId = "SEC-S-E", FromStationSequence = 2, ToStationSequence = 3, MinRunningTimeMinutes = Rt() },
            new() { SectionId = SectionId, FromStationSequence = 3, ToStationSequence = 4, MinRunningTimeMinutes = Rt() }
        });

    private static IReadOnlyDictionary<Direction, int> Rt() =>
        new Dictionary<Direction, int> { [Direction.Inbound] = 1, [Direction.Outbound] = 1 };

    private static TrainService FollowerService() => new(
        serviceId: "FOLLOWER", trainCode: "FOLLOWER", direction: Direction.Inbound,
        originStationSequence: 1, destinationStationSequence: 4,
        fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: 10000, priority: 1,
        stopRequirements: new List<TrainStopRequirement>());

    /// <summary>
    /// candidateEntry = ArrivalTimeMinutes cua E (E la Through nen Departure == Arrival) TRUOC khi ap
    /// ForcedStop - dieu chinh de dat duoc HeadwayDeficitMinutes goc mong muon (103 - candidateEntry).
    /// </summary>
    private static TrainServiceTrajectory BuildFollowerTrajectory(bool sIsThrough, int followerEntryBeforeForcedStop, int recoveryAtE)
    {
        var sArrival = 50;
        var entries = new List<TimetableEntry>
        {
            new() { StationSequence = 1, ArrivalTimeMinutes = null, DepartureTimeMinutes = 0, StopType = StopType.Through, RunningTimeFromPrevMinutes = 0, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 },
            sIsThrough
                ? new TimetableEntry { StationSequence = 2, ArrivalTimeMinutes = sArrival, DepartureTimeMinutes = sArrival, StopType = StopType.Through, RunningTimeFromPrevMinutes = sArrival, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 }
                : new TimetableEntry { StationSequence = 2, ArrivalTimeMinutes = sArrival, DepartureTimeMinutes = sArrival + 3, StopType = StopType.Passenger, RunningTimeFromPrevMinutes = sArrival, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 3, CumulativeInsertedDelayMinutes = 0 },
            new() { StationSequence = 3, ArrivalTimeMinutes = followerEntryBeforeForcedStop, DepartureTimeMinutes = followerEntryBeforeForcedStop, StopType = StopType.Through, RunningTimeFromPrevMinutes = followerEntryBeforeForcedStop - (sIsThrough ? sArrival : sArrival + 3), RecoveryTimeFromPrevMinutes = recoveryAtE, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 },
            new() { StationSequence = 4, ArrivalTimeMinutes = followerEntryBeforeForcedStop + 20, DepartureTimeMinutes = null, StopType = StopType.Through, RunningTimeFromPrevMinutes = 20, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 }
        };

        return new TrainServiceTrajectory { ServiceId = "FOLLOWER", Entries = entries };
    }

    private static (TrainService Service, TrainServiceTrajectory Trajectory, RailwayNetwork Network) BuildLeader()
    {
        var network = new RailwayNetwork(
            new List<Station> { Station(1), Station(2) },
            new List<Section> { new() { SectionId = SectionId, FromStationSequence = 1, ToStationSequence = 2, MinRunningTimeMinutes = Rt() } });

        var service = new TrainService(
            serviceId: "LEADER", trainCode: "LEADER", direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: 2,
            fixedDepartureTimeOfDayMinutes: 90, journeyTimeMinutes: 10, priority: 1,
            stopRequirements: new List<TrainStopRequirement>());

        var trajectory = new TrainServiceTrajectory
        {
            ServiceId = "LEADER",
            Entries = new List<TimetableEntry>
            {
                new() { StationSequence = 1, ArrivalTimeMinutes = null, DepartureTimeMinutes = 90, StopType = StopType.Through, RunningTimeFromPrevMinutes = 0, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 },
                new() { StationSequence = 2, ArrivalTimeMinutes = LeaderExitTime, DepartureTimeMinutes = null, StopType = StopType.Through, RunningTimeFromPrevMinutes = 10, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 }
            }
        };

        return (service, trajectory, network);
    }

    private static Conflict HeadwayConflict() => new()
    {
        ConflictId = "C1", Type = ConflictType.HEADWAY, ConstraintKind = ConstraintKind.SectionReleaseHeadway,
        ServiceIdA = "LEADER", CycleIndexA = 0, ServiceIdB = "FOLLOWER", CycleIndexB = 0,
        SectionId = SectionId, ConflictStartTimeMinutes = 0, ConflictEndTimeMinutes = LeaderExitTime + RequiredHeadway,
        RequiredHeadwayMinutes = RequiredHeadway, ActualGapMinutes = 0, HeadwayDeficitMinutes = 0
    };

    private static RequiredShiftCalculator BuildCalculator() =>
        new(new RunningTimeRules(), new BufferCalculator());

    [Fact]
    public void H1_StructuralAloneMoreThanEnough_RequiredWaitingIsZero_NotRawDeficit()
    {
        // deficit goc = 103-101 = 2; structural (decel1+accel2=3) mot minh du -> RequiredWaiting = 0 (khong phai 2).
        var (leaderService, leaderTrajectory, leaderNetwork) = BuildLeader();
        var followerService = FollowerService();
        var followerTrajectory = BuildFollowerTrajectory(sIsThrough: true, followerEntryBeforeForcedStop: 101, recoveryAtE: 0);
        var followerNetwork = BuildFollowerNetwork();

        var result = BuildCalculator().ComputeHeadway(
            HeadwayConflict(), leaderService, leaderTrajectory, leaderNetwork,
            followerService, followerTrajectory, followerNetwork, candidateLocalIndex: 1);

        Assert.True(result.IsForcedStop);
        Assert.Equal(1, result.DecelerationPenaltyMinutes);
        Assert.Equal(2, result.AccelerationPenaltyMinutes);
        Assert.Equal(0, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void H2_StructuralContributesOnlyPart_RemainingDeficitStillNeedsWaiting()
    {
        // deficit goc = 103-98 = 5; structural = 3 -> con lai 2, recovery=0 -> RequiredWaiting = 2.
        var (leaderService, leaderTrajectory, leaderNetwork) = BuildLeader();
        var followerService = FollowerService();
        var followerTrajectory = BuildFollowerTrajectory(sIsThrough: true, followerEntryBeforeForcedStop: 98, recoveryAtE: 0);
        var followerNetwork = BuildFollowerNetwork();

        var result = BuildCalculator().ComputeHeadway(
            HeadwayConflict(), leaderService, leaderTrajectory, leaderNetwork,
            followerService, followerTrajectory, followerNetwork, candidateLocalIndex: 1);

        Assert.Equal(2, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void H3_NoForcedStop_RecoveryBetweenIsFullyAdded()
    {
        // Khong ForcedStop (S da la Passenger stop san): deficit=5, recovery giua S va E = 4 -> RequiredWaiting = 5+4 = 9.
        var (leaderService, leaderTrajectory, leaderNetwork) = BuildLeader();
        var followerService = FollowerService();
        var followerTrajectory = BuildFollowerTrajectory(sIsThrough: false, followerEntryBeforeForcedStop: 98, recoveryAtE: 4);
        var followerNetwork = BuildFollowerNetwork();

        var result = BuildCalculator().ComputeHeadway(
            HeadwayConflict(), leaderService, leaderTrajectory, leaderNetwork,
            followerService, followerTrajectory, followerNetwork, candidateLocalIndex: 1);

        Assert.False(result.IsForcedStop);
        Assert.Equal(0, result.DecelerationPenaltyMinutes);
        Assert.Equal(0, result.AccelerationPenaltyMinutes);
        Assert.Equal(9, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void H4_StructuralAloneAlreadyEnough_RecoveryBetweenIgnored_NotAddedOnTop()
    {
        // deficit goc = 2, structural = 3 (du), recovery giua S va E = 4 -> RequiredWaiting VAN phai = 0,
        // KHONG duoc cong them 4 (day chinh la ly do can dieu kien RemainingDeficit==0 ? 0 : ...).
        var (leaderService, leaderTrajectory, leaderNetwork) = BuildLeader();
        var followerService = FollowerService();
        var followerTrajectory = BuildFollowerTrajectory(sIsThrough: true, followerEntryBeforeForcedStop: 101, recoveryAtE: 4);
        var followerNetwork = BuildFollowerNetwork();

        var result = BuildCalculator().ComputeHeadway(
            HeadwayConflict(), leaderService, leaderTrajectory, leaderNetwork,
            followerService, followerTrajectory, followerNetwork, candidateLocalIndex: 1);

        Assert.Equal(0, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void ForcedStopMutator_Headway_SetsForcedHeadwayStopType_AccelDecelCorrect()
    {
        var followerTrajectory = BuildFollowerTrajectory(sIsThrough: true, followerEntryBeforeForcedStop: 101, recoveryAtE: 0);

        var preview = ForcedStopMutator.Preview(followerTrajectory, localIndex: 1, ForcedStopReason.Headway, new RunningTimeRules());

        Assert.Equal(StopType.ForcedHeadway, preview.Entries[1].StopType);
        Assert.NotEqual(StopType.ForcedMeet, preview.Entries[1].StopType);
        Assert.NotEqual(StopType.ForcedOvertake, preview.Entries[1].StopType);
        // Nguyen trajectory dau vao khong bi mutate (preview la ban sao).
        Assert.Equal(StopType.Through, followerTrajectory.Entries[1].StopType);
    }

    [Fact]
    public void LeaderExitTime_ReadDirectlyFromOccupation_NotFromConflictEndTime()
    {
        // Conflict.ConflictEndTimeMinutes/RequiredHeadwayMinutes bi set SAI lech (khong khop LeaderExitTime
        // thuc te = 100) de xac nhan cong thuc KHONG con phu thuoc 2 truong nay lam nguon du lieu vat ly.
        var (leaderService, leaderTrajectory, leaderNetwork) = BuildLeader();
        var followerService = FollowerService();
        var followerTrajectory = BuildFollowerTrajectory(sIsThrough: true, followerEntryBeforeForcedStop: 101, recoveryAtE: 0);
        var followerNetwork = BuildFollowerNetwork();

        var misleadingConflict = HeadwayConflict() with { ConflictEndTimeMinutes = 99999, HeadwayDeficitMinutes = -12345 };

        var result = BuildCalculator().ComputeHeadway(
            misleadingConflict, leaderService, leaderTrajectory, leaderNetwork,
            followerService, followerTrajectory, followerNetwork, candidateLocalIndex: 1);

        // Ket qua phai giong het H1 (deficit thuc = 2, structural = 3 -> RequiredWaiting = 0) - khong bi
        // anh huong boi ConflictEndTimeMinutes/HeadwayDeficitMinutes sai lech tren Conflict.
        Assert.Equal(0, result.RequiredWaitingMinutes);
    }

    // ===================== MEET (muc 7.1, review lan 5/6) =====================
    // ComputeMeet KHONG dung SectionTimingResolver.GetEntryTime/GetExitTime (khong can RailwayNetwork) -
    // candidate S la 1 local index cu the, doc thang qua GetArrivalAtLocalIndex/GetDepartureAtLocalIndex.
    // Route dung chung: Origin(seq1) - Candidate(seq2, index1) - Dest(seq3).

    private static TrainService MeetService(string id, int journeyTimeMinutes = 10000) => new(
        serviceId: id, trainCode: id, direction: Direction.Inbound,
        originStationSequence: 1, destinationStationSequence: 3,
        fixedDepartureTimeOfDayMinutes: 0, journeyTimeMinutes: journeyTimeMinutes, priority: 1,
        stopRequirements: new List<TrainStopRequirement>());

    private static TrainServiceTrajectory MeetTrajectory(string serviceId, bool candidateIsThrough, int arrivalAtCandidate, int stopDuration)
    {
        var departureAtCandidate = candidateIsThrough ? arrivalAtCandidate : arrivalAtCandidate + stopDuration;
        return new TrainServiceTrajectory
        {
            ServiceId = serviceId,
            Entries = new List<TimetableEntry>
            {
                new() { StationSequence = 1, ArrivalTimeMinutes = null, DepartureTimeMinutes = 0, StopType = StopType.Through, RunningTimeFromPrevMinutes = 0, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 },
                new()
                {
                    StationSequence = 2, ArrivalTimeMinutes = arrivalAtCandidate, DepartureTimeMinutes = departureAtCandidate,
                    StopType = candidateIsThrough ? StopType.Through : StopType.Passenger,
                    RunningTimeFromPrevMinutes = arrivalAtCandidate, RecoveryTimeFromPrevMinutes = 0,
                    StopDurationMinutes = candidateIsThrough ? 0 : stopDuration, CumulativeInsertedDelayMinutes = 0
                },
                new() { StationSequence = 3, ArrivalTimeMinutes = departureAtCandidate + 20, DepartureTimeMinutes = null, StopType = StopType.Through, RunningTimeFromPrevMinutes = 20, RecoveryTimeFromPrevMinutes = 0, StopDurationMinutes = 0, CumulativeInsertedDelayMinutes = 0 }
            }
        };
    }

    private static Conflict MeetConflict(string serviceIdA, int cycleIndexA, string serviceIdB, int cycleIndexB, int requiredHeadway = 3) => new()
    {
        ConflictId = "M1", Type = ConflictType.MEET, ConstraintKind = ConstraintKind.SectionOverlap,
        ServiceIdA = serviceIdA, CycleIndexA = cycleIndexA, ServiceIdB = serviceIdB, CycleIndexB = cycleIndexB,
        SectionId = "SEC-MEET", ConflictStartTimeMinutes = 0, ConflictEndTimeMinutes = 0,
        RequiredHeadwayMinutes = requiredHeadway, ActualGapMinutes = 0, HeadwayDeficitMinutes = 0
    };

    private static CandidateSolution MeetCandidate(Conflict conflict, string trainToWait, int waitIndex, int otherIndex) => new()
    {
        Conflict = conflict, CandidateStationCode = 999, TrainToWait = trainToWait,
        TrainToWaitLocalStationIndex = waitIndex, OtherTrainLocalStationIndex = otherIndex
    };

    [Fact]
    public void Meet_AWaits_NoForcedStop_RequiredWaitingMatchesArithmetic()
    {
        // P(=B) den candidate luc t=100; headway=3 -> EarliestSafeDeparture=103.
        // W(=A) da la Passenger stop san (khong ForcedStop): arrival=90, stop=2 -> NaturalDeparture=92.
        // RequiredWaiting = max(0, 103-92) = 11.
        var conflict = MeetConflict("A", 0, "B", 0);
        var serviceA = MeetService("A");
        var trajA = MeetTrajectory("A", candidateIsThrough: false, arrivalAtCandidate: 90, stopDuration: 2);
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: 100, stopDuration: 1);

        var candidate = MeetCandidate(conflict, "A", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceA, trajA, serviceB, trajB, candidate);

        Assert.False(result.IsForcedStop);
        Assert.Equal(0, result.DecelerationPenaltyMinutes);
        Assert.Equal(0, result.AccelerationPenaltyMinutes);
        Assert.Equal(11, result.RequiredWaitingMinutes);
        Assert.Equal(11, result.TotalAdditionalTimeMinutes);
        Assert.True(result.IsFeasible);
    }

    [Fact]
    public void Meet_BWaits_SymmetricOfAWaits_ComputesIndependently()
    {
        // Y HET so lieu vat ly nhu Meet_AWaits nhung doi vai W/P - phai ra CUNG ket qua (11), khong
        // hard-code "A luon la ben cho" hay "A luon la Other".
        var conflict = MeetConflict("A", 0, "B", 0);
        var serviceA = MeetService("A");
        var trajA = MeetTrajectory("A", candidateIsThrough: false, arrivalAtCandidate: 100, stopDuration: 1); // gio la P
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: 90, stopDuration: 2); // gio la W

        var candidate = MeetCandidate(conflict, "B", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceB, trajB, serviceA, trajA, candidate);

        Assert.Equal(11, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void Meet_OtherTrainCycleIndexPlusOne_AddsExactly1440()
    {
        // P (=B) o CycleIndexB=+1 - phan biet ro 3 dai luong, KHONG duoc goi 1543 la "ArrivalP":
        //   ArrivalP(instance)      = canonicalArrivalP + cycleIndexP * 1440 = 100 + 1440 = 1540
        //   EarliestSafeDepartureW  = ArrivalP(instance) + RequiredHeadway   = 1540 + 3    = 1543
        //   NaturalDepartureW       = 92 (khong ForcedStop)
        //   RequiredWaiting         = EarliestSafeDepartureW - NaturalDepartureW = 1543 - 92 = 1451
        const int canonicalArrivalP = 100;
        const int cycleIndexP = 1;
        const int requiredHeadway = 3;
        const int naturalDepartureW = 92;

        var arrivalPInstance = canonicalArrivalP + cycleIndexP * TrainService.CycleLengthMinutes;
        var earliestSafeDepartureW = arrivalPInstance + requiredHeadway;
        var expectedRequiredWaiting = earliestSafeDepartureW - naturalDepartureW;

        var conflict = MeetConflict("A", 0, "B", cycleIndexP, requiredHeadway);
        var serviceA = MeetService("A");
        var trajA = MeetTrajectory("A", candidateIsThrough: false, arrivalAtCandidate: 90, stopDuration: 2); // W, NaturalDepartureW=92
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: canonicalArrivalP, stopDuration: 1); // P, canonical Arrival

        var candidate = MeetCandidate(conflict, "A", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceA, trajA, serviceB, trajB, candidate);

        Assert.Equal(expectedRequiredWaiting, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void ForcedStopMutator_Meet_SetsForcedMeetStopType_AccelDecelCorrect()
    {
        var trajectory = MeetTrajectory("A", candidateIsThrough: true, arrivalAtCandidate: 95, stopDuration: 0);

        var preview = ForcedStopMutator.Preview(trajectory, localIndex: 1, ForcedStopReason.Meet, new RunningTimeRules());

        Assert.Equal(StopType.ForcedMeet, preview.Entries[1].StopType);
        Assert.NotEqual(StopType.ForcedHeadway, preview.Entries[1].StopType);
        Assert.NotEqual(StopType.ForcedOvertake, preview.Entries[1].StopType);
        Assert.Equal(StopType.Through, trajectory.Entries[1].StopType); // ban goc khong bi mutate
    }

    [Fact]
    public void Meet_ForcedStop_ReducesRequiredWaiting_ButDoesNotEliminateIt()
    {
        // Khong ForcedStop, NaturalDeparture se la 95 -> RequiredWaiting tho = max(0,100-95) = 5.
        // Co ForcedStop (candidate dang Through): NaturalDeparture tren preview = 95+decel(1) = 96
        // -> RequiredWaiting THUC = max(0,100-96) = 4 (giam 1, dung bang decel, chua ve 0).
        var conflict = MeetConflict("A", 0, "B", 0);
        var serviceA = MeetService("A");
        var trajA = MeetTrajectory("A", candidateIsThrough: true, arrivalAtCandidate: 95, stopDuration: 0);
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: 97, stopDuration: 1);

        var candidate = MeetCandidate(conflict, "A", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceA, trajA, serviceB, trajB, candidate);

        Assert.True(result.IsForcedStop);
        Assert.Equal(1, result.DecelerationPenaltyMinutes);
        Assert.Equal(2, result.AccelerationPenaltyMinutes);
        Assert.Equal(4, result.RequiredWaitingMinutes);
        Assert.Equal(4 + 1 + 2, result.TotalAdditionalTimeMinutes);
    }

    [Fact]
    public void Meet_ForcedStop_EliminatesRequiredWaitingEntirely()
    {
        // deficit tho = 100-99 = 1; decel = 1 vua du trung khop -> RequiredWaiting = 0.
        var conflict = MeetConflict("A", 0, "B", 0);
        var serviceA = MeetService("A");
        var trajA = MeetTrajectory("A", candidateIsThrough: true, arrivalAtCandidate: 99, stopDuration: 0);
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: 97, stopDuration: 1);

        var candidate = MeetCandidate(conflict, "A", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceA, trajA, serviceB, trajB, candidate);

        Assert.True(result.IsForcedStop);
        Assert.Equal(0, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void Meet_NotEnoughForwardSlack_IsInfeasible()
    {
        // Giong Meet_AWaits (RequiredWaiting=11) nhung journeyTimeMinutes chi vua du minimum -> ForwardSlack=0.
        var conflict = MeetConflict("A", 0, "B", 0);
        var serviceA = MeetService("A", journeyTimeMinutes: 112); // = 92 (departure tai candidate) + 20 (remaining)
        var trajA = MeetTrajectory("A", candidateIsThrough: false, arrivalAtCandidate: 90, stopDuration: 2);
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: 100, stopDuration: 1);

        var candidate = MeetCandidate(conflict, "A", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceA, trajA, serviceB, trajB, candidate);

        Assert.Equal(11, result.RequiredWaitingMinutes);
        Assert.False(result.IsFeasible);
        Assert.NotNull(result.ViolatedConstraint);
    }

    [Fact]
    public void Meet_NaturalDepartureAlreadySufficient_RequiredWaitingIsZero()
    {
        // W da dung san (khong ForcedStop) den 100, stop=5 -> NaturalDeparture=105.
        // P den candidate luc 90 -> EarliestSafeDeparture=93. RequiredWaiting = max(0,93-105) = 0.
        var conflict = MeetConflict("A", 0, "B", 0);
        var serviceA = MeetService("A");
        var trajA = MeetTrajectory("A", candidateIsThrough: false, arrivalAtCandidate: 100, stopDuration: 5);
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: 90, stopDuration: 1);

        var candidate = MeetCandidate(conflict, "A", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceA, trajA, serviceB, trajB, candidate);

        Assert.False(result.IsForcedStop);
        Assert.Equal(0, result.RequiredWaitingMinutes);
    }

    [Fact]
    public void Meet_DoesNotUseConflictStartOrEndTime_ForArrivalOfP()
    {
        // Giong het Meet_AWaits (ket qua dung phai la 11) nhung ConflictStartTime/EndTime bi set SAI lech -
        // xac nhan ComputeMeet khong doc 2 truong nay de suy ArrivalOfP.
        var conflict = MeetConflict("A", 0, "B", 0) with { ConflictStartTimeMinutes = 99999, ConflictEndTimeMinutes = -99999 };
        var serviceA = MeetService("A");
        var trajA = MeetTrajectory("A", candidateIsThrough: false, arrivalAtCandidate: 90, stopDuration: 2);
        var serviceB = MeetService("B");
        var trajB = MeetTrajectory("B", candidateIsThrough: false, arrivalAtCandidate: 100, stopDuration: 1);

        var candidate = MeetCandidate(conflict, "A", waitIndex: 1, otherIndex: 1);

        var result = BuildCalculator().ComputeMeet(conflict, serviceA, trajA, serviceB, trajB, candidate);

        Assert.Equal(11, result.RequiredWaitingMinutes);
    }
}
