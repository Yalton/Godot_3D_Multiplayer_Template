using System.Collections.ObjectModel;

namespace MultiplayerTemplate.Communication;

public readonly record struct QuickChatLine(int Id, string Text);

/// <summary>
/// Canned chat lines, sent by id so no client-supplied string ever becomes displayable
/// text. This lookup is the server's validation. Id 0 is reserved. Pure and engine-free;
/// must not reference the Godot namespace.
/// </summary>
public static class QuickChatLines
{
    private static readonly QuickChatLine[] Entries =
    [
        new(1, "Hello!"),
        new(2, "Yes"),
        new(3, "No"),
        new(4, "Follow me"),
        new(5, "Wait here"),
        new(6, "Nice one"),
        new(7, "Sorry"),
        new(8, "Help!"),
    ];

    // ReadOnlyCollection is a real barrier; returning the array as IReadOnlyList is not,
    // because a caller can cast it back. This table is the server's validation.
    private static readonly ReadOnlyCollection<QuickChatLine> Lines = new(Entries);

    public static IReadOnlyList<QuickChatLine> All => Lines;

    public static bool TryGet(int id, out QuickChatLine line)
    {
        foreach (var entry in Entries)
        {
            if (entry.Id != id) continue;
            line = entry;
            return true;
        }
        line = default;
        return false;
    }
}
