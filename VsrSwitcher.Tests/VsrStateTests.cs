using VsrSwitcher.Core;

namespace VsrSwitcher.Tests;

public class VsrStateTests
{
    internal static VsrState State(SuperResValue? superRes = null, GpuUtilization? gpu = null, int? flags = null) => new()
    {
        Adapter = "0001",
        RegistryPath = @"HKLM\test",
        SuperRes = superRes,
        GpuUtil = gpu,
        RtxVideoFlags = flags,
        LooksSupported = true,
    };

    [Fact]
    public void An_absent_value_means_off()
    {
        var state = State(superRes: null);

        Assert.Equal(SuperResValue.Off, state.EffectiveSuperRes);
        Assert.False(state.Enabled);
    }

    [Theory]
    [InlineData(SuperResValue.Auto)]
    [InlineData(SuperResValue.Level1)]
    [InlineData(SuperResValue.Level4)]
    public void Any_level_but_off_is_enabled(SuperResValue level)
    {
        Assert.True(State(level).Enabled);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0x000, false)]
    [InlineData(0x0FF, false)]
    [InlineData(0x100, true)]
    [InlineData(0x1FF, true)]
    public void The_indicator_is_bit_0x100_alone(int? flags, bool shown)
    {
        Assert.Equal(shown, State(flags: flags).IndicatorShown);
    }

    [Fact]
    public void Live_values_agree_when_level_and_flags_match()
    {
        var state = State(SuperResValue.Level3, GpuUtilization.High, 0x100);

        Assert.True(new VsrLiveState(3, 2, 0x100, "NvCpl.dll").Agrees(state));
        Assert.False(new VsrLiveState(4, 2, 0x100, "NvCpl.dll").Agrees(state));
        Assert.False(new VsrLiveState(3, 2, 0x000, "NvCpl.dll").Agrees(state));
    }

    [Fact]
    public void Live_off_agrees_with_an_absent_registry_value()
    {
        Assert.True(new VsrLiveState(0, 0, 0, "NvCpl.dll").Agrees(State()));
    }
}
