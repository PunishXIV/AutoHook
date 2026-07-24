using AutoHook.Spearfishing.Enums;
using System.ComponentModel;

namespace AutoHook.Classes;

public class BaseGig(int itemId) : BaseOption {
    [DefaultValue(true)]
    public bool Enabled = true;

    private int _itemId = itemId;
    public ImportedFish? Fish {
        get {
            if (field == null && _itemId != 0)
                field = GameRes.SpearfishFishes.FirstOrDefault(f => f.ItemId == _itemId);
            return field;
        }
        set {
            field = value;
            _itemId = value?.ItemId ?? 0;
        }
    } = GameRes.SpearfishFishes.FirstOrDefault(f => f.ItemId == itemId);

    public bool UseNaturesBounty;

    public float LeftOffset;
    public float RightOffset;

    public SpearfishSpeed Speed => Fish?.Speed ?? SpearfishSpeed.Unknown;
    public SpearfishSize Size => Fish?.Size ?? SpearfishSize.Unknown;

    public override void DrawOptions() {
        DrawUtil.DrawComboSelector([.. GameRes.SpearfishFishes], item => item.Name, Fish?.Name ?? UIStrings.None, item => Fish = item);

        DrawUtil.Checkbox(UIStrings.UseNaturesBounty, ref UseNaturesBounty);

        DrawUtil.DrawTreeNodeEx(UIStrings.Fish_Hitbox_Offset, () => {
            if (DrawUtil.EditFloatField(UIStrings.OffsetLR, ref LeftOffset,
                    UIStrings.OffsetLRHelpText, true)) {
                LeftOffset = Math.Max(-10, Math.Min(LeftOffset, 10));
                Service.Save();
            }

            if (DrawUtil.EditFloatField(UIStrings.OffsetRL, ref RightOffset,
                    UIStrings.OffsetRLHelpText, true)) {
                RightOffset = Math.Max(-10, Math.Min(RightOffset, 10));
                Service.Save();
            }
        }, UIStrings.FishHitboxHelpText);

    }

    public override bool Equals(object? obj) {
        return obj is BaseGig settings &&
               Fish?.ItemId == settings.Fish?.ItemId;
    }

    public override int GetHashCode() {
        return HashCode.Combine(UniqueId);
    }
}
