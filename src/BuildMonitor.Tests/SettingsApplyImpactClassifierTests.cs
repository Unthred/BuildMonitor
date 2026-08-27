using System.Collections;
using System.Reflection;
using BuildMonitor.Core.Models;
using BuildMonitor.Core.Rules;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Tests;

public sealed class SettingsApplyImpactClassifierTests
{
    [Fact]
    public void Catalog_covers_every_discovered_persisted_leaf_path()
    {
        var discovered = SettingsPersistedPropertyDiscovery.DiscoverLeafPaths();
        var catalogued = SettingsApplyImpactCatalog.Paths;

        var missingFromCatalog = discovered.Where(p => !catalogued.Contains(p)).ToList();
        var extraInCatalog = catalogued.Where(p => !discovered.Contains(p)).ToList();

        Assert.True(
            missingFromCatalog.Count == 0,
            "Persisted settings missing from SettingsApplyImpactCatalog (add an Entry):\n"
            + string.Join("\n", missingFromCatalog));
        Assert.True(
            extraInCatalog.Count == 0,
            "Catalog paths not discovered on AppSettings (remove or fix path):\n"
            + string.Join("\n", extraInCatalog));
    }

    [Fact]
    public void Catalog_has_no_duplicate_paths()
    {
        var dupes = SettingsApplyImpactCatalog.All
            .GroupBy(e => e.Path, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(dupes.Count == 0, "Duplicate catalog paths: " + string.Join(", ", dupes));
    }

    [Fact]
    public void Identical_settings_are_none_and_do_not_restart()
    {
        var settings = SampleSettings();
        var plan = SettingsApplyImpactClassifier.CreatePlan(settings, Clone(settings));
        Assert.Equal(SettingsApplyImpact.None, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
        Assert.False(plan.ApplyOrchestratorSettings);
        Assert.False(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void TrayMenuLayout_only_is_presentation_with_zero_restarts()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.AppBehavior.TrayMenuLayout = TrayMenuLayout.ByProject;

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.Presentation, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
        Assert.False(plan.ApplyOrchestratorSettings);
        Assert.False(plan.ResetHealthTransitionState);
        Assert.False(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void Theme_only_is_presentation()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.AppBehavior.Theme = AppThemePreference.Dark;

        Assert.Equal(
            SettingsApplyImpact.Presentation,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Azure_attachment_only_is_soft_runtime_without_local_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Azure = new AzureDevOpsProjectAttachment
        {
            ConnectionId = "c1",
            AdoProjectId = "p1",
            AdoProjectName = "P",
            RepositoryId = "r1",
            RepositoryName = "Repo"
        };

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
        Assert.True(plan.ApplyOrchestratorSettings);
        Assert.False(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void Adding_azure_only_project_is_soft_runtime_not_hard_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects.Add(new MonitoredProjectSettings
        {
            Id = "azure-only",
            DisplayName = "Azure only",
            IsActiveInSession = true,
            Local = null,
            Azure = new AzureDevOpsProjectAttachment
            {
                ConnectionId = "c1",
                AdoProjectId = "p1",
                AdoProjectName = "P",
                RepositoryId = "r1",
                RepositoryName = "Repo"
            }
        });

        Assert.Equal(
            SettingsApplyImpact.SoftRuntime,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Monitor_debounce_only_is_soft_runtime()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Monitor.FileChangeDebounceMs = 9_000;

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
    }

    [Fact]
    public void Local_project_file_change_is_hard_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Local!.ProjectFile = "Other.csproj";

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.HardRestart, plan.Impact);
        Assert.True(plan.StopAllAndRestartActiveProjects);
        Assert.True(plan.ApplyOrchestratorSettings);
        Assert.True(plan.ShowProjectsStartingToast);
    }

    [Fact]
    public void Active_session_toggle_on_local_project_is_hard_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].IsActiveInSession = false;

        Assert.Equal(
            SettingsApplyImpact.HardRestart,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Active_session_toggle_on_azure_only_project_is_soft_runtime()
    {
        var before = SampleSettings();
        before.Projects.Add(new MonitoredProjectSettings
        {
            Id = "azure-only",
            DisplayName = "Azure only",
            IsActiveInSession = true,
            Local = null,
            Azure = new AzureDevOpsProjectAttachment
            {
                ConnectionId = "c1",
                AdoProjectId = "p1",
                AdoProjectName = "P",
                RepositoryId = "r1",
                RepositoryName = "Repo"
            }
        });
        var after = Clone(before);
        after.Projects.Single(p => p.Id == "azure-only").IsActiveInSession = false;

        Assert.Equal(
            SettingsApplyImpact.SoftRuntime,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Local_ui_preference_auto_open_log_is_soft_runtime()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Local!.RunOptions.AutoOpenLog = AutoOpenLogMode.Errors;

        Assert.Equal(
            SettingsApplyImpact.SoftRuntime,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Ai_controlled_mode_change_is_soft_runtime()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Local!.BuildControlMode = ProjectBuildControlMode.AiControlled;

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
        Assert.True(plan.ApplyOrchestratorSettings);
    }

    [Fact]
    public void RunTests_and_TestProjectFile_are_soft_runtime_without_local_rebuild()
    {
        var before = SampleSettings();
        var afterTests = Clone(before);
        afterTests.Projects[0].Local!.RunOptions.RunTests = TestRunTrigger.OnBuildSuccess;
        Assert.Equal(
            SettingsApplyImpact.SoftRuntime,
            SettingsApplyImpactClassifier.Classify(before, afterTests));

        var afterTarget = Clone(before);
        afterTarget.Projects[0].Local!.TestProjectFile = "Other.Tests.csproj";
        Assert.Equal(
            SettingsApplyImpact.SoftRuntime,
            SettingsApplyImpactClassifier.Classify(before, afterTarget));
    }

    [Fact]
    public void Restart_policy_flags_are_soft_runtime()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Local!.RunOptions.RestartOnCrash = true;
        after.Projects[0].Local!.RunOptions.MaxRestartRetries = 9;

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
    }

    [Fact]
    public void RunMode_change_is_hard_restart()
    {
        var before = SampleSettings();
        // Default RunMode is Watch — flip to Run so the hard fingerprint changes.
        Assert.Equal(ProjectRunMode.Watch, before.Projects[0].Local!.RunOptions.RunMode);
        var after = Clone(before);
        after.Projects[0].Local!.RunOptions.RunMode = ProjectRunMode.Run;

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.HardRestart, plan.Impact);
        Assert.True(plan.StopAllAndRestartActiveProjects);
    }

    [Fact]
    public void Watch_exclude_segments_change_is_hard_restart()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].Local!.RunOptions.WatchExcludeSegments = "bin;obj;custom";

        Assert.Equal(
            SettingsApplyImpact.HardRestart,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    [Fact]
    public void Null_before_is_hard_restart_like_cold_start()
    {
        Assert.Equal(
            SettingsApplyImpact.HardRestart,
            SettingsApplyImpactClassifier.Classify(null, SampleSettings()));
    }

    [Fact]
    public void Display_name_only_is_soft_runtime()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.Projects[0].DisplayName = "Renamed";

        var plan = SettingsApplyImpactClassifier.CreatePlan(before, after);
        Assert.Equal(SettingsApplyImpact.SoftRuntime, plan.Impact);
        Assert.False(plan.StopAllAndRestartActiveProjects);
    }

    [Fact]
    public void Schema_version_only_is_none()
    {
        var before = SampleSettings();
        var after = Clone(before);
        after.SchemaVersion = 99;

        Assert.Equal(
            SettingsApplyImpact.None,
            SettingsApplyImpactClassifier.Classify(before, after));
    }

    public static TheoryData<string, SettingsApplyImpact> CatalogMutationCases()
    {
        var data = new TheoryData<string, SettingsApplyImpact>();
        foreach (var entry in SettingsApplyImpactCatalog.All)
        {
            // IsActiveInSession is Hard when Local exists; Soft for Azure-only — tested separately.
            if (entry.Path == "Projects[].IsActiveInSession")
            {
                continue;
            }

            // SchemaVersion alone → None (catalog Impact None)
            data.Add(entry.Path, entry.Impact);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogMutationCases))]
    public void Mutating_each_catalog_path_yields_declared_impact(string path, SettingsApplyImpact expected)
    {
        var before = RichSampleSettings();
        var after = Clone(before);
        SettingsPathMutator.Mutate(after, path);

        var actual = SettingsApplyImpactClassifier.Classify(before, after);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_hard_restart_catalog_entry_has_non_empty_rationale()
    {
        foreach (var entry in SettingsApplyImpactCatalog.All.Where(e => e.Impact == SettingsApplyImpact.HardRestart))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Rationale), entry.Path);
        }
    }

    private static AppSettings SampleSettings() => new()
    {
        Projects =
        [
            new MonitoredProjectSettings
            {
                Id = "proj1",
                DisplayName = "WitherbyConnect (main)",
                IsActiveInSession = true,
                Local = new LocalProjectAttachment
                {
                    RootFolder = @"C:\src\WitherbyConnectDotNet9",
                    ProjectFile = "WitherbyConnect.csproj",
                    BuildControlMode = ProjectBuildControlMode.FileWatching,
                    StartOnLaunch = true
                }
            }
        ],
        Monitor = new GlobalMonitorSettings(),
        AppBehavior = new AppBehaviorSettings
        {
            TrayMenuLayout = TrayMenuLayout.ByOperation
        }
    };

    /// <summary>Fixture with Local + Azure + connection so every catalog path can be mutated.</summary>
    private static AppSettings RichSampleSettings()
    {
        var settings = SampleSettings();
        settings.Connections =
        [
            new AzureDevOpsConnectionSettings
            {
                Id = "conn1",
                DisplayName = "Org",
                OrganizationUrl = "https://dev.azure.com/org"
            }
        ];
        settings.Projects[0].Azure = new AzureDevOpsProjectAttachment
        {
            ConnectionId = "conn1",
            AdoProjectId = "p1",
            AdoProjectName = "P",
            RepositoryId = "r1",
            RepositoryName = "Repo",
            RepositoryRemoteUrl = "https://dev.azure.com/org/P/_git/Repo",
            DefaultBranch = "main",
            ExtraWatchedBranches = ["develop"],
            Pipelines =
            [
                new AzurePipelineSelection
                {
                    DefinitionId = 1,
                    DisplayName = "CI",
                    IncludedBranches = ["main"],
                    NotificationMode = NotificationMode.FailuresAndRecovery,
                    Priority = 1
                }
            ]
        };
        return settings;
    }

    private static AppSettings Clone(AppSettings source) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            System.Text.Json.JsonSerializer.Serialize(source))
        ?? new AppSettings();
}

/// <summary>Mutates a single catalog path on an <see cref="AppSettings"/> graph for coverage tests.</summary>
internal static class SettingsPathMutator
{
    public static void Mutate(AppSettings settings, string path)
    {
        var segments = path.Split('.');
        MutateAt(settings, segments, 0);
    }

    private static void MutateAt(object target, string[] segments, int index)
    {
        var raw = segments[index];
        var isList = raw.EndsWith("[]", StringComparison.Ordinal);
        var name = isList ? raw[..^2] : raw;
        var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                   ?? throw new InvalidOperationException($"Missing property {name} on {target.GetType().Name} for path {string.Join('.', segments)}");

        if (index == segments.Length - 1)
        {
            if (isList)
            {
                MutateStringList(prop.GetValue(target));
                return;
            }

            SetAlteredValue(target, prop);
            return;
        }

        if (isList)
        {
            var list = prop.GetValue(target) as IList
                       ?? throw new InvalidOperationException($"Expected list at {name}");
            if (list.Count == 0)
            {
                throw new InvalidOperationException($"Empty list at {name}; enrich fixture.");
            }

            MutateAt(list[0]!, segments, index + 1);
            return;
        }

        var child = prop.GetValue(target)
                    ?? throw new InvalidOperationException($"Null child at {name}; enrich fixture.");
        MutateAt(child, segments, index + 1);
    }

    private static void MutateStringList(object? listObj)
    {
        if (listObj is not IList list)
        {
            throw new InvalidOperationException("Expected string list.");
        }

        list.Add("mutated-branch-" + Guid.NewGuid().ToString("N")[..8]);
    }

    private static void SetAlteredValue(object target, PropertyInfo prop)
    {
        var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        object? next;
        if (type == typeof(string))
        {
            next = (prop.GetValue(target) as string ?? "") + "-x";
        }
        else if (type == typeof(bool))
        {
            next = !(bool)(prop.GetValue(target) ?? false);
        }
        else if (type == typeof(int))
        {
            next = (int)(prop.GetValue(target) ?? 0) + 1;
        }
        else if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            var current = prop.GetValue(target) ?? values.GetValue(0)!;
            var i = Array.IndexOf(values, current);
            next = values.GetValue((i + 1) % values.Length)!;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported leaf type {type.Name}");
        }

        prop.SetValue(target, next);
    }
}
