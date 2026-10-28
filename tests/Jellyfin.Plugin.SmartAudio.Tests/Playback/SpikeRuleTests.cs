using Jellyfin.Plugin.SmartAudio.Playback;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartAudio.Tests.Playback;

public class SpikeRuleTests
{
    private static readonly MediaStream[] Streams =
    [
        new() { Index = 0, Type = MediaStreamType.Video },
        new() { Index = 1, Type = MediaStreamType.Audio, Language = "fre" },
        new() { Index = 2, Type = MediaStreamType.Subtitle, Language = "jpn" },
        new() { Index = 3, Type = MediaStreamType.Audio, Language = "jpn" },
        new() { Index = 4, Type = MediaStreamType.Audio, Language = "jpn" },
    ];

    [Theory]
    [InlineData("jpn", 3)]
    [InlineData("JPN", 3)]
    [InlineData("fre", 1)]
    [InlineData("eng", null)]
    public void PicksFirstAudioStreamInLanguage(string language, int? expected)
    {
        Assert.Equal(expected, SpikeRule.PickAudioStream(Streams, language));
    }
}
