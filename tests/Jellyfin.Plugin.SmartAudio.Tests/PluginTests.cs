using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.SmartAudio.Tests;

public class PluginTests
{
    [Fact]
    public void PluginIdMatchesBuildYaml()
    {
        Assert.Equal(Guid.Parse("26618d45-9cdd-4fda-8779-5baa1ec293b1"), Guid.Parse(Plugin.PluginId));
    }

    [Fact]
    public void ConfigPageIsEmbedded()
    {
        using Stream? page = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.SmartAudio.Configuration.configPage.html");

        Assert.NotNull(page);
    }
}
