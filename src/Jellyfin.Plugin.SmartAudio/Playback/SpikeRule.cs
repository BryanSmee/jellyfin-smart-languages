using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.SmartAudio.Playback;

/// <summary>
/// Milestone 1 spike rule: the first audio stream in the user's language.
/// Throwaway, replaced by the classifier and <c>SelectAudio</c> in milestone 2.
/// </summary>
public static class SpikeRule
{
    /// <summary>
    /// Finds the first audio stream with the given language.
    /// </summary>
    /// <param name="streams">The media source's streams.</param>
    /// <param name="language">The ISO 639-2 language tag.</param>
    /// <returns>The stream's index, or <c>null</c> when no audio stream has that language.</returns>
    public static int? PickAudioStream(IEnumerable<MediaStream> streams, string language)
    {
        return streams
            .Where(stream => stream.Type == MediaStreamType.Audio && string.Equals(stream.Language, language, StringComparison.OrdinalIgnoreCase))
            .Select(stream => (int?)stream.Index)
            .FirstOrDefault();
    }
}
