using System.Text.Json;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class ControlPlaneOperationOutcomeMapperTests
{
    [Fact]
    public void Rebuild_success_is_succeeded()
    {
        var outcome = ControlPlaneOperationOutcomeMapper.FromRebuild(buildOk: true);
        Assert.Equal(ControlPlaneOperationOutcome.Succeeded, outcome);
        Assert.True(ControlPlaneOperationOutcomeMapper.IsOkConsistent(ok: true, outcome));
    }

    [Fact]
    public void Rebuild_fail_is_buildFailed()
    {
        var outcome = ControlPlaneOperationOutcomeMapper.FromRebuild(buildOk: false);
        Assert.Equal(ControlPlaneOperationOutcome.BuildFailed, outcome);
        Assert.True(ControlPlaneOperationOutcomeMapper.IsOkConsistent(ok: false, outcome));
    }

    [Fact]
    public void Tests_success_is_succeeded()
    {
        var evidence = new ControlPlaneTestPhaseEvidence(
            LifecycleTestOk: true,
            NoTargetsConfigured: false,
            Counts: new ControlPlaneTestCounts(0, 10, 1));
        var outcome = ControlPlaneOperationOutcomeMapper.FromTests(evidence);
        Assert.Equal(ControlPlaneOperationOutcome.Succeeded, outcome);
        Assert.True(ControlPlaneOperationOutcomeMapper.IsOkConsistent(ok: true, outcome));
    }

    [Fact]
    public void Tests_failed_count_is_testsFailed()
    {
        var evidence = new ControlPlaneTestPhaseEvidence(
            LifecycleTestOk: false,
            NoTargetsConfigured: false,
            Counts: new ControlPlaneTestCounts(2, 8, 0));
        Assert.Equal(
            ControlPlaneOperationOutcome.TestsFailed,
            ControlPlaneOperationOutcomeMapper.FromTests(evidence));
    }

    [Fact]
    public void Tests_zero_targets_is_noTests()
    {
        var evidence = new ControlPlaneTestPhaseEvidence(
            LifecycleTestOk: false,
            NoTargetsConfigured: true,
            Counts: null);
        Assert.Equal(
            ControlPlaneOperationOutcome.NoTests,
            ControlPlaneOperationOutcomeMapper.FromTests(evidence));
    }

    [Fact]
    public void Tests_terminal_failure_without_execution_counts_is_executionFailed()
    {
        var evidence = new ControlPlaneTestPhaseEvidence(
            LifecycleTestOk: false,
            NoTargetsConfigured: false,
            Counts: null);
        Assert.Equal(
            ControlPlaneOperationOutcome.ExecutionFailed,
            ControlPlaneOperationOutcomeMapper.FromTests(evidence));
    }

    [Fact]
    public void Ship_check_zero_targets_is_succeeded_not_noTests()
    {
        var outcome = ControlPlaneOperationOutcomeMapper.FromShipCheck(
            buildOk: true,
            noTestTargetsConfigured: true,
            testEvidence: null);
        Assert.Equal(ControlPlaneOperationOutcome.Succeeded, outcome);
    }

    [Fact]
    public void Ship_check_build_fail_is_buildFailed()
    {
        Assert.Equal(
            ControlPlaneOperationOutcome.BuildFailed,
            ControlPlaneOperationOutcomeMapper.FromShipCheck(
                buildOk: false,
                noTestTargetsConfigured: false,
                testEvidence: null));
    }

    [Fact]
    public void Ship_check_test_fail_matches_tests_mapper()
    {
        var evidence = new ControlPlaneTestPhaseEvidence(
            LifecycleTestOk: false,
            NoTargetsConfigured: false,
            Counts: new ControlPlaneTestCounts(1, 5, 0));
        var fromTests = ControlPlaneOperationOutcomeMapper.FromTests(evidence);
        var fromShip = ControlPlaneOperationOutcomeMapper.FromShipCheck(
            buildOk: true,
            noTestTargetsConfigured: false,
            testEvidence: evidence);
        Assert.Equal(ControlPlaneOperationOutcome.TestsFailed, fromTests);
        Assert.Equal(fromTests, fromShip);
    }

    [Fact]
    public void Mapper_does_not_classify_noTests_from_prose_alone()
    {
        // failures[] may contain discovery prose; without NoTargetsConfigured the outcome is not noTests.
        var evidence = new ControlPlaneTestPhaseEvidence(
            LifecycleTestOk: false,
            NoTargetsConfigured: false,
            Counts: null);
        Assert.Equal(
            ControlPlaneOperationOutcome.ExecutionFailed,
            ControlPlaneOperationOutcomeMapper.FromTests(evidence));

        var mapperPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Core", "Rules", "ControlPlaneOperationOutcomeMapper.cs"));
        Assert.True(File.Exists(mapperPath), mapperPath);
        var source = File.ReadAllText(mapperPath);
        Assert.DoesNotContain("No tests found", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No test is available", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failures.Contains", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure.Contains", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Outcome_enum_serializes_camelCase()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        Assert.Equal("\"succeeded\"", JsonSerializer.Serialize(ControlPlaneOperationOutcome.Succeeded, options));
        Assert.Equal("\"buildFailed\"", JsonSerializer.Serialize(ControlPlaneOperationOutcome.BuildFailed, options));
        Assert.Equal("\"testsFailed\"", JsonSerializer.Serialize(ControlPlaneOperationOutcome.TestsFailed, options));
        Assert.Equal("\"noTests\"", JsonSerializer.Serialize(ControlPlaneOperationOutcome.NoTests, options));
        Assert.Equal(
            "\"executionFailed\"",
            JsonSerializer.Serialize(ControlPlaneOperationOutcome.ExecutionFailed, options));
    }

    [Fact]
    public void Repeated_classification_does_not_leak_prior_outcome()
    {
        var first = ControlPlaneOperationOutcomeMapper.FromTests(
            new ControlPlaneTestPhaseEvidence(true, false, new ControlPlaneTestCounts(0, 100, 0)));
        var second = ControlPlaneOperationOutcomeMapper.FromTests(
            new ControlPlaneTestPhaseEvidence(false, false, new ControlPlaneTestCounts(1, 2, 0)));
        Assert.Equal(ControlPlaneOperationOutcome.Succeeded, first);
        Assert.Equal(ControlPlaneOperationOutcome.TestsFailed, second);
        Assert.NotEqual(first, second);
    }
}
