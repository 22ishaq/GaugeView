using GaugeView.Core;
using static GaugeView.Core.Tests.TestValues;

namespace GaugeView.Core.Tests;

public class ToleranceTests
{
    private static readonly Tolerance BoreH7 = new(12.000m, 0.018m, 0.000m);
    private static readonly Tolerance SpigotG6 = new(8.000m, -0.005m, -0.014m);

    [Fact]
    public void Limits_come_from_nominal_plus_signed_deviations()
    {
        Assert.Equal(12.000m, BoreH7.LowerLimit);
        Assert.Equal(12.018m, BoreH7.UpperLimit);
        Assert.Equal(7.986m, SpigotG6.LowerLimit);
        Assert.Equal(7.995m, SpigotG6.UpperLimit);
    }

    [Theory]
    [InlineData("12.000", true)]
    [InlineData("12.018", true)]
    [InlineData("12.009", true)]
    [InlineData("11.999", false)]
    [InlineData("12.0181", false)]
    public void Limits_are_inclusive(string reading, bool expected) =>
        Assert.Equal(expected, BoreH7.Contains(D(reading)));

    [Fact]
    public void Reading_on_the_limit_passes_where_double_arithmetic_would_fail_it()
    {
        double nominal = 0.7, plus = 0.1;
        Assert.True(nominal + plus < 0.8); // 0.7999999999999999 in binary floating point

        Assert.True(Tolerance.Symmetric(0.7m, 0.1m).Contains(0.8m));
    }

    [Fact]
    public void Fraction_used_is_measured_from_the_middle_of_the_zone()
    {
        Assert.Equal(12.009m, BoreH7.MidLimit);
        Assert.Equal(0m, BoreH7.FractionUsed(12.009m));
        Assert.Equal(1m, BoreH7.FractionUsed(12.018m));
        Assert.Equal(1m, BoreH7.FractionUsed(12.000m)); // an H7 bore's nominal sits on its lower limit
        Assert.True(BoreH7.FractionUsed(12.020m) > 1m);
    }

    [Theory]
    [InlineData("0.05", "0.05")]
    [InlineData("0.05", "0.06")]
    public void Upper_deviation_must_be_above_lower_deviation(string upper, string lower) =>
        Assert.Throws<ArgumentException>(() => new Tolerance(10m, D(upper), D(lower)));

    [Fact]
    public void Formats_like_a_drawing_callout()
    {
        Assert.Equal("12.000 +0.018/0", BoreH7.ToString());
        Assert.Equal("8.000 -0.005/-0.014", SpigotG6.ToString());
    }
}
