using GaugeView.Core;
using GaugeView.Core.IO;

namespace GaugeView.Core.Tests;

// Expected values were computed independently with NumPy and scipy.stats.f.
public class GaugeRrTests
{
    // 4 parts × 2 operators × 2 trials, where operator B reads parts 3 and 4 very differently from A.
    private const string InteractionStudy = """
        part,operator,value
        1,A,10.001
        1,A,10.000
        2,A,10.021
        2,A,10.019
        3,A,10.040
        3,A,10.041
        4,A,10.060
        4,A,10.059
        1,B,10.002
        1,B,10.001
        2,B,10.020
        2,B,10.022
        3,B,10.081
        3,B,10.079
        4,B,10.031
        4,B,10.032
        """;

    private static GaugeRrResult Sample(decimal tolerance = 0.018m) =>
        GaugeRrCalculator.Calculate(GaugeStudyCsvReader.ReadFile(TestValues.SamplePath("bore-gauge-study.csv")), tolerance);

    [Fact]
    public void Sample_bore_gauge_is_marginal()
    {
        var result = Sample();

        Assert.Equal((10, 3, 3, 90), (result.PartCount, result.OperatorCount, result.TrialCount, result.ReadingCount));
        Assert.Equal(15.6527, result.PercentOfTolerance, 4);
        Assert.Equal(13.5703, result.PercentStudyVariation, 4);
        Assert.Equal(10, result.DistinctCategories);
        Assert.Equal(GrrVerdict.Marginal, result.Verdict);
    }

    [Fact]
    public void Sample_interaction_is_not_significant_so_it_is_pooled()
    {
        var result = Sample();

        Assert.True(result.InteractionRemoved);
        Assert.Equal(0.704955, result.InteractionP!.Value, 6);
        Assert.Equal(0, result.InteractionVariance);
        Assert.Equal(new[] { "Part", "Operator", "Repeatability", "Total" }, result.Anova.Select(a => a.Source));

        var repeatability = result.Anova[2];
        Assert.Equal(78, repeatability.DegreesOfFreedom); // 18 interaction df pooled with 60 repeatability df
        Assert.Equal(1.0325641e-7, repeatability.MeanSquare!.Value, 13);
        Assert.Equal(35.0659, result.Anova[1].F!.Value, 4);
    }

    [Fact]
    public void Sample_variance_components_match_the_reference()
    {
        var result = Sample();

        Assert.Equal(1.0325641e-7, result.RepeatabilityVariance, 13);
        Assert.Equal(1.1725071e-7, result.OperatorVariance, 13);
        Assert.Equal(1.1753597e-5, result.PartVariance, 11);
    }

    [Fact]
    public void Operator_summary_shows_who_reads_high()
    {
        var calum = Sample().Operators.Single(o => o.Name == "Calum");

        Assert.Equal(12.009337, calum.Mean, 6);
        Assert.Equal(0.0004, calum.Bias, 4);
    }

    [Fact]
    public void Components_add_up()
    {
        var result = Sample();
        var byName = result.Components.ToDictionary(c => c.Source);

        Assert.Equal(100, byName["Total gauge R&R"].PercentContribution + byName["Part-to-part"].PercentContribution, 9);
        Assert.Equal(100, byName["Total variation"].PercentStudyVariation, 9);
        Assert.Equal(result.PercentOfTolerance, byName["Total gauge R&R"].PercentOfTolerance, 9);
        Assert.Equal(6 * byName["Repeatability (equipment)"].StandardDeviation, byName["Repeatability (equipment)"].StudyVariation, 12);
    }

    [Fact]
    public void Significant_interaction_is_kept_in_reproducibility()
    {
        var result = GaugeRrCalculator.Calculate(GaugeStudyCsvReader.Read(new StringReader(InteractionStudy)), 0.2m);

        Assert.False(result.InteractionRemoved);
        Assert.True(result.InteractionP < 1e-9);
        var interaction = result.Anova.Single(a => a.Source == "Part × operator");
        Assert.Equal(721.7843, interaction.F!.Value, 4);
        Assert.Equal(3.8291667e-4, result.InteractionVariance, 10);
        Assert.Equal(0, result.OperatorVariance); // the estimate is negative, so it is set to zero
        Assert.Equal(58.7862, result.PercentOfTolerance, 4);
        Assert.Equal(1, result.DistinctCategories);
        Assert.Equal(GrrVerdict.Unacceptable, result.Verdict);
    }

    [Theory]
    [InlineData("0.05", GrrVerdict.Acceptable)]    // 5.6% of tolerance
    [InlineData("0.018", GrrVerdict.Marginal)]     // 15.7%
    [InlineData("0.009", GrrVerdict.Unacceptable)] // 31.3%
    public void Verdict_uses_the_10_and_30_percent_bands(string tolerance, GrrVerdict expected) =>
        Assert.Equal(expected, Sample(TestValues.D(tolerance)).Verdict);

    [Fact]
    public void Unbalanced_study_says_what_is_missing()
    {
        var readings = GaugeStudyCsvReader.Read(new StringReader(InteractionStudy)).Skip(1);

        var ex = Assert.Throws<InvalidDataException>(() => GaugeRrCalculator.Calculate(readings, 0.2m));
        Assert.Contains("same number of times", ex.Message);
    }

    [Theory]
    [InlineData("part,operator,value\n1,A,1.0\n1,A,1.1\n2,A,1.2\n2,A,1.3", "two operators")]
    [InlineData("part,operator,value\n1,A,1.0\n1,B,1.1\n1,A,1.2\n1,B,1.3", "two parts")]
    [InlineData("part,operator,value\n1,A,1.0\n1,B,1.1\n2,A,1.2\n2,B,1.3", "two trials")]
    public void Study_too_small_to_analyse_is_rejected(string csv, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => GaugeRrCalculator.Calculate(GaugeStudyCsvReader.Read(new StringReader(csv)), 1m));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Tolerance_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GaugeRrCalculator.Calculate([], 0m));
}

public class FDistributionTests
{
    [Theory]
    [InlineData(4.1028, 2, 10, 0.0500006)] // the 5% critical value of F(2, 10)
    [InlineData(1.0, 1, 1, 0.5)]
    [InlineData(2.5, 3, 20, 0.0888438)]
    [InlineData(0.0, 4, 9, 1.0)]
    public void Upper_tail_matches_scipy(double f, int df1, int df2, double expected) =>
        Assert.Equal(expected, FDistribution.UpperTail(f, df1, df2), 7);

    [Fact]
    public void Very_large_f_has_a_tiny_p_value() =>
        Assert.Equal(6.7858e-12, FDistribution.UpperTail(35.0658886, 2, 87), 15);

    [Fact]
    public void Log_gamma_of_an_integer_is_the_log_of_a_factorial() =>
        Assert.Equal(Math.Log(24), FDistribution.LogGamma(5), 12);
}

public class GaugeStudyCsvReaderTests
{
    [Fact]
    public void Reads_columns_in_any_order_and_ignores_extra_columns()
    {
        var rows = GaugeStudyCsvReader.Read(new StringReader("""
            # study on gauge 7
            Trial,Value,Operator,Part
            1,12.0043,Ali,1
            """));

        Assert.Equal(new GrrReading("1", "Ali", 12.0043m), Assert.Single(rows));
    }

    [Theory]
    [InlineData("part,value\n1,12.0", "Line 1")]
    [InlineData("part,operator,value\n1,Ali,12.0\n2,Ali,twelve", "Line 3")]
    [InlineData("part,operator,value\n1,,12.0", "must not be empty")]
    public void Malformed_input_says_what_is_wrong(string csv, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => GaugeStudyCsvReader.Read(new StringReader(csv)));
        Assert.Contains(expected, ex.Message);
    }
}
