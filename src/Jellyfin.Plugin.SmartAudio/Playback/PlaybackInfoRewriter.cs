using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.SmartAudio.Playback;

/// <summary>
/// Rewrites a <see cref="PlaybackInfoResponse"/> so clients start on the plugin's audio stream (SPEC.md, Mechanism A).
/// </summary>
public static partial class PlaybackInfoRewriter
{
    /// <summary>
    /// Points each media source at the picked audio stream, and moves the sources with a pick first.
    /// </summary>
    /// <remarks>
    /// The server bakes its own pick into <see cref="MediaSourceInfo.TranscodingUrl"/> as <c>AudioStreamIndex</c>,
    /// and web clients follow that URL when transcoding, so the URL is rewritten along with
    /// <see cref="MediaSourceInfo.DefaultAudioStreamIndex"/>.
    /// </remarks>
    /// <param name="response">The response, changed in place.</param>
    /// <param name="pickAudioStream">The audio stream index for a media source, or <c>null</c> to leave it alone.</param>
    /// <returns>The media sources that changed, in their original order.</returns>
    public static IReadOnlyList<AudioStreamRewrite> Rewrite(PlaybackInfoResponse response, Func<MediaSourceInfo, int?> pickAudioStream)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(pickAudioStream);

        var rewrites = new List<AudioStreamRewrite>();
        var picked = new List<MediaSourceInfo>();
        var others = new List<MediaSourceInfo>();
        foreach (var source in response.MediaSources)
        {
            int? index = pickAudioStream(source);
            if (index is null)
            {
                others.Add(source);
                continue;
            }

            rewrites.Add(new AudioStreamRewrite(source.Id, source.DefaultAudioStreamIndex, index.Value));
            source.DefaultAudioStreamIndex = index.Value;
            if (source.TranscodingUrl is not null)
            {
                source.TranscodingUrl = SetAudioStreamIndex(source.TranscodingUrl, index.Value);
            }

            picked.Add(source);
        }

        if (picked.Count > 0 && others.Count > 0)
        {
            response.MediaSources = [.. picked.Concat(others)];
        }

        return rewrites;
    }

    /// <summary>
    /// Sets the <c>AudioStreamIndex</c> query parameter, leaving the rest of the URL as is.
    /// </summary>
    /// <remarks>
    /// The server leaves the parameter out when it picked no audio stream; it is appended then, so the transcode
    /// plays the same stream the response announces.
    /// </remarks>
    /// <param name="url">The transcoding URL.</param>
    /// <param name="index">The new audio stream index.</param>
    /// <returns>The URL with the new index.</returns>
    public static string SetAudioStreamIndex(string url, int index)
    {
        ArgumentNullException.ThrowIfNull(url);

        string value = index.ToString(CultureInfo.InvariantCulture);
        if (AudioStreamIndexParameter().IsMatch(url))
        {
            return AudioStreamIndexParameter().Replace(url, match => match.Groups["name"].Value + value);
        }

        int fragment = url.IndexOf('#', StringComparison.Ordinal);
        string path = fragment < 0 ? url : url[..fragment];
        string separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return path + separator + "AudioStreamIndex=" + value + (fragment < 0 ? string.Empty : url[fragment..]);
    }

    [GeneratedRegex(@"(?<=[?&])(?<name>AudioStreamIndex=)[^&#]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AudioStreamIndexParameter();
}
