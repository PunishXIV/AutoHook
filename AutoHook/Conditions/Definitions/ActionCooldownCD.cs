using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action;
using static AutoHook.Conditions.IConditionDefinition;

namespace AutoHook.Conditions.Definitions;

public sealed class ActionCooldownCD : IConditionDefinition {
    public const string CheckAvailability = "avail";
    public const string CheckCooldown = "cd";

    public string Id => nameof(ActionCooldownCD);
    public string Name => "Action";
    public ConditionScopeFlags AllowedScopes => ConditionScopeFlags.Hook | ConditionScopeFlags.AutoCast;

    public readonly record struct ActionCooldownParams(uint Id, int Type, string Check, int Seconds, string Op, bool Invert) {
        public bool Apply(bool result) => Invert ? !result : result;

        public bool IsAvailability => Check == CheckAvailability;

        public Dictionary<string, object> ToParams() {
            var dict = new Dictionary<string, object>();

            if (Id != 0)
                dict["id"] = (long)Id;

            if (Type != 0)
                dict["type"] = (long)Type;

            dict["chk"] = IsAvailability ? CheckAvailability : CheckCooldown;

            if (!IsAvailability) {
                dict["sec"] = (long)Seconds;

                if (!string.IsNullOrEmpty(Op) && Op != "=")
                    dict["op"] = Op;
            }

            if (Invert)
                dict["inv"] = true;

            return dict;
        }
    }

    public bool Evaluate(WorldState world, IReadOnlyDictionary<string, object> parameters) {
        var args = GetParams(parameters);
        if (args.Id == 0)
            return false;

        var actionType = GetActionType(args.Type, args.Id);

        if (args.IsAvailability)
            return args.Apply(world.ActionAvailable(args.Id, actionType));

        var lhs = GetCooldownSeconds(world, args.Id, actionType);
        var rhs = args.Seconds;
        var result = CompareInt(lhs, rhs, args.Op);
        return args.Apply(result);
    }

    public void DrawParams(Condition condition) {
        var args = GetParams(condition.Params);

        var typeInt = args.Type;
        var label = typeInt == 1 ? "Item" : "Action";

        ImGui.SetNextItemWidth(90.Scaled());
        using (var comboType = ImRaii.Combo("Type", label)) {
            if (comboType.Success) {
                if (ImGui.Selectable("Action", typeInt == 0)) typeInt = 0;
                if (ImGui.Selectable("Item", typeInt == 1)) typeInt = 1;

                args = args with { Type = typeInt };
                condition.Params = args.ToParams();
            }
        }

        ImGui.SameLine();
        var currentId = args.Id;
        var idLabel = GetIdLabel(typeInt, currentId);

        if (typeInt == 1) {
            var items = typeof(IDs.Item).GetFields()
                .Select(f => f.GetValue(null))
                .OfType<uint>()
                .Where(id => id != 0)
                .Select(id => {
                    var (baseId, itemKind) = ItemUtil.GetBaseId(id);
                    var name = Item.GetRow(baseId).Name.ToString();
                    var hq = itemKind is ItemKind.Hq ? $" {SeIconChar.HighQuality.ToIconString()}" : string.Empty;
                    return (Id: id, Name: $"{id}: {name}{hq}");
                })
                .Where(x => !string.IsNullOrEmpty(x.Name))
                .OrderBy(x => x.Name)
                .ToList();

            DrawUtil.DrawComboSelector(
                items,
                i => i.Name,
                idLabel,
                i => {
                    currentId = i.Id;
                    args = args with { Id = i.Id };
                    condition.Params = args.ToParams();
                });
        }
        else {
            var actions = typeof(IDs.Actions).GetFields()
                .Select(f => f.GetValue(null))
                .OfType<uint>()
                .Where(id => id != 0)
                .Select(id => (Id: id, Name: $"{id}: {LuminaAction.GetRow(id).Name}"))
                .Where(x => !string.IsNullOrEmpty(x.Name))
                .OrderBy(x => x.Name)
                .ToList();

            DrawUtil.DrawComboSelector(
                actions,
                a => a.Name,
                idLabel,
                a => {
                    currentId = a.Id;
                    args = args with { Id = a.Id };
                    condition.Params = args.ToParams();
                });
        }

        ImGui.SameLine();
        var checkLabel = args.IsAvailability ? UIStrings.Availability : UIStrings.Cooldown;
        ImGui.SetNextItemWidth(110.Scaled());
        using (var comboCheck = ImRaii.Combo("##act_cd_chk", checkLabel)) {
            if (comboCheck.Success) {
                if (ImGui.Selectable(UIStrings.Availability, args.IsAvailability)) {
                    args = args with { Check = CheckAvailability };
                    condition.Params = args.ToParams();
                }

                if (ImGui.Selectable(UIStrings.Cooldown, !args.IsAvailability)) {
                    args = args with { Check = CheckCooldown };
                    condition.Params = args.ToParams();
                }
            }
        }

        if (args.IsAvailability)
            return;

        var opLabel = args.Op is ">" or ">=" or "<" or "<=" or "=" ? args.Op : "=";
        ImGui.SetNextItemWidth(50.Scaled());
        using (var comboOp = ImRaii.Combo("##act_cd_op", opLabel)) {
            if (comboOp.Success) {
                foreach (var choice in new[] { ">", ">=", "<", "<=", "=" }) {
                    var sel = choice == args.Op;
                    if (!ImGui.Selectable(choice, sel))
                        continue;

                    args = args with { Op = choice };
                    condition.Params = args.ToParams();
                }
            }
        }

        ImGui.SameLine();
        var sec = args.Seconds;
        ImGui.SetNextItemWidth(80.Scaled());
        if (ImGui.InputInt("Cooldown (sec)", ref sec)) {
            sec = Math.Max(0, sec);
            args = args with { Seconds = sec };
            condition.Params = args.ToParams();
        }
    }

    private static ActionCooldownParams GetParams(IReadOnlyDictionary<string, object> p) {
        var id = GetUInt(p, "id", 0);
        var type = GetInt(p, "type", 0);
        var sec = GetDouble(p, "sec", 0);
        var op = GetOp(p, "op", "=");
        var inv = GetBool(p, "inv", false);
        var secondsInt = (int)Math.Floor(sec);
        var check = GetOp(p, "chk", CheckCooldown) is CheckAvailability ? CheckAvailability : CheckCooldown;
        return new ActionCooldownParams(id, type, check, secondsInt, op, inv);
    }

    private static string GetIdLabel(int type, uint id) => type switch {
        1 when id is 0 => "Select item",
        _ when id is 0 => "Select action",
        1 => $"{id}: {Item.GetRow(ItemUtil.GetBaseId(id).ItemId).Name}",
        _ => $"{id}: {LuminaAction.GetRow(id).Name}",
    };

    private static ActionType GetActionType(int type, uint id) {
        if (type == 1)
            return ActionType.Item;
        return ActionType.Action;
    }

    private static int GetCooldownSeconds(WorldState world, uint id, ActionType type) {
        try {
            return world.GetCooldownSeconds(id, type);
        }
        catch {
            return int.MaxValue;
        }
    }

    public string DescribeParameters(IReadOnlyDictionary<string, object> parameters) {
        var args = GetParams(parameters);
        var action = args.Id == 0 ? "action" : LuminaAction.GetRow(args.Id).Name.ToString();
        if (args.IsAvailability)
            return $"{action} available";

        return $"{action} {ConditionParameterFormat.FormatIntCompare(parameters, valueKey: "sec", defaultValue: 0, defaultOp: "=")}s remaining";
    }
}
