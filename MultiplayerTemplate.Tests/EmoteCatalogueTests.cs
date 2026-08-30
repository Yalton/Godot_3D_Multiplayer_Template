using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Tests;

public sealed class EmoteCatalogueTests
{
    [Fact]
    public void EveryCatalogueIdResolves()
    {
        Assert.NotEmpty(EmoteCatalogue.All);
        foreach (var entry in EmoteCatalogue.All)
        {
            Assert.True(EmoteCatalogue.TryGet(entry.Id, out var found));
            Assert.Equal(entry.Clip, found.Clip);
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
}
