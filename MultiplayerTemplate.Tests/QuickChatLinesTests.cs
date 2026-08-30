using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class QuickChatLinesTests
{
    [Fact]
    public void EveryLineResolves()
    {
        Assert.NotEmpty(QuickChatLines.All);
        foreach (var line in QuickChatLines.All)
        {
            Assert.True(QuickChatLines.TryGet(line.Id, out var found));
            Assert.Equal(line.Text, found.Text);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(9999)]
    public void UnknownIdsAreRejected(int id)
    {
        Assert.False(QuickChatLines.TryGet(id, out _));
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = QuickChatLines.All.Select(line => line.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void TextSurvivesChatSanitisationUnchanged()
    {
        foreach (var line in QuickChatLines.All)
            Assert.Equal(line.Text, ChatPolicy.Sanitize(line.Text));
    }

    [Fact]
    public void AllCannotBeCastBackToAMutableArray()
    {
        Assert.IsNotType<QuickChatLine[]>(QuickChatLines.All);
    }
}
