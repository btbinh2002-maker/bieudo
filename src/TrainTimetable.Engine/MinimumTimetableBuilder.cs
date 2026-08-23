using TrainTimetable.Configuration;
using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>
/// Dung free-running/minimum trajectory cho mot TrainService: chi dung minimum running time,
/// dung bat buoc (passenger/technical), va acceleration/deceleration penalty - CHUA co recovery
/// time, CHUA xet xung dot voi tau khac. Arrival tai Destination vi vay thuong SOM HON
/// FixedArrivalTime dung bang TotalBuffer (xem BufferCalculator).
/// </summary>
public sealed class MinimumTimetableBuilder
{
    private readonly IStopRules _stopRules;
    private readonly IRunningTimeRules _runningTimeRules;

    public MinimumTimetableBuilder(IStopRules stopRules, IRunningTimeRules runningTimeRules)
    {
        _stopRules = stopRules;
        _runningTimeRules = runningTimeRules;
    }

    public TrainServiceTrajectory Build(TrainService service, RailwayNetwork network)
    {
        var stationSequences = network.GetStationSequencesOnRoute(
            service.OriginStationSequence, service.DestinationStationSequence, service.Direction);

        if (stationSequences.Count < 2)
        {
            throw new ArgumentException("Route phai co it nhat 2 ga (Origin va Destination).");
        }

        var entries = new List<TimetableEntry>(stationSequences.Count);

        // Tau xuat phat tu trang thai dung tai ga dau -> khu gian dau tien luon co AccelerationPenalty.
        var previousWasStopped = true;
        int? previousDeparture = null;

        for (var i = 0; i < stationSequences.Count; i++)
        {
            var stationSeq = stationSequences[i];
            var isOrigin = i == 0;
            var isDestination = i == stationSequences.Count - 1;

            var stopRequirement = service.GetStopRequirement(stationSeq);
            var requiresPassenger = stopRequirement?.RequiresPassengerStop ?? false;
            var requiresTechnical = stopRequirement?.RequiresTechnicalStop ?? false;

            // Ga cuoi hanh trinh luon phai dung (tau ket thuc tai day) du khong co StopRequirement khai bao.
            var willStop = isOrigin || isDestination || requiresPassenger || requiresTechnical;

            int? arrival = null;
            var runningTime = 0;
            var accelerationApplied = false;
            var decelerationApplied = false;

            if (!isOrigin)
            {
                var prevSeq = stationSequences[i - 1];
                var section = network.GetSectionBetween(prevSeq, stationSeq);
                var minRunning = section.GetMinRunningTimeMinutes(service.Direction);

                decelerationApplied = willStop;
                accelerationApplied = previousWasStopped;

                runningTime = minRunning
                    + (accelerationApplied ? _runningTimeRules.AccelerationPenaltyMinutes : 0)
                    + (decelerationApplied ? _runningTimeRules.DecelerationPenaltyMinutes : 0);

                arrival = previousDeparture!.Value + runningTime;
            }

            var stopDuration = !isOrigin && !isDestination && willStop
                ? _stopRules.ResolveStopMinutes(requiresPassenger, requiresTechnical, stopRequirement?.StopDurationOverrideMinutes)
                : 0;

            int? departure = isOrigin
                ? service.FixedDepartureTimeOfDayMinutes
                : isDestination
                    ? null
                    : arrival!.Value + stopDuration;

            var stopType = !willStop
                ? StopType.Through
                : requiresPassenger && requiresTechnical
                    ? StopType.PassengerAndTechnical
                    : requiresTechnical
                        ? StopType.Technical
                        : StopType.Passenger;

            entries.Add(new TimetableEntry
            {
                StationSequence = stationSeq,
                ArrivalTimeMinutes = arrival,
                DepartureTimeMinutes = departure,
                StopType = stopType,
                StopDurationMinutes = stopDuration,
                RunningTimeFromPrevMinutes = runningTime,
                AccelerationApplied = accelerationApplied,
                DecelerationApplied = decelerationApplied,
                RecoveryTimeFromPrevMinutes = 0,
                CumulativeInsertedDelayMinutes = 0
            });

            previousWasStopped = willStop;
            previousDeparture = departure;
        }

        return new TrainServiceTrajectory { ServiceId = service.ServiceId, Entries = entries };
    }
}
