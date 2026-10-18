using VsrSwitcher.Core;
using static VsrSwitcher.Tests.VsrStateTests;

namespace VsrSwitcher.Tests;

public class VsrChangeTests
{
    [Fact]
    public void Asking_for_the_current_level_writes_nothing()
    {
        var change = VsrChange.ForSuperResolution(State(SuperResValue.Level3), SuperResValue.Level3);

        Assert.True(change.IsNoOp);
        Assert.Empty(change.Writes);
        Assert.Contains("already", change.Summary);
    }

    [Fact]
    public void Turning_off_writes_zero_and_remembers_the_level_being_left()
    {
        var change = VsrChange.ForSuperResolution(State(SuperResValue.Level3), SuperResValue.Off);

        Assert.Equal([new SettingWrite(VsrCpl.SettingSuperResolution, 0)], change.Writes);
        Assert.Equal(SuperResValue.Level3, change.QualityToRemember);
    }

    [Fact]
    public void Turning_on_or_changing_level_remembers_nothing()
    {
        var on = VsrChange.ForSuperResolution(State(SuperResValue.Off), SuperResValue.Auto);
        var level = VsrChange.ForSuperResolution(State(SuperResValue.Level2), SuperResValue.Level4);

        Assert.Equal([new SettingWrite(VsrCpl.SettingSuperResolution, 5)], on.Writes);
        Assert.Null(on.QualityToRemember);
        Assert.Equal([new SettingWrite(VsrCpl.SettingSuperResolution, 4)], level.Writes);
        Assert.Null(level.QualityToRemember);
    }

    [Fact]
    public void Gpu_utilization_writes_its_own_setting()
    {
        var change = VsrChange.ForGpuUtilization(State(gpu: GpuUtilization.High), GpuUtilization.Medium);

        Assert.Equal([new SettingWrite(VsrCpl.SettingGpuUtilization, 1)], change.Writes);
    }

    [Theory]
    [InlineData(0x005, true, 0x105)]
    [InlineData(0x105, false, 0x005)]
    [InlineData(null, true, 0x100)]
    public void The_indicator_changes_only_its_own_bit(int? flags, bool show, int expected)
    {
        // The other bits carry settings whose positions are unknown: they must survive untouched.
        var change = VsrChange.ForIndicator(State(flags: flags), show);

        Assert.Equal([new SettingWrite(VsrCpl.SettingRtxVideoFlags, expected)], change.Writes);
    }

    [Fact]
    public void Asking_for_the_current_indicator_state_writes_nothing()
    {
        Assert.True(VsrChange.ForIndicator(State(flags: 0x100), show: true).IsNoOp);
        Assert.True(VsrChange.ForIndicator(State(flags: 0x005), show: false).IsNoOp);
    }
}
