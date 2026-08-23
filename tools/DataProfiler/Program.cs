using Microsoft.Data.SqlClient;
using TrainTimetable.Configuration;
using TrainTimetable.Domain;
using TrainTimetable.Engine;

var connectionString = Environment.GetEnvironmentVariable("HANHTRINH_DB_CONNECTION")
    ?? throw new InvalidOperationException("HANHTRINH_DB_CONNECTION env var chua duoc set.");

var rows = await LoadRowsAsync(connectionString);
Console.WriteLine($"Tong so dong doc tu dbo.HanhTrinh: {rows.Count}");

var byTrain = rows
    .GroupBy(r => r.TrainCode ?? "<NULL>")
    .OrderBy(g => g.Key)
    .ToList();

Console.WriteLine();
Console.WriteLine("========== 1. So TrainCode & route thuc te ==========");
Console.WriteLine($"So TrainCode phan biet: {byTrain.Count}");
Console.WriteLine($"Danh sach: {string.Join(", ", byTrain.Select(g => $"{g.Key}({g.Count()})"))}");

var caseInsensitiveDuplicates = byTrain
    .GroupBy(g => g.Key.ToUpperInvariant())
    .Where(g => g.Count() > 1)
    .ToList();
Console.WriteLine($"CANH BAO - TrainCode trung nhau neu bo qua hoa/thuong: {caseInsensitiveDuplicates.Count} nhom");
foreach (var g in caseInsensitiveDuplicates)
{
    Console.WriteLine($"  {string.Join(" / ", g.Select(x => x.Key))}");
}

var stopRules = new StopRules();
var runningTimeRules = new RunningTimeRules();

var profiles = byTrain.Select(g => AnalyzeTrain(g.Key, g.ToList(), stopRules, runningTimeRules)).ToList();

var routePatterns = profiles
    .Where(p => p.OriginStationCode is not null && p.DestinationStationCode is not null)
    .GroupBy(p => (p.OriginStationCode, p.DestinationStationCode))
    .OrderByDescending(g => g.Count())
    .ToList();

Console.WriteLine($"So route (Origin,Destination) StationCode phan biet: {routePatterns.Count}");
Console.WriteLine("Top 15 route pho bien nhat:");
foreach (var g in routePatterns.Take(15))
{
    Console.WriteLine($"  {g.Key.OriginStationCode} -> {g.Key.DestinationStationCode}  ({g.Count()} tau)");
}

Console.WriteLine();
Console.WriteLine("========== 2. JourneySequence ==========");
var badSequence = profiles.Where(p => !p.JourneySequenceOk).ToList();
Console.WriteLine($"So TrainCode co JourneySequence KHONG lien tuc 1..N (thieu/lap): {badSequence.Count}");
foreach (var p in badSequence.Take(20))
{
    Console.WriteLine($"  {p.TrainCode}: {string.Join(" | ", p.Issues)}");
}

Console.WriteLine();
Console.WriteLine("========== 3. JourneyTime > 24h (1440 phut) ==========");
var withJourneyTime = profiles.Where(p => p.JourneyTimeMinutes is not null).ToList();
var over24h = withJourneyTime.Where(p => p.JourneyTimeMinutes > 1440).OrderByDescending(p => p.JourneyTimeMinutes).ToList();
Console.WriteLine($"So tau tinh duoc JourneyTime: {withJourneyTime.Count} / {profiles.Count}");
Console.WriteLine($"So tau JourneyTime > 1440 phut (>24h): {over24h.Count}");
foreach (var p in over24h.Take(20))
{
    Console.WriteLine($"  {p.TrainCode}: JourneyTime={p.JourneyTimeMinutes} phut (~{p.JourneyTimeMinutes / 60.0:F1}h)");
}

var nonPositiveJourney = withJourneyTime.Where(p => p.JourneyTimeMinutes <= 0).ToList();
if (nonPositiveJourney.Count > 0)
{
    Console.WriteLine($"CANH BAO: {nonPositiveJourney.Count} tau co JourneyTime <= 0:");
    foreach (var p in nonPositiveJourney.Take(20))
    {
        Console.WriteLine($"  {p.TrainCode}: JourneyTime={p.JourneyTimeMinutes}");
    }
}

Console.WriteLine();
Console.WriteLine("========== 4. TotalBuffer tung tau (qua MinimumTimetableBuilder + BufferCalculator that) ==========");
var withBuffer = profiles.Where(p => p.TotalBufferMinutes is not null).ToList();
Console.WriteLine($"So tau tinh duoc TotalBuffer (khong loi validate): {withBuffer.Count} / {profiles.Count}");
if (withBuffer.Count > 0)
{
    var buffers = withBuffer.Select(p => p.TotalBufferMinutes!.Value).ToList();
    Console.WriteLine($"  Min={buffers.Min()}  Max={buffers.Max()}  Avg={buffers.Average():F1}");
    var negative = withBuffer.Where(p => p.TotalBufferMinutes < 0).OrderBy(p => p.TotalBufferMinutes).ToList();
    Console.WriteLine($"  So tau TotalBuffer < 0 (INFEASIBLE ngay tu input): {negative.Count}");
    foreach (var p in negative.Take(20))
    {
        Console.WriteLine($"    {p.TrainCode}: TotalBuffer={p.TotalBufferMinutes} (JourneyTime={p.JourneyTimeMinutes})");
    }
}

var withErrors = profiles.Where(p => p.TotalBufferMinutes is null && p.JourneySequenceOk).ToList();
Console.WriteLine($"So tau JourneySequence OK nhung van loi khi build trajectory/buffer: {withErrors.Count}");
foreach (var p in withErrors.Take(20))
{
    Console.WriteLine($"  {p.TrainCode}: {string.Join(" | ", p.Issues)}");
}

Console.WriteLine();
Console.WriteLine("========== 5. MinimumRunningTimeToNextStation co uniform theo (StationCode A->B THEO DUNG THU TU THAT) khong ==========");
// SUA LOI heuristic cu (suy chieu tu Origin/Destination toan hanh trinh sai voi QB1/QB2) - gio group
// THANG theo cap (From,To) DUNG THU TU xuat hien trong du lieu, khong can suy dien chieu nua.
var sectionObservations = new List<(int From, int To, string TrainCode, int MinRunningTime)>();
foreach (var g in byTrain)
{
    var ordered = g.OrderBy(r => r.JourneySequence ?? int.MinValue).ToList();
    for (var i = 0; i < ordered.Count - 1; i++)
    {
        var a = ordered[i];
        var b = ordered[i + 1];
        if (a.StationCode is not { } from || b.StationCode is not { } to || a.MinimumRunningTimeToNextStation is not { } minRunning)
        {
            continue;
        }

        sectionObservations.Add((from, to, g.Key, minRunning));
    }
}

var sectionGroups = sectionObservations
    .GroupBy(o => (o.From, o.To))
    .ToList();

var nonUniformSections = sectionGroups
    .Where(sg => sg.Select(x => x.MinRunningTime).Distinct().Count() > 1)
    .ToList();

Console.WriteLine($"So (From->To) phan biet quan sat duoc: {sectionGroups.Count}");
Console.WriteLine($"So (From->To) KHONG uniform (nhieu gia tri MinimumRunningTime khac nhau): {nonUniformSections.Count}");
foreach (var sg in nonUniformSections.Take(20))
{
    var values = sg.Select(x => (x.TrainCode, x.MinRunningTime)).Distinct().Take(10).ToList();
    Console.WriteLine($"  {sg.Key.From}->{sg.Key.To}: " +
        string.Join(", ", values.Select(v => $"{v.TrainCode}={v.MinRunningTime}")));
}

var zeroRunningTime = sectionObservations.Where(o => o.MinRunningTime <= 0).ToList();
Console.WriteLine($"So dong MinimumRunningTimeToNextStation <= 0 (nghi ngo loi du lieu): {zeroRunningTime.Count} / {sectionObservations.Count}");
foreach (var o in zeroRunningTime.Take(10))
{
    Console.WriteLine($"  {o.TrainCode}: {o.From}->{o.To} = {o.MinRunningTime}");
}

Console.WriteLine();
Console.WriteLine("--- Uniformity SAU KHI loai gia tri <= 0 (chi so sanh giua cac gia tri THUC > 0) ---");
var sectionGroupsPositiveOnly = sectionObservations
    .Where(o => o.MinRunningTime > 0)
    .GroupBy(o => (o.From, o.To))
    .ToList();
var nonUniformPositiveOnly = sectionGroupsPositiveOnly
    .Where(sg => sg.Select(x => x.MinRunningTime).Distinct().Count() > 1)
    .ToList();
Console.WriteLine($"So (From->To) co it nhat 1 gia tri > 0: {sectionGroupsPositiveOnly.Count}");
Console.WriteLine($"So (From->To) KHONG uniform GIUA CAC GIA TRI > 0 (loai nhieu do 0): {nonUniformPositiveOnly.Count}");
foreach (var sg in nonUniformPositiveOnly.Take(20))
{
    var values = sg.Select(x => (x.TrainCode, x.MinRunningTime)).Distinct().Take(10).ToList();
    Console.WriteLine($"  {sg.Key.From}->{sg.Key.To}: " +
        string.Join(", ", values.Select(v => $"{v.TrainCode}={v.MinRunningTime}")));
}

Console.WriteLine();
Console.WriteLine("--- Uniformity THEO TUNG HO TAU (bo chu so cuoi TrainCode, vd SE1/SE2 -> \"SE\") ---");
static string FamilyOf(string trainCode)
{
    var i = trainCode.Length;
    while (i > 0 && char.IsDigit(trainCode[i - 1])) i--;
    return trainCode[..i].ToUpperInvariant();
}

var families = sectionObservations.Select(o => FamilyOf(o.TrainCode)).Distinct().OrderBy(x => x).ToList();
Console.WriteLine($"Cac ho tau: {string.Join(", ", families)}");

foreach (var family in families)
{
    var familyObservations = sectionObservations.Where(o => FamilyOf(o.TrainCode) == family && o.MinRunningTime > 0).ToList();
    var familyGroups = familyObservations.GroupBy(o => (o.From, o.To)).ToList();
    var familyNonUniform = familyGroups.Where(sg => sg.Select(x => x.MinRunningTime).Distinct().Count() > 1).ToList();
    Console.WriteLine($"  Ho {family}: {familyGroups.Count} (From->To), khong-uniform={familyNonUniform.Count}");
    foreach (var sg in familyNonUniform.Take(5))
    {
        var values = sg.Select(x => (x.TrainCode, x.MinRunningTime)).Distinct().ToList();
        Console.WriteLine($"    {sg.Key.From}->{sg.Key.To}: " + string.Join(", ", values.Select(v => $"{v.TrainCode}={v.MinRunningTime}")));
    }
}

Console.WriteLine();
Console.WriteLine("========== 6. Passenger/Technical stop ==========");
var passengerValues = rows.Where(r => r.PassengerStopMinutes is > 0).Select(r => r.PassengerStopMinutes!.Value).ToList();
var technicalValues = rows.Where(r => r.TechnicalStopMinutes is > 0).Select(r => r.TechnicalStopMinutes!.Value).ToList();
var bothValues = rows.Where(r => r.PassengerStopMinutes is > 0 && r.TechnicalStopMinutes is > 0).ToList();

Console.WriteLine($"So dong PassengerStopMinutes > 0: {passengerValues.Count} (distinct values: {string.Join(",", passengerValues.Distinct().OrderBy(x => x).Take(20))})");
Console.WriteLine($"So dong TechnicalStopMinutes > 0: {technicalValues.Count} (distinct values: {string.Join(",", technicalValues.Distinct().OrderBy(x => x).Take(20))})");
Console.WriteLine($"So dong CA HAI PassengerStopMinutes va TechnicalStopMinutes > 0 (can CombineMode): {bothValues.Count}");
foreach (var r in bothValues.Take(10))
{
    Console.WriteLine($"  {r.TrainCode} seq={r.JourneySequence}: Passenger={r.PassengerStopMinutes} Technical={r.TechnicalStopMinutes}");
}

Console.WriteLine();
Console.WriteLine("========== 7. NULL / duplicate / bat thuong khac ==========");
Console.WriteLine($"So dong TrainCode NULL: {rows.Count(r => r.TrainCode is null)}");
Console.WriteLine($"So dong JourneySequence NULL: {rows.Count(r => r.JourneySequence is null)}");
Console.WriteLine($"So dong StationCode NULL: {rows.Count(r => r.StationCode is null)}");
Console.WriteLine($"So dong PassengerStopMinutes NULL: {rows.Count(r => r.PassengerStopMinutes is null)}");
Console.WriteLine($"So dong TechnicalStopMinutes NULL: {rows.Count(r => r.TechnicalStopMinutes is null)}");
Console.WriteLine($"So dong MinimumRunningTimeToNextStation NULL: {rows.Count(r => r.MinimumRunningTimeToNextStation is null)}");
foreach (var r in rows.Where(r => r.MinimumRunningTimeToNextStation is null))
{
    Console.WriteLine($"  {r.TrainCode} seq={r.JourneySequence} station={r.StationCode}");
}

var duplicateKeys = rows
    .Where(r => r.TrainCode is not null && r.JourneySequence is not null)
    .GroupBy(r => (r.TrainCode, r.JourneySequence))
    .Where(g => g.Count() > 1)
    .ToList();
Console.WriteLine($"So cap (TrainCode,JourneySequence) BI TRUNG LAP: {duplicateKeys.Count}");
foreach (var g in duplicateKeys.Take(20))
{
    Console.WriteLine($"  {g.Key.TrainCode} / seq={g.Key.JourneySequence} (x{g.Count()})");
}

var wrongDepartureDay = profiles.Where(p => p.Issues.Any(i => i.Contains("DepartureDayNumber"))).ToList();
Console.WriteLine($"So tau co DepartureDayNumber dong dau != 0: {wrongDepartureDay.Count}");
foreach (var p in wrongDepartureDay.Take(20))
{
    Console.WriteLine($"  {p.TrainCode}: {string.Join(" | ", p.Issues.Where(i => i.Contains("DepartureDayNumber")))}");
}

Console.WriteLine();
Console.WriteLine("========== HET ==========");

static async Task<List<RawRow>> LoadRowsAsync(string connectionString)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    const string query = """
        SELECT id, TrainCode, JourneySequence, StationCode, ArrivalTime, ArrivalDayNumber,
               DepartureTime, DepartureDayNumber, MinimumRunningTimeToNextStation,
               PassengerStopMinutes, TechnicalStopMinutes
        FROM dbo.HanhTrinh
        ORDER BY TrainCode, JourneySequence
        """;

    var result = new List<RawRow>();
    await using var cmd = new SqlCommand(query, connection);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new RawRow(
            Id: reader.GetInt64(0),
            TrainCode: reader.IsDBNull(1) ? null : reader.GetString(1),
            JourneySequence: reader.IsDBNull(2) ? null : reader.GetInt32(2),
            StationCode: reader.IsDBNull(3) ? null : reader.GetInt32(3),
            ArrivalTime: reader.IsDBNull(4) ? null : reader.GetTimeSpan(4),
            ArrivalDayNumber: reader.IsDBNull(5) ? null : reader.GetInt32(5),
            DepartureTime: reader.IsDBNull(6) ? null : reader.GetTimeSpan(6),
            DepartureDayNumber: reader.IsDBNull(7) ? null : reader.GetInt32(7),
            MinimumRunningTimeToNextStation: reader.IsDBNull(8) ? null : reader.GetInt32(8),
            PassengerStopMinutes: reader.IsDBNull(9) ? null : reader.GetInt32(9),
            TechnicalStopMinutes: reader.IsDBNull(10) ? null : reader.GetInt32(10)));
    }

    return result;
}

static TrainProfile AnalyzeTrain(
    string trainCode, List<RawRow> rowsForTrain, IStopRules stopRules, IRunningTimeRules runningTimeRules)
{
    var issues = new List<string>();
    var ordered = rowsForTrain.OrderBy(r => r.JourneySequence ?? int.MinValue).ToList();
    var n = ordered.Count;

    if (ordered.Any(r => r.JourneySequence is null))
    {
        issues.Add("JourneySequence NULL o mot hoac nhieu dong");
        return new TrainProfile(trainCode, n, false, null, null, null, null, null, issues);
    }

    var sequences = ordered.Select(r => r.JourneySequence!.Value).ToList();
    var expected = Enumerable.Range(1, n).ToHashSet();
    var missing = expected.Except(sequences).ToList();
    var duplicates = sequences.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    var sequenceOk = missing.Count == 0 && duplicates.Count == 0;

    var originCode = ordered[0].StationCode;
    var destCode = ordered[^1].StationCode;

    if (!sequenceOk)
    {
        issues.Add($"JourneySequence khong lien tuc 1..{n}: missing=[{string.Join(",", missing)}] dup=[{string.Join(",", duplicates)}]");
        return new TrainProfile(trainCode, n, false, originCode, destCode, null, null, null, issues);
    }

    if (n < 2)
    {
        issues.Add("Hanh trinh chi co 1 dong - khong the tinh trajectory");
        return new TrainProfile(trainCode, n, true, originCode, destCode, null, null, null, issues);
    }

    var first = ordered[0];
    var last = ordered[^1];

    if (first.DepartureTime is null || first.DepartureDayNumber is null)
    {
        issues.Add("Dong dau thieu DepartureTime/DepartureDayNumber");
    }
    else if (first.DepartureDayNumber != 0)
    {
        issues.Add($"DepartureDayNumber dong dau = {first.DepartureDayNumber}, ky vong 0");
    }

    if (last.ArrivalTime is null || last.ArrivalDayNumber is null)
    {
        issues.Add("Dong cuoi thieu ArrivalTime/ArrivalDayNumber");
    }

    for (var i = 0; i < n; i++)
    {
        var r = ordered[i];
        if (r.StationCode is null)
        {
            issues.Add($"StationCode NULL tai seq={r.JourneySequence}");
        }

        if (i < n - 1 && r.MinimumRunningTimeToNextStation is null)
        {
            issues.Add($"MinimumRunningTimeToNextStation NULL tai seq={r.JourneySequence}");
        }

        if (r.PassengerStopMinutes is null)
        {
            issues.Add($"PassengerStopMinutes NULL tai seq={r.JourneySequence}");
        }

        if (r.TechnicalStopMinutes is null)
        {
            issues.Add($"TechnicalStopMinutes NULL tai seq={r.JourneySequence}");
        }
    }

    if (issues.Count > 0)
    {
        return new TrainProfile(trainCode, n, true, originCode, destCode, null, null, null, issues);
    }

    var fixedDeparture = (int)first.DepartureTime!.Value.TotalMinutes;
    var fixedArrivalAbsolute = last.ArrivalDayNumber!.Value * 1440 + (int)last.ArrivalTime!.Value.TotalMinutes;
    var journeyTime = fixedArrivalAbsolute - fixedDeparture;

    if (journeyTime <= 0)
    {
        issues.Add($"JourneyTime <= 0 ({journeyTime})");
        return new TrainProfile(trainCode, n, true, originCode, destCode, fixedDeparture, journeyTime, null, issues);
    }

    try
    {
        var stations = new List<Station>(n);
        var sections = new List<Section>(n - 1);
        for (var i = 0; i < n; i++)
        {
            stations.Add(new Station
            {
                StationId = $"{trainCode}-{i + 1}",
                Code = ordered[i].StationCode?.ToString() ?? $"?{i + 1}",
                Name = $"seq{i + 1}",
                Sequence = i + 1,
                Tracks = new List<StationTrack> { new($"{trainCode}-{i + 1}-T1", TrackType.MainThrough, false, false) }
            });

            if (i < n - 1)
            {
                sections.Add(new Section
                {
                    SectionId = $"{trainCode}-KG{i + 1}-{i + 2}",
                    FromStationSequence = i + 1,
                    ToStationSequence = i + 2,
                    MinRunningTimeMinutes = new Dictionary<Direction, int>
                    {
                        [Direction.Inbound] = ordered[i].MinimumRunningTimeToNextStation!.Value
                    }
                });
            }
        }

        var network = new RailwayNetwork(stations, sections);

        var stopRequirements = new List<TrainStopRequirement>();
        for (var i = 1; i < n - 1; i++)
        {
            var r = ordered[i];
            var passenger = r.PassengerStopMinutes!.Value;
            var technical = r.TechnicalStopMinutes!.Value;
            if (passenger <= 0 && technical <= 0)
            {
                continue;
            }

            var duration = Math.Max(passenger, technical);

            stopRequirements.Add(new TrainStopRequirement
            {
                StationSequence = i + 1,
                RequiresPassengerStop = passenger > 0,
                RequiresTechnicalStop = technical > 0,
                StopDurationOverrideMinutes = duration
            });
        }

        var service = new TrainService(
            serviceId: trainCode, trainCode: trainCode, direction: Direction.Inbound,
            originStationSequence: 1, destinationStationSequence: n,
            fixedDepartureTimeOfDayMinutes: fixedDeparture, journeyTimeMinutes: journeyTime,
            priority: 1, stopRequirements: stopRequirements);

        var builder = new MinimumTimetableBuilder(stopRules, runningTimeRules);
        var trajectory = builder.Build(service, network);
        var bufferResult = new BufferCalculator().Calculate(service, trajectory);

        return new TrainProfile(trainCode, n, true, originCode, destCode, fixedDeparture, journeyTime,
            bufferResult.TotalBufferMinutes, issues);
    }
    catch (Exception ex)
    {
        issues.Add($"Loi khi build trajectory/buffer: {ex.GetType().Name}: {ex.Message}");
        return new TrainProfile(trainCode, n, true, originCode, destCode, fixedDeparture, journeyTime, null, issues);
    }
}

sealed record RawRow(
    long Id,
    string? TrainCode,
    int? JourneySequence,
    int? StationCode,
    TimeSpan? ArrivalTime,
    int? ArrivalDayNumber,
    TimeSpan? DepartureTime,
    int? DepartureDayNumber,
    int? MinimumRunningTimeToNextStation,
    int? PassengerStopMinutes,
    int? TechnicalStopMinutes);

sealed record TrainProfile(
    string TrainCode,
    int RowCount,
    bool JourneySequenceOk,
    int? OriginStationCode,
    int? DestinationStationCode,
    int? FixedDepartureTimeOfDayMinutes,
    int? JourneyTimeMinutes,
    int? TotalBufferMinutes,
    List<string> Issues);
