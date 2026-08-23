using TrainTimetable.Domain;

namespace TrainTimetable.Engine.Tests;

/// <summary>
/// Mang 5 ga / 4 khu gian dung chung cho cac test. So lieu running time duoc chon tuy y nhung co
/// chu dich de tay tinh MinimumJourneyTime doi chieu voi thuat toan (xem comment trong tung test).
/// </summary>
internal static class TestNetworkFixture
{
    public static RailwayNetwork BuildFiveStationLine()
    {
        var stations = new List<Station>
        {
            NoTrackStation(1, "GA1"),
            NoTrackStation(2, "GA2"),
            MeetCapableStation(3, "GA3"),
            NoTrackStation(4, "GA4"),
            MeetCapableStation(5, "GA5")
        };

        var sections = new List<Section>
        {
            Section(1, 2, inbound: 30, outbound: 31),
            Section(2, 3, inbound: 25, outbound: 26),
            Section(3, 4, inbound: 40, outbound: 41),
            Section(4, 5, inbound: 35, outbound: 36)
        };

        return new RailwayNetwork(stations, sections);
    }

    private static Station NoTrackStation(int sequence, string code) => new()
    {
        StationId = $"S{sequence}",
        Code = code,
        Name = code,
        Sequence = sequence,
        Tracks = new List<StationTrack> { new($"S{sequence}-T1", TrackType.MainThrough, false, false) }
    };

    private static Station MeetCapableStation(int sequence, string code) => new()
    {
        StationId = $"S{sequence}",
        Code = code,
        Name = code,
        Sequence = sequence,
        Tracks = new List<StationTrack>
        {
            new($"S{sequence}-T1", TrackType.MainThrough, true, true),
            new($"S{sequence}-T2", TrackType.Siding, true, true)
        }
    };

    private static Section Section(int from, int to, int inbound, int outbound) => new()
    {
        SectionId = $"KG{from}-{to}",
        FromStationSequence = from,
        ToStationSequence = to,
        MinRunningTimeMinutes = new Dictionary<Direction, int>
        {
            [Direction.Inbound] = inbound,
            [Direction.Outbound] = outbound
        }
    };
}
