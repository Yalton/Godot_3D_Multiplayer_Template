using System.Collections.ObjectModel;

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

    // Referencing ProceduralAnimationLibrary here does not reintroduce an engine
    // dependency: EmoteWave..EmoteDance are `const string`, and the C# compiler inlines a
    // compile-time constant's value into this assembly at build time, so no Godot type is
    // ever loaded at runtime and the xUnit suite still cannot crash. If those are ever
    // changed to `static readonly`, this silently starts loading Godot.AnimationLibrary's
    // declaring assembly the moment this class is touched — do not "tidy" them that way.
    private static readonly EmoteDefinition[] Entries =
    [
        new(1, ProceduralAnimationLibrary.EmoteWave, "Wave", false),
        new(2, ProceduralAnimationLibrary.EmotePoint, "Point", false),
        new(3, ProceduralAnimationLibrary.EmoteClap, "Clap", false),
        new(4, ProceduralAnimationLibrary.EmoteCheer, "Cheer", false),
        new(5, ProceduralAnimationLibrary.EmoteSit, "Sit", true),
        new(6, ProceduralAnimationLibrary.EmoteDance, "Dance", true),
    ];

    // ReadOnlyCollection is a real barrier; returning the array as IReadOnlyList is not,
    // because a caller can cast it back. This table is the server's validation.
    private static readonly ReadOnlyCollection<EmoteDefinition> Catalogue = new(Entries);

    public static IReadOnlyList<EmoteDefinition> All => Catalogue;

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
