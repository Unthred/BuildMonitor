using System.Text.RegularExpressions;

namespace BuildMonitor.Infrastructure.LocalBuild;

public static class BuildPlanResolver
{
    private static readonly Regex ProjectReferenceRegex = new(
        @"<ProjectReference\s+Include\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Resolve(string rootFolder, string projectFile)
    {
        var path = Path.IsPathRooted(projectFile)
            ? projectFile
            : Path.Combine(rootFolder, projectFile);

        if (!File.Exists(path))
        {
            return [Path.GetFileNameWithoutExtension(projectFile)];
        }

        if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSolution(path);
        }

        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return ParseProjectGraph(path);
        }

        return [Path.GetFileNameWithoutExtension(path)];
    }

    private static List<string> ParseSolution(string slnPath)
    {
        var names = new List<string>();
        foreach (var line in File.ReadAllLines(slnPath))
        {
            if (!line.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var firstQuote = line.IndexOf('"');
            if (firstQuote < 0)
            {
                continue;
            }

            var secondQuote = line.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0)
            {
                continue;
            }

            var name = line[(firstQuote + 1)..secondQuote];
            if (name.Equals("Solution Items", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            names.Add(name);
        }

        return names.Count > 0
            ? names
            : [Path.GetFileNameWithoutExtension(slnPath)];
    }

    private static List<string> ParseProjectGraph(string csprojPath)
    {
        var ordered = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitProject(csprojPath, ordered, visited);
        return ordered;
    }

    private static void VisitProject(string csprojPath, List<string> ordered, HashSet<string> visited)
    {
        var fullPath = Path.GetFullPath(csprojPath);
        if (!visited.Add(fullPath))
        {
            return;
        }

        foreach (var reference in ReadProjectReferences(fullPath))
        {
            VisitProject(reference, ordered, visited);
        }

        ordered.Add(Path.GetFileNameWithoutExtension(fullPath));
    }

    private static IEnumerable<string> ReadProjectReferences(string csprojPath)
    {
        var directory = Path.GetDirectoryName(csprojPath) ?? string.Empty;
        var text = File.ReadAllText(csprojPath);

        foreach (Match match in ProjectReferenceRegex.Matches(text))
        {
            var include = match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
            var referencePath = Path.GetFullPath(Path.Combine(directory, include));
            if (File.Exists(referencePath))
            {
                yield return referencePath;
            }
        }
    }
}
