namespace TrainTimetable.Domain;

/// <summary>
/// Inbound: Ha Noi -> Sai Gon, StationSequence tang dan doc tuyen.
/// Outbound: Sai Gon -> Ha Noi, StationSequence giam dan doc tuyen.
/// </summary>
public enum Direction
{
    Inbound,
    Outbound
}
