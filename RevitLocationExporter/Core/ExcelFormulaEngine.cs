using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RevitLocationExporter.Configuration;

namespace RevitLocationExporter.Core
{
    /// <summary>
    /// Evaluates Excel-like formulas (=LEFT, =RIGHT, =MID, =IF, =IFERROR, &, comparisons, arithmetic)
    /// against dynamic parameter lookups, with topological dependency sorting and cycle detection.
    /// </summary>
    public static class ExcelFormulaEngine
    {
        public sealed class FormulaException : Exception
        {
            public FormulaException(string message) : base(message) { }
        }

        private enum TokenKind
        {
            Eof,
            Number,
            String,
            Boolean,
            Identifier,   // Function name
            Reference,    // [ParamName] or {ParamName}
            LParen,
            RParen,
            Comma,
            Amp,
            Plus,
            Minus,
            Star,
            Slash,
            Eq,
            NotEq,
            Lt,
            Gt,
            LtEq,
            GtEq
        }

        private readonly struct Token
        {
            public readonly TokenKind Kind;
            public readonly string Text;
            public readonly double NumberValue;
            public readonly bool BoolValue;
            public readonly int Position;

            public Token(TokenKind kind, string text, int position, double num = 0, bool b = false)
            {
                Kind = kind;
                Text = text;
                Position = position;
                NumberValue = num;
                BoolValue = b;
            }
        }

        private abstract class ExprNode
        {
            public abstract object Evaluate(Func<string, string?> resolver);
            public abstract void CollectReferences(HashSet<string> refs);
        }

        private sealed class LiteralNode : ExprNode
        {
            private readonly object _value;
            public LiteralNode(object value) { _value = value; }
            public override object Evaluate(Func<string, string?> resolver) => _value;
            public override void CollectReferences(HashSet<string> refs) { }
        }

        private sealed class ReferenceNode : ExprNode
        {
            public readonly string Name;
            public ReferenceNode(string name) { Name = name; }
            public override object Evaluate(Func<string, string?> resolver) => resolver(Name) ?? string.Empty;
            public override void CollectReferences(HashSet<string> refs) => refs.Add(Name);
        }

        private sealed class UnaryNode : ExprNode
        {
            private readonly TokenKind _op;
            private readonly ExprNode _inner;
            public UnaryNode(TokenKind op, ExprNode inner) { _op = op; _inner = inner; }

            public override object Evaluate(Func<string, string?> resolver)
            {
                double val = ToNumber(_inner.Evaluate(resolver));
                return _op == TokenKind.Minus ? -val : val;
            }

            public override void CollectReferences(HashSet<string> refs) => _inner.CollectReferences(refs);
        }

        private sealed class BinaryNode : ExprNode
        {
            private readonly TokenKind _op;
            private readonly ExprNode _left;
            private readonly ExprNode _right;
            public BinaryNode(TokenKind op, ExprNode left, ExprNode right)
            {
                _op = op;
                _left = left;
                _right = right;
            }

            public override object Evaluate(Func<string, string?> resolver)
            {
                object l = _left.Evaluate(resolver);
                object r = _right.Evaluate(resolver);

                switch (_op)
                {
                    case TokenKind.Amp:
                        return ToStr(l) + ToStr(r);
                    case TokenKind.Plus:
                        return ToNumber(l) + ToNumber(r);
                    case TokenKind.Minus:
                        return ToNumber(l) - ToNumber(r);
                    case TokenKind.Star:
                        return ToNumber(l) * ToNumber(r);
                    case TokenKind.Slash:
                        double denom = ToNumber(r);
                        if (Math.Abs(denom) < 1e-15) throw new FormulaException("#DIV/0!");
                        return ToNumber(l) / denom;
                    case TokenKind.Eq:
                        return CompareValues(l, r) == 0;
                    case TokenKind.NotEq:
                        return CompareValues(l, r) != 0;
                    case TokenKind.Lt:
                        return CompareValues(l, r) < 0;
                    case TokenKind.Gt:
                        return CompareValues(l, r) > 0;
                    case TokenKind.LtEq:
                        return CompareValues(l, r) <= 0;
                    case TokenKind.GtEq:
                        return CompareValues(l, r) >= 0;
                    default:
                        throw new FormulaException($"Unsupported operator '{_op}'.");
                }
            }

            public override void CollectReferences(HashSet<string> refs)
            {
                _left.CollectReferences(refs);
                _right.CollectReferences(refs);
            }
        }

        private sealed class CallNode : ExprNode
        {
            private readonly string _name;
            private readonly List<ExprNode> _args;
            public CallNode(string name, List<ExprNode> args)
            {
                _name = name.ToUpperInvariant();
                _args = args;
            }

            public override object Evaluate(Func<string, string?> resolver) => EvaluateFunction(_name, _args, resolver);

            public override void CollectReferences(HashSet<string> refs)
            {
                foreach (var a in _args) a.CollectReferences(refs);
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        public static string Evaluate(string expression, Func<string, string?> resolver)
        {
            if (string.IsNullOrEmpty(expression)) return string.Empty;
            string trimmed = expression.TrimStart();
            if (!trimmed.StartsWith("=", StringComparison.Ordinal))
                return expression;

            string body = trimmed.Substring(1);
            var ast = Parse(body);
            object val = ast.Evaluate(resolver ?? (_ => string.Empty));
            return ToStr(val);
        }

        public static bool TryEvaluate(
            string expression,
            Func<string, string?> resolver,
            out string result,
            out string? error)
        {
            try
            {
                result = Evaluate(expression, resolver);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                result = string.Empty;
                error = ex.Message;
                return false;
            }
        }

        public static List<string> ExtractReferences(string expression)
        {
            var refs = new List<string>();
            if (string.IsNullOrWhiteSpace(expression)) return refs;
            string trimmed = expression.TrimStart();
            if (!trimmed.StartsWith("=", StringComparison.Ordinal))
                return refs;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var ast = Parse(trimmed.Substring(1));
                var astRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ast.CollectReferences(astRefs);
                foreach (var r in astRefs)
                {
                    if (seen.Add(r)) refs.Add(r);
                }
            }
            catch
            {
                // Fallback regex scan if expression has partial syntax while typing
                foreach (Match m in Regex.Matches(trimmed, @"\[([^\[\]]+)\]|\{([^\{\}]+)\}"))
                {
                    string name = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                        refs.Add(name);
                }
            }
            return refs;
        }

        public static bool Validate(string expression, out string? error, out List<string> references)
        {
            references = new List<string>();
            error = null;
            if (string.IsNullOrEmpty(expression)) return true;

            string trimmed = expression.TrimStart();
            if (!trimmed.StartsWith("=", StringComparison.Ordinal))
                return true;

            try
            {
                var ast = Parse(trimmed.Substring(1));
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ast.CollectReferences(set);
                references.AddRange(set);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                references = ExtractReferences(expression);
                return false;
            }
        }

        /// <summary>
        /// Evaluates enabled ParameterRules in topological dependency order and returns a dictionary
        /// keyed by ParameterName. Circular dependencies emit warnings without throwing.
        /// </summary>
        public static Dictionary<string, (ParameterRule Rule, string Value)> ResolveRulesInOrder(
            IList<ParameterRule> rules,
            Func<string, string?> baseLookup,
            out List<string> warnings)
        {
            var orderedResults = EvaluateRulesOrdered(rules, baseLookup, out warnings);
            var map = new Dictionary<string, (ParameterRule Rule, string Value)>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in orderedResults)
            {
                if (item.Error == null && !string.IsNullOrWhiteSpace(item.Rule.ParameterName))
                {
                    map[item.Rule.ParameterName.Trim()] = (item.Rule, item.Value);
                }
            }
            return map;
        }

        /// <summary>
        /// Evaluates all enabled rules in dependency order and returns per-rule results aligned with the input list
        /// (plus evaluation errors per rule, e.g. for UI DataGrid preview).
        /// </summary>
        public static List<(ParameterRule Rule, string Value, string? Error)> EvaluateRulesOrdered(
            IList<ParameterRule> rules,
            Func<string, string?> baseLookup,
            out List<string> warnings)
        {
            warnings = new List<string>();
            if (rules == null || rules.Count == 0)
                return new List<(ParameterRule, string, string?)>();

            var output = new (ParameterRule Rule, string Value, string? Error)[rules.Count];

            // Build rule index lookup per parameter name (only enabled rules with non-empty ParameterName)
            var paramToIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var activeIndices = new List<int>();

            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null || !r.Enabled || string.IsNullOrWhiteSpace(r.ParameterName))
                {
                    output[i] = (r!, string.Empty, null);
                    continue;
                }
                activeIndices.Add(i);
                string pName = r.ParameterName.Trim();
                if (!paramToIndices.TryGetValue(pName, out var list))
                {
                    list = new List<int>();
                    paramToIndices[pName] = list;
                }
                list.Add(i);
            }

            // Build dependency graph among active rule indices
            var deps = new Dictionary<int, HashSet<int>>();
            var dependents = new Dictionary<int, List<int>>();
            var inDegree = new Dictionary<int, int>();

            foreach (int idx in activeIndices)
            {
                deps[idx] = new HashSet<int>();
                dependents[idx] = new List<int>();
                inDegree[idx] = 0;
            }

            foreach (int idx in activeIndices)
            {
                var r = rules[idx];
                if (!r.IsFormula) continue;

                var refs = ExtractReferences(r.ValueOrFormula);
                string selfName = r.ParameterName.Trim();
                foreach (string refName in refs)
                {
                    if (string.Equals(refName, selfName, StringComparison.OrdinalIgnoreCase))
                        continue; // self-reference falls back to baseLookup

                    if (paramToIndices.TryGetValue(refName, out var providerIndices))
                    {
                        foreach (int provIdx in providerIndices)
                        {
                            if (provIdx != idx && deps[idx].Add(provIdx))
                            {
                                dependents[provIdx].Add(idx);
                                inDegree[idx]++;
                            }
                        }
                    }
                }
            }

            // Kahn's topological sort prioritizing non-formula rules first, then original index
            var ready = new SortedSet<(int IsFormulaRank, int Index)>();
            foreach (int idx in activeIndices)
            {
                if (inDegree[idx] == 0)
                    ready.Add((rules[idx].IsFormula ? 1 : 0, idx));
            }

            var executionOrder = new List<int>(activeIndices.Count);
            while (ready.Count > 0)
            {
                var next = ready.Min;
                ready.Remove(next);
                executionOrder.Add(next.Index);

                foreach (int depIdx in dependents[next.Index])
                {
                    inDegree[depIdx]--;
                    if (inDegree[depIdx] == 0)
                        ready.Add((rules[depIdx].IsFormula ? 1 : 0, depIdx));
                }
            }

            // Remaining nodes with inDegree > 0 are part of or blocked by circular dependencies
            var cyclicSet = new HashSet<int>(activeIndices.Where(i => inDegree[i] > 0));
            foreach (int cycIdx in cyclicSet)
            {
                string pName = rules[cycIdx].ParameterName.Trim();
                string errMsg = $"Circular reference detected involving [{pName}].";
                warnings.Add(errMsg);
                output[cycIdx] = (rules[cycIdx], string.Empty, errMsg);
            }

            // Evaluate in dependency order, accumulating resolved values
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string? LookupForRule(string selfParamName, string refName)
            {
                // Avoid reading own newly written value if referencing self
                if (!string.Equals(selfParamName, refName, StringComparison.OrdinalIgnoreCase) &&
                    resolved.TryGetValue(refName, out var computed))
                {
                    return computed;
                }
                return baseLookup?.Invoke(refName);
            }

            foreach (int idx in executionOrder)
            {
                var rule = rules[idx];
                string pName = rule.ParameterName.Trim();

                if (!rule.IsFormula)
                {
                    string lit = rule.ValueOrFormula ?? string.Empty;
                    resolved[pName] = lit;
                    output[idx] = (rule, lit, null);
                }
                else
                {
                    if (TryEvaluate(rule.ValueOrFormula, k => LookupForRule(pName, k), out string val, out string? err))
                    {
                        resolved[pName] = val;
                        output[idx] = (rule, val, null);
                    }
                    else
                    {
                        warnings.Add($"Rule '{pName}' formula error: {err}");
                        output[idx] = (rule, string.Empty, err);
                    }
                }
            }

            return output.ToList();
        }

        // ── Tokenizer & Recursive-Descent Parser ─────────────────────────────

        private static ExprNode Parse(string body)
        {
            var tokens = Tokenize(body);
            int pos = 0;
            var node = ParseComparison(tokens, ref pos);
            if (tokens[pos].Kind != TokenKind.Eof)
                throw new FormulaException($"Unexpected token '{tokens[pos].Text}' at position {tokens[pos].Position + 1}.");
            return node;
        }

        private static List<Token> Tokenize(string input)
        {
            var tokens = new List<Token>();
            int i = 0;
            int len = input.Length;

            while (i < len)
            {
                char ch = input[i];
                if (char.IsWhiteSpace(ch)) { i++; continue; }

                int start = i;
                switch (ch)
                {
                    case '(': tokens.Add(new Token(TokenKind.LParen, "(", start)); i++; continue;
                    case ')': tokens.Add(new Token(TokenKind.RParen, ")", start)); i++; continue;
                    case ',':
                    case ';': tokens.Add(new Token(TokenKind.Comma, ",", start)); i++; continue;
                    case '&': tokens.Add(new Token(TokenKind.Amp, "&", start)); i++; continue;
                    case '+': tokens.Add(new Token(TokenKind.Plus, "+", start)); i++; continue;
                    case '-': tokens.Add(new Token(TokenKind.Minus, "-", start)); i++; continue;
                    case '*': tokens.Add(new Token(TokenKind.Star, "*", start)); i++; continue;
                    case '/': tokens.Add(new Token(TokenKind.Slash, "/", start)); i++; continue;
                    case '=': tokens.Add(new Token(TokenKind.Eq, "=", start)); i++; continue;
                    case '!':
                        if (i + 1 < len && input[i + 1] == '=')
                        {
                            tokens.Add(new Token(TokenKind.NotEq, "!=", start));
                            i += 2;
                            continue;
                        }
                        throw new FormulaException($"Unexpected character '!' at position {start + 1}.");
                    case '<':
                        if (i + 1 < len && input[i + 1] == '>')
                        {
                            tokens.Add(new Token(TokenKind.NotEq, "<>", start));
                            i += 2;
                        }
                        else if (i + 1 < len && input[i + 1] == '=')
                        {
                            tokens.Add(new Token(TokenKind.LtEq, "<=", start));
                            i += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenKind.Lt, "<", start));
                            i++;
                        }
                        continue;
                    case '>':
                        if (i + 1 < len && input[i + 1] == '=')
                        {
                            tokens.Add(new Token(TokenKind.GtEq, ">=", start));
                            i += 2;
                        }
                        else
                        {
                            tokens.Add(new Token(TokenKind.Gt, ">", start));
                            i++;
                        }
                        continue;
                    case '"':
                        i++;
                        var sb = new StringBuilder();
                        bool closed = false;
                        while (i < len)
                        {
                            if (input[i] == '"')
                            {
                                if (i + 1 < len && input[i + 1] == '"')
                                {
                                    sb.Append('"');
                                    i += 2;
                                }
                                else
                                {
                                    closed = true;
                                    i++;
                                    break;
                                }
                            }
                            else
                            {
                                sb.Append(input[i]);
                                i++;
                            }
                        }
                        if (!closed) throw new FormulaException($"Unterminated string literal at position {start + 1}.");
                        tokens.Add(new Token(TokenKind.String, sb.ToString(), start));
                        continue;
                    case '[':
                    case '{':
                        char endBracket = ch == '[' ? ']' : '}';
                        i++;
                        int closeIdx = input.IndexOf(endBracket, i);
                        if (closeIdx < 0)
                            throw new FormulaException($"Unclosed parameter reference '{ch}' at position {start + 1}.");
                        string refName = input.Substring(i, closeIdx - i).Trim();
                        if (string.IsNullOrEmpty(refName))
                            throw new FormulaException($"Empty parameter reference at position {start + 1}.");
                        tokens.Add(new Token(TokenKind.Reference, refName, start));
                        i = closeIdx + 1;
                        continue;
                }

                if (char.IsDigit(ch) || (ch == '.' && i + 1 < len && char.IsDigit(input[i + 1])))
                {
                    while (i < len && (char.IsDigit(input[i]) || input[i] == '.')) i++;
                    string numStr = input.Substring(start, i - start);
                    if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        throw new FormulaException($"Invalid number '{numStr}' at position {start + 1}.");
                    tokens.Add(new Token(TokenKind.Number, numStr, start, num: d));
                    continue;
                }

                if (char.IsLetter(ch) || ch == '_')
                {
                    while (i < len && (char.IsLetterOrDigit(input[i]) || input[i] == '_')) i++;
                    string ident = input.Substring(start, i - start);
                    if (ident.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                        tokens.Add(new Token(TokenKind.Boolean, ident, start, b: true));
                    else if (ident.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                        tokens.Add(new Token(TokenKind.Boolean, ident, start, b: false));
                    else
                        tokens.Add(new Token(TokenKind.Identifier, ident, start));
                    continue;
                }

                throw new FormulaException($"Unexpected character '{ch}' at position {start + 1}.");
            }

            tokens.Add(new Token(TokenKind.Eof, string.Empty, len));
            return tokens;
        }

        private static ExprNode ParseComparison(List<Token> t, ref int p)
        {
            var left = ParseConcat(t, ref p);
            while (t[p].Kind is TokenKind.Eq or TokenKind.NotEq or TokenKind.Lt or TokenKind.Gt or TokenKind.LtEq or TokenKind.GtEq)
            {
                var op = t[p].Kind;
                p++;
                var right = ParseConcat(t, ref p);
                left = new BinaryNode(op, left, right);
            }
            return left;
        }

        private static ExprNode ParseConcat(List<Token> t, ref int p)
        {
            var left = ParseAdditive(t, ref p);
            while (t[p].Kind == TokenKind.Amp)
            {
                p++;
                var right = ParseAdditive(t, ref p);
                left = new BinaryNode(TokenKind.Amp, left, right);
            }
            return left;
        }

        private static ExprNode ParseAdditive(List<Token> t, ref int p)
        {
            var left = ParseMultiplicative(t, ref p);
            while (t[p].Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = t[p].Kind;
                p++;
                var right = ParseMultiplicative(t, ref p);
                left = new BinaryNode(op, left, right);
            }
            return left;
        }

        private static ExprNode ParseMultiplicative(List<Token> t, ref int p)
        {
            var left = ParseUnary(t, ref p);
            while (t[p].Kind is TokenKind.Star or TokenKind.Slash)
            {
                var op = t[p].Kind;
                p++;
                var right = ParseUnary(t, ref p);
                left = new BinaryNode(op, left, right);
            }
            return left;
        }

        private static ExprNode ParseUnary(List<Token> t, ref int p)
        {
            if (t[p].Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = t[p].Kind;
                p++;
                return new UnaryNode(op, ParseUnary(t, ref p));
            }
            return ParsePrimary(t, ref p);
        }

        private static ExprNode ParsePrimary(List<Token> t, ref int p)
        {
            var tok = t[p];
            switch (tok.Kind)
            {
                case TokenKind.Number:
                    p++;
                    return new LiteralNode(tok.NumberValue);
                case TokenKind.String:
                    p++;
                    return new LiteralNode(tok.Text);
                case TokenKind.Boolean:
                    p++;
                    return new LiteralNode(tok.BoolValue);
                case TokenKind.Reference:
                    p++;
                    return new ReferenceNode(tok.Text);
                case TokenKind.Identifier:
                    p++;
                    if (t[p].Kind != TokenKind.LParen)
                        throw new FormulaException($"Unknown identifier '{tok.Text}'. Wrap parameter names in brackets like [{tok.Text}].");
                    p++; // consume '('
                    var args = new List<ExprNode>();
                    if (t[p].Kind != TokenKind.RParen)
                    {
                        while (true)
                        {
                            args.Add(ParseComparison(t, ref p));
                            if (t[p].Kind == TokenKind.Comma)
                            {
                                p++;
                                continue;
                            }
                            break;
                        }
                    }
                    if (t[p].Kind != TokenKind.RParen)
                        throw new FormulaException($"Expected ')' after arguments of {tok.Text} at position {t[p].Position + 1}.");
                    p++; // consume ')'
                    return new CallNode(tok.Text, args);
                case TokenKind.LParen:
                    p++;
                    var inner = ParseComparison(t, ref p);
                    if (t[p].Kind != TokenKind.RParen)
                        throw new FormulaException($"Expected ')' at position {t[p].Position + 1}.");
                    p++;
                    return inner;
                default:
                    throw new FormulaException($"Unexpected token '{tok.Text}' at position {tok.Position + 1}.");
            }
        }

        // ── Function Implementations ─────────────────────────────────────────

        private static object EvaluateFunction(string name, List<ExprNode> args, Func<string, string?> resolver)
        {
            switch (name)
            {
                // Lazy control-flow functions
                case "IF":
                {
                    RequireArgs(name, args, 2, 3);
                    bool cond = ToBool(args[0].Evaluate(resolver));
                    if (cond) return args[1].Evaluate(resolver);
                    return args.Count >= 3 ? args[2].Evaluate(resolver) : false;
                }
                case "IFERROR":
                {
                    RequireArgs(name, args, 2, 2);
                    try { return args[0].Evaluate(resolver); }
                    catch (FormulaException) { return args[1].Evaluate(resolver); }
                }
                case "AND":
                {
                    RequireArgs(name, args, 1, 64);
                    foreach (var a in args)
                        if (!ToBool(a.Evaluate(resolver))) return false;
                    return true;
                }
                case "OR":
                {
                    RequireArgs(name, args, 1, 64);
                    foreach (var a in args)
                        if (ToBool(a.Evaluate(resolver))) return true;
                    return false;
                }
                case "NOT":
                {
                    RequireArgs(name, args, 1, 1);
                    return !ToBool(args[0].Evaluate(resolver));
                }
                case "ISBLANK":
                {
                    RequireArgs(name, args, 1, 1);
                    return string.IsNullOrWhiteSpace(ToStr(args[0].Evaluate(resolver)));
                }

                // Text functions
                case "LEFT":
                {
                    RequireArgs(name, args, 1, 2);
                    string s = ToStr(args[0].Evaluate(resolver));
                    int n = args.Count >= 2 ? ToInt(args[1].Evaluate(resolver)) : 1;
                    if (n < 0) throw new FormulaException("#VALUE! (LEFT count < 0)");
                    return n >= s.Length ? s : s.Substring(0, n);
                }
                case "RIGHT":
                {
                    RequireArgs(name, args, 1, 2);
                    string s = ToStr(args[0].Evaluate(resolver));
                    int n = args.Count >= 2 ? ToInt(args[1].Evaluate(resolver)) : 1;
                    if (n < 0) throw new FormulaException("#VALUE! (RIGHT count < 0)");
                    return n >= s.Length ? s : s.Substring(s.Length - n, n);
                }
                case "MID":
                {
                    RequireArgs(name, args, 3, 3);
                    string s = ToStr(args[0].Evaluate(resolver));
                    int start = ToInt(args[1].Evaluate(resolver));
                    int count = ToInt(args[2].Evaluate(resolver));
                    if (start < 1 || count < 0) throw new FormulaException("#VALUE! (MID invalid start/count)");
                    int idx0 = start - 1;
                    if (idx0 >= s.Length || count == 0) return string.Empty;
                    int take = Math.Min(count, s.Length - idx0);
                    return s.Substring(idx0, take);
                }
                case "LEN":
                {
                    RequireArgs(name, args, 1, 1);
                    return (double)ToStr(args[0].Evaluate(resolver)).Length;
                }
                case "FIND":
                {
                    RequireArgs(name, args, 2, 3);
                    string find = ToStr(args[0].Evaluate(resolver));
                    string within = ToStr(args[1].Evaluate(resolver));
                    int start = args.Count >= 3 ? ToInt(args[2].Evaluate(resolver)) : 1;
                    if (start < 1 || start > within.Length + 1) throw new FormulaException("#VALUE! (FIND start out of range)");
                    int idx = within.IndexOf(find, start - 1, StringComparison.Ordinal);
                    if (idx < 0) throw new FormulaException($"#VALUE! (FIND '{find}' not found)");
                    return (double)(idx + 1);
                }
                case "SEARCH":
                {
                    RequireArgs(name, args, 2, 3);
                    string find = ToStr(args[0].Evaluate(resolver));
                    string within = ToStr(args[1].Evaluate(resolver));
                    int start = args.Count >= 3 ? ToInt(args[2].Evaluate(resolver)) : 1;
                    if (start < 1 || start > within.Length + 1) throw new FormulaException("#VALUE! (SEARCH start out of range)");
                    if (find.IndexOf('*') >= 0 || find.IndexOf('?') >= 0)
                    {
                        string rx = ExcelWildcardToRegex(find);
                        var m = Regex.Match(within.Substring(start - 1), rx, RegexOptions.IgnoreCase);
                        if (!m.Success) throw new FormulaException($"#VALUE! (SEARCH '{find}' not found)");
                        return (double)(start + m.Index);
                    }
                    int idx = within.IndexOf(find, start - 1, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) throw new FormulaException($"#VALUE! (SEARCH '{find}' not found)");
                    return (double)(idx + 1);
                }
                case "SUBSTITUTE":
                {
                    RequireArgs(name, args, 3, 4);
                    string text = ToStr(args[0].Evaluate(resolver));
                    string oldText = ToStr(args[1].Evaluate(resolver));
                    string newText = ToStr(args[2].Evaluate(resolver));
                    if (string.IsNullOrEmpty(oldText)) return text;

                    if (args.Count < 4)
                        return text.Replace(oldText, newText);

                    int instance = ToInt(args[3].Evaluate(resolver));
                    if (instance < 1) throw new FormulaException("#VALUE! (SUBSTITUTE instance < 1)");

                    int pos = 0;
                    int count = 0;
                    while (pos <= text.Length - oldText.Length)
                    {
                        int hit = text.IndexOf(oldText, pos, StringComparison.Ordinal);
                        if (hit < 0) break;
                        count++;
                        if (count == instance)
                            return text.Substring(0, hit) + newText + text.Substring(hit + oldText.Length);
                        pos = hit + oldText.Length;
                    }
                    return text;
                }
                case "REPLACE":
                {
                    RequireArgs(name, args, 4, 4);
                    string oldText = ToStr(args[0].Evaluate(resolver));
                    int start = ToInt(args[1].Evaluate(resolver));
                    int numChars = ToInt(args[2].Evaluate(resolver));
                    string newText = ToStr(args[3].Evaluate(resolver));
                    if (start < 1 || numChars < 0) throw new FormulaException("#VALUE! (REPLACE invalid start/count)");
                    int idx0 = Math.Min(start - 1, oldText.Length);
                    int delLen = Math.Min(numChars, oldText.Length - idx0);
                    return oldText.Substring(0, idx0) + newText + oldText.Substring(idx0 + delLen);
                }
                case "UPPER":
                {
                    RequireArgs(name, args, 1, 1);
                    return ToStr(args[0].Evaluate(resolver)).ToUpperInvariant();
                }
                case "LOWER":
                {
                    RequireArgs(name, args, 1, 1);
                    return ToStr(args[0].Evaluate(resolver)).ToLowerInvariant();
                }
                case "PROPER":
                {
                    RequireArgs(name, args, 1, 1);
                    string s = ToStr(args[0].Evaluate(resolver)).ToLowerInvariant();
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
                }
                case "TRIM":
                {
                    RequireArgs(name, args, 1, 1);
                    string s = ToStr(args[0].Evaluate(resolver)).Trim();
                    return Regex.Replace(s, @"\s+", " ");
                }
                case "CONCAT":
                case "CONCATENATE":
                {
                    var sb = new StringBuilder();
                    foreach (var a in args) sb.Append(ToStr(a.Evaluate(resolver)));
                    return sb.ToString();
                }
                case "REPT":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(resolver));
                    int times = ToInt(args[1].Evaluate(resolver));
                    if (times < 0) throw new FormulaException("#VALUE! (REPT count < 0)");
                    if (times == 0 || string.IsNullOrEmpty(text)) return string.Empty;
                    var sb = new StringBuilder(text.Length * Math.Min(times, 1024));
                    for (int k = 0; k < times; k++) sb.Append(text);
                    return sb.ToString();
                }
                case "EXACT":
                {
                    RequireArgs(name, args, 2, 2);
                    return string.Equals(
                        ToStr(args[0].Evaluate(resolver)),
                        ToStr(args[1].Evaluate(resolver)),
                        StringComparison.Ordinal);
                }
                case "SPLIT":
                {
                    RequireArgs(name, args, 3, 3);
                    string text = ToStr(args[0].Evaluate(resolver));
                    string delim = ToStr(args[1].Evaluate(resolver));
                    int index = ToInt(args[2].Evaluate(resolver));
                    if (string.IsNullOrEmpty(delim) || index == 0)
                        throw new FormulaException("#VALUE! (SPLIT invalid delimiter or 0 index)");

                    string[] parts = text.Split(new[] { delim }, StringSplitOptions.None);
                    int idx0 = index > 0 ? index - 1 : parts.Length + index;
                    if (idx0 < 0 || idx0 >= parts.Length)
                        throw new FormulaException($"#VALUE! (SPLIT index {index} out of bounds)");
                    return parts[idx0];
                }
                case "CONTAINS":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(resolver));
                    string sub = ToStr(args[1].Evaluate(resolver));
                    return text.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                case "STARTSWITH":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(resolver));
                    string prefix = ToStr(args[1].Evaluate(resolver));
                    return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }
                case "ENDSWITH":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(resolver));
                    string suffix = ToStr(args[1].Evaluate(resolver));
                    return text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
                }

                // Numeric / Formatting functions
                case "VALUE":
                {
                    RequireArgs(name, args, 1, 1);
                    return ToNumber(args[0].Evaluate(resolver));
                }
                case "TEXT":
                {
                    RequireArgs(name, args, 2, 2);
                    object raw = args[0].Evaluate(resolver);
                    string fmt = ToStr(args[1].Evaluate(resolver));
                    if (TryParseNumber(raw, out double num))
                        return num.ToString(fmt, CultureInfo.InvariantCulture);
                    if (DateTime.TryParse(ToStr(raw), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                        return dt.ToString(fmt, CultureInfo.InvariantCulture);
                    return ToStr(raw);
                }
                case "ROUND":
                {
                    RequireArgs(name, args, 2, 2);
                    double val = ToNumber(args[0].Evaluate(resolver));
                    int digits = ToInt(args[1].Evaluate(resolver));
                    if (digits >= 0) return Math.Round(val, digits, MidpointRounding.AwayFromZero);
                    double factor = Math.Pow(10, -digits);
                    return Math.Round(val / factor, 0, MidpointRounding.AwayFromZero) * factor;
                }
                case "ABS":
                {
                    RequireArgs(name, args, 1, 1);
                    return Math.Abs(ToNumber(args[0].Evaluate(resolver)));
                }
                case "INT":
                {
                    RequireArgs(name, args, 1, 1);
                    return Math.Floor(ToNumber(args[0].Evaluate(resolver)));
                }
                case "MIN":
                {
                    RequireArgs(name, args, 1, 64);
                    double m = double.PositiveInfinity;
                    foreach (var a in args) m = Math.Min(m, ToNumber(a.Evaluate(resolver)));
                    return m;
                }
                case "MAX":
                {
                    RequireArgs(name, args, 1, 64);
                    double m = double.NegativeInfinity;
                    foreach (var a in args) m = Math.Max(m, ToNumber(a.Evaluate(resolver)));
                    return m;
                }

                default:
                    throw new FormulaException($"#NAME? Unknown function '{name}'.");
            }
        }

        // ── Conversion & Comparison Helpers ──────────────────────────────────

        private static void RequireArgs(string name, List<ExprNode> args, int min, int max)
        {
            if (args.Count < min || args.Count > max)
                throw new FormulaException($"Function {name} expects {min}..{max} argument(s), got {args.Count}.");
        }

        private static string ToStr(object? v)
        {
            if (v == null) return string.Empty;
            if (v is string s) return s;
            if (v is bool b) return b ? "TRUE" : "FALSE";
            if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static bool TryParseNumber(object? v, out double num)
        {
            if (v is double d) { num = d; return true; }
            if (v is bool b) { num = b ? 1 : 0; return true; }
            string s = ToStr(v).Trim();
            if (string.IsNullOrEmpty(s)) { num = 0; return false; }
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out num);
        }

        private static double ToNumber(object? v)
        {
            if (v is double d) return d;
            if (v is bool b) return b ? 1.0 : 0.0;
            string s = ToStr(v).Trim();
            if (string.IsNullOrEmpty(s)) return 0.0;
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double n))
                return n;
            throw new FormulaException($"#VALUE! Cannot convert '{s}' to number.");
        }

        private static int ToInt(object? v) => (int)Math.Truncate(ToNumber(v));

        private static bool ToBool(object? v)
        {
            if (v is bool b) return b;
            if (v is double d) return Math.Abs(d) > 1e-15;
            string s = ToStr(v).Trim();
            if (s.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(s)) return false;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                return Math.Abs(n) > 1e-15;
            throw new FormulaException($"#VALUE! Cannot convert '{s}' to boolean.");
        }

        private static int CompareValues(object? l, object? r)
        {
            if (l is bool lb && r is bool rb)
                return lb.CompareTo(rb);

            if (TryParseNumber(l, out double ln) && TryParseNumber(r, out double rn))
                return ln.CompareTo(rn);

            return string.Compare(ToStr(l), ToStr(r), StringComparison.OrdinalIgnoreCase);
        }

        private static string ExcelWildcardToRegex(string pattern)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '~' && i + 1 < pattern.Length && (pattern[i + 1] == '*' || pattern[i + 1] == '?' || pattern[i + 1] == '~'))
                {
                    sb.Append(Regex.Escape(pattern[i + 1].ToString()));
                    i++;
                }
                else if (c == '*') sb.Append(".*");
                else if (c == '?') sb.Append('.');
                else sb.Append(Regex.Escape(c.ToString()));
            }
            return sb.ToString();
        }
    }
}
