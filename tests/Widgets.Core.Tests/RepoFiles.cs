namespace Widgets.Core.Tests;

/// <summary>Finds files of the repository from a test, for the tests that check the packaging files agree with each other.</summary>
internal static class RepoFiles
{
    private static readonly Lazy<string?> Root = new(FindRoot);

    /// <summary>The full text of a repository file, given its path relative to the repository root. Fails the test when the repository cannot be found.</summary>
    public static string Read(string relative)
    {
        var root = Root.Value ?? throw new InvalidOperationException("The repository root was not found from " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    // The solution file marks the root; a .git entry does too, so renaming the solution does not switch the checks off.
    private static string? FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.GetFiles(dir.FullName, "*.slnx").Length > 0 || Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        return null;
    }
}
