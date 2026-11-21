using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;

namespace AutoHook.Modules;

public class AutoCollectables : IPluginService, IDisposable {
    private bool _pendingResolve;
    private bool _pendingForceNo;

    public AutoCollectables() {
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostUpdate, "SelectYesno", HandleAddon); // onupdate instead of setup since the pending can trigger before setup fires
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PostSetup, "SelectYesno", HandleAddon);
        IAddonLifecycle.Get().RegisterListener(AddonEvent.PreFinalize, "SelectYesno", HandleAddon);
    }

    public void Dispose() {
        IAddonLifecycle.Get().UnregisterListener(HandleAddon);
    }

    // TODO: handle new line characters in the string. Large ui scale changes the actual string in the addon
    private readonly List<string> collectablePatterns =
    [
        "Preserve the following",
        "収集価値",
        "Sammlerwert",
        "Valeur de collection",
        "收藏价值", // cn
        // if someone could add the korean translation that'd be nice
    ];

    public void RequestResolve(bool forceNo) {
        _pendingForceNo = forceNo;
        _pendingResolve = true;
    }

    private unsafe void HandleAddon(AddonEvent type, AddonArgs args) {
        switch (type) {
            case AddonEvent.PreFinalize when WorldState.Get().Fishing.CollectableWindowOpen:
                WorldState.Get().Execute(new RodState.OpSetCollectableWindowOpen(false));
                break;
            case AddonEvent.PostSetup:
                if (IsCollectableWindow(args.GetAddon<AddonSelectYesno>()) is bool open && open != WorldState.Get().Fishing.CollectableWindowOpen)
                    WorldState.Get().Execute(new RodState.OpSetCollectableWindowOpen(open));
                break;
            case AddonEvent.PostUpdate:
                if (!Configuration.C.PluginEnabled)
                    return;

                var addon = args.GetAddon<AddonSelectYesno>();
                if (!addon->AtkUnitBase.IsReady)
                    return;

                if (IsWaitingOnConditions()) {
                    _pendingResolve = false;
                    return;
                }

                if (TryGetPresetResolve(out var forceNo)) {
                    if (TrySelectYesNo(addon, forceNo))
                        _pendingResolve = false;
                    return;
                }

                if (_pendingResolve) {
                    if (TrySelectYesNo(addon, _pendingForceNo))
                        _pendingResolve = false;
                    return;
                }

                if (!Configuration.C.AutoCollectablesEnabled)
                    return;

                TrySelectYesNo(addon, forceNo: false);
                break;
        }
    }

    private IEnumerable<ExtraTrigger> GetTriggers()
        => Configuration.C.HookPresets.CurrentPreset.ExtraCfg.Triggers.Where(t => t is { Enabled: true, ResolveCollectablesWindow: true, ConditionSet: not null });

    private bool IsWaitingOnConditions() {
        var sets = GetTriggers().Select(t => t.ConditionSet!).Where(set => set.HasAnyCondition());
        return sets.Any() && sets.All(set => !set.Evaluate(WorldState.Get(), Registry));
    }

    private bool TryGetPresetResolve(out bool forceNo) {
        forceNo = false;
        if (GetTriggers().FirstOrDefault(t => t.ConditionSet!.Evaluate(WorldState.Get(), Registry)) is not { } match)
            return false;
        forceNo = match.ResolveCollectablesForceNo;
        return true;
    }

    private unsafe bool TryGetCollectablePrompt(AddonSelectYesno* addon, out Item item) {
        item = default;
        var text = addon->PromptText->NodeText.AsReadOnlySeString();
        if (!text.ContainsAny(collectablePatterns))
            return false;

        if (Item.GetRow(ItemUtil.GetBaseId(addon->AtkValues[14].UInt).ItemId) is not { IsCollectable: true, RowId: > 0 } row)
            return false;

        item = row;
        return true;
    }

    private unsafe bool IsCollectableWindow(AddonSelectYesno* addon)
        => TryGetCollectablePrompt(addon, out _);

    private unsafe bool TrySelectYesNo(AddonSelectYesno* addon, bool forceNo) {
        if (!TryGetCollectablePrompt(addon, out var item))
            return false;

        if (forceNo) {
            Answer(addon, false);
            return true;
        }

        var text = addon->PromptText->NodeText.AsReadOnlySeString().ExtractText();
        if (!int.TryParse(Regex.Match(text, @"\d+").Value, out var value))
            return false;

        if (CollectablesShopItem.FirstOrNull(x => x.Item.Value.RowId == item.RowId) is { } collectability) {
            if (value >= collectability.CollectablesShopRefine.Value.LowCollectability)
                Answer(addon, true);
            else
                Answer(addon, false);

            return true;
        }

        if (item.AetherialReduce > 0) {
            Answer(addon, true);
            return true;
        }

        if (TryGetRow<WKSItemInfo>(item.AdditionalData.RowId, out _)) {
            Answer(addon, true);
            return true;
        }

        return false;
    }

    public static unsafe void Answer(AddonSelectYesno* addon, bool IsYes) {
        var evt = new AtkEvent() { Listener = &addon->AtkUnitBase.AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
        var data = new AtkEventData();
        addon->ReceiveEvent(AtkEventType.ButtonClick, IsYes ? 0 : 1, &evt, &data);
    }
}
