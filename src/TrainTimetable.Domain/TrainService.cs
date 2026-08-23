namespace TrainTimetable.Domain;

/// <summary>
/// Canonical cyclic pattern (chu ky 0) cua mot doan tau khach. Day la noi DUY NHAT chua decision
/// variable cua solver - TrainInstance(cycleIndex) chi la view suy dien = Schedule + cycleIndex * 1440,
/// khong bao gio duoc luu hay sua doc lap (xem docs/design/01-phase1-domain-analysis.md, muc 0.1/1.4).
/// </summary>
public sealed class TrainService
{
    public const int CycleLengthMinutes = 1440;

    public TrainService(
        string serviceId,
        string trainCode,
        Direction direction,
        int originStationSequence,
        int destinationStationSequence,
        int fixedDepartureTimeOfDayMinutes,
        int journeyTimeMinutes,
        int priority,
        IReadOnlyList<TrainStopRequirement> stopRequirements)
    {
        if (fixedDepartureTimeOfDayMinutes < 0 || fixedDepartureTimeOfDayMinutes >= CycleLengthMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fixedDepartureTimeOfDayMinutes),
                fixedDepartureTimeOfDayMinutes,
                $"Phai nam trong [0, {CycleLengthMinutes}) - day la gio khoi hanh CANONICAL cua chu ky 0.");
        }

        if (journeyTimeMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(journeyTimeMinutes), journeyTimeMinutes, "JourneyTime phai duong.");
        }

        ServiceId = serviceId;
        TrainCode = trainCode;
        Direction = direction;
        OriginStationSequence = originStationSequence;
        DestinationStationSequence = destinationStationSequence;
        FixedDepartureTimeOfDayMinutes = fixedDepartureTimeOfDayMinutes;
        JourneyTimeMinutes = journeyTimeMinutes;
        Priority = priority;
        StopRequirements = stopRequirements;
    }

    public string ServiceId { get; }
    public string TrainCode { get; }
    public Direction Direction { get; }
    public int OriginStationSequence { get; }
    public int DestinationStationSequence { get; }
    public int FixedDepartureTimeOfDayMinutes { get; }

    /// <summary>Co the vuot qua 1440 phut - hanh trinh HN-SG keo dai ~30-34h, dai hon 1 chu ky.</summary>
    public int JourneyTimeMinutes { get; }

    public int Priority { get; }
    public IReadOnlyList<TrainStopRequirement> StopRequirements { get; }

    public int FixedArrivalTimeMinutes => FixedDepartureTimeOfDayMinutes + JourneyTimeMinutes;

    public TrainStopRequirement? GetStopRequirement(int stationSequence) =>
        StopRequirements.FirstOrDefault(s => s.StationSequence == stationSequence);
}
