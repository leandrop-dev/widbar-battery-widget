using System.Text.RegularExpressions;

namespace Widgets.Core.Tests;

public class PackagingTests
{
    private const string PluginId = "com.codemonk.devicebattery";

    private static string Capture(string file, string pattern)
    {
        var match = Regex.Match(RepoFiles.Read(file), pattern);
        Assert.True(match.Success, $"{file} does not match {pattern}");
        return match.Groups[1].Value;
    }

    private static (string Class, string Manifest, string Project) Ids() => (
        Capture("src/Widgets.WidBar/MainPlugin.cs", "Id => \"([^\"]+)\""),
        Capture("src/Widgets.WidBar/Package.appxmanifest", "<WidBarPluginId>([^<]+)</WidBarPluginId>"),
        Capture("src/Widgets.WidBar/Widgets.WidBar.csproj", "<WidBarPluginId>([^<]+)</WidBarPluginId>"));

    [Fact]
    public void PluginIdIsTheSameInTheClassTheManifestAndTheProject()
    {
        if (!RepoFiles.Available) return; // a packaged test run has no source tree to read
        var (cls, manifest, project) = Ids();
        Assert.Equal(PluginId, cls);
        Assert.Equal(PluginId, manifest);
        Assert.Equal(PluginId, project);
    }

    [Fact]
    public void PluginIdIsLowerCaseReverseDns()
    {
        if (!RepoFiles.Available) return;
        Assert.Matches(@"^[a-z0-9]+(\.[a-z0-9]+)+$", Ids().Class);
    }
}
