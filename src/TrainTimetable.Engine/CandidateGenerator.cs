using TrainTimetable.Domain;

namespace TrainTimetable.Engine;

/// <summary>
/// Cap (Trajectory, Network) CUA RIENG mot TrainService (muc 1.4/15.14) - khong phai type moi trong
/// Domain, chi la mot cach goi ten tien dung khi truyen vao CandidateGenerator.
/// </summary>
public sealed record TrainServiceRoute
{
    public required TrainService Service { get; init; }
    public required TrainServiceTrajectory Trajectory { get; init; }
    public required RailwayNetwork Network { get; init; }

    /// <summary>
    /// StationCode tai mot local index - Domain.Station.Code hien la string (khoang ho muc 15.11 chua
    /// giai quyet), CandidateGenerator dung int (khop cot StationCode int trong DB that) nen phai
    /// parse tai DUY NHAT diem tra cuu nay.
    /// </summary>
    public int StationCodeAt(int localIndex) =>
        int.Parse(Network.GetStation(Trajectory.Entries[localIndex].StationSequence).Code);

    public int? IndexOfStationCode(int stationCode)
    {
        for (var i = 0; i < Trajectory.Entries.Count; i++)
        {
            if (StationCodeAt(i) == stationCode)
            {
                return i;
            }
        }

        return null;
    }
}

public sealed record PhysicalCandidateStation
{
    public required int StationCode { get; init; }
    public required bool CanMeet { get; init; }
    public required bool CanOvertake { get; init; }
    public required bool CanHold { get; init; }
}

public sealed record CandidateSolution
{
    public required Conflict Conflict { get; init; }
    public required int CandidateStationCode { get; init; }
    public required string TrainToWait { get; init; }
    public required int TrainToWaitLocalStationIndex { get; init; }

    /// <summary>
    /// Local index cua "tau kia" (P) tai CUNG CandidateStationCode, tren route CUA P - chi co gia tri
    /// (khong null) khi Conflict.Type == MEET (review lan 5/6, muc 6.0). HEADWAY khong tao gia tri nay
    /// (nhanh Follower-only khong can biet Leader o dau).
    /// </summary>
    public int? OtherTrainLocalStationIndex { get; init; }
}

/// <summary>Registry rieng SectionId -> (FromStationCode, ToStationCode) - KHONG phai RailwayNetwork toan cuc (muc 6.0).</summary>
public sealed class PhysicalSectionCatalog
{
    private readonly IReadOnlyDictionary<string, (int FromStationCode, int ToStationCode)> _lookup;

    public PhysicalSectionCatalog(IReadOnlyDictionary<string, (int FromStationCode, int ToStationCode)> lookup)
    {
        _lookup = lookup;
    }

    public (int FromStationCode, int ToStationCode) Lookup(string sectionId) => _lookup[sectionId];
}

/// <summary>
/// Sinh candidate cho MEET (intersection 2 route) va HEADWAY (chi route cua Follower) - 2 nhanh khac nhau
/// theo Type (muc 6.1, review lan 3). ConflictDetector khong bao gio sinh Type=OVERTAKE (muc 5.5) nen
/// nhanh do khong duoc ho tro o day - la viec cua ConflictAnalyzer (Phase 6, ngoai pham vi).
/// </summary>
public sealed class CandidateGenerator
{
    private readonly int _window;

    public CandidateGenerator(int window)
    {
        _window = window;
    }

    public IReadOnlyList<CandidateSolution> GenerateCandidates(
        Conflict conflict,
        TrainServiceRoute routeA,
        TrainServiceRoute routeB,
        PhysicalSectionCatalog catalog,
        Func<int, PhysicalCandidateStation> capabilityLookup)
    {
        var (fromCode, toCode) = catalog.Lookup(conflict.SectionId);

        return conflict.Type switch
        {
            ConflictType.MEET => GenerateMeetCandidates(conflict, routeA, routeB, fromCode, toCode, capabilityLookup),
            ConflictType.HEADWAY => GenerateHeadwayCandidates(
                conflict,
                conflict.FollowerServiceId == conflict.ServiceIdA ? routeA : routeB,
                fromCode, toCode, capabilityLookup),
            _ => throw new NotSupportedException(
                $"CandidateGenerator khong ho tro Type={conflict.Type} - OVERTAKE la viec cua ConflictAnalyzer (Phase 6).")
        };
    }

    private List<CandidateSolution> GenerateMeetCandidates(
        Conflict conflict, TrainServiceRoute routeA, TrainServiceRoute routeB,
        int fromCode, int toCode, Func<int, PhysicalCandidateStation> capabilityLookup)
    {
        var idxAFrom = RequireIndex(routeA, fromCode);
        var idxATo = RequireIndex(routeA, toCode);
        var idxBFrom = RequireIndex(routeB, fromCode);
        var idxBTo = RequireIndex(routeB, toCode);

        var entryIdxA = Math.Min(idxAFrom, idxATo);
        var exitIdxA = Math.Max(idxAFrom, idxATo);
        var entryIdxB = Math.Min(idxBFrom, idxBTo);
        var exitIdxB = Math.Max(idxBFrom, idxBTo);

        var candidatesA = LocalStationsWithinWindow(routeA, entryIdxA, exitIdxA);
        var candidatesB = LocalStationsWithinWindow(routeB, entryIdxB, exitIdxB);

        var results = new List<CandidateSolution>();
        foreach (var (code, indexA) in candidatesA)
        {
            if (!candidatesB.TryGetValue(code, out var indexB))
            {
                continue;
            }

            var station = capabilityLookup(code);
            if (!station.CanMeet)
            {
                continue;
            }

            // Invariant (review lan 6, muc 6.1 buoc 5): waiting train W phai co Departure(W,S), passing
            // train P phai co Arrival(P,S) - xet DOC LAP tung huong, KHONG loai ca physical station.
            // KHONG fallback Arrival<->Departure khi thieu (ban chat vat ly khac nhau).
            var aHasDeparture = routeA.Trajectory.Entries[indexA].DepartureTimeMinutes is not null;
            var bHasArrival = routeB.Trajectory.Entries[indexB].ArrivalTimeMinutes is not null;
            if (indexA <= entryIdxA && aHasDeparture && bHasArrival)
            {
                results.Add(new CandidateSolution
                {
                    Conflict = conflict,
                    CandidateStationCode = code,
                    TrainToWait = conflict.ServiceIdA,
                    TrainToWaitLocalStationIndex = indexA,
                    OtherTrainLocalStationIndex = indexB
                });
            }

            var bHasDeparture = routeB.Trajectory.Entries[indexB].DepartureTimeMinutes is not null;
            var aHasArrival = routeA.Trajectory.Entries[indexA].ArrivalTimeMinutes is not null;
            if (indexB <= entryIdxB && bHasDeparture && aHasArrival)
            {
                results.Add(new CandidateSolution
                {
                    Conflict = conflict,
                    CandidateStationCode = code,
                    TrainToWait = conflict.ServiceIdB,
                    TrainToWaitLocalStationIndex = indexB,
                    OtherTrainLocalStationIndex = indexA
                });
            }
        }

        return results;
    }

    private List<CandidateSolution> GenerateHeadwayCandidates(
        Conflict conflict, TrainServiceRoute followerRoute,
        int fromCode, int toCode, Func<int, PhysicalCandidateStation> capabilityLookup)
    {
        var idxFrom = RequireIndex(followerRoute, fromCode);
        var idxTo = RequireIndex(followerRoute, toCode);

        var entryIdx = Math.Min(idxFrom, idxTo);
        var exitIdx = Math.Max(idxFrom, idxTo);

        var candidates = LocalStationsWithinWindow(followerRoute, entryIdx, exitIdx);

        var results = new List<CandidateSolution>();
        foreach (var (code, localIndex) in candidates)
        {
            if (localIndex > entryIdx)
            {
                continue;
            }

            var station = capabilityLookup(code);
            if (!station.CanHold)
            {
                continue;
            }

            results.Add(new CandidateSolution
            {
                Conflict = conflict,
                CandidateStationCode = code,
                TrainToWait = conflict.FollowerServiceId,
                TrainToWaitLocalStationIndex = localIndex
            });
        }

        return results;
    }

    private static int RequireIndex(TrainServiceRoute route, int stationCode)
    {
        return route.IndexOfStationCode(stationCode)
            ?? throw new InvalidOperationException(
                $"StationCode {stationCode} khong xuat hien tren route cua {route.Service.ServiceId} - " +
                "Conflict.SectionId khong khop voi route cua chinh tau da sinh ra occupation nay " +
                "(loi du lieu o PhysicalSectionCatalog/SectionOccupationBuilder).");
    }

    private Dictionary<int, int> LocalStationsWithinWindow(TrainServiceRoute route, int entryIdx, int exitIdx)
    {
        var lo = Math.Max(0, entryIdx - _window);
        var hi = Math.Min(route.Trajectory.Entries.Count - 1, exitIdx + _window);

        var result = new Dictionary<int, int>();
        for (var i = lo; i <= hi; i++)
        {
            result[route.StationCodeAt(i)] = i;
        }

        return result;
    }
}
