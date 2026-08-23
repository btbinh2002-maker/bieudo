namespace TrainTimetable.Domain;

/// <summary>
/// Mot tuyen tuyen tinh (linear line): danh sach Station theo Sequence va Section giua cac cap
/// ga lien tiep. Mot Section duoc luu MOT LAN cho ca hai chieu (running time khac nhau qua map
/// MinRunningTimeMinutes), khong tach thanh 2 Section theo chieu.
/// </summary>
public sealed class RailwayNetwork
{
    private readonly Dictionary<int, Station> _stationsBySequence;
    private readonly Dictionary<(int Lower, int Higher), Section> _sectionsByEndpoints;

    public RailwayNetwork(IReadOnlyList<Station> stations, IReadOnlyList<Section> sections)
    {
        Stations = stations.OrderBy(s => s.Sequence).ToList();
        Sections = sections;
        _stationsBySequence = Stations.ToDictionary(s => s.Sequence);
        _sectionsByEndpoints = sections.ToDictionary(s =>
            (Math.Min(s.FromStationSequence, s.ToStationSequence), Math.Max(s.FromStationSequence, s.ToStationSequence)));
    }

    public IReadOnlyList<Station> Stations { get; }
    public IReadOnlyList<Section> Sections { get; }

    public Station GetStation(int sequence) => _stationsBySequence[sequence];

    public Section GetSectionBetween(int fromSequence, int toSequence)
    {
        var key = (Math.Min(fromSequence, toSequence), Math.Max(fromSequence, toSequence));
        return _sectionsByEndpoints[key];
    }

    /// <summary>
    /// Tra ve danh sach StationSequence tu Origin den Destination theo dung chieu di chuyen -
    /// tang dan neu Inbound, giam dan neu Outbound (tuyen tuyen tinh, khong can routing).
    /// </summary>
    public IReadOnlyList<int> GetStationSequencesOnRoute(int originSequence, int destinationSequence, Direction direction)
    {
        if (direction == Direction.Inbound)
        {
            if (originSequence >= destinationSequence)
            {
                throw new ArgumentException("Inbound: OriginStationSequence phai nho hon DestinationStationSequence.");
            }

            return Enumerable.Range(originSequence, destinationSequence - originSequence + 1).ToList();
        }

        if (originSequence <= destinationSequence)
        {
            throw new ArgumentException("Outbound: OriginStationSequence phai lon hon DestinationStationSequence.");
        }

        return Enumerable.Range(destinationSequence, originSequence - destinationSequence + 1)
            .Reverse()
            .ToList();
    }
}
