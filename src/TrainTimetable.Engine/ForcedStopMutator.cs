using TrainTimetable.Configuration;
using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>
/// Ly do mot ga Through duoc chuyen thanh forced-stop - quyet dinh StopType nao duoc gan (muc 7.0), KHONG
/// anh huong logic accel/decel/propagation (dung chung cho ca 3 reason).
/// </summary>
public enum ForcedStopReason
{
    Meet,
    Headway,
    Overtake
}

/// <summary>
/// Structural stop mutation (muc 7.0): chuyen mot ga tu Through sang co dung, KHONG phu thuoc headway voi
/// tau kia. Luon tra ve MOT TRAJECTORY MOI (preview), khong mutate tham so dau vao - RequiredShiftCalculator
/// (muc 7) phai giu nguyen tac "chi tinh, khong mutate trajectory that".
/// </summary>
public static class ForcedStopMutator
{
    public static bool IsForcedStop(TrainServiceTrajectory trajectory, int localIndex) =>
        trajectory.Entries[localIndex].StopType == StopType.Through;

    public static TrainServiceTrajectory Preview(
        TrainServiceTrajectory trajectory, int localIndex, ForcedStopReason reason, IRunningTimeRules runningTimeRules)
    {
        var current = trajectory.Entries[localIndex];
        if (current.StopType != StopType.Through)
        {
            throw new InvalidOperationException(
                $"PreviewApplyForcedStop chi ap dung cho ga dang Through - ga tai local index {localIndex} " +
                $"da co StopType={current.StopType}.");
        }

        var stopType = reason switch
        {
            ForcedStopReason.Meet => StopType.ForcedMeet,
            ForcedStopReason.Headway => StopType.ForcedHeadway,
            ForcedStopReason.Overtake => StopType.ForcedOvertake,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

        var decel = runningTimeRules.DecelerationPenaltyMinutes;
        var accel = runningTimeRules.AccelerationPenaltyMinutes;

        var entries = trajectory.Entries.ToList();

        entries[localIndex] = current with
        {
            StopType = stopType,
            DecelerationApplied = true,
            RunningTimeFromPrevMinutes = current.RunningTimeFromPrevMinutes + decel,
            ArrivalTimeMinutes = current.ArrivalTimeMinutes!.Value + decel,
            DepartureTimeMinutes = current.DepartureTimeMinutes.HasValue
                ? current.DepartureTimeMinutes.Value + decel
                : null
        };

        if (localIndex + 1 >= entries.Count)
        {
            // S la ga cuoi hanh trinh - khong co khu gian S->S+1, khong co AccelerationPenalty de cong.
            return trajectory with { Entries = entries };
        }

        var next = entries[localIndex + 1];
        var carry = decel + accel;
        entries[localIndex + 1] = next with
        {
            AccelerationApplied = true,
            RunningTimeFromPrevMinutes = next.RunningTimeFromPrevMinutes + accel,
            ArrivalTimeMinutes = next.ArrivalTimeMinutes!.Value + carry,
            DepartureTimeMinutes = next.DepartureTimeMinutes.HasValue
                ? next.DepartureTimeMinutes.Value + carry
                : null
        };

        for (var i = localIndex + 2; i < entries.Count && carry > 0; i++)
        {
            var entry = entries[i];
            var absorbed = Math.Min(entry.RecoveryTimeFromPrevMinutes, carry);
            carry -= absorbed;

            entries[i] = entry with
            {
                ArrivalTimeMinutes = entry.ArrivalTimeMinutes!.Value + carry,
                DepartureTimeMinutes = entry.DepartureTimeMinutes.HasValue
                    ? entry.DepartureTimeMinutes.Value + carry
                    : null,
                RunningTimeFromPrevMinutes = entry.RunningTimeFromPrevMinutes - absorbed,
                RecoveryTimeFromPrevMinutes = entry.RecoveryTimeFromPrevMinutes - absorbed
            };
        }

        return trajectory with { Entries = entries };
    }
}
