using Godot;
using MultiplayerTemplate.Avatar;
using MultiplayerTemplate.Network;

namespace MultiplayerTemplate.UI;

/// <summary>
/// Lobby-only modal for the local player's requested appearance. Its preview is a fully
/// local SubViewport scene so it never relies on a match-spawned GameWorld player.
/// </summary>
public partial class AppearanceCustomizationPanel : CanvasLayer
{
    private const string BlockerId = "appearance_customization";

    private readonly System.Collections.Generic.Dictionary<CosmeticSlot, OptionButton> _selectors = new();
    private PanelContainer _panel = null!;
    private BlockmanRig _previewRig = null!;

    public bool IsOpen => _panel is { Visible: true };
    public BlockmanRig? PreviewRig => _previewRig;

    public override void _Ready()
    {
        Layer = 30;
        BuildUi();
        SessionManager.Instance.RosterChanged += ReconcileRoster;
        SessionManager.Instance.StateChanged += OnSessionStateChanged;
    }

    public override void _ExitTree()
    {
        Hide();
        if (SessionManager.Instance == null) return;
        SessionManager.Instance.RosterChanged -= ReconcileRoster;
        SessionManager.Instance.StateChanged -= OnSessionStateChanged;
    }

    /// <summary>Shows the modal only while a connected session is in its Lobby state.</summary>
    public void ShowForLobby()
    {
        if (SessionManager.Instance.State != SessionManager.SessionState.Lobby) return;
        ApplyAppearance(AppearanceProfileService.Instance.RequestedAppearance, submit: false);
        _panel.Visible = true;
        LocalInputContext.Instance.SetBlocked(BlockerId, true);
    }

    /// <summary>Closes the modal and always releases its local-input blocker.</summary>
    public new void Hide()
    {
        if (_panel != null) _panel.Visible = false;
        LocalInputContext.Instance?.SetBlocked(BlockerId, false);
    }

    private void BuildUi()
    {
        _panel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(760, 520) };
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        AddChild(_panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        _panel.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);
        var heading = new Label { Text = "CUSTOMIZE APPEARANCE", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 24);
        content.AddChild(heading);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        content.AddChild(body);
        var choices = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(choices);
        foreach (var slot in new[] { CosmeticSlot.BodyColor, CosmeticSlot.Head, CosmeticSlot.Back, CosmeticSlot.Hips, CosmeticSlot.Hand })
            choices.AddChild(BuildSelector(slot));
        body.AddChild(BuildPreview());
        content.AddChild(Button("Close", Hide));
    }

    private Control BuildSelector(CosmeticSlot slot)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = SlotLabel(slot), CustomMinimumSize = new Vector2(105, 0) });
        var select = new OptionButton { Name = $"{slot}Select", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var definition in CosmeticCatalogue.DefinitionsFor(slot)) select.AddItem(definition.DisplayName, definition.Id);
        select.ItemSelected += _ => ApplySelection(slot);
        _selectors[slot] = select;
        row.AddChild(select);
        return row;
    }

    private Control BuildPreview()
    {
        var container = new SubViewportContainer { CustomMinimumSize = new Vector2(300, 360), Stretch = true };
        var viewport = new SubViewport
        {
            Name = "AppearancePreviewViewport", Size = new Vector2I(300, 360), OwnWorld3D = true,
            TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        container.AddChild(viewport);
        var world = new Node3D { Name = "AppearancePreviewWorld" };
        viewport.AddChild(world);
        _previewRig = new BlockmanRig { Name = "AppearancePreviewRig", Position = new Vector3(0, -1.05f, 0) };
        world.AddChild(_previewRig);
        var camera = new Camera3D
        {
            Name = "AppearancePreviewCamera", Position = new Vector3(0, 1.25f, 4.5f),
            RotationDegrees = new Vector3(-2, 0, 0)
        };
        world.AddChild(camera);
        world.AddChild(new DirectionalLight3D
        {
            Name = "AppearancePreviewLight", RotationDegrees = new Vector3(-40, -25, 0), LightEnergy = 1.4f,
            ShadowEnabled = true
        });
        return container;
    }

    private void ApplySelection(CosmeticSlot slot)
    {
        var current = AppearanceProfileService.Instance.RequestedAppearance;
        var appearance = slot switch
        {
            CosmeticSlot.BodyColor => current with { BodyColorId = SelectedId(slot) },
            CosmeticSlot.Head => current with { HeadId = SelectedId(slot) },
            CosmeticSlot.Back => current with { BackId = SelectedId(slot) },
            CosmeticSlot.Hips => current with { HipsId = SelectedId(slot) },
            CosmeticSlot.Hand => current with { HandId = SelectedId(slot) },
            _ => current
        };
        ApplyAppearance(appearance, submit: true);
    }

    private int SelectedId(CosmeticSlot slot)
    {
        var selector = _selectors[slot];
        return selector.Selected < 0 ? CosmeticCatalogue.None : selector.GetItemId(selector.Selected);
    }

    private void ApplyAppearance(AvatarAppearance appearance, bool submit)
    {
        appearance = CosmeticCatalogue.Normalize(appearance);
        _previewRig.ApplyAppearance(appearance);
        SetSelections(appearance);
        if (!submit) return;
        AppearanceProfileService.Instance.SetRequestedAppearance(appearance);
        SessionManager.Instance.RequestAppearance(appearance);
    }

    private void ReconcileRoster(Godot.Collections.Dictionary<long, Godot.Collections.Dictionary> roster)
    {
        if (!IsOpen || !Multiplayer.HasMultiplayerPeer() ||
            !roster.TryGetValue(Multiplayer.GetUniqueId(), out var mine)) return;
        ApplyAppearance(PlayerIdentity.FromDictionary(Multiplayer.GetUniqueId(), mine).Appearance, submit: false);
    }

    private void OnSessionStateChanged(SessionManager.SessionState _, SessionManager.SessionState current)
    {
        if (current != SessionManager.SessionState.Lobby) Hide();
    }

    private void SetSelections(AvatarAppearance appearance)
    {
        Select(CosmeticSlot.BodyColor, appearance.BodyColorId);
        Select(CosmeticSlot.Head, appearance.HeadId);
        Select(CosmeticSlot.Back, appearance.BackId);
        Select(CosmeticSlot.Hips, appearance.HipsId);
        Select(CosmeticSlot.Hand, appearance.HandId);
    }

    private void Select(CosmeticSlot slot, int id)
    {
        var selector = _selectors[slot];
        for (var index = 0; index < selector.ItemCount; index++)
            if (selector.GetItemId(index) == id)
            {
                selector.Select(index);
                return;
            }
    }

    private static string SlotLabel(CosmeticSlot slot) => slot switch
    {
        CosmeticSlot.BodyColor => "Body color",
        _ => slot.ToString()
    };

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text };
        button.Pressed += action;
        return button;
    }
}
