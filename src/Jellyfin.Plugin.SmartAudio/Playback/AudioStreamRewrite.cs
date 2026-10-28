namespace Jellyfin.Plugin.SmartAudio.Playback;

/// <summary>
/// One media source whose audio stream the plugin changed.
/// </summary>
/// <param name="MediaSourceId">The media source's ID.</param>
/// <param name="OldIndex">The audio stream index the server picked.</param>
/// <param name="NewIndex">The audio stream index the plugin picked.</param>
public sealed record AudioStreamRewrite(string? MediaSourceId, int? OldIndex, int NewIndex);
