using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Newtonsoft.Json;
using System.Numerics;

namespace AutoHook.Ui;

public enum ConditionScope {
    Hook,
    AutoCordial,
    FishIgnore,
    AutoCast,
    Spearfishing,
    PresetDefinition,
}

public static class ConditionUi {
    private static ConditionSet? _clipboard;
    private static string _exportBase64 = string.Empty;
    // ui: only this condition draws its editor, the rest draw their summary.
    private static int _editingConditionUiId;
    private static readonly Dictionary<ConditionScope, List<ConditionTypeDef>> ScopedTypesCache = [];

    public static CustomPresetConfig? EvaluationPreset { get; set; }
    public static Guid? ExcludeNamedConditionId { get; set; }

    public static bool IsConditionCurrentlyTrue(Condition cond)
        => cond.Enabled && cond.Evaluate(WorldState.Get(), Registry);

    public static bool IsGroupCurrentlyTrue(ConditionGroup group)
        => group.Enabled
           && group.Conditions.Any(c => c.Enabled)
           && group.Evaluate(WorldState.Get(), Registry);

    private static bool RequiresComplexConditionUi(ConditionSet set)
        => set.Groups.Count > 1 || !string.IsNullOrWhiteSpace(set.Expression);

    private static bool IsExpressionEditorVisible(ConditionSet set)
        => set.ExprVisible || !string.IsNullOrWhiteSpace(set.Expression);

    public static ConditionSet? DrawConditionSet(string label, ConditionSet? set, ConditionScope scope, bool showAdvanced = true, IReadOnlyList<string>? allowedTypeIds = null, bool showSubPrefix = false, Action? drawHeaderExtras = null) {
        set ??= new ConditionSet();
        if (set.Groups.Count == 0)
            set.Groups.Add(new ConditionGroup());

        // Either slim view or advanced view, never both. Toggle via SlimAdvancedExpanded.
        // Multi-group sets and sets with an expression always use the advanced editor.
        if (set.SlimAdvancedExpanded || RequiresComplexConditionUi(set)) {
            DrawSlimAdvancedEditor(label, set, scope, showSubPrefix, drawHeaderExtras);
            return set;
        }

        var group = set.Groups[0];
        var types = GetTypesForScope(scope, allowedTypeIds);
        var defaultTypeId = types.FirstOrDefault(d => !PresetConditionHelper.IsPresetType(d.Id))?.Id
                            ?? types.FirstOrDefault()?.Id
                            ?? Registry.GetId<StatusActiveCD>();

        // Header: (optional) sub marker + label + add + small advanced icon + optional extras.
        if (!label.IsNullOrEmpty()) {
            if (showSubPrefix) {
                ImGui.Text(" └");
                ImGui.SameLine();
            }

            ImGui.Text(label);
            ImGui.SameLine();
        }

        var newlyAddedIndex = -1;
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus)) {
            newlyAddedIndex = group.Conditions.Count;
            group.Conditions.Add(new Condition { TypeId = defaultTypeId, Params = [] });
            Configuration.Save();
        }
        ImGui.TooltipOnHover("Add condition");

        ImGui.SameLine(0, 3.Scaled());
        if (showAdvanced && ImGuiComponents.IconButton(FontAwesomeIcon.Code)) {
            set.SlimAdvancedExpanded = true;
            Configuration.Save();
        }
        ImGui.TooltipOnHover("Advanced (groups, expression)");

        if (drawHeaderExtras != null) {
            ImGui.SameLine(0, 3.Scaled());
            drawHeaderExtras();
        }

        var toRemove = new List<int>();
        using (ImRaii.PushIndent()) {
            for (var ci = 0; ci < group.Conditions.Count; ci++) {
                var cond = group.Conditions[ci];
                cond.EnsureUiId();
                using var _ = ImRaii.PushId($"slim_cond{cond.UiId}");
                if (ci == newlyAddedIndex)
                    _editingConditionUiId = cond.UiId;
                if (DrawConditionRow(cond, scope, types))
                    toRemove.Add(ci);
            }
        }
        foreach (var idx in toRemove.OrderByDescending(x => x)) {
            group.Conditions.RemoveAt(idx);
        }
        if (toRemove.Count > 0)
            Configuration.Save();

        return set;
    }

    private static void DrawSlimAdvancedEditor(string label, ConditionSet set, ConditionScope scope, bool showSubPrefix, Action? drawHeaderExtras) {
        if (!label.IsNullOrEmpty()) {
            if (showSubPrefix) {
                ImGui.Text(" └");
                ImGui.SameLine();
            }

            ImGui.Text(label);
            ImGui.SameLine();
        }

        // Back icon inline with advanced editor controls (set header).
        if (!RequiresComplexConditionUi(set)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowLeft)) {
                set.SlimAdvancedExpanded = false;
                Configuration.Save();
            }
            ImGui.TooltipOnHover("Back to simple view");
            ImGui.SameLine();
        }
        DrawSetHeader(set);
        if (drawHeaderExtras != null) {
            ImGui.SameLine(0, 3.Scaled());
            drawHeaderExtras();
        }
        ImGui.Spacing();
        var toRemoveGroup = new List<int>();
        for (var gi = 0; gi < set.Groups.Count; gi++) {
            var g = set.Groups[gi];
            var groupLetter = (char)('A' + gi);
            using var _ = ImRaii.PushId($"slim_adv_grp{gi}");
            var groupTypes = GetTypesForScope(scope);

            var gEnabled = g.Enabled;
            if (ImGui.Checkbox("##grp_enabled", ref gEnabled)) {
                g.Enabled = gEnabled;
                Configuration.Save();
            }
            ImGui.SameLine(0, 3.Scaled());
            using (IsGroupCurrentlyTrue(g) ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null)
                DrawUtil.TextV($"Group {groupLetter}");
            ImGui.TooltipOnHover($"Group {groupLetter}: {SummarizeGroup(g, groupTypes)}");
            ImGui.SameLine();
            if (DrawGroupHeader(set, g, gi, scope))
                toRemoveGroup.Add(gi);

            using (ImRaii.PushIndent()) {
                var start = ImGui.GetCursorScreenPos();
                using (ImRaii.Group())
                    DrawConditionsWithTypes(g, scope, groupTypes);
                var end = ImGui.GetItemRectMax();
                var lineX = start.X - 4.Scaled();
                if (g.Conditions.Count > 0)
                    ImGui.GetWindowDrawList().AddLine(new Vector2(lineX, start.Y), new Vector2(lineX, end.Y),
                        ImGui.GetColorU32(CombineModeColor(g.CombineMode)), 2.Scaled());
            }
            ImGui.Spacing();
        }
        foreach (var idx in toRemoveGroup.OrderByDescending(x => x)) {
            set.Groups.RemoveAt(idx);
        }
        if (toRemoveGroup.Count > 0)
            Configuration.Save();
    }

    private static void DrawSetHeader(ConditionSet set) {
        var mode = set.CombineMode;

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
            set.Groups.Add(new ConditionGroup());
        ImGui.TooltipOnHover("Add group");

        ImGui.SameLine();
        if (ImGui.Button($"{CombineModeLabel(mode)}###set_mode"))
            set.CombineMode = mode == ConditionCombineMode.All ? ConditionCombineMode.Any : ConditionCombineMode.All;
        ImGui.TooltipOnHover(mode is ConditionCombineMode.All
            ? "ALL groups must be true (AND)\nClick to switch to ANY"
            : "ANY group can be true (OR)\nClick to switch to ALL");

        ImGui.SameLine();
        var exprVisible = IsExpressionEditorVisible(set);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Code))
            set.ExprVisible = !exprVisible;
        ImGui.TooltipOnHover(exprVisible ? "Hide editor" : "Show editor");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy))
            _clipboard = CloneConditionSet(set);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Clipboard))
            if (_clipboard != null && _clipboard.Groups.Count > 0)
                ApplyClipboard(set, _clipboard);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ShareAlt)) {
            ImGui.OpenPopup("CondExport");
        }
        ImGui.TooltipOnHover("Export/import conditions & expression (Base64)");

        using (var popup = ImRaii.Popup("CondExport")) {
            if (popup.Success) {
                ImGui.TextColored(ImGuiColors.DalamudYellow, "Conditions / Expression Export");
                ImGui.Separator();

                var buttonSize = new Vector2(120.Scaled(), 0);

                if (ImGui.Button("Export conditions", buttonSize)) {
                    var json = JsonConvert.SerializeObject(set);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    _exportBase64 = Convert.ToBase64String(bytes);
                    ImGui.SetClipboardText(_exportBase64);
                }

                ImGui.SameLine();
                if (ImGui.Button("Export expression", buttonSize)) {
                    var expr = set.Expression ?? string.Empty;
                    var bytes = Encoding.UTF8.GetBytes(expr);
                    var exprBase64 = Convert.ToBase64String(bytes);
                    ImGui.SetClipboardText(exprBase64);
                }

                if (ImGui.Button("Import conditions", buttonSize)) {
                    try {
                        var fromClipboard = ImGui.GetClipboardText();
                        var data = Convert.FromBase64String(fromClipboard.Trim());
                        var json = Encoding.UTF8.GetString(data);
                        var imported = JsonConvert.DeserializeObject<ConditionSet>(json);
                        if (imported != null && imported.Groups.Count > 0) {
                            ApplyClipboard(set, imported);
                            Configuration.Save();
                        }
                    }
                    catch { }
                }

                ImGui.SameLine();
                if (ImGui.Button("Import expression", buttonSize)) {
                    try {
                        var fromClipboard = ImGui.GetClipboardText();
                        var data = Convert.FromBase64String(fromClipboard.Trim());
                        var expr = Encoding.UTF8.GetString(data);
                        set.Expression = string.IsNullOrWhiteSpace(expr) ? null : expr;
                        Configuration.Save();
                    }
                    catch { }
                }
            }
        }

        if (!IsExpressionEditorVisible(set))
            return;

        ConditionExpressionUi.DrawExpressionEditor(set);
    }

    private static bool DrawGroupHeader(ConditionSet set, ConditionGroup group, int index, ConditionScope scope) {
        var mode = group.CombineMode;
        using (ImRaii.PushColor(ImGuiCol.Text, CombineModeColor(mode))) {
            if (ImGui.Button($"{CombineModeLabel(mode)} of###grp_mode"))
                group.CombineMode = mode == ConditionCombineMode.All ? ConditionCombineMode.Any : ConditionCombineMode.All;
        }
        ImGui.TooltipOnHover(mode is ConditionCombineMode.All
            ? "ALL conditions in this group must be true (AND)\nClick to switch to ANY"
            : "ANY condition in this group can be true (OR)\nClick to switch to ALL");

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.PlusCircle)) {
            var added = new Condition {
                TypeId = GetTypesForScope(scope).FirstOrDefault(d => !PresetConditionHelper.IsPresetType(d.Id))?.Id
                         ?? GetTypesForScope(scope).FirstOrDefault()?.Id
                         ?? Registry.GetId<StatusActiveCD>(),
                Params = []
            };
            added.EnsureUiId();
            _editingConditionUiId = added.UiId;
            group.Conditions.Add(added);
            Configuration.Save();
        }
        ImGui.TooltipOnHover("Add condition");

        if (set.Groups.Count > 1 || group.Conditions.Count > 0) {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                if (ImGui.GetIO().KeyShift)
                    return true;
                group.Conditions.Clear();
            }
            ImGui.TooltipOnHover("Click: clear conditions in this group\nShift+Click: delete group");
        }

        return false;
    }

    private static bool DrawConditionContent(Condition cond, ConditionScope scope, IReadOnlyList<ConditionTypeDef> defs) {
        var typeChanged = false;
        DrawInverseToggle(cond);
        ImGui.SameLine();
        var currentLabel = ResolveTypeLabel(cond.TypeId, defs);
        ImGui.SetNextItemWidth(Math.Max(60.Scaled(), ImGui.CalcTextSize(currentLabel).X + ImGui.GetFrameHeight() + ImGui.GetStyle().FramePadding.X * 2));
        ImGui.SetNextWindowSizeConstraints(new Vector2(200.Scaled(), 0), new Vector2(float.MaxValue, float.MaxValue));
        var custom = defs.Where(d => PresetConditionHelper.IsPresetType(d.Id)).ToList();
        var builtIn = defs.Where(d => !PresetConditionHelper.IsPresetType(d.Id)).ToList();
        using (var combo = ImRaii.Combo("##type", currentLabel)) {
            if (combo) {
                foreach (var def in custom) {
                    var sel = def.Id == cond.TypeId;
                    if (ImGui.Selectable($"{def.Name}##{def.Id}", sel)) {
                        cond.TypeId = def.Id;
                        cond.Params.Clear();
                        typeChanged = true;
                    }
                }

                if (custom.Count > 0)
                    ImGui.Separator();

                foreach (var def in builtIn) {
                    var sel = def.Id == cond.TypeId;
                    if (ImGui.Selectable($"{def.Name}##{def.Id}", sel)) {
                        cond.TypeId = def.Id;
                        cond.Params.Clear();
                        typeChanged = true;
                    }
                }
            }
        }
        ImGui.SameLine();
        if (PresetConditionHelper.IsPresetType(cond.TypeId))
            custom.FirstOrDefault(d => d.Id == cond.TypeId)?.DrawParams?.Invoke(cond);
        else
            Registry.Get(cond.TypeId)?.DrawParams?.Invoke(cond);
        return typeChanged;
    }

    private static void DrawConditionsWithTypes(ConditionGroup group, ConditionScope scope, IReadOnlyList<ConditionTypeDef> defs) {
        for (var ci = 0; ci < group.Conditions.Count; ci++) {
            var cond = group.Conditions[ci];
            cond.EnsureUiId();
            using var _ = ImRaii.PushId($"cond{cond.UiId}");
            if (DrawConditionRow(cond, scope, defs)) {
                group.Conditions.RemoveAt(ci);
                ci--;
                Configuration.Save();
            }
        }
    }

    // summary row, or the editor if it's being edited. true = delete.
    private static bool DrawConditionRow(Condition cond, ConditionScope scope, IReadOnlyList<ConditionTypeDef> defs) {
        var enabled = cond.Enabled;
        if (ImGui.Checkbox("##enabled", ref enabled)) {
            cond.Enabled = enabled;
            Configuration.Save();
        }
        ImGui.TooltipOnHover(enabled ? "Enabled" : "Disabled (skipped)");
        ImGui.SameLine(0, 3.Scaled());

        if (_editingConditionUiId == cond.UiId) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Check))
                _editingConditionUiId = 0;
            ImGui.TooltipOnHover("Done editing");
            ImGui.SameLine();

            using (DrawUtil.CompactCombos()) {
                if (DrawConditionContent(cond, scope, defs))
                    Configuration.Save();
            }

            ImGui.SameLine();
            var delete = ImGuiComponents.IconButton(FontAwesomeIcon.Trash);
            ImGui.TooltipOnHover("Delete condition");
            return delete;
        }

        var summary = Summarize(cond, defs);
        var incomplete = IsIncomplete(cond);
        var frame = ImGui.GetFrameHeight();
        var avail = ImGui.GetContentRegionAvail().X;
        var textWidth = Math.Max(60.Scaled(), avail - frame - ImGui.GetStyle().ItemSpacing.X);
        var rowStart = ImGui.GetCursorScreenPos();
        var rowHovered = ImGui.IsMouseHoveringRect(rowStart, rowStart + new Vector2(avail, frame));

        var color = incomplete ? ImGuiColors.DalamudOrange
            : !cond.Enabled ? ImGuiColors.DalamudGrey
            : IsConditionCurrentlyTrue(cond) ? ImGuiColors.ParsedGreen
            : (Vector4?)null;
        var label = DrawUtil.TruncateToWidth(summary, textWidth - ImGui.GetStyle().FramePadding.X * 2);

        bool clicked;
        using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0.5f)))
        using (color is { } c ? ImRaii.PushColor(ImGuiCol.Text, c) : null)
            clicked = ImGui.Selectable($"{label}###row", false, ImGuiSelectableFlags.None, new Vector2(textWidth, frame));
        if (clicked)
            _editingConditionUiId = cond.UiId;
        ImGui.TooltipOnHover($"{ConditionTooltip(summary, incomplete)}\n\nClick to edit");

        ImGui.SameLine();
        if (!rowHovered) {
            ImGui.Dummy(new Vector2(frame, frame));
            return false;
        }

        var remove = ImGuiComponents.IconButton(FontAwesomeIcon.Trash);
        ImGui.TooltipOnHover("Delete condition");
        return remove;
    }

    private static IReadOnlyList<ConditionTypeDef> GetTypesForScope(ConditionScope scope, IReadOnlyList<string>? allowedTypeIds = null) {
        var builtIn = GetScopedTypes(scope);
        if (allowedTypeIds is { Count: > 0 })
            builtIn = [.. builtIn.Where(d => allowedTypeIds.Contains(d.Id))];

        var preset = EvaluationPreset;
        if (preset == null || preset.NamedConditions.Count == 0)
            return builtIn;

        var custom = PresetConditionHelper.GetPresetTypeDefs(preset, ExcludeNamedConditionId);
        return [.. custom, .. builtIn];
    }

    private static string CombineModeLabel(ConditionCombineMode mode)
        => mode == ConditionCombineMode.All ? "ALL" : "ANY";

    private static Vector4 CombineModeColor(ConditionCombineMode mode)
        => mode == ConditionCombineMode.All ? ImGuiColors.TankBlue : ImGuiColors.DalamudViolet;


    private static bool IsIncomplete(Condition cond)
        => !PresetConditionHelper.IsPresetType(cond.TypeId)
           && Registry.Get(cond.TypeId)?.Definition?.IsIncomplete(cond.Params) == true;

    private static string ConditionTooltip(string summary, bool incomplete)
        => incomplete ? $"{summary}\n\nIncomplete: finish setting this up or it won't behave as expected." : summary;

     private static string Summarize(Condition cond, IReadOnlyList<ConditionTypeDef> defs) {
        var name = ResolveTypeLabel(cond.TypeId, defs);
        var inv = GetBool(cond.Params, "inv", false);

        var detail = string.Empty;
        if (!PresetConditionHelper.IsPresetType(cond.TypeId) && Registry.Get(cond.TypeId)?.Definition is { } def) {
             var p = inv ? cond.Params.Where(kv => kv.Key != "inv").ToDictionary(kv => kv.Key, kv => kv.Value) : cond.Params;
            try {
                detail = def.DescribeParameters(p);
            }
            catch {
                detail = string.Empty;
            }
        }

        var text = string.IsNullOrEmpty(detail) ? name : $"{name}: {detail}";
        return inv ? $"NOT {text}" : text;
    }

    private static string SummarizeGroup(ConditionGroup group, IReadOnlyList<ConditionTypeDef> defs) {
        var parts = group.Conditions.Where(c => c.Enabled).Select(c => Summarize(c, defs)).ToList();
        if (parts.Count == 0)
            return "(empty)";

        return string.Join(group.CombineMode == ConditionCombineMode.All ? " and " : " or ", parts);
    }

    private static string ResolveTypeLabel(string typeId, IReadOnlyList<ConditionTypeDef> defs) {
        var fromDefs = defs.FirstOrDefault(d => d.Id == typeId)?.Name;
        if (fromDefs != null)
            return fromDefs;

        return PresetConditionHelper.ResolveDisplayName(typeId, EvaluationPreset) ?? typeId;
    }

    private static IReadOnlyList<ConditionTypeDef> GetScopedTypes(ConditionScope scope) {
        if (ScopedTypesCache.TryGetValue(scope, out var cached))
            return cached;

        var all = Registry.All;
        var flag = scope switch {
            ConditionScope.Hook => ConditionScopeFlags.Hook,
            ConditionScope.AutoCordial => ConditionScopeFlags.AutoCordial,
            ConditionScope.FishIgnore => ConditionScopeFlags.FishIgnore,
            ConditionScope.AutoCast => ConditionScopeFlags.AutoCast,
            ConditionScope.Spearfishing => ConditionScopeFlags.Spearfishing,
            ConditionScope.PresetDefinition => ConditionScopeFlags.All,
            _ => ConditionScopeFlags.All,
        };
        cached = scope == ConditionScope.PresetDefinition ? [.. all] : [.. all.Where(d => (d.AllowedScopes & flag) != 0)];
        ScopedTypesCache[scope] = cached;
        return cached;
    }

    private static void DrawInverseToggle(Condition cond) {
        var inv = GetBool(cond.Params, "inv", false);
        var color = inv ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudGrey;
        using (ImRaii.PushColor(ImGuiCol.Text, color)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Exclamation)) {
                inv = !inv;
                if (inv)
                    cond.Params["inv"] = true;
                else
                    cond.Params.Remove("inv");
            }
        }
        ImGui.TooltipOnHover("Evaluate: NOT - condition must be false");
    }

    private static ConditionSet CloneConditionSet(ConditionSet src) {
        var clone = new ConditionSet {
            CombineMode = src.CombineMode,
            Groups = []
        };

        foreach (var g in src.Groups) {
            var cg = new ConditionGroup {
                CombineMode = g.CombineMode,
                Conditions = [],
                Enabled = g.Enabled
            };

            foreach (var c in g.Conditions) {
                var nc = new Condition {
                    TypeId = c.TypeId,
                    Params = CloneParams(c.Params),
                    Enabled = c.Enabled
                };
                cg.Conditions.Add(nc);
            }

            clone.Groups.Add(cg);
        }

        return clone;
    }

    private static Dictionary<string, object> CloneParams(Dictionary<string, object> src) {
        var dict = new Dictionary<string, object>(src.Count);
        foreach (var (key, value) in src) {
            dict[key] = value is List<object> list ? new List<object>(list) : value;
        }
        return dict;
    }

    private static void ApplyClipboard(ConditionSet target, ConditionSet source) {
        target.CombineMode = source.CombineMode;
        target.Expression = source.Expression;
        target.Groups.Clear();
        foreach (var g in source.Groups) {
            var cg = new ConditionGroup {
                CombineMode = g.CombineMode,
                Conditions = [],
                Enabled = g.Enabled
            };

            foreach (var c in g.Conditions) {
                var nc = new Condition {
                    TypeId = c.TypeId,
                    Params = CloneParams(c.Params),
                    Enabled = c.Enabled
                };
                cg.Conditions.Add(nc);
            }

            target.Groups.Add(cg);
        }
    }
}

