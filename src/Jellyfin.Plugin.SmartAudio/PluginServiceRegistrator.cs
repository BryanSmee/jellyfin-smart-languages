using Jellyfin.Plugin.SmartAudio.Playback;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SmartAudio;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Plugins register before the server's AddMvc, so this composes with the server's own MVC options.
        serviceCollection.Configure<MvcOptions>(options => options.Filters.Add<PlaybackInfoFilter>());
    }
}
