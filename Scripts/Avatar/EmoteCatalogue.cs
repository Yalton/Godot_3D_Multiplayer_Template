namespace MultiplayerTemplate.Avatar;

/// <summary>One emote. <see cref="Clip"/> must name an animation the rig defines.</summary>
public readonly record struct EmoteDefinition(int Id, string Clip, string DisplayName, bool Looping);

/// <summary>
/// The emote table. This lookup is also the server's validation: an id absent here is
/// rejected, so a client cannot index anything with an arbitrary integer. Id 0 is reserved
/// for "no emote". Pure and engine-free so it can be unit tested; must not reference the
/// Godot namespace.
///
/// To add an emote: add a row here and a clip of the same name in
/// ProceduralAnimationLibrary. To replace the set wholesale, replace this table — a rig
/// that defines different clip names owns what those names mean.
/// </summary>
public static class EmoteCatalogue
{
    public const int None = 0;

    private static readonly EmoteDefinition[] Entries =
    [
        new(1, "emote_wave", "Wave", false),
        new(2, "emote_point", "Point", false),
        new(3, "emote_clap", "Clap", false),
        new(4, "emote_cheer", "Cheer", false),
        new(5, "emote_sit", "Sit", true),
        new(6, "emote_dance", "Dance", true),
    ];

    public static IReadOnlyList<EmoteDefinition> All => Entries;

    public static bool TryGet(int id, out EmoteDefinition definition)
    {
        foreach (var entry in Entries)
        {
            if (entry.Id != id) continue;
            definition = entry;
            return true;
        }
        definition = default;
        return false;
    }
}
