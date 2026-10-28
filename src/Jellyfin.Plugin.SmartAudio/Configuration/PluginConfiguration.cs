using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartAudio.Configuration;

/// <summary>
/// Plugin configuration. Only holds the milestone 1 spike rule until the server default profile lands (SPEC.md milestone 3).
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the spike rule: the audio language each listed user starts on.
    /// Throwaway, replaced by the real profiles in milestone 2.
    /// </summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Set by the XML and JSON serializers.")]
    public Collection<SpikeUserLanguage> SpikeUserLanguages { get; set; } = [];
}
