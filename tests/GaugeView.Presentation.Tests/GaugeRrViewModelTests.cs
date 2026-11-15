using System.Globalization;
using GaugeView.Core;

namespace GaugeView.Presentation.Tests;

public class GaugeRrViewModelTests
{
    private static readonly string Samples = Path.Combine(AppContext.BaseDirectory, "samples");

    [Fact]
    public void Load_sample_also_analyses_the_sample_gauge_study_against_the_bore_tolerance()
    {
        var vm = new MainViewModel(new RecordingDialogs(), Samples);

        vm.LoadSampleCommand.Execute(null);

        var grr = vm.GaugeRr;
        Assert.Equal(GaugeRrViewModel.SampleStudyFeature, grr.SelectedFeature?.Name);
        Assert.Equal(0.018m, decimal.Parse(grr.ToleranceText, CultureInfo.CurrentCulture));
        Assert.Equal(GrrVerdict.Marginal, grr.Result?.Verdict);
        Assert.StartsWith("Marginal", grr.Message);
        Assert.Contains("10 parts × 3 operators × 3 trials", grr.StudyTitle);
        Assert.Contains("pooled", grr.InteractionNote);
    }

    [Fact]
    public void Changing_the_tolerance_re_analyses_the_study()
    {
        var grr = LoadedSample();

        grr.ToleranceText = "0.05";

        Assert.Equal(GrrVerdict.Acceptable, grr.Result?.Verdict);
    }

    [Fact]
    public void Picking_another_feature_takes_its_tolerance()
    {
        var vm = new MainViewModel(new RecordingDialogs(), Samples);
        vm.LoadSampleCommand.Execute(null);

        vm.GaugeRr.SelectedFeature = vm.GaugeRr.Features.Single(f => f.Name == "Overall length");

        Assert.Equal(0.20m, decimal.Parse(vm.GaugeRr.ToleranceText, CultureInfo.CurrentCulture));
        Assert.Equal(GrrVerdict.Acceptable, vm.GaugeRr.Result?.Verdict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-0.01")]
    public void Missing_or_invalid_tolerance_explains_what_to_do(string tolerance)
    {
        var grr = LoadedSample();

        grr.ToleranceText = tolerance;

        Assert.False(grr.HasResult);
        Assert.StartsWith("Enter the tolerance band", grr.Message);
    }

    [Fact]
    public void Unbalanced_study_reports_the_problem_instead_of_crashing()
    {
        var grr = new GaugeRrViewModel(new RecordingDialogs()) { ToleranceText = "0.1" };

        grr.LoadStudy("Short study",
        [
            new GrrReading("1", "A", 1.00m), new GrrReading("1", "A", 1.01m),
            new GrrReading("1", "B", 1.00m), new GrrReading("2", "A", 1.02m),
            new GrrReading("2", "A", 1.03m), new GrrReading("2", "B", 1.02m),
        ]);

        Assert.False(grr.HasResult);
        Assert.Contains("same number of times", grr.Message);
    }

    [Fact]
    public void Bad_study_file_shows_an_error()
    {
        var dialogs = new RecordingDialogs { OpenPath = Path.Combine(Path.GetTempPath(), $"gaugeview-{Guid.NewGuid():N}.csv") };
        File.WriteAllText(dialogs.OpenPath, "part,operator\n1,A");
        try
        {
            var grr = new GaugeRrViewModel(dialogs);

            grr.ImportStudyCommand.Execute(null);

            Assert.Equal("Could not import the gauge study", dialogs.LastErrorTitle);
            Assert.False(grr.HasResult);
        }
        finally
        {
            File.Delete(dialogs.OpenPath);
        }
    }

    private static GaugeRrViewModel LoadedSample()
    {
        var grr = new GaugeRrViewModel(new RecordingDialogs(), Samples);
        grr.LoadSampleStudy();
        Assert.True(grr.HasResult);
        return grr;
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public string? OpenPath { get; init; }

        public string? LastErrorTitle { get; private set; }

        public string? PickFileToOpen(string title, string filter) => OpenPath;

        public string? PickFileToSave(string title, string filter, string suggestedName) => null;

        public void ShowError(string title, string message) => LastErrorTitle = title;
    }
}
