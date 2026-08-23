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

    private static void RequireOwnTrajectory(TrainService service, TrainServiceTrajectory trajectory)
    {
        if (trajectory.ServiceId != service.ServiceId)
        {
            throw new ArgumentException("Trajectory khong thuoc TrainService nay.");
        }
    }
}
