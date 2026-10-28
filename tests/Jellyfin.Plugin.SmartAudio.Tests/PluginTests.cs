using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.SmartAudio.Tests.Stubs;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Xunit;

namespace Jellyfin.Plugin.SmartAudio.Tests;

public class PluginTests
{
    [Fact]
    public void PluginIdMatchesBuildYaml()
    {
        string guidLine = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "build.yaml"))
            .Single(line => line.StartsWith("guid:", StringComparison.Ordinal));
        string buildYamlId = guidLine["guid:".Length..].Trim().Trim('"');

        Assert.Equal(Guid.Parse(buildYamlId), Guid.Parse(Plugin.PluginId));
    }

    [Fact]
    public void EveryConfigPageIsEmbedded()
    {
        var plugin = new Plugin(ServerStub.Create<IApplicationPaths>(), ServerStub.Create<IXmlSerializer>());

        PluginPageInfo[] pages = plugin.GetPages().ToArray();

        Assert.NotEmpty(pages);
        Assert.All(pages, page =>
        {
            using Stream? resource = typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath);
            Assert.NotNull(resource);
        });
    }
}
