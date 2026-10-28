using System;

namespace Jellyfin.Plugin.SmartAudio.Configuration;

/// <summary>
/// One entry of the milestone 1 spike rule: a user and the audio language they start on.
/// </summary>
public class SpikeUserLanguage
{
    /// <summary>
    /// Gets or sets the user's ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the audio language, as an ISO 639-2 tag (<c>jpn</c>).
    /// </summary>
    public string Language { get; set; } = string.Empty;
}
