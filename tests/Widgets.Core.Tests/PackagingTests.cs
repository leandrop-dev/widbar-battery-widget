using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Widgets.Core.Tests;

public class PackagingTests
{
    private const string PluginId = "com.codemonk.devicebattery";

    /// <summary>The id of the plugin class: the one `override string Id =>` of MainPlugin.cs.</summary>
    private static string ClassId()
    {
        var matches = Regex.Matches(RepoFiles.Read("src/Widgets.WidBar/MainPlugin.cs"), @"override\s+string\s+Id\s*=>\s*""([^""]+)""");
        Assert.Single(matches);
        return matches[0].Groups[1].Value;
    }

    /// <summary>The value of the one WidBarPluginId element of an XML file; comments are ignored and a second element fails.</summary>
    private static string XmlId(string file) =>
        XDocument.Parse(RepoFiles.Read(file)).Descendants().Single(e => e.Name.LocalName == "WidBarPluginId").Value.Trim();

    [Fact]
    public void PluginIdIsTheSameInTheClassTheManifestAndTheProject()
    {
        Assert.Equal(PluginId, ClassId());
        Assert.Equal(PluginId, XmlId("src/Widgets.WidBar/Package.appxmanifest"));
        Assert.Equal(PluginId, XmlId("src/Widgets.WidBar/Widgets.WidBar.csproj"));
    }

    [Fact]
    public void PluginIdIsLowerCaseReverseDns() => Assert.Matches(@"^[a-z0-9]+(\.[a-z0-9]+)+$", ClassId());
}
