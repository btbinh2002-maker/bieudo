using TrainTimetable.Configuration;
using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>Ket qua tinh toan cua RequiredShiftCalculator (muc 7.0) - chi tinh, khong tu mutate trajectory that.</summary>
public sealed record RequiredShiftResult
{
    public required bool IsFeasible { get; init; }
    public required bool IsForcedStop { get; init; }
    public required int DecelerationPenaltyMinutes { get; init; }
    public required int AccelerationPenaltyMinutes { get; init; }
    public required int RequiredWaitingMinutes { get; init; }
    public required int TotalAdditionalTimeMinutes { get; init; }
    public string? ViolatedConstraint { get; init; }
}

/// <summary>
/// Tinh RequiredShift cho tung Conflict.Type (muc 7). MEET (muc 7.1) va HEADWAY (muc 7.2) da implement -
/// OVERTAKE (muc 7.3) la viec cua ConflictAnalyzer (Phase 6, ngoai pham vi hien tai).
/// </summary>
public sealed class RequiredShiftCalculator
{
    private readonly IRunningTimeRules _runningTimeRules;
    private readonly BufferCalculator _bufferCalculator;

    public RequiredShiftCalculator(IRunningTimeRules runningTimeRules, BufferCalculator bufferCalculator)
    {
        _runningTimeRules = runningTimeRules;
        _bufferCalculator = bufferCalculator;
    }

    /// <summary>
    /// HEADWAY tai ga S (muc 7.2): tau cho (Follower) = candidate.TrainToWait, tau dan (Leader) doc
    /// truc tiep qua conflict.LeaderServiceId/LeaderCycleIndex (muc 1.7) - khong tu suy dien qua A/B.
    /// </summary>
    public RequiredShiftResult ComputeHeadway(
        Conflict conflict,
        TrainService leaderService, TrainServiceTrajectory leaderTrajectory, RailwayNetwork leaderNetwork,
        TrainService followerService, TrainServiceTrajectory followerTrajectory, RailwayNetwork followerNetwork,
        int candidateLocalIndex)
    {
        if (conflict.Type != ConflictType.HEADWAY)
        {
            throw new ArgumentException($"ComputeHeadway chi ap dung cho Type=HEADWAY, khong phai {conflict.Type}.");
        }

        var isForcedStop = ForcedStopMutator.IsForcedStop(followerTrajectory, candidateLocalIndex);

        var preview = isForcedStop
            ? ForcedStopMutator.Preview(followerTrajectory, candidateLocalIndex, ForcedStopReason.Headway, _runningTimeRules)
            : followerTrajectory;

        var decel = isForcedStop ? _runningTimeRules.DecelerationPenaltyMinutes : 0;
        var accel = isForcedStop ? _runningTimeRules.AccelerationPenaltyMinutes : 0;

        var leaderExitTime = SectionTimingResolver.GetExitTime(
            leaderService, leaderTrajectory, leaderNetwork, conflict.SectionId, conflict.LeaderCycleIndex);

        var previewFollowerEntry = SectionTimingResolver.GetEntryTime(
            followerService, preview, followerNetwork, conflict.SectionId, conflict.FollowerCycleIndex);

        var requiredSafeEntry = leaderExitTime + conflict.RequiredHeadwayMinutes;
        var remainingDeficit = Math.Max(0, requiredSafeEntry - previewFollowerEntry);

        var entryIndex = SectionTimingResolver.GetEntryIndex(preview, followerNetwork, conflict.SectionId);
        var recoveryRemainingBetween = 0;
        for (var j = candidateLocalIndex + 1; j <= entryIndex; j++)
        {
            recoveryRemainingBetween += preview.Entries[j].RecoveryTimeFromPrevMinutes;
        }

        var requiredWaiting = remainingDeficit == 0 ? 0 : remainingDeficit + recoveryRemainingBetween;
        var totalAdditional = requiredWaiting + decel + accel;

        var forwardSlack = _bufferCalculator.ComputeForwardSlackMinutes(
            followerService, followerTrajectory, followerTrajectory.Entries[candidateLocalIndex].StationSequence);
        var isFeasible = totalAdditional <= forwardSlack;

        return new RequiredShiftResult
        {
            IsFeasible = isFeasible,
            IsForcedStop = isForcedStop,
            DecelerationPenaltyMinutes = decel,
            AccelerationPenaltyMinutes = accel,
            RequiredWaitingMinutes = requiredWaiting,
            TotalAdditionalTimeMinutes = totalAdditional,
            ViolatedConstraint = isFeasible ? null : "ForwardSlack exceeded (muc 7.4 dieu kien 6)"
        };
    }

    /// <summary>
    /// MEET tai ga S (muc 7.1): W = candidate.TrainToWait, P = conflict.OtherServiceId(W) (muc 1.7 -
    /// khong tu suy dien qua A/B). CandidateGenerator (muc 6.1 buoc 5) da guarantee Departure(W,S) va
    /// Arrival(P,S) khac null - KHONG fallback, KHONG tu sua candidate o day (doc theo dung thiet ke).
    /// </summary>
    public RequiredShiftResult ComputeMeet(
        Conflict conflict,
        TrainService waitingService, TrainServiceTrajectory waitingTrajectory,
        TrainService otherService, TrainServiceTrajectory otherTrajectory,
        CandidateSolution candidate)
    {
        if (conflict.Type != ConflictType.MEET)
        {
            throw new ArgumentException($"ComputeMeet chi ap dung cho Type=MEET, khong phai {conflict.Type}.");
        }

        if (waitingService.ServiceId != candidate.TrainToWait)
        {
            throw new ArgumentException(
                $"waitingService ({waitingService.ServiceId}) khong khop candidate.TrainToWait ({candidate.TrainToWait}).");
        }

        var otherServiceId = conflict.OtherServiceId(candidate.TrainToWait);
        if (otherService.ServiceId != otherServiceId)
        {
            throw new ArgumentException(
                $"otherService ({otherService.ServiceId}) khong khop conflict.OtherServiceId ({otherServiceId}).");
        }

        var s = candidate.TrainToWaitLocalStationIndex;
        var sp = candidate.OtherTrainLocalStationIndex
            ?? throw new ArgumentException("CandidateSolution cho MEET phai co OtherTrainLocalStationIndex.");

        var cycleW = conflict.CycleIndexOf(candidate.TrainToWait);
        var cycleP = conflict.CycleIndexOf(otherServiceId);

        var isForcedStop = ForcedStopMutator.IsForcedStop(waitingTrajectory, s);

        var preview = isForcedStop
            ? ForcedStopMutator.Preview(waitingTrajectory, s, ForcedStopReason.Meet, _runningTimeRules)
            : waitingTrajectory;

        var decel = isForcedStop ? _runningTimeRules.DecelerationPenaltyMinutes : 0;
        var accel = isForcedStop ? _runningTimeRules.AccelerationPenaltyMinutes : 0;

        var arrivalOfP = SectionTimingResolver.GetArrivalAtLocalIndex(otherTrajectory, sp, cycleP);
        var earliestSafeDeparture = arrivalOfP + conflict.RequiredHeadwayMinutes;

        var naturalDeparture = SectionTimingResolver.GetDepartureAtLocalIndex(preview, s, cycleW);

        var requiredWaiting = Math.Max(0, earliestSafeDeparture - naturalDeparture);
        var totalAdditional = requiredWaiting + decel + accel;

        var forwardSlack = _bufferCalculator.ComputeForwardSlackMinutes(
            waitingService, waitingTrajectory, waitingTrajectory.Entries[s].StationSequence);
        var isFeasible = totalAdditional <= forwardSlack;

        return new RequiredShiftResult
        {
            IsFeasible = isFeasible,
            IsForcedStop = isForcedStop,
            DecelerationPenaltyMinutes = decel,
            AccelerationPenaltyMinutes = accel,
            RequiredWaitingMinutes = requiredWaiting,
            TotalAdditionalTimeMinutes = totalAdditional,
            ViolatedConstraint = isFeasible ? null : "ForwardSlack exceeded (muc 7.4 dieu kien 6)"
        };
    }
}
