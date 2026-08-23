using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

public sealed record BufferCalculationResult
{
    public required int JourneyTimeMinutes { get; init; }
    public required int MinimumJourneyTimeMinutes { get; init; }
    public required int TotalBufferMinutes { get; init; }

    public bool IsFeasible => TotalBufferMinutes >= 0;
}

/// <summary>
/// Phan ke toan tuong minh cua TotalBuffer(T) tai MOT thoi diem cu the tren lich su propagation (xem
/// thiet ke muc 4/15.10): TotalBuffer = AllocatedRecovery + ConsumedBuffer + UnallocatedBuffer, dung
/// cho moi trajectory sinh ra tu MinimumTimetableBuilder roi qua 0 hoac nhieu lan
/// TrajectoryPropagator.InsertDelay. Khac voi BufferCalculationResult.TotalBufferMinutes (chi dung
/// dung khi goi tren chinh minimum trajectory ban dau), BufferState.TotalBufferMinutes la HANG SO bat
/// bien qua moi lan InsertDelay - an toan de goi tren bat ky trajectory nao cua cung TrainService.
/// </summary>
public sealed record BufferState
{
    public required int TotalBufferMinutes { get; init; }
    public required int AllocatedRecoveryMinutes { get; init; }
    public required int ConsumedBufferMinutes { get; init; }
    public required int UnallocatedBufferMinutes { get; init; }
}

/// <summary>
/// TotalBuffer(T) = JourneyTime(T) - MinimumJourneyTime(T), va ForwardSlack(T,k) - tran cung cho
/// luong delay co the chen tai ga k ma khong vi pham FixedArrivalTime (xem thiet ke muc 3, 4.1).
/// </summary>
public sealed class BufferCalculator
{
    public BufferCalculationResult Calculate(TrainService service, TrainServiceTrajectory minimumTrajectory)
    {
        RequireOwnTrajectory(service, minimumTrajectory);

        var lastEntry = minimumTrajectory.Last;
        if (lastEntry.ArrivalTimeMinutes is not { } destinationArrival)
        {
            throw new ArgumentException("Trajectory khong hop le: ga cuoi khong co ArrivalTime.");
        }

        var minimumJourneyTime = destinationArrival - service.FixedDepartureTimeOfDayMinutes;
        var journeyTime = service.JourneyTimeMinutes;

        return new BufferCalculationResult
        {
            JourneyTimeMinutes = journeyTime,
            MinimumJourneyTimeMinutes = minimumJourneyTime,
            TotalBufferMinutes = journeyTime - minimumJourneyTime
        };
    }

    /// <summary>
    /// ForwardSlack(T,k) = FixedArrivalTime(T) - CurrentDeparture(T,k) - MinimumRemainingJourneyTime(T,k->dest).
    /// MinimumRemainingJourneyTime duoc suy ra tu chinh cac entry hien co (RunningTimeFromPrev tru phan
    /// RecoveryTimeFromPrev da cay, cong StopDuration) - khong dung lai RailwayNetwork.
    /// </summary>
    public int ComputeForwardSlackMinutes(TrainService service, TrainServiceTrajectory trajectory, int stationSequence)
    {
        RequireOwnTrajectory(service, trajectory);

        var index = trajectory.IndexOf(stationSequence);
        var departureAtStation = trajectory.Entries[index].DepartureTimeMinutes
            ?? throw new ArgumentException($"Ga {stationSequence} khong co DepartureTime (co the la ga cuoi hanh trinh).");

        var minimumRemaining = 0;
        for (var i = index + 1; i < trajectory.Entries.Count; i++)
        {
            var entry = trajectory.Entries[i];
            minimumRemaining += entry.RunningTimeFromPrevMinutes - entry.RecoveryTimeFromPrevMinutes
                + entry.StopDurationMinutes;
        }

        return service.FixedArrivalTimeMinutes - departureAtStation - minimumRemaining;
    }

    /// <summary>
    /// Tinh BufferState tai trang thai HIEN TAI cua trajectory (co the la minimum trajectory ban dau,
    /// hoac ket qua sau 0..n lan TrajectoryPropagator.InsertDelay). Khong doc BufferCalculationResult
    /// vi TotalBufferMinutes o do phu thuoc Arrival(destination) hien tai (se SAI ne u trajectory da bi
    /// InsertDelay lam dich); TotalBufferMinutes o day duoc tinh lai tu tong
    /// (RunningTimeFromPrev - RecoveryTimeFromPrev + StopDuration) tren toan bo trajectory - dai luong
    /// nay BAT BIEN qua moi lan InsertDelay (moi don vi Recovery bi tieu thi Running cung giam dung bang
    /// do, xem TrajectoryPropagator.InsertDelay), nen la mot hang so dung cua rieng TrainService.
    /// </summary>
    public BufferState ComputeBufferState(TrainService service, TrainServiceTrajectory trajectory)
    {
        RequireOwnTrajectory(service, trajectory);

        var trueMinimumJourneyTime = 0;
        var allocatedRecovery = 0;
        for (var i = 1; i < trajectory.Entries.Count; i++)
        {
            var entry = trajectory.Entries[i];
            trueMinimumJourneyTime += entry.RunningTimeFromPrevMinutes - entry.RecoveryTimeFromPrevMinutes
                + entry.StopDurationMinutes;
            allocatedRecovery += entry.RecoveryTimeFromPrevMinutes;
        }

        var totalBuffer = service.JourneyTimeMinutes - trueMinimumJourneyTime;
        var consumedBuffer = trajectory.Last.CumulativeInsertedDelayMinutes;

        return new BufferState
        {
            TotalBufferMinutes = totalBuffer,
            AllocatedRecoveryMinutes = allocatedRecovery,
            ConsumedBufferMinutes = consumedBuffer,
            UnallocatedBufferMinutes = totalBuffer - allocatedRecovery - consumedBuffer
        };
    }

    private static void RequireOwnTrajectory(TrainService service, TrainServiceTrajectory trajectory)
    {
        if (trajectory.ServiceId != service.ServiceId)
        {
            throw new ArgumentException("Trajectory khong thuoc TrainService nay.");
        }
    }
}
