namespace GaugeView.Core;

/// <summary>
/// The F distribution's upper tail, for ANOVA p-values. Implemented with the regularized incomplete beta
/// function (continued fraction, Lentz's method) and a Lanczos log-gamma, so Core needs no maths package.
/// </summary>
public static class FDistribution
{
    private static readonly double[] Lanczos =
    [
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
        1.5056327351493116e-7,
    ];

    /// <summary>P(F &gt; <paramref name="f"/>) for an F distribution with the given degrees of freedom.</summary>
    public static double UpperTail(double f, double numeratorDf, double denominatorDf)
    {
        if (numeratorDf <= 0 || denominatorDf <= 0)
            throw new ArgumentOutOfRangeException(nameof(numeratorDf), "Degrees of freedom must be positive.");
        if (double.IsNaN(f))
            return double.NaN;
        if (f <= 0)
            return 1;
        if (double.IsPositiveInfinity(f))
            return 0;

        var x = denominatorDf / (denominatorDf + numeratorDf * f);
        return RegularizedBeta(x, denominatorDf / 2, numeratorDf / 2);
    }

    /// <summary>The regularized incomplete beta function I_x(a, b).</summary>
    public static double RegularizedBeta(double x, double a, double b)
    {
        if (x <= 0)
            return 0;
        if (x >= 1)
            return 1;

        var logFront = LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x);

        // The continued fraction converges fastest below the mean; use the symmetry I_x(a,b) = 1 − I_(1−x)(b,a) above it.
        return x < (a + 1) / (a + b + 2)
            ? Math.Exp(logFront) * BetaContinuedFraction(x, a, b) / a
            : 1 - Math.Exp(logFront) * BetaContinuedFraction(1 - x, b, a) / b;
    }

    /// <summary>ln Γ(x) by the Lanczos approximation (g = 7), accurate to about 15 significant figures.</summary>
    public static double LogGamma(double x)
    {
        if (x < 0.5)
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);

        x -= 1;
        var sum = Lanczos[0];
        for (var i = 1; i < Lanczos.Length; i++)
            sum += Lanczos[i] / (x + i);

        var t = x + 7.5;
        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(sum);
    }

    private static double BetaContinuedFraction(double x, double a, double b)
    {
        const int maxIterations = 500;
        const double epsilon = 1e-15;
        const double tiny = 1e-300;

        double qab = a + b, qap = a + 1, qam = a - 1;
        var c = 1.0;
        var d = 1 - qab * x / qap;
        d = 1 / (Math.Abs(d) < tiny ? tiny : d);
        var h = d;

        for (var m = 1; m <= maxIterations; m++)
        {
            var m2 = 2 * m;

            var even = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + even * d;
            d = 1 / (Math.Abs(d) < tiny ? tiny : d);
            c = 1 + even / c;
            c = Math.Abs(c) < tiny ? tiny : c;
            h *= d * c;

            var odd = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + odd * d;
            d = 1 / (Math.Abs(d) < tiny ? tiny : d);
            c = 1 + odd / c;
            c = Math.Abs(c) < tiny ? tiny : c;
            var delta = d * c;
            h *= delta;

            if (Math.Abs(delta - 1) < epsilon)
                break;
        }

        return h;
    }
}
