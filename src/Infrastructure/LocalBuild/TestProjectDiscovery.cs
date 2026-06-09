namespace BuildMonitor.Infrastructure.LocalBuild;

public sealed record TestTargetResolution(
    IReadOnlyList<string> Targets,
    bool AutoDiscovered,
    string DiscoveryNote);

public static class TestProjectDiscovery
{
    private static readonly string[] ExcludedPathSegments = ["\\bin\\", "\\obj\\", "/bin/", "/obj/"];

    public static TestTargetResolution Resolve(string rootFolder, string projectFile, string? testProjectFile)
    {
        if (!string.IsNullOrWhiteSpace(testProjectFile))
        {
            var explicitPath = ResolvePath(rootFolder, testProjectFile.Trim());
            if (File.Exists(explicitPath))
            {
                return new TestTargetResolution([explicitPath], false, "using Test project / solution from settings");
            }
        }

        var mainPath = ResolvePath(rootFolder, projectFile);
        if (File.Exists(mainPath) && IsTestProject(mainPath))
        {
            return new TestTargetResolution([mainPath], false, "main project file is a test project");
        }

        var solutions = FindSolutions(rootFolder);
        var bestSolution = PickBestSolution(solutions, rootFolder, mainPath);
        if (bestSolution is not null)
        {
            return new TestTargetResolution(
                [bestSolution],
                true,
                $"auto-detected solution ({Path.GetFileName(bestSolution)}) — app .csproj is not a test project");
        }

        var testProjects = FindTestProjects(rootFolder);
        if (testProjects.Count == 1)
        {
            return new TestTargetResolution(
                [testProjects[0]],
                true,
                $"auto-detected test project ({Path.GetFileName(testProjects[0])})");
        }

        if (testProjects.Count > 1)
        {
            var names = string.Join(", ", testProjects.Select(Path.GetFileName));
            return new TestTargetResolution(
                testProjects,
                true,
                $"auto-detected {testProjects.Count} test projects ({names})");
        }

        return new TestTargetResolution(
            File.Exists(mainPath) ? [mainPath] : [],
            false,
            "no solution or test project found — set Test project / solution in settings");
    }

    public static IReadOnlyList<string> DiscoverCandidates(string rootFolder, string projectFile)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return [];
        }

        var candidates = new List<string>();
        candidates.AddRange(FindSolutions(rootFolder));
        candidates.AddRange(FindTestProjects(rootFolder));

        var mainPath = ResolvePath(rootFolder, projectFile);
        if (File.Exists(mainPath) && !candidates.Contains(mainPath, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(mainPath);
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsTestProject(string csprojPath)
    {
        if (!File.Exists(csprojPath)
            || !csprojPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(csprojPath);
            if (text.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return text.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("xunit", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("NUnit", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("MSTest", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> FindSolutions(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
        {
            return [];
        }

        var results = new List<string>();
        foreach (var pattern in new[] { "*.sln", "*.slnx" })
        {
            try
            {
                results.AddRange(Directory.EnumerateFiles(rootFolder, pattern, SearchOption.TopDirectoryOnly));
            }
            catch
            {
                // ignore unreadable folders
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> FindTestProjects(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
        {
            return [];
        }

        var results = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(rootFolder, "*.csproj", SearchOption.AllDirectories))
            {
                if (IsExcludedPath(file) || !IsTestProject(file))
                {
                    continue;
                }

                results.Add(file);
            }
        }
        catch
        {
            return [];
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? PickBestSolution(IReadOnlyList<string> solutions, string rootFolder, string mainPath)
    {
        if (solutions.Count == 0)
        {
            return null;
        }

        if (solutions.Count == 1)
        {
            return solutions[0];
        }

        var folderName = Path.GetFileName(rootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var byName = solutions.FirstOrDefault(s =>
            Path.GetFileNameWithoutExtension(s).Equals(folderName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        var mainName = Path.GetFileNameWithoutExtension(mainPath);
        byName = solutions.FirstOrDefault(s =>
            Path.GetFileNameWithoutExtension(s).Equals(mainName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        return solutions
            .OrderBy(s => Path.GetExtension(s).Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static bool IsExcludedPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        return ExcludedPathSegments.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePath(string rootFolder, string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.Combine(rootFolder, path);
}
