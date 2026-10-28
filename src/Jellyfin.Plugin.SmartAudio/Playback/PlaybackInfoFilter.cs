using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartAudio.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
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
/// <para>
/// An action filter rather than a result filter: the posted <c>PlaybackInfoDto</c>, which may carry the client's own
/// <c>AudioStreamIndex</c>, is only visible as an action argument, and the plugin's choice has to reach the action before
/// it runs. The server decides between direct play and transcoding for the audio stream it is asked for: rewriting only
/// the response leaves a direct play the client can't honour (a browser without <c>audioTracks</c> plays the file's
/// first audio track, whatever <c>DefaultAudioStreamIndex</c> says).
/// </para>
/// <para>
/// Also rewrites <c>DefaultAudioStreamIndex</c> and the order of <c>MediaSources</c> in the single-item responses of
/// <c>UserLibraryController</c>: Jellyfin Web's details page pre-selects its version and audio menus from them and sends
/// those choices as the PlaybackInfo request's own <c>MediaSourceId</c> and <c>AudioStreamIndex</c>.
/// </para>
/// </remarks>
public class PlaybackInfoFilter : IAsyncActionFilter
{
    private const string UserIdClaimType = "Jellyfin-UserId";

    private readonly ILogger<PlaybackInfoFilter> _logger;
    private readonly Func<PluginConfiguration?> _configuration;
    private readonly Func<Guid, IReadOnlyList<MediaSourceInfo>> _mediaSources;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackInfoFilter"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{PlaybackInfoFilter}"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    public PlaybackInfoFilter(ILogger<PlaybackInfoFilter> logger, ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
        : this(
            logger,
            () => Plugin.Instance?.Configuration,
            itemId => libraryManager.GetItemById(itemId) is { } item ? mediaSourceManager.GetStaticMediaSources(item, false) : [])
    {
    }

    internal PlaybackInfoFilter(
        ILogger<PlaybackInfoFilter> logger,
        Func<PluginConfiguration?> configuration,
        Func<Guid, IReadOnlyList<MediaSourceInfo>> mediaSources)
    {
        _logger = logger;
        _configuration = configuration;
        _mediaSources = mediaSources;
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

    /// <summary>
    /// Whether an action is one of the single-item endpoints whose <c>MediaSources</c> the filter rewrites.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns><c>true</c> for <c>UserLibraryController.GetItem</c> and <c>GetItemLegacy</c>.</returns>
    public static bool IsItemAction(ActionDescriptor action)
    {
        return action is ControllerActionDescriptor
        {
            ControllerName: "UserLibrary",
            ActionName: "GetItem" or "GetItemLegacy"
        };
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (IsItemAction(context.ActionDescriptor))
        {
            await RewriteItemAsync(context, next).ConfigureAwait(false);
            return;
        }

        if (!IsPlaybackInfoAction(context.ActionDescriptor))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Read before the action runs: model binding is done, and the arguments are what the client sent.
        bool explicitAudio = HasExplicitAudioStreamIndex(context);
        Guid userId = GetUserId(context);
        object? itemId = GetArgument(context, "itemId");
        string? language = GetLanguage(userId);
        if (explicitAudio && language is not null)
        {
            _logger.LogDebug("Smart Audio: user {UserId}, item {ItemId}: the client asked for its own audio stream, leaving it", userId, itemId);
            language = null;
        }

        // Ask the server for the plugin's version and stream, so it picks the play method for that stream. Only the
        // posted endpoint takes them. Naming the version matters for Jellyfin Web, which plays the item's own version
        // whenever the response has it, whatever the order of MediaSources.
        if (language is not null
            && itemId is Guid id
            && context.ActionDescriptor is ControllerActionDescriptor { ActionName: "GetPostedPlaybackInfo" })
        {
            string? mediaSourceId = GetArgument(context, "mediaSourceId") as string ?? ReadDtoProperty<string>(context, "MediaSourceId");
            var choice = _mediaSources(id)
                .Where(source => string.IsNullOrEmpty(mediaSourceId) || string.Equals(source.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase))
                .Select(source => (Source: source, Index: SpikeRule.PickAudioStream(source.MediaStreams, language)))
                .FirstOrDefault(pick => pick.Index is not null);
            if (choice.Index is not null)
            {
                context.ActionArguments["audioStreamIndex"] = choice.Index;
                if (string.IsNullOrEmpty(mediaSourceId))
                {
                    context.ActionArguments["mediaSourceId"] = choice.Source.Id;
                }

                _logger.LogDebug(
                    "Smart Audio: user {UserId}, item {ItemId}: asking the server for media source {MediaSourceId}, audio stream {Index}",
                    userId,
                    itemId,
                    choice.Source.Id,
                    choice.Index);
            }
        }

        var executed = await next().ConfigureAwait(false);

        if (language is null
            || executed.Result is not ObjectResult { Value: PlaybackInfoResponse { ErrorCode: null } response })
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

    private async Task RewriteItemAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Guid userId = GetUserId(context);
        var executed = await next().ConfigureAwait(false);

        string? language = GetLanguage(userId);
        if (language is null || executed.Result is not ObjectResult { Value: BaseItemDto { MediaSources: { } sources } item })
        {
            return;
        }

        var picked = new List<MediaSourceInfo>();
        foreach (var source in sources)
        {
            if (SpikeRule.PickAudioStream(source.MediaStreams, language) is not int index)
            {
                continue;
            }

            _logger.LogDebug(
                "Smart Audio: user {UserId}, item {ItemId}, media source {MediaSourceId}: item default audio stream {OldIndex} -> {NewIndex}",
                userId,
                item.Id,
                source.Id,
                source.DefaultAudioStreamIndex,
                index);
            source.DefaultAudioStreamIndex = index;
            picked.Add(source);
        }

        // The details page offers the versions in this order and plays the first one.
        item.MediaSources = [.. picked, .. sources.Except(picked)];
    }

    private string? GetLanguage(Guid userId)
    {
        return _configuration()?.SpikeUserLanguages
            .FirstOrDefault(entry => entry.UserId.Equals(userId) && !string.IsNullOrWhiteSpace(entry.Language))?
            .Language
            .Trim();
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
