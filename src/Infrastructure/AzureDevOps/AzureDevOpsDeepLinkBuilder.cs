using BuildMonitor.Core.Models;
using BuildMonitor.Core.Settings;

namespace BuildMonitor.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsDeepLinkBuilder(AzureDevOpsSettings settings)
{
    public string BuildRunUrl(PipelineSnapshot pipeline) =>
        $"{settings.OrganizationUrl.TrimEnd('/')}/{settings.Project}/_build/results?buildId={pipeline.RunId}&view=results";

    public string StageUrl(PipelineSnapshot pipeline, StageSnapshot stage)
    {
        var stageFilter = Uri.EscapeDataString(stage.StageName);
        return $"{settings.OrganizationUrl.TrimEnd('/')}/{settings.Project}/_build/results?buildId={pipeline.RunId}&view=results&j={stageFilter}&t={stageFilter}";
    }

    public string Resolve(PipelineSnapshot pipeline, StageSnapshot? stage) =>
        stage is null ? BuildRunUrl(pipeline) : StageUrl(pipeline, stage);
}
