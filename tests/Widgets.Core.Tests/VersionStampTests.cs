using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Widgets.Core.Tests;

/// <summary>Runs build/Set-Version.ps1 against copies of the three files a release stamps and checks what changed.</summary>
public sealed class VersionStampTests : IDisposable
{
    private const string Manifest = "src/Widgets.WidBar/Package.appxmanifest";
    private const string Project = "src/Widgets.WidBar/Widgets.WidBar.csproj";
    private const string Plugin = "src/Widgets.WidBar/MainPlugin.cs";
    private static readonly string[] Files = [Manifest, Project, Plugin];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "stamp-" + Guid.NewGuid().ToString("N"));

    public VersionStampTests()
    {
        foreach (var file in Files)
        {
            var target = Path.Combine(_root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(RepoFiles.PathOf(file), target);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { /* a temp folder left behind is harmless */ }
    }

    private string Text(string file) => File.ReadAllText(Path.Combine(_root, file.Replace('/', Path.DirectorySeparatorChar)));
    private byte[] Bytes(string file) => File.ReadAllBytes(Path.Combine(_root, file.Replace('/', Path.DirectorySeparatorChar)));

    private (int Code, string Output) Run(string version)
    {
        var info = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", RepoFiles.PathOf("build/Set-Version.ps1"), "-Root", _root, "-Version", version })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [Fact]
    public void StampsTheManifestTheProjectAndThePluginClass()
    {
        var (code, output) = Run("0.3.2");
        Assert.Equal(0, code);
        Assert.Matches(@"<Identity\b[^>]*\bVersion=""0\.3\.2\.0""", Text(Manifest).Replace("\n", " "));
        Assert.Contains("<WidBarPluginVersion>0.3.2</WidBarPluginVersion>", Text(Project));
        Assert.Contains("override string Version => \"0.3.2\";", Text(Plugin));
        Assert.Contains("version=0.3.2", output);
        Assert.Contains("package_version=0.3.2.0", output);
        Assert.Contains("prerelease=false", output);
    }

    [Fact]
    public void AcceptsALeadingV()
    {
        var (code, output) = Run("v1.2.3");
        Assert.Equal(0, code);
        Assert.Contains("override string Version => \"1.2.3\";", Text(Plugin));
        Assert.Contains("package_version=1.2.3.0", output);
    }

    [Fact]
    public void ASuffixIsDroppedFromTheStampAndFlagsAPrerelease()
    {
        var (code, output) = Run("1.2.3-beta.1");
        Assert.Equal(0, code);
        Assert.Contains("<WidBarPluginVersion>1.2.3</WidBarPluginVersion>", Text(Project));
        Assert.Contains("prerelease=true", output);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("70000.0.0")]
    [InlineData("01.2.3")]
    [InlineData("١.٢.٣")]
    [InlineData("")]
    public void ABadVersionFailsAndLeavesTheFilesUntouched(string version)
    {
        var before = Files.Select(Bytes).ToArray();
        var (code, _) = Run(version);
        Assert.NotEqual(0, code);
        Assert.Equal(before, Files.Select(Bytes).ToArray());
    }

    [Fact]
    public void RunningTwiceGivesTheSameFiles()
    {
        Run("0.4.0");
        var once = Files.Select(Bytes).ToArray();
        var (code, _) = Run("0.4.0");
        Assert.Equal(0, code);
        Assert.Equal(once, Files.Select(Bytes).ToArray());
    }

    [Fact]
    public void ChangesOnlyTheVersionValuesAndKeepsEveryOtherByte()
    {
        var latin1 = System.Text.Encoding.Latin1;
        var expected = new Dictionary<string, byte[]>();
        foreach (var file in Files)
        {
            var text = latin1.GetString(Bytes(file));   // Latin-1 maps every byte to one char, so BOM and line endings survive
            text = Regex.Replace(text, @"(?<=override string Version => "")[^""]+", "9.8.7");
            text = Regex.Replace(text, @"(?<=<WidBarPluginVersion>)[^<]+", "9.8.7");
            text = Regex.Replace(text, @"(?<=<Identity\b[^>]*?\bVersion="")[^""]+", "9.8.7.0", RegexOptions.Singleline);
            expected[file] = latin1.GetBytes(text);
        }
        Assert.Equal(0, Run("9.8.7").Code);
        foreach (var file in Files)
            Assert.Equal(expected[file], Bytes(file));
    }
}
