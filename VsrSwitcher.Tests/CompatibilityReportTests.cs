using VsrSwitcher.Core;
using static VsrSwitcher.Tests.VsrStateTests;

namespace VsrSwitcher.Tests;

public class CompatibilityReportTests
{
    [Theory]
    [InlineData(CompatibilityVerdict.Compatible, true)]
    [InlineData(CompatibilityVerdict.CompatibleWithWarnings, true)]
    [InlineData(CompatibilityVerdict.NotCompatible, false)]
    [InlineData(CompatibilityVerdict.Unusable, false)]
    [InlineData(CompatibilityVerdict.Unavailable, false)]
    public void Only_compatible_verdicts_allow_writes(CompatibilityVerdict verdict, bool allowed)
    {
        Assert.Equal(allowed, new CompatibilityReport(State(), [], verdict).IsCompatible);
    }

    [Fact]
    public void Failures_and_warnings_are_counted()
    {
        CompatibilityCheck[] checks =
        [
            new(CheckOutcome.Pass, "a"),
            new(CheckOutcome.Warn, "b"),
            new(CheckOutcome.Fail, "c"),
            new(CheckOutcome.Fail, "d"),
        ];

        var report = new CompatibilityReport(State(), checks, CompatibilityVerdict.NotCompatible);

        Assert.Equal(2, report.Failures);
        Assert.Equal(1, report.Warnings);
    }
}
