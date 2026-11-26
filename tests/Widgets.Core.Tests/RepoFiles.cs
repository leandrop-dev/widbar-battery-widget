namespace Widgets.Core.Tests;

/// <summary>Finds files of the repository from a test, for the tests that check the packaging files agree with each other.</summary>
internal static class RepoFiles
{
    private static readonly Lazy<string?> Root = new(FindRoot);

    /// <summary>True when the repository can be found from where the tests run (not so in a packaged test run).</summary>
    public static bool Available => Root.Value is not null;

    /// <summary>The full text of a repository file, given its path relative to the repository root.</summary>
    public static string Read(string relative)
    {
        var root = Root.Value ?? throw new InvalidOperationException("The repository root was not found from " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string? FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WidBarBatteryWidget.slnx"))) return dir.FullName;
        return null;
    }
}
