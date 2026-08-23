using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

public sealed record PropagationResult
{
    public required bool IsFeasible { get; init; }
    public TrainServiceTrajectory? NewTrajectory { get; init; }

    /// <summary>Chi > 0 khi IsFeasible = false: phan delay vuot qua ForwardSlack tai diem chen.</summary>
    public int ResidualDelayMinutes { get; init; }
}

/// <summary>
/// Chen mot khoang delay tai mot ga tren trajectory va lan truyen ve phia dich, hap thu dan bang
/// RecoveryTimeFromPrev con lai o cac ga phia sau (xem thiet ke muc 8). Feasibility duoc quyet dinh
/// truoc bang ForwardSlack (muc 4.1/7.3-dieu-kien-6): neu delay &lt;= ForwardSlack thi phep lan truyen
/// nay LUON hap thu het truoc khi cham FixedArrivalTime - day la invariant da chung minh trong thiet ke,
/// khong phai gia dinh.
/// </summary>
public sealed class TrajectoryPropagator
{
    private readonly BufferCalculator _bufferCalculator;

    public TrajectoryPropagator(BufferCalculator bufferCalculator)
    {
        _bufferCalculator = bufferCalculator;
    }

    public PropagationResult InsertDelay(
        TrainService service, TrainServiceTrajectory trajectory, int stationSequence, int delayMinutes)
    {
        if (delayMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayMinutes), delayMinutes, "Delay khong the am.");
        }

        if (stationSequence == service.OriginStationSequence)
        {
            throw new ArgumentException(
                "Khong the chen delay tai ga xuat phat - FixedDepartureTime la hard constraint bat bien.");
        }

        if (delayMinutes == 0)
        {
            return new PropagationResult { IsFeasible = true, NewTrajectory = trajectory, ResidualDelayMinutes = 0 };
        }

        var forwardSlack = _bufferCalculator.ComputeForwardSlackMinutes(service, trajectory, stationSequence);
        if (delayMinutes > forwardSlack)
        {
            return new PropagationResult
            {
                IsFeasible = false,
                ResidualDelayMinutes = delayMinutes - forwardSlack
            };
        }

        var entries = trajectory.Entries;
        var insertIndex = trajectory.IndexOf(stationSequence);
        var insertEntry = entries[insertIndex];
        if (insertEntry.DepartureTimeMinutes is not { } insertDeparture)
        {
            throw new ArgumentException(
                $"Ga {stationSequence} khong co DepartureTime - khong the chen delay tai ga cuoi hanh trinh.");
        }

        var newEntries = new List<TimetableEntry>(entries.Count);
        for (var i = 0; i < insertIndex; i++)
        {
            newEntries.Add(entries[i]);
        }

        newEntries.Add(insertEntry with
        {
            DepartureTimeMinutes = insertDeparture + delayMinutes,
            CumulativeInsertedDelayMinutes = insertEntry.CumulativeInsertedDelayMinutes + delayMinutes
        });

        var carry = delayMinutes;
        for (var i = insertIndex + 1; i < entries.Count; i++)
        {
            var entry = entries[i];
            var absorbed = Math.Min(entry.RecoveryTimeFromPrevMinutes, carry);
            carry -= absorbed;

            newEntries.Add(entry with
            {
                ArrivalTimeMinutes = entry.ArrivalTimeMinutes!.Value + carry,
                DepartureTimeMinutes = entry.DepartureTimeMinutes.HasValue
                    ? entry.DepartureTimeMinutes.Value + carry
                    : null,
                RunningTimeFromPrevMinutes = entry.RunningTimeFromPrevMinutes - absorbed,
                RecoveryTimeFromPrevMinutes = entry.RecoveryTimeFromPrevMinutes - absorbed,
                CumulativeInsertedDelayMinutes = entry.CumulativeInsertedDelayMinutes + delayMinutes
            });
        }

        return new PropagationResult
        {
            IsFeasible = true,
            NewTrajectory = trajectory with { Entries = newEntries },
            ResidualDelayMinutes = 0
        };
    }
}
