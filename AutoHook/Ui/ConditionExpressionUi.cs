using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;
using ExprToken = AutoHook.Conditions.Model.ConditionExpression.Token;
using ExprTokenKind = AutoHook.Conditions.Model.ConditionExpression.TokenKind;

namespace AutoHook.Ui;

public static class ConditionExpressionUi {
    // edits requested while drawing the token line, applied after it's drawn.
    private struct FrameEdits {
        public int MoveFrom;
        public int MoveTo;
        public int? DeleteIndex;
    }

    public static void DrawExpressionEditor(ConditionSet set) {
        using var id = ImRaii.PushId("expr_editor");

        if (set.ExprTextMode)
            DrawTextMode(set);
        else
            DrawTokenMode(set);
    }

    private static void DrawTokenMode(ConditionSet set) {
        var tokens = ConditionExpression.ParseTokens(set.Expression, set.Groups.Count);
        var error = ConditionExpression.DescribeError(tokens, set.Expression, set.Groups.Count);

        var edits = DrawFrame(set, tokens, error, interactive: true);
        var changed = false;

        // Apply token move
        if (edits.MoveFrom >= 0 && edits.MoveTo >= 0 && edits.MoveFrom < tokens.Count) {
            var moveTo = edits.MoveTo;
            var t = tokens[edits.MoveFrom];
            tokens.RemoveAt(edits.MoveFrom);
            if (moveTo > edits.MoveFrom) moveTo--;
            moveTo = Math.Clamp(moveTo, 0, tokens.Count);
            tokens.Insert(moveTo, t);
            changed = true;
        }

        // Apply delete
        if (edits.DeleteIndex is { } del && del >= 0 && del < tokens.Count) {
            tokens.RemoveAt(del);
            changed = true;
        }

        ClampSelection(set, tokens.Count);
        changed |= DrawToolbar(set, tokens);

        if (changed)
            Commit(set, tokens);
    }

    private static void DrawTextMode(ConditionSet set) {
        var buffer = set.ExprTextBuffer ??= set.Expression ?? string.Empty;

        var style = ImGui.GetStyle();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - IconButtonWidth(FontAwesomeIcon.Pen) - style.ItemSpacing.X);
        if (ImGui.InputText("##expr_text", ref buffer, 256)) {
            set.ExprTextBuffer = buffer;
            if (string.IsNullOrWhiteSpace(buffer)) {
                set.Expression = null;
                Configuration.Save();
            }
            else {
                var parsed = ConditionExpression.ParseTokens(buffer, set.Groups.Count);
                if (ConditionExpression.DescribeError(parsed, buffer, set.Groups.Count) == null) {
                    set.Expression = ConditionExpression.BuildExpression(parsed);
                    Configuration.Save();
                }
            }
        }
        ImGui.TooltipOnHover("Group letters with && (AND), || (OR), ! (NOT) and ( )\nOnly saved while the expression is valid");

        ImGui.SameLine();
        DrawTextModeToggle(set);

        var tokens = ConditionExpression.ParseTokens(buffer, set.Groups.Count);
        var error = ConditionExpression.DescribeError(tokens, buffer, set.Groups.Count);
        DrawFrame(set, tokens, error, interactive: false);
    }

    private static FrameEdits DrawFrame(ConditionSet set, List<ExprToken> tokens, string? error, bool interactive) {
        var edits = new FrameEdits { MoveFrom = -1, MoveTo = -1 };
        var style = ImGui.GetStyle();
        var pad = style.FramePadding;
        var dl = ImGui.GetWindowDrawList();

        var startPos = ImGui.GetCursorScreenPos();
        var fullWidth = Math.Max(ImGui.GetContentRegionAvail().X, 100.Scaled());

        var empty = tokens.Count == 0 && string.IsNullOrWhiteSpace(error);
        string badgeText;
        Vector4 badgeColor;
        if (error != null) {
            badgeText = error;
            badgeColor = ImGuiColors.DalamudRed;
        }
        else if (!empty && ConditionExpression.TryEvaluate(ConditionExpression.BuildExpression(tokens), set.EvaluateGroups(WorldState.Get(), Registry), out var result)) {
            badgeText = result ? "true" : "false";
            badgeColor = result ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudGrey;
        }
        else {
            badgeText = string.Empty;
            badgeColor = ImGuiColors.DalamudGrey;
        }

        var showBadge = badgeText.Length > 0;
        var badgeWidth = showBadge ? ImGui.CalcTextSize(badgeText).X : 0;

        var innerLeft = startPos.X + pad.X;
        var innerRight = startPos.X + fullWidth - pad.X - (showBadge ? badgeWidth + style.ItemSpacing.X : 0);

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(new Vector2(innerLeft, startPos.Y + pad.Y));
        using (ImRaii.Group()) {
            if (empty) {
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled("Empty - groups combined with AND/OR");
            }

            var selStart = set.ExprSelectionStart;
            var selEnd = set.ExprSelectionEnd;
            var invalidFlags = ConditionExpression.ValidateTokens(tokens);
            var tokenPos = new Vector2(innerLeft, startPos.Y + pad.Y);
            for (var i = 0; i < tokens.Count; i++) {
                using var tokenId = ImRaii.PushId(i);
                var token = tokens[i];
                var label = ConditionExpression.GetDisplayLabel(token);

                var width = ImGui.CalcTextSize(label).X + pad.X * 2;
                if (i > 0 && tokenPos.X + width > innerRight)
                    tokenPos = new Vector2(innerLeft, tokenPos.Y + ImGui.GetFrameHeight() + style.ItemSpacing.Y);
                ImGui.SetCursorScreenPos(tokenPos);

                var isSelected = interactive && selStart.HasValue && selEnd.HasValue && i >= selStart && i <= selEnd;
                var isInvalid = i < invalidFlags.Length && invalidFlags[i];
                var clicked = DrawToken(set, token, label, isSelected, isInvalid);
                tokenPos.X = ImGui.GetItemRectMax().X + style.ItemSpacing.X * 0.5f;

                if (!interactive)
                    continue;

                if (clicked)
                    HandleTokenClick(set, i);

                ImGui.DragDropSource(i, "COND_EXPR_TOKEN"u8, label);
                ImGui.DragDropTarget(i, "COND_EXPR_TOKEN"u8, tokens.Count, (sourceIndex, insertIndex) => {
                    edits.MoveFrom = sourceIndex;
                    edits.MoveTo = insertIndex;
                });

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    edits.DeleteIndex = i;
            }
        }
        var contentMax = ImGui.GetItemRectMax();

        var frameBottom = contentMax.Y + pad.Y;
        if (showBadge) {
            ImGui.SetCursorScreenPos(new Vector2(startPos.X + fullWidth - pad.X - badgeWidth, startPos.Y + pad.Y));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(badgeColor, badgeText);
            if (error != null)
                ImGui.TooltipOnHover($"{error}\nInvalid - falling back to {(set.CombineMode == ConditionCombineMode.All ? "AND" : "OR")} of groups");
            else
                ImGui.TooltipOnHover("Current result of the expression");
            frameBottom = Math.Max(frameBottom, ImGui.GetItemRectMax().Y + pad.Y);
        }

        dl.ChannelsSetCurrent(0);
        var frameMax = new Vector2(startPos.X + fullWidth, frameBottom);
        dl.AddRectFilled(startPos, frameMax, ImGui.GetColorU32(ImGuiCol.FrameBg), style.FrameRounding);
        dl.ChannelsMerge();

        // reserve the frame's space in the layout
        ImGui.SetCursorScreenPos(startPos);
        ImGui.Dummy(frameMax - startPos);

        return edits;
    }

    private static bool DrawToken(ConditionSet set, ExprToken token, string label, bool isSelected, bool isInvalid) {
        var colors = ImGui.GetStyle().Colors;
        var isGroup = token.Kind == ExprTokenKind.Group;
        var group = isGroup && token.GroupIndex >= 0 && token.GroupIndex < set.Groups.Count ? set.Groups[token.GroupIndex] : null;
        var groupTrue = group != null && ConditionUi.IsGroupCurrentlyTrue(group);

        Vector4? textColor = isInvalid ? ImGuiColors.DalamudRed
            : group is { Enabled: false } ? ImGuiColors.DalamudGrey with { W = 0.6f }
            : token.Kind is ExprTokenKind.LParen or ExprTokenKind.RParen ? ImGuiColors.DalamudGrey3
            : null;

        // groups look like buttons, operators and parens like text
        var buttonColor = isSelected ? colors[(int)ImGuiCol.ButtonActive]
            : groupTrue ? ImGuiColors.ParsedGreen
            : isGroup ? colors[(int)ImGuiCol.Button]
            : Vector4.Zero;

        using var col = ImRaii.PushColor(ImGuiCol.Button, buttonColor)
            .Push(ImGuiCol.Text, textColor ?? Vector4.Zero, textColor.HasValue);
        return ImGui.Button(label);
    }

    private static void HandleTokenClick(ConditionSet set, int i) {
        var selStart = set.ExprSelectionStart;
        var selEnd = set.ExprSelectionEnd;
        if (ImGui.GetIO().KeyShift && selStart.HasValue && selEnd.HasValue) {
            set.ExprSelectionStart = Math.Min(selStart.Value, i);
            set.ExprSelectionEnd = Math.Max(selEnd.Value, i);
        }
        else if (selStart == i && selEnd == i) {
            // untoggling
            set.ExprSelectionStart = null;
            set.ExprSelectionEnd = null;
        }
        else {
            set.ExprSelectionStart = i;
            set.ExprSelectionEnd = i;
        }
    }

    // Insert: A B C | AND OR NOT | ( … ) ( )   [backspace] [clear] [text mode]
    private static bool DrawToolbar(ConditionSet set, List<ExprToken> tokens) {
        var style = ImGui.GetStyle();
        var startX = ImGui.GetCursorPosX();
        var changed = false;
        var hasSelection = set.ExprSelectionStart.HasValue && set.ExprSelectionEnd.HasValue && tokens.Count > 0;

        void Place(float width, bool first = false) {
            if (first)
                return;
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X < width) {
                ImGui.NewLine();
                ImGui.SetCursorPosX(startX);
            }
        }

        float ButtonWidth(string label) => ImGui.CalcTextSize(label).X + style.FramePadding.X * 2;

        void Separator() {
            Place(ImGui.CalcTextSize("|").X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("|");
        }

        void Insert(ExprToken token) {
            if (hasSelection) {
                var at = set.ExprSelectionEnd!.Value + 1;
                tokens.Insert(at, token);
                // keep inserting after the new token
                set.ExprSelectionStart = at;
                set.ExprSelectionEnd = at;
            }
            else {
                tokens.Add(token);
            }
            hasSelection = set.ExprSelectionStart.HasValue && set.ExprSelectionEnd.HasValue;
            changed = true;
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudGrey, "Insert:");

        // Group chips A, B, C...
        for (var i = 0; i < set.Groups.Count && i < 26; i++) {
            var label = ((char)('A' + i)).ToString();
            Place(ButtonWidth(label));
            using (ConditionUi.IsGroupCurrentlyTrue(set.Groups[i]) ? ImRaii.PushColor(ImGuiCol.Button, ImGuiColors.ParsedGreen) : null)
                if (ImGui.Button($"{label}##expr_grp{i}"))
                    Insert(new ExprToken(ExprTokenKind.Group, i));
        }

        Separator();
        foreach (var kind in new[] { ExprTokenKind.And, ExprTokenKind.Or, ExprTokenKind.Not }) {
            var label = ConditionExpression.GetDisplayLabel(new ExprToken(kind));
            Place(ButtonWidth(label));
            if (ImGui.Button($"{label}##expr_{kind}"))
                Insert(new ExprToken(kind));
        }

        Separator();
        Place(ButtonWidth("( … )"));
        using (ImRaii.Disabled(!hasSelection)) {
            if (ImGui.Button("( … )##expr_wrap") && hasSelection) {
                var start = set.ExprSelectionStart!.Value;
                var end = set.ExprSelectionEnd!.Value;
                tokens.Insert(start, new ExprToken(ExprTokenKind.LParen));
                end++;
                tokens.Insert(end + 1, new ExprToken(ExprTokenKind.RParen));
                set.ExprSelectionStart = start;
                set.ExprSelectionEnd = end;
                changed = true;
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(hasSelection ? "Wrap selection in parentheses" : "Select tokens (shift+click to extend) to wrap them in parentheses");

        Place(ButtonWidth("("));
        if (ImGui.Button("(##expr_lparen"))
            Insert(new ExprToken(ExprTokenKind.LParen));
        Place(ButtonWidth(")"));
        if (ImGui.Button(")##expr_rparen"))
            Insert(new ExprToken(ExprTokenKind.RParen));

        Separator();
        Place(IconButtonWidth(FontAwesomeIcon.Backspace));
        using (ImRaii.Disabled(tokens.Count == 0)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Backspace)) {
                if (hasSelection) {
                    var start = set.ExprSelectionStart!.Value;
                    var end = set.ExprSelectionEnd!.Value;
                    tokens.RemoveRange(start, end - start + 1);
                    set.ExprSelectionStart = null;
                    set.ExprSelectionEnd = null;
                }
                else {
                    tokens.RemoveAt(tokens.Count - 1);
                }
                changed = true;
            }
        }
        ImGui.TooltipOnHover(hasSelection ? "Delete selection" : "Delete last token");

        Place(IconButtonWidth(FontAwesomeIcon.Trash));
        using (ImRaii.Disabled(tokens.Count == 0)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                tokens.Clear();
                set.ExprSelectionStart = null;
                set.ExprSelectionEnd = null;
                changed = true;
            }
        }
        ImGui.TooltipOnHover("Clear expression");

        Place(IconButtonWidth(FontAwesomeIcon.Pen));
        DrawTextModeToggle(set);

        return changed;
    }

    private static void DrawTextModeToggle(ConditionSet set) {
        using (set.ExprTextMode ? ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]) : null) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Pen)) {
                set.ExprTextMode = !set.ExprTextMode;
                set.ExprTextBuffer = set.ExprTextMode ? set.Expression ?? string.Empty : null;
                set.ExprSelectionStart = null;
                set.ExprSelectionEnd = null;
            }
        }
        ImGui.TooltipOnHover(set.ExprTextMode ? "Back to token editor" : "Edit as text");
    }

    private static void ClampSelection(ConditionSet set, int tokenCount) {
        if (!set.ExprSelectionStart.HasValue || !set.ExprSelectionEnd.HasValue)
            return;

        if (tokenCount == 0) {
            set.ExprSelectionStart = null;
            set.ExprSelectionEnd = null;
            return;
        }

        var start = Math.Clamp(set.ExprSelectionStart.Value, 0, tokenCount - 1);
        var end = Math.Clamp(set.ExprSelectionEnd.Value, 0, tokenCount - 1);
        if (start > end)
            (start, end) = (end, start);
        set.ExprSelectionStart = start;
        set.ExprSelectionEnd = end;
    }

    private static void Commit(ConditionSet set, List<ExprToken> tokens) {
        set.Expression = tokens.Count == 0 ? null : ConditionExpression.BuildExpression(tokens);
        ClampSelection(set, tokens.Count);
        Configuration.Save();
    }

    private static float IconButtonWidth(FontAwesomeIcon icon) {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return Math.Max(ImGui.CalcTextSize(icon.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2, ImGui.GetFrameHeight());
    }
}
