namespace MultiplayerTemplate.Avatar;

/// <summary>Immutable catalogue IDs selected for a player's appearance.</summary>
public readonly record struct AvatarAppearance(
    int BodyColorId,
    int HeadId,
    int BackId,
    int HipsId,
    int HandId)
{
    public static AvatarAppearance Default => new(
        CosmeticCatalogue.BlueBodyId, CosmeticCatalogue.None, CosmeticCatalogue.None,
        CosmeticCatalogue.None, CosmeticCatalogue.None);
}
