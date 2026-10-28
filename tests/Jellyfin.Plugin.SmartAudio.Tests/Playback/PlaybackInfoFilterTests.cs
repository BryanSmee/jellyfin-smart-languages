using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartAudio.Configuration;
using Jellyfin.Plugin.SmartAudio.Playback;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SmartAudio.Tests.Playback;

public class PlaybackInfoFilterTests
{
    private static readonly Guid MappedUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Item = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("MediaInfo", "GetPlaybackInfo", true)]
    [InlineData("MediaInfo", "GetPostedPlaybackInfo", true)]
    [InlineData("MediaInfo", "GetBitrateTestBytes", false)]
    [InlineData("Items", "GetPlaybackInfo", false)]
    public void IsPlaybackInfoActionChecksControllerAndAction(string controller, string action, bool expected)
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action };

        Assert.Equal(expected, PlaybackInfoFilter.IsPlaybackInfoAction(descriptor));
    }

    [Fact]
    public void IsPlaybackInfoActionIgnoresNonControllerActions()
    {
        Assert.False(PlaybackInfoFilter.IsPlaybackInfoAction(new ActionDescriptor()));
    }

    [Theory]
    [InlineData("UserLibrary", "GetItem", true)]
    [InlineData("UserLibrary", "GetItemLegacy", true)]
    [InlineData("UserLibrary", "GetRootFolder", false)]
    [InlineData("Items", "GetItem", false)]
    public void IsItemActionChecksControllerAndAction(string controller, string action, bool expected)
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action };

        Assert.Equal(expected, PlaybackInfoFilter.IsItemAction(descriptor));
    }

    [Fact]
    public async Task AsksTheServerForTheMappedUsersStream()
    {
        var executing = await RunFilter(Response(), claimUser: MappedUser, arguments: new() { ["itemId"] = Item });

        Assert.Equal(2, executing.ActionArguments["audioStreamIndex"]);
        Assert.Equal("original", executing.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task KeepsTheRequestedMediaSource()
    {
        var executing = await RunFilter(Response(), claimUser: MappedUser, arguments: new() { ["itemId"] = Item, ["mediaSourceId"] = "original" });

        Assert.Equal(2, executing.ActionArguments["audioStreamIndex"]);
        Assert.Equal("original", executing.ActionArguments["mediaSourceId"]);
    }

    [Fact]
    public async Task AsksForTheStreamOfTheRequestedMediaSource()
    {
        var executing = await RunFilter(
            Response(),
            claimUser: MappedUser,
            arguments: new() { ["itemId"] = Item, ["playbackInfoDto"] = new FakePlaybackInfoDto { MediaSourceId = "dub" } });

        Assert.False(executing.ActionArguments.ContainsKey("audioStreamIndex"));
    }

    [Fact]
    public async Task DoesNotAskForAStreamForUnmappedUser()
    {
        var executing = await RunFilter(Response(), claimUser: OtherUser, arguments: new() { ["itemId"] = Item });

        Assert.False(executing.ActionArguments.ContainsKey("audioStreamIndex"));
    }

    [Fact]
    public async Task KeepsTheClientsOwnStreamIndex()
    {
        var executing = await RunFilter(Response(), claimUser: MappedUser, arguments: new() { ["itemId"] = Item, ["audioStreamIndex"] = 1 });

        Assert.Equal(1, executing.ActionArguments["audioStreamIndex"]);
    }

    [Fact]
    public async Task DoesNotAskForAStreamOnGetPlaybackInfo()
    {
        var executing = await RunFilter(Response(), claimUser: MappedUser, arguments: new() { ["itemId"] = Item }, action: "GetPlaybackInfo");

        Assert.False(executing.ActionArguments.ContainsKey("audioStreamIndex"));
    }

    [Fact]
    public async Task RewritesItemMediaSourcesForMappedUser()
    {
        var item = new BaseItemDto { Id = Item, MediaSources = [.. Response().MediaSources] };

        await RunFilter(item, claimUser: MappedUser, controller: "UserLibrary", action: "GetItem");

        Assert.Equal(["original", "dub"], [item.MediaSources[0].Id, item.MediaSources[1].Id]);
        Assert.Equal([2, 1], [item.MediaSources[0].DefaultAudioStreamIndex, item.MediaSources[1].DefaultAudioStreamIndex]);
    }

    [Fact]
    public async Task LeavesItemAloneForUnmappedUser()
    {
        var item = new BaseItemDto { Id = Item, MediaSources = [.. Response().MediaSources] };

        await RunFilter(item, claimUser: OtherUser, controller: "UserLibrary", action: "GetItem");

        Assert.Equal(["dub", "original"], [item.MediaSources[0].Id, item.MediaSources[1].Id]);
        Assert.Equal([1, 1], [item.MediaSources[0].DefaultAudioStreamIndex, item.MediaSources[1].DefaultAudioStreamIndex]);
    }

    [Fact]
    public async Task ItemWithoutMediaSourcesIsLeftAlone()
    {
        var item = new BaseItemDto { Id = Item };

        await RunFilter(item, claimUser: MappedUser, controller: "UserLibrary", action: "GetItem");

        Assert.Null(item.MediaSources);
    }

    [Fact]
    public async Task RewritesForMappedUser()
    {
        var response = Response();

        await RunFilter(response, claimUser: MappedUser);

        Assert.Equal(["original", "dub"], [response.MediaSources[0].Id, response.MediaSources[1].Id]);
        Assert.Equal(2, response.MediaSources[0].DefaultAudioStreamIndex);
        Assert.Equal("/v?AudioStreamIndex=2&x=y", response.MediaSources[0].TranscodingUrl);
    }

    [Fact]
    public async Task UsesUserIdFromBody()
    {
        var response = Response();

        await RunFilter(response, claimUser: OtherUser, arguments: new() { ["playbackInfoDto"] = new FakePlaybackInfoDto { UserId = MappedUser } });

        Assert.Equal(2, response.MediaSources[0].DefaultAudioStreamIndex);
    }

    [Fact]
    public async Task QueryUserIdWinsOverBody()
    {
        var response = Response();

        await RunFilter(response, claimUser: MappedUser, arguments: new() { ["userId"] = OtherUser, ["playbackInfoDto"] = new FakePlaybackInfoDto { UserId = MappedUser } });

        AssertUnchanged(response);
    }

    [Fact]
    public async Task RewritesGetPlaybackInfo()
    {
        var response = Response();

        await RunFilter(response, claimUser: MappedUser, arguments: new() { ["itemId"] = Guid.NewGuid(), ["userId"] = null }, action: "GetPlaybackInfo");

        Assert.Equal(2, response.MediaSources[0].DefaultAudioStreamIndex);
    }

    [Fact]
    public async Task UnmappedUserLeavesResponseAlone()
    {
        var response = Response();

        await RunFilter(response, claimUser: OtherUser);

        AssertUnchanged(response);
    }

    [Fact]
    public async Task ExplicitAudioStreamIndexInQueryArgumentLeavesResponseAlone()
    {
        var response = Response();

        await RunFilter(response, claimUser: MappedUser, arguments: new() { ["audioStreamIndex"] = 1 });

        AssertUnchanged(response);
    }

    [Fact]
    public async Task ExplicitAudioStreamIndexInBodyLeavesResponseAlone()
    {
        var response = Response();

        await RunFilter(response, claimUser: MappedUser, arguments: new() { ["playbackInfoDto"] = new FakePlaybackInfoDto { AudioStreamIndex = 1 } });

        AssertUnchanged(response);
    }

    [Fact]
    public async Task ExplicitAudioStreamIndexInRawQueryLeavesResponseAlone()
    {
        var response = Response();

        await RunFilter(response, claimUser: MappedUser, query: "?AudioStreamIndex=1");

        AssertUnchanged(response);
    }

    [Fact]
    public async Task ErrorCodeResponseIsLeftAlone()
    {
        var response = Response();
        response.ErrorCode = PlaybackErrorCode.NoCompatibleStream;

        await RunFilter(response, claimUser: MappedUser);

        AssertUnchanged(response);
    }

    [Fact]
    public async Task OtherEndpointsAreLeftAlone()
    {
        var response = Response();

        await RunFilter(response, claimUser: MappedUser, action: "GetBitrateTestBytes");

        AssertUnchanged(response);
    }

    private static PlaybackInfoResponse Response()
    {
        var streams = new[]
        {
            new MediaStream { Index = 0, Type = MediaStreamType.Video },
            new MediaStream { Index = 1, Type = MediaStreamType.Audio, Language = "fre" },
            new MediaStream { Index = 2, Type = MediaStreamType.Audio, Language = "jpn" },
        };
        return PlaybackInfoRewriterTests.Response(
            new MediaSourceInfo { Id = "dub", DefaultAudioStreamIndex = 1, MediaStreams = [streams[0], streams[1]] },
            new MediaSourceInfo { Id = "original", DefaultAudioStreamIndex = 1, MediaStreams = streams, TranscodingUrl = "/v?AudioStreamIndex=1&x=y" });
    }

    private static void AssertUnchanged(PlaybackInfoResponse response)
    {
        Assert.Equal(["dub", "original"], [response.MediaSources[0].Id, response.MediaSources[1].Id]);
        Assert.Equal(1, response.MediaSources[1].DefaultAudioStreamIndex);
        Assert.Equal("/v?AudioStreamIndex=1&x=y", response.MediaSources[1].TranscodingUrl);
    }

    private static async Task<ActionExecutingContext> RunFilter(
        object response,
        Guid claimUser,
        Dictionary<string, object?>? arguments = null,
        string query = "",
        string controller = "MediaInfo",
        string action = "GetPostedPlaybackInfo")
    {
        var configuration = new PluginConfiguration();
        configuration.SpikeUserLanguages.Add(new SpikeUserLanguage { UserId = MappedUser, Language = "jpn" });
        var filter = new PlaybackInfoFilter(
            NullLogger<PlaybackInfoFilter>.Instance,
            () => configuration,
            itemId => itemId.Equals(Item) ? Response().MediaSources : []);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", claimUser.ToString())], "Test")),
        };
        httpContext.Request.QueryString = new QueryString(query);
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor { ControllerName = controller, ActionName = action });
        var executing = new ActionExecutingContext(actionContext, [], arguments ?? [], controller: new object());

        await filter.OnActionExecutionAsync(
            executing,
            () => Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()) { Result = new ObjectResult(response) }));
        return executing;
    }

    // Stands in for Jellyfin.Api's PlaybackInfoDto, which the filter reads by property name.
    private sealed class FakePlaybackInfoDto
    {
        public Guid? UserId { get; set; }

        public int? AudioStreamIndex { get; set; }

        public string? MediaSourceId { get; set; }
    }
}
