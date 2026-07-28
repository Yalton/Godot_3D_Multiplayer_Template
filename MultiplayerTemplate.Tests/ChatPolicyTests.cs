using MultiplayerTemplate.Communication;

namespace MultiplayerTemplate.Tests;

public sealed class ChatPolicyTests
{
    [Fact]
    public void SanitizesWhitespaceAndLineBreaks()
    {
        Assert.Equal("hello   world", ChatPolicy.Sanitize("  hello \r\n world  "));
    }

    [Fact]
    public void TruncatesToProtocolLimit()
    {
        var result = ChatPolicy.Sanitize(new string('x', ChatPolicy.MaxLength + 20));
        Assert.Equal(ChatPolicy.MaxLength, result.Length);
    }

    [Fact]
    public void EmptyInputRemainsEmpty()
    {
        Assert.Empty(ChatPolicy.Sanitize(" \r\n "));
    }

    [Fact]
    public void EscapesBbCodeDelimiters()
    {
        Assert.Equal("[lb]color=red[rb]name[lb]/color[rb]",
            ChatPolicy.EscapeBbCode("[color=red]name[/color]"));
    }
}
