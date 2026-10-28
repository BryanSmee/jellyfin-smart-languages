using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartAudio.Configuration;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartAudio.Playback;

/// <summary>
/// Rewrites the responses of <c>MediaInfoController</c>'s PlaybackInfo endpoints for the calling user (SPEC.md, Mechanism A).
/// </summary>
/// <remarks>
/// An action filter rather than a result filter: the posted <c>PlaybackInfoDto</c>, which may carry the client's own
/// <c>AudioStreamIndex</c>, is only visible as an action argument.
/// </remarks>
public class PlaybackInfoFilter : IAsyncActionFilter
{
    private const string UserIdClaimType = "Jellyfin-UserId";

    private readonly ILogger<PlaybackInfoFilter> _logger;
    private readonly Func<PluginConfiguration?> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackInfoFilter"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{PlaybackInfoFilter}"/> interface.</param>
    public PlaybackInfoFilter(ILogger<PlaybackInfoFilter> logger)
        : this(logger, () => Plugin.Instance?.Configuration)
    {
    }

    internal PlaybackInfoFilter(ILogger<PlaybackInfoFilter> logger, Func<PluginConfiguration?> configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Whether an action is one of the PlaybackInfo endpoints the filter rewrites.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns><c>true</c> for <c>MediaInfoController.GetPlaybackInfo</c> and <c>GetPostedPlaybackInfo</c>.</returns>
    public static bool IsPlaybackInfoAction(ActionDescriptor action)
    {
        return action is ControllerActionDescriptor
        {
            ControllerName: "MediaInfo",
            ActionName: "GetPlaybackInfo" or "GetPostedPlaybackInfo"
        };
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!IsPlaybackInfoAction(context.ActionDescriptor))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Read before the action runs: model binding is done, and the arguments are what the client sent.
        bool explicitAudio = HasExplicitAudioStreamIndex(context);
        Guid userId = GetUserId(context);
        object? itemId = GetArgument(context, "itemId");

        var executed = await next().ConfigureAwait(false);

        if (explicitAudio
            || executed.Result is not ObjectResult { Value: PlaybackInfoResponse { ErrorCode: null } response })
        {
            return;
        }

        string? language = _configuration()?.SpikeUserLanguages
            .FirstOrDefault(entry => entry.UserId.Equals(userId) && !string.IsNullOrWhiteSpace(entry.Language))?
            .Language
            .Trim();
        if (language is null)
        {
            return;
        }

        var rewrites = PlaybackInfoRewriter.Rewrite(response, source => SpikeRule.PickAudioStream(source.MediaStreams, language));
        foreach (var rewrite in rewrites)
        {
            _logger.LogDebug(
                "Smart Audio: user {UserId}, item {ItemId}, media source {MediaSourceId}: audio stream {OldIndex} -> {NewIndex}",
                userId,
                itemId,
                rewrite.MediaSourceId,
                rewrite.OldIndex,
                rewrite.NewIndex);
        }
    }

    private static bool HasExplicitAudioStreamIndex(ActionExecutingContext context)
    {
        return GetArgument(context, "audioStreamIndex") is int
            || ReadDtoProperty<int?>(context, "AudioStreamIndex") is not null
            || context.HttpContext.Request.Query.ContainsKey("audioStreamIndex");
    }

    private static Guid GetUserId(ActionExecutingContext context)
    {
        // Same order as the controller: query, then body, then the authenticated user.
        if (GetArgument(context, "userId") is Guid queryUserId && !queryUserId.Equals(Guid.Empty))
        {
            return queryUserId;
        }

        if (ReadDtoProperty<Guid?>(context, "UserId") is Guid bodyUserId && !bodyUserId.Equals(Guid.Empty))
        {
            return bodyUserId;
        }

        return Guid.TryParse(context.HttpContext.User.FindFirst(UserIdClaimType)?.Value, out var claimUserId) ? claimUserId : Guid.Empty;
    }

    private static object? GetArgument(ActionExecutingContext context, string name)
    {
        return context.ActionArguments.TryGetValue(name, out object? value) ? value : null;
    }

    // PlaybackInfoDto lives in Jellyfin.Api, which plugins can't reference: read it by property name.
    private static T? ReadDtoProperty<T>(ActionExecutingContext context, string name)
    {
        object? dto = GetArgument(context, "playbackInfoDto");
        return dto?.GetType().GetProperty(name)?.GetValue(dto) is T value ? value : default;
    }
}
