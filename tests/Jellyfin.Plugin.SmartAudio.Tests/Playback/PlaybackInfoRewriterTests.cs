using System.Linq;
using Jellyfin.Plugin.SmartAudio.Playback;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Xunit;

namespace Jellyfin.Plugin.SmartAudio.Tests.Playback;

public class PlaybackInfoRewriterTests
{
    private const string TranscodingUrl = "/videos/abc/master.m3u8?DeviceId=d&MediaSourceId=abc&AudioCodec=aac&AudioStreamIndex=1&SubtitleStreamIndex=3&api_key=t";

    [Fact]
    public void RewritesDefaultAudioStreamIndex()
    {
        var response = Response(new MediaSourceInfo { Id = "a", DefaultAudioStreamIndex = 1 });

        var rewrites = PlaybackInfoRewriter.Rewrite(response, _ => 2);

        Assert.Equal(2, response.MediaSources[0].DefaultAudioStreamIndex);
        Assert.Equal([new AudioStreamRewrite("a", 1, 2)], rewrites);
    }

    [Fact]
    public void ReplacesOnlyAudioStreamIndexInTranscodingUrl()
    {
        var response = Response(new MediaSourceInfo { Id = "a", DefaultAudioStreamIndex = 1, TranscodingUrl = TranscodingUrl });

        PlaybackInfoRewriter.Rewrite(response, _ => 12);

        Assert.Equal(TranscodingUrl.Replace("&AudioStreamIndex=1&", "&AudioStreamIndex=12&", System.StringComparison.Ordinal), response.MediaSources[0].TranscodingUrl);
    }

    [Theory]
    [InlineData("/v?AudioStreamIndex=1", "/v?AudioStreamIndex=4")]
    [InlineData("/v?a=1&audiostreamindex=1", "/v?a=1&audiostreamindex=4")]
    [InlineData("/v?MaxAudioStreamIndex=1&AudioStreamIndex=", "/v?MaxAudioStreamIndex=1&AudioStreamIndex=4")]
    [InlineData("/v?a=1", "/v?a=1")]
    public void ReplaceAudioStreamIndexTouchesOnlyThatParameter(string url, string expected)
    {
        Assert.Equal(expected, PlaybackInfoRewriter.ReplaceAudioStreamIndex(url, 4));
    }

    [Fact]
    public void SourceWithoutTranscodingUrlIsFine()
    {
        var response = Response(new MediaSourceInfo { Id = "a", DefaultAudioStreamIndex = 1, TranscodingUrl = null });

        PlaybackInfoRewriter.Rewrite(response, _ => 2);

        Assert.Null(response.MediaSources[0].TranscodingUrl);
        Assert.Equal(2, response.MediaSources[0].DefaultAudioStreamIndex);
    }

    [Fact]
    public void MovesSourcesWithAPickFirst()
    {
        var response = Response(
            new MediaSourceInfo { Id = "dub", DefaultAudioStreamIndex = 1 },
            new MediaSourceInfo { Id = "original", DefaultAudioStreamIndex = 1 },
            new MediaSourceInfo { Id = "other", DefaultAudioStreamIndex = 1 });

        PlaybackInfoRewriter.Rewrite(response, source => source.Id == "original" ? 2 : null);

        Assert.Equal(["original", "dub", "other"], response.MediaSources.Select(source => source.Id));
        Assert.Equal(1, response.MediaSources[1].DefaultAudioStreamIndex);
    }

    [Fact]
    public void NoPickLeavesResponseAlone()
    {
        var sources = new[] { new MediaSourceInfo { Id = "a", DefaultAudioStreamIndex = 1, TranscodingUrl = TranscodingUrl }, new MediaSourceInfo { Id = "b" } };
        var response = Response(sources);

        var rewrites = PlaybackInfoRewriter.Rewrite(response, _ => null);

        Assert.Empty(rewrites);
        Assert.Same(sources, response.MediaSources);
        Assert.Equal(1, sources[0].DefaultAudioStreamIndex);
        Assert.Equal(TranscodingUrl, sources[0].TranscodingUrl);
    }

    internal static PlaybackInfoResponse Response(params MediaSourceInfo[] sources) => new() { MediaSources = sources };
}
