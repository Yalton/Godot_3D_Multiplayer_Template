using MultiplayerTemplate.Avatar;

namespace MultiplayerTemplate.Tests;

public sealed class CosmeticCatalogueTests
{
    [Fact]
    public void UnknownAndCrossSlotIdsNormalizeToDefaults()
    {
        var requested = new AvatarAppearance(
            CosmeticCatalogue.BlueBodyId,
            CosmeticCatalogue.HandItemId,
            99999,
            -4,
            CosmeticCatalogue.HeadItemId);

        var normalized = CosmeticCatalogue.Normalize(requested);

        Assert.Equal(AvatarAppearance.Default.BodyColorId, normalized.BodyColorId);
        Assert.Equal(AvatarAppearance.Default.HeadId, normalized.HeadId);
        Assert.Equal(AvatarAppearance.Default.BackId, normalized.BackId);
        Assert.Equal(AvatarAppearance.Default.HipsId, normalized.HipsId);
        Assert.Equal(AvatarAppearance.Default.HandId, normalized.HandId);
    }

    [Fact]
    public void ValidIdsSurviveNormalization()
    {
        var requested = new AvatarAppearance(
            CosmeticCatalogue.BlueBodyId,
            CosmeticCatalogue.HeadItemId,
            CosmeticCatalogue.BackItemId,
            CosmeticCatalogue.HipsItemId,
            CosmeticCatalogue.HandItemId);

        Assert.Equal(requested, CosmeticCatalogue.Normalize(requested));
    }

    [Fact]
    public void DefinitionsHaveGloballyUniqueNonzeroIds()
    {
        var definitions = Enum.GetValues<CosmeticSlot>().SelectMany(CosmeticCatalogue.DefinitionsFor).ToArray();
        var concrete = definitions.Where(definition => definition.Id != CosmeticCatalogue.None).ToArray();
        Assert.All(concrete, definition => Assert.NotEqual(0, definition.Id));
        Assert.Equal(concrete.Length, concrete.Select(definition => definition.Id).Distinct().Count());
        Assert.True(CosmeticCatalogue.IsValid(CosmeticSlot.Head, CosmeticCatalogue.HeadItemId));
        Assert.False(CosmeticCatalogue.IsValid(CosmeticSlot.Head, CosmeticCatalogue.HandItemId));
    }
}
