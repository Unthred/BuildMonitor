using BuildMonitor.Core.Models;
using BuildMonitor.Infrastructure.ControlPlane;
using BuildMonitor.Infrastructure.LocalBuild;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneTestResultMapperTests
{
    [Fact]
    public void MapCompletedTestPhase_success_maps_real_counts()
    {
        var summary = new DotNetTestSummary(1083, 1083, 0, 0, "20 s", null);
        var failures = new List<string>();

        var (ok, counts) = ControlPlaneTestResultMapper.MapCompletedTestPhase(
            summary,
            lifecycleTestOk: true,
            failures);

        Assert.True(ok);
        Assert.NotNull(counts);
        Assert.Equal(1083, counts!.Passed);
        Assert.Equal(0, counts.Failed);
        Assert.Equal(0, counts.Skipped);
        Assert.Empty(failures);
    }

    [Fact]
    public void MapCompletedTestPhase_assertion_failures_map_real_counts()
    {
        var summary = new DotNetTestSummary(10, 8, 2, 0, null, null);
        var failures = new List<string> { "SampleTests.FailingTest — assert" };

        var (ok, counts) = ControlPlaneTestResultMapper.MapCompletedTestPhase(
            summary,
            lifecycleTestOk: false,
            failures);

        Assert.False(ok);
        Assert.NotNull(counts);
        Assert.Equal(8, counts!.Passed);
        Assert.Equal(2, counts.Failed);
        Assert.Equal(0, counts.Skipped);
        Assert.DoesNotContain(ControlPlaneTestResultMapper.CountsUnavailableMessage, failures);
    }

    [Fact]
    public void MapCompletedTestPhase_null_summary_success_omits_counts_and_does_not_fabricate_zeros()
    {
        var failures = new List<string>();

        var (ok, counts) = ControlPlaneTestResultMapper.MapCompletedTestPhase(
            summary: null,
            lifecycleTestOk: true,
            failures);

        Assert.True(ok);
        Assert.Null(counts);
        Assert.Contains(ControlPlaneTestResultMapper.CountsUnavailableMessage, failures);
    }

    [Fact]
    public void MapCompletedTestPhase_null_summary_failure_preserves_ok_false()
    {
        var failures = new List<string> { "CS0001" };

        var (ok, counts) = ControlPlaneTestResultMapper.MapCompletedTestPhase(
            summary: null,
            lifecycleTestOk: false,
            failures);

        Assert.False(ok);
        Assert.Null(counts);
        Assert.Contains(ControlPlaneTestResultMapper.CountsUnavailableMessage, failures);
    }

    [Fact]
    public void MapCompletedTestPhase_ship_check_and_run_tests_share_same_mapping()
    {
        var summary = new DotNetTestSummary(5, 4, 0, 1, null, null);
        var runFailures = new List<string>();
        var shipFailures = new List<string>();

        var run = ControlPlaneTestResultMapper.MapCompletedTestPhase(summary, true, runFailures);
        var ship = ControlPlaneTestResultMapper.MapCompletedTestPhase(summary, true, shipFailures);

        Assert.Equal(run.TestsOk, ship.TestsOk);
        Assert.Equal(run.Counts, ship.Counts);
    }

    [Fact]
    public void Repeated_mapping_does_not_leak_prior_counts()
    {
        var first = new DotNetTestSummary(100, 100, 0, 0, null, null);
        var second = new DotNetTestSummary(3, 2, 1, 0, null, null);
        var failures = new List<string>();

        var a = ControlPlaneTestResultMapper.MapCompletedTestPhase(first, true, failures);
        failures.Clear();
        var b = ControlPlaneTestResultMapper.MapCompletedTestPhase(second, false, failures);

        Assert.Equal(100, a.Counts!.Passed);
        Assert.Equal(2, b.Counts!.Passed);
        Assert.Equal(1, b.Counts.Failed);
        Assert.NotEqual(a.Counts.Passed, b.Counts.Passed);
    }
}
