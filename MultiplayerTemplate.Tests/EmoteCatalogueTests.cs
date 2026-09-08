using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Tests;

public sealed class EmoteCatalogueTests
{
    [Fact]
    public void ExpectedEmoteIdsResolveToExpectedClips()
    {
        var expected = new Dictionary<int, string>
        {
            [1] = "emote_wave", [2] = "emote_point", [3] = "emote_clap",
            [4] = "emote_cheer", [5] = "emote_sit", [6] = "emote_dance"
        };
        Assert.Equal(expected.Count, EmoteCatalogue.All.Count);
        foreach (var (id, clip) in expected)
        {
            Assert.True(EmoteCatalogue.TryGet(id, out var found));
            Assert.Equal(clip, found.Clip);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9999)]
    public void UnknownIdsAreRejected(int id)
    {
        Assert.False(EmoteCatalogue.TryGet(id, out _));
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = EmoteCatalogue.All.Select(entry => entry.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ClipNamesAreUniqueAndNonEmpty()
    {
        var clips = EmoteCatalogue.All.Select(entry => entry.Clip).ToList();
        Assert.All(clips, clip => Assert.False(string.IsNullOrWhiteSpace(clip)));
        Assert.Equal(clips.Count, clips.Distinct().Count());
    }

    [Fact]
    public void CatalogueContainsBothOneShotsAndLoops()
    {
        Assert.Contains(EmoteCatalogue.All, entry => !entry.Looping);
        Assert.Contains(EmoteCatalogue.All, entry => entry.Looping);
    }

    [Fact]
    public void ZeroIsReservedForNoEmote()
    {
        Assert.DoesNotContain(EmoteCatalogue.All, entry => entry.Id == 0);
    }

    [Fact]
    public void AllCannotBeCastBackToAMutableArray()
    {
        Assert.IsNotType<EmoteDefinition[]>(EmoteCatalogue.All);
    }
}
