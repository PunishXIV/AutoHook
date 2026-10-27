using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ExprToken = AutoHook.Conditions.Model.ConditionExpression.Token;
using ExprTokenKind = AutoHook.Conditions.Model.ConditionExpression.TokenKind;

namespace AutoHook.Ui;

public static class ConditionExpressionUi {
    public static void DrawExpressionEditor(ConditionSet set) {
        ImGui.TextColored(ImGuiColors.DalamudGrey, "Expression:");

        var tokens = ConditionExpression.ParseTokens(set.Expression, set.Groups.Count);
        var invalidFlags = ConditionExpression.ValidateTokens(tokens);
        var selStart = set.ExprSelectionStart;
        var selEnd = set.ExprSelectionEnd;
        var changed = false;
        var moveFrom = -1;
        var moveTo = -1;
        int? deleteIndex = null;

        if (tokens.Count > 0) {
            ImGui.SameLine();

            for (var i = 0; i < tokens.Count; i++) {
                using var _ = ImRaii.PushId(i);

                var label = ConditionExpression.GetTokenLabel(tokens[i]);
                var isSelected = selStart.HasValue && selEnd.HasValue && i >= selStart && i <= selEnd;
                var isInvalid = i < invalidFlags.Length && invalidFlags[i];
                var groupTrue = tokens[i].Kind == ExprTokenKind.Group
                                && tokens[i].GroupIndex is var gi
                                && gi >= 0
                                && gi < set.Groups.Count
                                && ConditionUi.IsGroupCurrentlyTrue(set.Groups[gi]);
                var buttonColor = isInvalid
                    ? ImGuiColors.DalamudRed
                    : groupTrue
                        ? ImGuiColors.ParsedGreen
                        : ImGuiColors.DalamudGrey3;

                using (var colour = ImRaii.PushColor(ImGuiCol.Button, buttonColor, isSelected || isInvalid)) {
                    if (ImGui.SmallButton(label)) {
                        var io = ImGui.GetIO();
                        if (io.KeyShift && selStart.HasValue && selEnd.HasValue) {
                            var start = Math.Min(selStart.Value, i);
                            var end = Math.Max(selEnd.Value, i);
                            set.ExprSelectionStart = start;
                            set.ExprSelectionEnd = end;
                        }
                        else {
                            // untoggling
                            if (selStart.HasValue && selEnd.HasValue && selStart.Value == i && selEnd.Value == i) {
                                set.ExprSelectionStart = null;
                                set.ExprSelectionEnd = null;
                            }
                            else {
                                set.ExprSelectionStart = i;
                                set.ExprSelectionEnd = i;
                            }
                        }
                    }
                }

                ImGui.DragDropSource(i, "COND_EXPR_TOKEN"u8, label);
                ImGui.DragDropTarget(i, "COND_EXPR_TOKEN"u8, tokens.Count, (sourceIndex, insertIndex) => {
                    moveFrom = sourceIndex;
                    moveTo = insertIndex;
                });

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    deleteIndex = i;

                ImGui.SameLine();
            }

            if (ImGui.SmallButton("x##expr_clear")) {
                set.Expression = null;
                set.ExprSelectionStart = null;
                set.ExprSelectionEnd = null;
                tokens.Clear();
                changed = false; // don't rebuild after clear
            }
        }

        // Apply token move
        if (moveFrom >= 0 && moveTo >= 0 && moveFrom < tokens.Count) {
            var t = tokens[moveFrom];
            tokens.RemoveAt(moveFrom);
            if (moveTo > moveFrom) moveTo--;
            moveTo = Math.Clamp(moveTo, 0, tokens.Count);
            tokens.Insert(moveTo, t);
            changed = true;
        }

        // Apply delete
        if (deleteIndex.HasValue && deleteIndex.Value >= 0 && deleteIndex.Value < tokens.Count) {
            tokens.RemoveAt(deleteIndex.Value);
            changed = true;
        }

        var hasSelection = set.ExprSelectionStart.HasValue && set.ExprSelectionEnd.HasValue
                           && tokens.Count > 0;
        if (hasSelection) {
            var start = Math.Min(set.ExprSelectionStart!.Value, set.ExprSelectionEnd!.Value);
            var end = Math.Max(set.ExprSelectionStart.Value, set.ExprSelectionEnd.Value);
            start = Math.Clamp(start, 0, tokens.Count - 1);
            end = Math.Clamp(end, 0, tokens.Count - 1);
            set.ExprSelectionStart = start;
            set.ExprSelectionEnd = end;
        }

        ImGui.TextColored(ImGuiColors.DalamudGrey, $"{UIStrings.Add}:");
        ImGui.SameLine();

        // Group chips A, B, C...
        for (var i = 0; i < set.Groups.Count && i < 26; i++) {
            var label = ((char)('A' + i)).ToString();
            using (ConditionUi.IsGroupCurrentlyTrue(set.Groups[i]) ? ImRaii.PushColor(ImGuiCol.Button, ImGuiColors.ParsedGreen) : null)
                if (ImGui.SmallButton(label)) {
                    tokens.Add(new ExprToken(ExprTokenKind.Group, i));
                    changed = true;
                }
            ImGui.SameLine();
        }

        if (ImGui.SmallButton("&&##expr_and")) {
            tokens.Add(new ExprToken(ExprTokenKind.And));
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("||##expr_or")) {
            tokens.Add(new ExprToken(ExprTokenKind.Or));
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("!##expr_not")) {
            tokens.Add(new ExprToken(ExprTokenKind.Not));
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("(")) {
            if (hasSelection) {
                var start = set.ExprSelectionStart!.Value;
                var end = set.ExprSelectionEnd!.Value;
                tokens.Insert(start, new ExprToken(ExprTokenKind.LParen));
                end++;
                tokens.Insert(end + 1, new ExprToken(ExprTokenKind.RParen));
                set.ExprSelectionStart = start;
                set.ExprSelectionEnd = end;
            }
            else {
                tokens.Add(new ExprToken(ExprTokenKind.LParen));
            }
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(")")) {
            if (hasSelection) {
                var start = set.ExprSelectionStart!.Value;
                var end = set.ExprSelectionEnd!.Value;
                tokens.Insert(start, new ExprToken(ExprTokenKind.LParen));
                end++;
                tokens.Insert(end + 1, new ExprToken(ExprTokenKind.RParen));
                set.ExprSelectionStart = start;
                set.ExprSelectionEnd = end;
            }
            else {
                tokens.Add(new ExprToken(ExprTokenKind.RParen));
            }
            changed = true;
        }

        if (changed) {
            if (tokens.Count == 0) {
                set.Expression = null;
                set.ExprSelectionStart = null;
                set.ExprSelectionEnd = null;
            }
            else {
                set.Expression = ConditionExpression.BuildExpression(tokens);

                // Clamp selection to new token count
                if (set.ExprSelectionStart.HasValue && set.ExprSelectionEnd.HasValue) {
                    var start = Math.Clamp(set.ExprSelectionStart.Value, 0, tokens.Count - 1);
                    var end = Math.Clamp(set.ExprSelectionEnd.Value, 0, tokens.Count - 1);
                    if (start > end)
                        (start, end) = (end, start);
                    set.ExprSelectionStart = start;
                    set.ExprSelectionEnd = end;
                }
            }
        }
    }
}
