using System.Linq;
using Jellyfin.Plugin.SmartAudio.Playback;
using Jellyfin.Plugin.SmartAudio.Tests.Stubs;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jellyfin.Plugin.SmartAudio.Tests;

public class PluginServiceRegistratorTests
{
    [Fact]
    public void AddsPlaybackInfoFilterToMvcOptions()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, ServerStub.Create<IServerApplicationHost>());

        using var provider = services.AddOptions().BuildServiceProvider();
        var filters = provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters;
        Assert.Contains(filters.OfType<TypeFilterAttribute>(), filter => filter.ImplementationType == typeof(PlaybackInfoFilter));
    }
}
