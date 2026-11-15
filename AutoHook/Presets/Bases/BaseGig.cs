using AutoHook.Spearfishing.Enums;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;
using System.ComponentModel;

namespace AutoHook.Presets.Bases;

public class BaseGig(int itemId) : BaseOption {
    [DefaultValue(true)]
    public bool Enabled = true;

    [JsonProperty("ItemId")]
    private int _itemId = itemId;

    // legacy presets stored a nested ImportedFish under "Fish"
    [JsonProperty("Fish")]
    private ImportedFish? FishLegacy {
        set {
            if (value != null)
                _itemId = value.ItemId;
        }
    }

    [JsonIgnore]
    public Fish? Fish {
        get => _itemId != 0 ? FishBaitCatalog.Get()[_itemId] : null;
        set => _itemId = value?.Id ?? 0;
    }

    [DefaultValue(0u)]
    public uint SpearfishingNotebookId { get; set; }

    public bool IsAnyPool => SpearfishingNotebookId == 0;

    public ConditionSet? GigConditionSet { get; set; }
    public AutoNaturesBounty NaturesBounty { get; set; } = new(true);
    public AutoVeteranTrade VeteranTrade { get; set; } = new(true);

    public float LeftOffset;
    public float RightOffset;

    public SpearfishSpeed Speed => Fish?.Speed ?? SpearfishSpeed.Unknown;
    public SpearfishSize Size => Fish?.Size ?? SpearfishSize.Unknown;

    public override void DrawOptions() {
        DrawUtil.DrawTreeNodeEx(UIStrings.Conditions, () => {
            GigConditionSet = ConditionUi.DrawConditionSet("", GigConditionSet, ConditionScope.Spearfishing, showAdvanced: true);
        });

        ImGui.Spacing();
        DrawUtil.DrawTreeNodeEx(UIStrings.Auto_Casts, () => {
            NaturesBounty.DrawConfig();
            VeteranTrade.DrawConfig();
        });

        ImGui.Spacing();
        DrawUtil.DrawTreeNodeEx(UIStrings.Fish_Hitbox_Offset, () => {
            DrawUtil.EditFloatField(UIStrings.OffsetLR, ref LeftOffset, -10, 10, UIStrings.OffsetLRHelpText, hoverHelpText: true);
            DrawUtil.EditFloatField(UIStrings.OffsetRL, ref RightOffset, -10, 10, UIStrings.OffsetRLHelpText, hoverHelpText: true);
        }, UIStrings.FishHitboxHelpText);
    }

    public override bool Equals(object? obj) {
        return obj is BaseGig settings && Fish?.Id == settings.Fish?.Id && SpearfishingNotebookId == settings.SpearfishingNotebookId;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Fish?.Id, SpearfishingNotebookId);
    }
}
