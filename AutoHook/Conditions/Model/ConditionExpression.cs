namespace AutoHook.Conditions.Model;

public static class ConditionExpression {
    public enum TokenKind {
        Group,
        And,
        Or,
        Not,
        LParen,
        RParen,
    }

    public readonly record struct Token(TokenKind Kind, int GroupIndex = 0);

    public static List<Token> ParseTokens(string? expr, int groupCount) {
        var tokens = new List<Token>();
        if (string.IsNullOrWhiteSpace(expr))
            return tokens;

        var s = expr;
        var len = s.Length;
        var pos = 0;

        void SkipWs() {
            while (pos < len && char.IsWhiteSpace(s[pos])) pos++;
        }

        bool Match(string token) {
            SkipWs();
            if (pos + token.Length > len) return false;
            for (var i = 0; i < token.Length; i++) {
                if (s[pos + i] != token[i])
                    return false;
            }
            pos += token.Length;
            return true;
        }

        while (pos < len) {
            SkipWs();
            if (pos >= len) break;

            if (Match("&&")) {
                tokens.Add(new Token(TokenKind.And));
                continue;
            }

            if (Match("||")) {
                tokens.Add(new Token(TokenKind.Or));
                continue;
            }

            var ch = s[pos];
            if (ch == '(') {
                tokens.Add(new Token(TokenKind.LParen));
                pos++;
                continue;
            }

            if (ch == ')') {
                tokens.Add(new Token(TokenKind.RParen));
                pos++;
                continue;
            }

            if (ch == '!') {
                tokens.Add(new Token(TokenKind.Not));
                pos++;
                continue;
            }

            if (char.IsLetter(ch)) {
                var c = char.ToUpperInvariant(ch);
                var idx = c - 'A';
                if (idx >= 0 && idx < groupCount)
                    tokens.Add(new Token(TokenKind.Group, idx));
                pos++;
                continue;
            }

            // Unknown character, skip
            pos++;
        }

        return tokens;
    }

    public static bool[] ValidateTokens(List<Token> tokens) {
        var invalid = new bool[tokens.Count];
        if (tokens.Count == 0) return invalid;

        var last = TokenKind.LParen; // treat "none" as "expect operand"
        var depth = 0;

        for (var i = 0; i < tokens.Count; i++) {
            var t = tokens[i];
            switch (t.Kind) {
                case TokenKind.Group:
                    // Invalid if previous was also operand or right paren
                    if (last is TokenKind.Group or TokenKind.RParen)
                        invalid[i] = true;
                    last = TokenKind.Group;
                    break;

                case TokenKind.And:
                case TokenKind.Or:
                    // Invalid at start, after operator, or after '('
                    if (i == 0 || last is TokenKind.And or TokenKind.Or or TokenKind.LParen)
                        invalid[i] = true;
                    last = t.Kind;
                    break;

                case TokenKind.LParen:
                    // Invalid directly after operand or ')'
                    if (last is TokenKind.Group or TokenKind.RParen)
                        invalid[i] = true;
                    depth++;
                    last = TokenKind.LParen;
                    break;

                case TokenKind.RParen:
                    // Invalid if nothing to match or if previous wasn't an operand/closing paren
                    if (depth <= 0 || last is TokenKind.And or TokenKind.Or or TokenKind.LParen)
                        invalid[i] = true;
                    else
                        depth--;
                    last = TokenKind.RParen;
                    break;

                case TokenKind.Not:
                    // Prefix only; invalid after another operand
                    if (last is TokenKind.Group or TokenKind.RParen)
                        invalid[i] = true;
                    last = TokenKind.LParen; // expect operand (same as after '(')
                    break;
            }
        }

        // Trailing operator, '(', or unary '!' is invalid
        if (tokens.Count > 0) {
            var lastIdx = tokens.Count - 1;
            if (tokens[lastIdx].Kind is TokenKind.And or TokenKind.Or or TokenKind.LParen or TokenKind.Not)
                invalid[lastIdx] = true;
        }

        // Unmatched '(' – mark from right to left until depth is satisfied
        if (depth > 0) {
            for (var i = tokens.Count - 1; i >= 0 && depth > 0; i--) {
                if (tokens[i].Kind == TokenKind.LParen) {
                    invalid[i] = true;
                    depth--;
                }
            }
        }

        return invalid;
    }

    // first problem in the expression as a short message, or null if valid.
    // raw (when given) is checked for characters ParseTokens would silently drop.
    public static string? DescribeError(List<Token> tokens, string? raw, int groupCount) {
        if (raw != null) {
            for (var i = 0; i < raw.Length; i++) {
                var ch = raw[i];
                if (char.IsWhiteSpace(ch) || ch is '(' or ')' or '!')
                    continue;
                if ((ch == '&' || ch == '|') && i + 1 < raw.Length && raw[i + 1] == ch) {
                    i++;
                    continue;
                }
                if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z') {
                    var idx = char.ToUpperInvariant(ch) - 'A';
                    if (idx >= groupCount)
                        return $"group {char.ToUpperInvariant(ch)} doesn't exist";
                    continue;
                }
                return $"unknown character '{ch}'";
            }
        }

        var invalid = ValidateTokens(tokens);
        var bad = Array.IndexOf(invalid, true);
        if (bad < 0)
            return null;

        var t = tokens[bad];
        var label = GetDisplayLabel(t);
        var isLast = bad == tokens.Count - 1;
        Token? prev = bad > 0 ? tokens[bad - 1] : null;
        var afterOperand = prev?.Kind is TokenKind.Group or TokenKind.RParen;
        var afterOperator = prev?.Kind is TokenKind.And or TokenKind.Or or TokenKind.Not;

        switch (t.Kind) {
            case TokenKind.Group:
                return $"missing AND/OR before {label}";
            case TokenKind.And or TokenKind.Or:
                if (prev == null)
                    return $"missing operand before {label}";
                if (afterOperator || prev.Value.Kind == TokenKind.LParen)
                    return $"missing operand after {GetDisplayLabel(prev.Value)}";
                return $"missing operand after {label}";
            case TokenKind.Not:
                return afterOperand ? "missing AND/OR before NOT" : "missing operand after NOT";
            case TokenKind.LParen:
                if (afterOperand)
                    return "missing AND/OR before (";
                return !isLast && tokens[bad + 1].Kind == TokenKind.RParen ? "empty ( )" : "unclosed (";
            case TokenKind.RParen:
                if (afterOperator)
                    return $"missing operand after {GetDisplayLabel(prev!.Value)}";
                if (prev?.Kind == TokenKind.LParen)
                    return "empty ( )";
                return "unexpected )";
        }

        return isLast ? "incomplete expression" : "invalid expression";
    }

    // ui label (words); GetTokenLabel is the serialized form.
    public static string GetDisplayLabel(Token token) {
        return token.Kind switch {
            TokenKind.And => "AND",
            TokenKind.Or => "OR",
            TokenKind.Not => "NOT",
            _ => GetTokenLabel(token),
        };
    }

    public static string GetTokenLabel(Token token) {
        return token.Kind switch {
            TokenKind.Group => ((char)('A' + token.GroupIndex)).ToString(),
            TokenKind.And => "&&",
            TokenKind.Or => "||",
            TokenKind.Not => "!",
            TokenKind.LParen => "(",
            TokenKind.RParen => ")",
            _ => "?",
        };
    }

    public static string BuildExpression(List<Token> tokens) {
        if (tokens.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Count; i++) {
            if (i > 0)
                sb.Append(' ');
            sb.Append(GetTokenLabel(tokens[i]));
        }

        return sb.ToString();
    }

    public static bool TryEvaluate(string? expr, bool[] groupValues, out bool result) {
        result = false;
        if (string.IsNullOrWhiteSpace(expr) || groupValues.Length == 0)
            return false;

        return TryEvaluateInternal(expr!, groupValues, out result);
    }

    private static bool TryEvaluateInternal(string expr, bool[] groupValues, out bool result) {
        result = false;
        if (groupValues.Length == 0)
            return false;

        var s = expr;
        var len = s.Length;
        var pos = 0;

        bool ParseExpr() => ParseOr();

        bool ParseOr() {
            var left = ParseAnd();
            while (true) {
                SkipWs();
                if (Match("||")) {
                    var right = ParseAnd();
                    left = left || right;
                }
                else
                    break;
            }
            return left;
        }

        bool ParseAnd() {
            var left = ParseTerm();
            while (true) {
                SkipWs();
                if (Match("&&")) {
                    var right = ParseTerm();
                    left = left && right;
                }
                else
                    break;
            }
            return left;
        }

        bool ParseTerm() {
            SkipWs();
            if (Match("!"))
                return !ParseTerm();

            if (Match("(")) {
                var v = ParseOr();
                SkipWs();
                if (!Match(")"))
                    throw new FormatException("Missing )");
                return v;
            }

            SkipWs();
            if (pos < len && char.IsLetter(s[pos])) {
                var c = char.ToUpperInvariant(s[pos++]);
                var idx = c - 'A';
                if (idx < 0 || idx >= groupValues.Length)
                    throw new FormatException("Unknown group");
                return groupValues[idx];
            }

            throw new FormatException("Unexpected token");
        }

        void SkipWs() {
            while (pos < len && char.IsWhiteSpace(s[pos])) pos++;
        }

        bool Match(string token) {
            SkipWs();
            if (pos + token.Length > len) return false;
            for (var i = 0; i < token.Length; i++) {
                if (s[pos + i] != token[i])
                    return false;
            }
            pos += token.Length;
            return true;
        }

        try {
            result = ParseExpr();
            SkipWs();
            return pos == len;
        }
        catch {
            return false;
        }
    }
}

