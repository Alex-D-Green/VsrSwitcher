using VsrSwitcher.Core;

namespace VsrSwitcher.Tests;

public class LabelsTests
{
    [Theory]
    [InlineData("off", SuperResValue.Off)]
    [InlineData("0", SuperResValue.Off)]
    [InlineData("auto", SuperResValue.Auto)]
    [InlineData("5", SuperResValue.Auto)]
    [InlineData("1", SuperResValue.Level1)]
    [InlineData("verylow", SuperResValue.Level1)]
    [InlineData("very-low", SuperResValue.Level1)]
    [InlineData("Very Low", SuperResValue.Level1)]
    [InlineData("vlow", SuperResValue.Level1)]
    [InlineData("2", SuperResValue.Level2)]
    [InlineData("low", SuperResValue.Level2)]
    [InlineData("3", SuperResValue.Level3)]
    [InlineData("medium", SuperResValue.Level3)]
    [InlineData("med", SuperResValue.Level3)]
    [InlineData("4", SuperResValue.Level4)]
    [InlineData(" HIGH ", SuperResValue.Level4)]
    public void Quality_levels_parse_with_their_aliases(string text, SuperResValue expected)
    {
        Assert.True(Labels.TryParseSuperRes(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("6")]
    [InlineData("ultra")]
    [InlineData("hi")]
    public void Unknown_quality_levels_are_rejected(string text)
    {
        Assert.False(Labels.TryParseSuperRes(text, out _));
    }

    [Theory]
    [InlineData("low", GpuUtilization.Low)]
    [InlineData("0", GpuUtilization.Low)]
    [InlineData("medium", GpuUtilization.Medium)]
    [InlineData("med", GpuUtilization.Medium)]
    [InlineData("1", GpuUtilization.Medium)]
    [InlineData("high", GpuUtilization.High)]
    [InlineData("2", GpuUtilization.High)]
    public void Gpu_utilization_parses(string text, GpuUtilization expected)
    {
        Assert.True(Labels.TryParseGpuUtil(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("3")]
    public void Unknown_gpu_utilization_is_rejected(string text)
    {
        Assert.False(Labels.TryParseGpuUtil(text, out _));
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("show", true)]
    [InlineData("shown", true)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("hide", false)]
    [InlineData("hidden", false)]
    public void Switches_parse(string text, bool expected)
    {
        Assert.True(Labels.TryParseSwitch(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("maybe")]
    public void Unknown_switches_are_rejected(string text)
    {
        Assert.False(Labels.TryParseSwitch(text, out _));
    }

    [Fact]
    public void Every_short_name_parses_back_to_its_level()
    {
        foreach (var level in Enum.GetValues<SuperResValue>())
        {
            Assert.True(Labels.TryParseSuperRes(Labels.Short(level), out var parsed));
            Assert.Equal(level, parsed);
        }
    }

    [Theory]
    [InlineData(SuperResValue.Off, "off")]
    [InlineData(SuperResValue.Auto, "on, auto")]
    [InlineData(SuperResValue.Level1, "on, quality 1 (very low)")]
    [InlineData(SuperResValue.Level4, "on, quality 4 (high)")]
    public void Levels_are_described_as_nvidia_app_names_them(SuperResValue level, string expected)
    {
        Assert.Equal(expected, Labels.Describe(level));
    }
}
