using System.Collections.ObjectModel;

namespace MultiplayerTemplate.Avatar;

public enum CosmeticSlot
{
    BodyColor,
    Head,
    Back,
    Hips,
    Hand
}

/// <summary>Pure metadata for one stable cosmetic ID.</summary>
public readonly record struct CosmeticDefinition(
    int Id, CosmeticSlot Slot, string DisplayName, byte Red, byte Green, byte Blue)
{
    public (byte Red, byte Green, byte Blue) ColorBytes => (Red, Green, Blue);
}

/// <summary>Small, engine-free appearance policy and validation boundary.</summary>
public static class CosmeticCatalogue
{
    public const int None = 0;
    public const int BlueBodyId = 1;
    public const int RedBodyId = 2;
    public const int GreenBodyId = 3;
    public const int HeadItemId = 101;
    public const int BackItemId = 201;
    public const int HipsItemId = 301;
    public const int HandItemId = 401;

    private static readonly CosmeticDefinition[] Entries =
    [
        new(None, CosmeticSlot.BodyColor, "None", 255, 255, 255),
        new(BlueBodyId, CosmeticSlot.BodyColor, "Blue", 74, 144, 226),
        new(RedBodyId, CosmeticSlot.BodyColor, "Red", 220, 76, 76),
        new(GreenBodyId, CosmeticSlot.BodyColor, "Green", 76, 190, 108),
        new(None, CosmeticSlot.Head, "None", 0, 0, 0),
        new(HeadItemId, CosmeticSlot.Head, "Crown", 248, 196, 64),
        new(None, CosmeticSlot.Back, "None", 0, 0, 0),
        new(BackItemId, CosmeticSlot.Back, "Cape", 152, 92, 214),
        new(None, CosmeticSlot.Hips, "None", 0, 0, 0),
        new(HipsItemId, CosmeticSlot.Hips, "Belt", 124, 82, 48),
        new(None, CosmeticSlot.Hand, "None", 0, 0, 0),
        new(HandItemId, CosmeticSlot.Hand, "Gloves", 238, 238, 238)
    ];

    private static readonly ReadOnlyCollection<CosmeticDefinition> Catalogue = new(Entries);

    public static IReadOnlyList<CosmeticDefinition> All => Catalogue;

    public static IReadOnlyList<CosmeticDefinition> DefinitionsFor(CosmeticSlot slot) =>
        new ReadOnlyCollection<CosmeticDefinition>(Entries.Where(entry => entry.Slot == slot).ToArray());

    public static bool IsValid(CosmeticSlot slot, int id) =>
        id == None || Entries.Any(entry => entry.Slot == slot && entry.Id == id);

    public static AvatarAppearance Normalize(AvatarAppearance appearance) => new(
        IsValid(CosmeticSlot.BodyColor, appearance.BodyColorId) ? appearance.BodyColorId : AvatarAppearance.Default.BodyColorId,
        IsValid(CosmeticSlot.Head, appearance.HeadId) ? appearance.HeadId : None,
        IsValid(CosmeticSlot.Back, appearance.BackId) ? appearance.BackId : None,
        IsValid(CosmeticSlot.Hips, appearance.HipsId) ? appearance.HipsId : None,
        IsValid(CosmeticSlot.Hand, appearance.HandId) ? appearance.HandId : None);
}
