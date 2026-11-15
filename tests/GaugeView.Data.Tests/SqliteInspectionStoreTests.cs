using GaugeView.Core;
using GaugeView.Core.History;
using GaugeView.Core.IO;
using Microsoft.Data.Sqlite;

namespace GaugeView.Data.Tests;

public sealed class SqliteInspectionStoreTests : IDisposable
{
    private static readonly Feature Bore = new("Bore Ø12 H7", new Tolerance(12.000m, 0.018m, 0.000m));
    private static readonly Feature Length = new("Overall length", Tolerance.Symmetric(85.00m, 0.10m));
    private static readonly InspectionPlan Plan = new("BRK-1042", "Bracket", "mm", [Bore, Length]);
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 8, 30, 0, TimeSpan.Zero);

    private readonly string path = Path.Combine(Path.GetTempPath(), $"gaugeview-test-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(path);

    [Fact]
    public void New_database_is_created_at_the_current_schema_version()
    {
        var store = new SqliteInspectionStore(path);

        Assert.True(File.Exists(path));
        Assert.Equal(SqliteInspectionStore.SchemaVersion, store.StoredSchemaVersion);
        Assert.Equal(Path.GetFullPath(path), store.Location);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Saved_inspections_survive_reopening_the_file()
    {
        new SqliteInspectionStore(path).Save(Plan, Parts(("P1", 12.009m, 85.00m)), "Ali", "B-1", 0.8m, Monday);

        var reopened = new SqliteInspectionStore(path);

        Assert.Equal("B-1", Assert.Single(reopened.List()).Batch);
    }

    [Fact]
    public void Save_records_who_when_and_the_verdict_counts()
    {
        var store = new SqliteInspectionStore(path);

        var record = store.Save(Plan, Parts(("P1", 12.009m, 85.00m), ("P2", 12.017m, 85.00m), ("P3", 12.019m, null)),
            "  Ali  ", " 2611-A ", 0.8m, Monday);

        Assert.True(record.Id > 0);
        Assert.Equal(("Ali", "2611-A", Monday), (record.Operator, record.Batch, record.InspectedAt));
        Assert.Equal((3, 1, 1, 1, 0), (record.PartCount, record.PassCount, record.WarningCount, record.FailCount, record.MissingCount));
        Assert.Equal(66.7, record.YieldPercent);
        Assert.Equal(record, Assert.Single(store.List()));
    }

    [Fact]
    public void List_is_newest_first_and_can_filter_by_part_number()
    {
        var store = new SqliteInspectionStore(path);
        var other = new InspectionPlan("SHAFT-7", "Shaft", "mm", [Length]);
        store.Save(Plan, Parts(("P1", 12.009m, 85.00m)), "Ali", "first", 0.8m, Monday);
        store.Save(Plan, Parts(("P2", 12.009m, 85.00m)), "Beth", "third", 0.8m, Monday.AddDays(2));
        store.Save(other, [new Inspector(other, new ToleranceChecker()).Inspect("S1", new Dictionary<Feature, decimal> { [Length] = 85m })],
            "Calum", "second", 0.8m, Monday.AddDays(1));

        Assert.Equal(new[] { "third", "second", "first" }, store.List().Select(r => r.Batch));
        Assert.Equal(new[] { "third", "first" }, store.List("brk-1042").Select(r => r.Batch));
    }

    [Fact]
    public void Load_reopens_the_inspection_exactly_as_saved()
    {
        var store = new SqliteInspectionStore(path);
        var record = store.Save(Plan, Parts(("P1", 12.010m, 85.00m), ("P2", 12.019m, null)), "Ali", "B-1", 0.75m, Monday);

        var saved = store.Load(record.Id);

        Assert.Equal(record, saved.Record);
        Assert.Equal(0.75m, saved.WarningThreshold);
        Assert.Equal(new[] { Bore, Length }, saved.Plan.Features);
        Assert.Equal(("Bracket", "mm"), (saved.Plan.Description, saved.Plan.Unit));
        Assert.Equal(new[] { "P1", "P2" }, saved.Parts.Select(p => p.Serial));

        var p1Bore = saved.Parts[0].Results[0];
        Assert.Equal("12.010", p1Bore.MeasuredValue!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); // scale kept
        Assert.Null(saved.Parts[1].Results[1].MeasuredValue);
        Assert.Equal(InspectionStatus.Missing, saved.Parts[1].Results[1].Status);
        Assert.Equal(InspectionStatus.Fail, saved.Parts[1].Verdict);
    }

    [Fact]
    public void Trend_gives_one_point_per_inspection_oldest_first()
    {
        var store = new SqliteInspectionStore(path);
        store.Save(Plan, Parts(("P3", 12.004m, 85m), ("P4", 12.002m, 85m), ("P5", 11.999m, 85m)), "Beth", "late", 0.8m, Monday.AddDays(7));
        store.Save(Plan, Parts(("P1", 12.010m, 85m), ("P2", 12.008m, 85m)), "Ali", "early", 0.8m, Monday);

        var trend = store.Trend("brk-1042", "BORE Ø12 H7"); // SQLite NOCASE folds ASCII letters only

        Assert.Equal(new[] { "early", "late" }, trend.Select(t => t.Batch));
        Assert.Equal(12.009, trend[0].Mean!.Value, 9);
        Assert.Equal((11.999m, 12.004m), (trend[1].Minimum!.Value, trend[1].Maximum!.Value));
        Assert.Equal((2, 0), (trend[0].SampleCount, trend[0].FailCount));
        Assert.Equal((3, 1), (trend[1].SampleCount, trend[1].FailCount));
        Assert.NotNull(trend[1].Cpk);
        Assert.True(trend[1].Ppk < trend[1].Cpk); // steadily falling readings: drift shows in Ppk, not Cpk
    }

    [Fact]
    public void Trend_for_a_feature_nobody_measured_is_empty() =>
        Assert.Empty(new SqliteInspectionStore(path).Trend("BRK-1042", "Bore Ø12 H7"));

    [Fact]
    public void Database_from_a_newer_version_is_refused_rather_than_changed()
    {
        _ = new SqliteInspectionStore(path);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidDataException>(() => new SqliteInspectionStore(path));
        Assert.Contains("newer GaugeView", ex.Message);
    }

    [Fact]
    public void Text_is_stored_as_data_never_as_sql()
    {
        var store = new SqliteInspectionStore(path);
        const string batch = "x'); DROP TABLE inspection; --";

        store.Save(Plan, Parts(("P'1", 12.009m, 85m)), "O'Neill", batch, 0.8m, Monday);

        var record = Assert.Single(store.List());
        Assert.Equal((batch, "O'Neill"), (record.Batch, record.Operator));
        Assert.Equal("P'1", store.Load(record.Id).Parts[0].Serial);
    }

    [Theory]
    [InlineData("", "B-1")]
    [InlineData("Ali", " ")]
    public void Save_needs_an_operator_and_a_batch(string operatorName, string batch) =>
        Assert.ThrowsAny<ArgumentException>(
            () => new SqliteInspectionStore(path).Save(Plan, Parts(("P1", 12.009m, 85m)), operatorName, batch, 0.8m, Monday));

    [Fact]
    public void Save_needs_at_least_one_part() =>
        Assert.Throws<ArgumentException>(() => new SqliteInspectionStore(path).Save(Plan, [], "Ali", "B-1", 0.8m, Monday));

    [Fact]
    public void Loading_an_unknown_inspection_says_so() =>
        Assert.Throws<KeyNotFoundException>(() => new SqliteInspectionStore(path).Load(42));

    [Fact]
    public void Sample_data_round_trips_with_the_same_verdicts()
    {
        var samples = Path.Combine(AppContext.BaseDirectory, "samples");
        var plan = InspectionPlanReader.ReadFile(Path.Combine(samples, "bracket-plan.json"));
        var parts = new Inspector(plan, new ToleranceChecker())
            .InspectAll(MeasurementCsvReader.ReadFile(Path.Combine(samples, "bracket-measurements.csv")));
        var store = new SqliteInspectionStore(path);

        var saved = store.Load(store.Save(plan, parts, "Ali", "2611-A", 0.8m, Monday).Id);

        Assert.Equal(parts.Select(p => p.Verdict), saved.Parts.Select(p => p.Verdict));
        Assert.Equal(
            parts.SelectMany(p => p.Results).Select(r => r.MeasuredValue),
            saved.Parts.SelectMany(p => p.Results).Select(r => r.MeasuredValue));
    }

    private static List<PartInspection> Parts(params (string Serial, decimal? Bore, decimal? Length)[] parts)
    {
        var inspector = new Inspector(Plan, new ToleranceChecker());
        return parts.Select(p =>
        {
            var readings = new Dictionary<Feature, decimal>();
            if (p.Bore is { } bore)
                readings[Bore] = bore;
            if (p.Length is { } length)
                readings[Length] = length;
            return inspector.Inspect(p.Serial, readings);
        }).ToList();
    }
}
