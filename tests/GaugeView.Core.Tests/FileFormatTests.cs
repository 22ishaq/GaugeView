using System.Globalization;
using GaugeView.Core;
using GaugeView.Core.IO;

namespace GaugeView.Core.Tests;

internal static class TestValues
{
    public static decimal D(string text) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static string SamplePath(string file) => Path.Combine(AppContext.BaseDirectory, "samples", file);
}

public class MeasurementCsvReaderTests
{
    private static IReadOnlyList<Measurement> Read(string csv) => MeasurementCsvReader.Read(new StringReader(csv));

    [Fact]
    public void Reads_columns_in_any_order_and_skips_comments_and_blank_lines()
    {
        var rows = Read("""
            # exported from CMM 2
            Value,Serial,FEATURE

            12.009,BRK-0001,Bore Ø12 H7
            85.02,BRK-0001,Overall length
            """);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new Measurement("BRK-0001", "Bore Ø12 H7", 12.009m), rows[0]);
    }

    [Fact]
    public void Quoted_fields_may_contain_commas_and_quotes()
    {
        var rows = Read("serial,feature,value\nP1,\"Hole \"\"A\"\", position 2\",40.01\n");
        Assert.Equal("Hole \"A\", position 2", Assert.Single(rows).FeatureName);
    }

    [Fact]
    public void Numbers_use_a_full_stop_whatever_the_regional_settings()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(12.005m, Assert.Single(Read("serial,feature,value\nP1,Bore,12.005")).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("serial,feature,value\nP1,Bore,twelve", "Line 2")]
    [InlineData("serial,feature,value\nP1,Bore", "Line 2")]
    [InlineData("serial,feature,value\n,Bore,12.0", "Line 2")]
    [InlineData("serial,feature\nP1,Bore", "Line 1")]
    [InlineData("serial,feature,value\nP1,\"Bore,12.0", "not closed")]
    [InlineData("", "empty")]
    public void Malformed_input_says_what_is_wrong(string csv, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read(csv));
        Assert.Contains(expected, ex.Message);
    }
}

public class InspectionPlanReaderTests
{
    [Fact]
    public void Reads_features_with_signed_deviations()
    {
        var plan = InspectionPlanReader.Read("""
            {
              // comments and trailing commas are allowed
              "partNumber": "BRK-1042",
              "description": "Sensor mounting bracket",
              "features": [
                { "name": "Spigot Ø8 g6", "nominal": 8.000, "upper": -0.005, "lower": -0.014 },
              ]
            }
            """);

        Assert.Equal("BRK-1042", plan.PartNumber);
        Assert.Equal("mm", plan.Unit);
        var spigot = Assert.Single(plan.Features);
        Assert.Equal(7.986m, spigot.Tolerance.LowerLimit);
        Assert.Equal(7.995m, spigot.Tolerance.UpperLimit);
    }

    [Theory]
    [InlineData("""{ "features": [ { "name": "A", "nominal": 1, "upper": 0.1, "lower": -0.1 } ] }""", "partNumber")]
    [InlineData("""{ "partNumber": "X", "features": [] }""", "at least one feature")]
    [InlineData("""{ "partNumber": "X", "features": [ { "nominal": 1, "upper": 0.1, "lower": -0.1 } ] }""", "Feature 1 needs a name")]
    [InlineData("""{ "partNumber": "X", "features": [ { "name": "A", "nominal": 1 } ] }""", "nominal, upper and lower")]
    [InlineData("""{ "partNumber": "X", "features": [ { "name": "A", "nominal": 1, "upper": -0.1, "lower": 0.1 } ] }""", "Feature 'A'")]
    [InlineData("""{ "partNumber": "X", "features": [ { "name": "A", "nominal": 1, "upper": 0.1, "lower": -0.1 }, { "name": "a", "nominal": 2, "upper": 0.1, "lower": -0.1 } ] }""", "more than once")]
    [InlineData("{ not json", "not valid JSON")]
    public void Rejects_incomplete_plans(string json, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => InspectionPlanReader.Read(json));
        Assert.Contains(expected, ex.Message);
    }
}

public class InspectionReportWriterTests
{
    [Fact]
    public void Writes_one_row_per_feature_with_invariant_numbers()
    {
        var feature = new Feature("Hole \"A\", position 2", Tolerance.Symmetric(40.00m, 0.05m));
        var part = new PartInspection("P1", [new ToleranceChecker().Check(feature, 40.045m)]);
        var writer = new StringWriter();

        InspectionReportWriter.Write(writer, [part]);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(InspectionReportWriter.Header, lines[0]);
        Assert.Equal("P1,WARNING,\"Hole \"\"A\"\", position 2\",40.00,39.95,40.05,40.045,0.045,90.0,WARNING", lines[1]);
    }
}

public class SampleDataTests
{
    [Fact]
    public void Sample_bracket_data_gives_the_summary_in_the_readme()
    {
        var plan = InspectionPlanReader.ReadFile(TestValues.SamplePath("bracket-plan.json"));
        var measurements = MeasurementCsvReader.ReadFile(TestValues.SamplePath("bracket-measurements.csv"));

        var parts = new Inspector(plan, new ToleranceChecker()).InspectAll(measurements);

        Assert.Equal(12, parts.Count);
        Assert.Equal(5, parts.Count(p => p.Verdict == InspectionStatus.Pass));
        Assert.Equal(3, parts.Count(p => p.Verdict == InspectionStatus.Warning));
        Assert.Equal(3, parts.Count(p => p.Verdict == InspectionStatus.Fail));
        Assert.Single(parts, p => p.Verdict == InspectionStatus.Missing);
    }
}
