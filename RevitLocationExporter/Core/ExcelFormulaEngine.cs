using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RevitLocationExporter.Configuration;

namespace RevitLocationExporter.Core
{
    /// <summary>
    /// Evaluates Excel-like formulas (=LEFT, =RIGHT, =MID, =IF, =IFERROR, REGEX*, SPLIT, PADLEFT/RIGHT,
    /// COALESCE, XLOOKUP, &, comparisons, arithmetic) against dynamic parameter lookups, with
    /// cached AST compilation, topological dependency sorting, and condition evaluation.
    /// </summary>
    public static class ExcelFormulaEngine
    {
        public sealed class FormulaException : Exception
        {
            public FormulaException(string message) : base(message) { }
        }

        public delegate string? TableLookupFunc(string tableName, string keyColumn, string lookupValue, string returnColumn);

        private static readonly ConcurrentDictionary<string, ExprNode> _astCache =
            new(StringComparer.Ordinal);

        private static readonly ConcurrentDictionary<string, Regex> _regexCache =
            new(StringComparer.Ordinal);

        internal readonly struct EvalContext
        {
            public readonly Func<string, string?> Resolver;
            public readonly TableLookupFunc? TableLookup;

            public EvalContext(Func<string, string?> resolver, TableLookupFunc? tableLookup)
            {
                Resolver = resolver;
                TableLookup = tableLookup;
            }
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

        internal abstract class ExprNode
        {
            public abstract object Evaluate(in EvalContext ctx);
            public abstract void CollectReferences(HashSet<string> refs);
        }

        private sealed class LiteralNode : ExprNode
        {
            private readonly object _value;
            public LiteralNode(object value) { _value = value; }
            public override object Evaluate(in EvalContext ctx) => _value;
            public override void CollectReferences(HashSet<string> refs) { }
        }

        private sealed class ReferenceNode : ExprNode
        {
            public readonly string Name;
            public ReferenceNode(string name) { Name = name; }
            public override object Evaluate(in EvalContext ctx) => ctx.Resolver(Name) ?? string.Empty;
            public override void CollectReferences(HashSet<string> refs) => refs.Add(Name);
        }

        private sealed class UnaryNode : ExprNode
        {
            private readonly TokenKind _op;
            private readonly ExprNode _inner;
            public UnaryNode(TokenKind op, ExprNode inner) { _op = op; _inner = inner; }

            public override object Evaluate(in EvalContext ctx)
            {
                double val = ToNumber(_inner.Evaluate(in ctx));
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

            public override object Evaluate(in EvalContext ctx)
            {
                object l = _left.Evaluate(in ctx);
                object r = _right.Evaluate(in ctx);

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

            public override object Evaluate(in EvalContext ctx) => EvaluateFunction(_name, _args, in ctx);

            public override void CollectReferences(HashSet<string> refs)
            {
                foreach (var a in _args) a.CollectReferences(refs);
            }
        }

        // ── Compiled Rule Set (Zero Re-Parse / Zero Re-Sort Across Elements) ─

        internal sealed class CompiledRuleEntry
        {
            public int Index;
            public ParameterRule Rule = null!;
            public string ParameterName = string.Empty;
            public ExprNode? ValueAst;
            public string? ValueCompileError;
            public bool HasCondition;
            public ExprNode? ConditionAst;
            public bool StaticConditionValue = true;
            public string? ConditionCompileError;
        }

        public sealed class CompiledRuleSet
        {
            private readonly IList<ParameterRule> _rules;
            private readonly CompiledRuleEntry?[] _entries;
            private readonly List<int> _executionOrder;
            private readonly List<(int Index, string Message)> _circularErrors;

            public HashSet<string> AllReferencedTokens { get; }
            public int ActiveCount { get; }

            internal CompiledRuleSet(
                IList<ParameterRule> rules,
                CompiledRuleEntry?[] entries,
                List<int> executionOrder,
                List<(int Index, string Message)> circularErrors,
                HashSet<string> allReferencedTokens,
                int activeCount)
            {
                _rules = rules;
                _entries = entries;
                _executionOrder = executionOrder;
                _circularErrors = circularErrors;
                AllReferencedTokens = allReferencedTokens;
                ActiveCount = activeCount;
            }

            public List<(ParameterRule Rule, string Value, string? Error, bool ConditionMet)> EvaluateOrdered(
                Func<string, string?> baseLookup,
                out List<string> warnings,
                TableLookupFunc? tableLookup = null)
            {
                warnings = new List<string>();
                if (_rules.Count == 0)
                    return new List<(ParameterRule, string, string?, bool)>();

                var output = new (ParameterRule Rule, string Value, string? Error, bool ConditionMet)[_rules.Count];
                for (int i = 0; i < _rules.Count; i++)
                {
                    if (_entries[i] == null)
                        output[i] = (_rules[i], string.Empty, null, false);
                }

                foreach (var (cycIdx, msg) in _circularErrors)
                {
                    warnings.Add(msg);
                    output[cycIdx] = (_rules[cycIdx], string.Empty, msg, false);
                }

                var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                string? LookupForRule(string selfParamName, string refName)
                {
                    if (!string.Equals(selfParamName, refName, StringComparison.OrdinalIgnoreCase) &&
                        resolved.TryGetValue(refName, out var computed))
                    {
                        return computed;
                    }
                    return baseLookup?.Invoke(refName);
                }

                foreach (int idx in _executionOrder)
                {
                    var entry = _entries[idx]!;
                    var rule = entry.Rule;
                    string pName = entry.ParameterName;

                    Func<string, string?> ruleLookup = k => LookupForRule(pName, k);
                    var ctx = new EvalContext(ruleLookup, tableLookup);

                    // 1. Check optional Condition before evaluating/applying rule
                    if (entry.HasCondition)
                    {
                        if (entry.ConditionCompileError != null)
                        {
                            warnings.Add($"Rule '{pName}' condition error: {entry.ConditionCompileError}");
                            output[idx] = (rule, string.Empty, $"Condition: {entry.ConditionCompileError}", false);
                            continue;
                        }

                        bool condMet;
                        if (entry.ConditionAst != null)
                        {
                            try
                            {
                                condMet = ToBool(entry.ConditionAst.Evaluate(in ctx));
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"Rule '{pName}' condition error: {ex.Message}");
                                output[idx] = (rule, string.Empty, $"Condition: {ex.Message}", false);
                                continue;
                            }
                        }
                        else
                        {
                            condMet = entry.StaticConditionValue;
                        }

                        if (!condMet)
                        {
                            output[idx] = (rule, string.Empty, null, false);
                            continue;
                        }
                    }

                    // 2. Evaluate value or static literal
                    if (!rule.IsFormula)
                    {
                        string lit = rule.ValueOrFormula ?? string.Empty;
                        resolved[pName] = lit;
                        output[idx] = (rule, lit, null, true);
                    }
                    else if (entry.ValueCompileError != null)
                    {
                        warnings.Add($"Rule '{pName}' formula error: {entry.ValueCompileError}");
                        output[idx] = (rule, string.Empty, entry.ValueCompileError, true);
                    }
                    else
                    {
                        try
                        {
                            string val = ToStr(entry.ValueAst!.Evaluate(in ctx));
                            resolved[pName] = val;
                            output[idx] = (rule, val, null, true);
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Rule '{pName}' formula error: {ex.Message}");
                            output[idx] = (rule, string.Empty, ex.Message, true);
                        }
                    }
                }

                return output.ToList();
            }

            public Dictionary<string, (ParameterRule Rule, string Value)> EvaluateMap(
                Func<string, string?> baseLookup,
                out List<string> warnings,
                TableLookupFunc? tableLookup = null)
            {
                var map = new Dictionary<string, (ParameterRule Rule, string Value)>(StringComparer.OrdinalIgnoreCase);
                if (ActiveCount == 0)
                {
                    warnings = new List<string>();
                    return map;
                }

                var ordered = EvaluateOrdered(baseLookup, out warnings, tableLookup);
                foreach (var item in ordered)
                {
                    if (item.ConditionMet && item.Error == null && item.Rule != null && !string.IsNullOrWhiteSpace(item.Rule.ParameterName))
                    {
                        map[item.Rule.ParameterName.Trim()] = (item.Rule, item.Value);
                    }
                }
                return map;
            }
        }

        // ── Public API ───────────────────────────────────────────────────────

        private static ExprNode ParseCached(string body) => _astCache.GetOrAdd(body, Parse);

        private static Regex GetCachedRegex(string pattern) =>
            _regexCache.GetOrAdd(pattern, p => new Regex(p, RegexOptions.Compiled));

        public static TableLookupFunc CreateTableLookup(IDictionary<string, List<Dictionary<string, string>>>? tables)
        {
            if (tables == null || tables.Count == 0)
                return (_, _, _, _) => null;

            var indexCache = new ConcurrentDictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

            return (tableName, keyColumn, lookupValue, returnColumn) =>
            {
                if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(keyColumn) || string.IsNullOrWhiteSpace(returnColumn))
                    return null;

                if (!tables.TryGetValue(tableName.Trim(), out var rows) || rows == null || rows.Count == 0)
                    return null;

                string tName = tableName.Trim();
                string kCol = keyColumn.Trim();
                string cacheKey = $"{tName}\0{kCol}";

                var rowIndex = indexCache.GetOrAdd(cacheKey, _ =>
                {
                    var dict = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in rows)
                    {
                        if (row != null && row.TryGetValue(kCol, out string? kVal) && !string.IsNullOrWhiteSpace(kVal))
                        {
                            string trimmedKey = kVal.Trim();
                            if (!dict.ContainsKey(trimmedKey))
                                dict[trimmedKey] = row;
                        }
                    }
                    return dict;
                });

                string needle = (lookupValue ?? string.Empty).Trim();
                if (rowIndex.TryGetValue(needle, out var hit) && hit.TryGetValue(returnColumn.Trim(), out var val))
                    return val;

                if (rowIndex.TryGetValue("*", out var wildcardHit) && wildcardHit.TryGetValue(returnColumn.Trim(), out var wVal))
                    return wVal;

                return null;
            };
        }

        public static string Evaluate(string expression, Func<string, string?> resolver) =>
            Evaluate(expression, resolver, null);

        public static string Evaluate(string expression, Func<string, string?> resolver, TableLookupFunc? tableLookup)
        {
            if (string.IsNullOrEmpty(expression)) return string.Empty;
            string trimmed = expression.TrimStart();
            if (!trimmed.StartsWith("=", StringComparison.Ordinal))
                return expression;

            string body = trimmed.Substring(1);
            var ast = ParseCached(body);
            var ctx = new EvalContext(resolver ?? (_ => string.Empty), tableLookup);
            object val = ast.Evaluate(in ctx);
            return ToStr(val);
        }

        public static bool TryEvaluate(
            string expression,
            Func<string, string?> resolver,
            out string result,
            out string? error) =>
            TryEvaluate(expression, resolver, null, out result, out error);

        public static bool TryEvaluate(
            string expression,
            Func<string, string?> resolver,
            TableLookupFunc? tableLookup,
            out string result,
            out string? error)
        {
            try
            {
                result = Evaluate(expression, resolver, tableLookup);
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
                var ast = ParseCached(trimmed.Substring(1));
                var astRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ast.CollectReferences(astRefs);
                foreach (var r in astRefs)
                {
                    if (seen.Add(r)) refs.Add(r);
                }
            }
            catch
            {
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
                var ast = ParseCached(trimmed.Substring(1));
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

        public static CompiledRuleSet CompileRules(IList<ParameterRule> rules)
        {
            var emptyRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rules == null || rules.Count == 0)
            {
                return new CompiledRuleSet(
                    Array.Empty<ParameterRule>(),
                    Array.Empty<CompiledRuleEntry?>(),
                    new List<int>(),
                    new List<(int, string)>(),
                    emptyRefs,
                    0);
            }

            var entries = new CompiledRuleEntry?[rules.Count];
            var paramToIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var activeIndices = new List<int>();
            var allRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null || !r.Enabled || string.IsNullOrWhiteSpace(r.ParameterName))
                    continue;

                string pName = r.ParameterName.Trim();
                var entry = new CompiledRuleEntry
                {
                    Index = i,
                    Rule = r,
                    ParameterName = pName
                };

                if (r.IsFormula)
                {
                    try
                    {
                        entry.ValueAst = ParseCached(r.ValueOrFormula.TrimStart().Substring(1));
                    }
                    catch (Exception ex)
                    {
                        entry.ValueCompileError = ex.Message;
                    }
                }

                if (!string.IsNullOrWhiteSpace(r.Condition))
                {
                    entry.HasCondition = true;
                    string condTrim = r.Condition.Trim();
                    if (condTrim.StartsWith("=", StringComparison.Ordinal))
                    {
                        try
                        {
                            entry.ConditionAst = ParseCached(condTrim.Substring(1));
                        }
                        catch (Exception ex)
                        {
                            entry.ConditionCompileError = ex.Message;
                        }
                    }
                    else if (condTrim.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.StaticConditionValue = true;
                    }
                    else if (condTrim.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.StaticConditionValue = false;
                    }
                    else
                    {
                        try
                        {
                            entry.ConditionAst = ParseCached(condTrim);
                        }
                        catch (Exception ex)
                        {
                            entry.ConditionCompileError = ex.Message;
                        }
                    }
                }

                entries[i] = entry;
                activeIndices.Add(i);

                if (!paramToIndices.TryGetValue(pName, out var list))
                {
                    list = new List<int>();
                    paramToIndices[pName] = list;
                }
                list.Add(i);
            }

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
                var entry = entries[idx]!;
                var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                entry.ValueAst?.CollectReferences(refs);
                entry.ConditionAst?.CollectReferences(refs);
                if (entry.Rule.IsFormula && entry.ValueAst == null)
                {
                    foreach (var r in ExtractReferences(entry.Rule.ValueOrFormula)) refs.Add(r);
                }

                foreach (string refName in refs)
                {
                    allRefs.Add(refName);

                    if (string.Equals(refName, entry.ParameterName, StringComparison.OrdinalIgnoreCase))
                        continue;

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

            var circularErrors = new List<(int Index, string Message)>();
            foreach (int cycIdx in activeIndices)
            {
                if (inDegree[cycIdx] > 0)
                {
                    circularErrors.Add((cycIdx, $"Circular reference detected involving [{entries[cycIdx]!.ParameterName}]."));
                }
            }

            return new CompiledRuleSet(rules, entries, executionOrder, circularErrors, allRefs, activeIndices.Count);
        }

        public static Dictionary<string, (ParameterRule Rule, string Value)> ResolveRulesInOrder(
            IList<ParameterRule> rules,
            Func<string, string?> baseLookup,
            out List<string> warnings) =>
            ResolveRulesInOrder(rules, baseLookup, null, out warnings);

        public static Dictionary<string, (ParameterRule Rule, string Value)> ResolveRulesInOrder(
            IList<ParameterRule> rules,
            Func<string, string?> baseLookup,
            TableLookupFunc? tableLookup,
            out List<string> warnings) =>
            CompileRules(rules).EvaluateMap(baseLookup, out warnings, tableLookup);

        public static List<(ParameterRule Rule, string Value, string? Error)> EvaluateRulesOrdered(
            IList<ParameterRule> rules,
            Func<string, string?> baseLookup,
            out List<string> warnings) =>
            EvaluateRulesOrdered(rules, baseLookup, null, out warnings);

        public static List<(ParameterRule Rule, string Value, string? Error)> EvaluateRulesOrdered(
            IList<ParameterRule> rules,
            Func<string, string?> baseLookup,
            TableLookupFunc? tableLookup,
            out List<string> warnings)
        {
            var results = CompileRules(rules).EvaluateOrdered(baseLookup, out warnings, tableLookup);
            var list = new List<(ParameterRule Rule, string Value, string? Error)>(results.Count);
            foreach (var item in results)
            {
                string val = item.ConditionMet ? item.Value : string.Empty;
                list.Add((item.Rule, val, item.Error));
            }
            return list;
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
                    if (p + 1 < t.Count && t[p].Kind == TokenKind.LParen && t[p + 1].Kind == TokenKind.RParen)
                        p += 2;
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

        private static object EvaluateFunction(string name, List<ExprNode> args, in EvalContext ctx)
        {
            switch (name)
            {
                // Lazy control-flow functions
                case "IF":
                {
                    RequireArgs(name, args, 2, 3);
                    bool cond = ToBool(args[0].Evaluate(in ctx));
                    if (cond) return args[1].Evaluate(in ctx);
                    return args.Count >= 3 ? args[2].Evaluate(in ctx) : false;
                }
                case "IFERROR":
                {
                    RequireArgs(name, args, 2, 2);
                    try { return args[0].Evaluate(in ctx); }
                    catch (FormulaException) { return args[1].Evaluate(in ctx); }
                }
                case "AND":
                {
                    RequireArgs(name, args, 1, 64);
                    foreach (var a in args)
                        if (!ToBool(a.Evaluate(in ctx))) return false;
                    return true;
                }
                case "OR":
                {
                    RequireArgs(name, args, 1, 64);
                    foreach (var a in args)
                        if (ToBool(a.Evaluate(in ctx))) return true;
                    return false;
                }
                case "NOT":
                {
                    RequireArgs(name, args, 1, 1);
                    return !ToBool(args[0].Evaluate(in ctx));
                }
                case "ISBLANK":
                {
                    RequireArgs(name, args, 1, 1);
                    return string.IsNullOrWhiteSpace(ToStr(args[0].Evaluate(in ctx)));
                }
                case "COALESCE":
                {
                    RequireArgs(name, args, 1, 64);
                    foreach (var a in args)
                    {
                        string val = ToStr(a.Evaluate(in ctx));
                        if (!string.IsNullOrWhiteSpace(val))
                            return val;
                    }
                    return string.Empty;
                }
                case "XLOOKUP":
                {
                    RequireArgs(name, args, 4, 5);
                    string lookupVal = ToStr(args[0].Evaluate(in ctx));
                    string tableName = ToStr(args[1].Evaluate(in ctx));
                    string keyCol = ToStr(args[2].Evaluate(in ctx));
                    string retCol = ToStr(args[3].Evaluate(in ctx));

                    string? found = ctx.TableLookup?.Invoke(tableName, keyCol, lookupVal, retCol);
                    if (found != null) return found;
                    return args.Count >= 5 ? args[4].Evaluate(in ctx) : string.Empty;
                }

                // Text functions
                case "LEFT":
                {
                    RequireArgs(name, args, 1, 2);
                    string s = ToStr(args[0].Evaluate(in ctx));
                    int n = args.Count >= 2 ? ToInt(args[1].Evaluate(in ctx)) : 1;
                    if (n < 0) throw new FormulaException("#VALUE! (LEFT count < 0)");
                    return n >= s.Length ? s : s.Substring(0, n);
                }
                case "RIGHT":
                {
                    RequireArgs(name, args, 1, 2);
                    string s = ToStr(args[0].Evaluate(in ctx));
                    int n = args.Count >= 2 ? ToInt(args[1].Evaluate(in ctx)) : 1;
                    if (n < 0) throw new FormulaException("#VALUE! (RIGHT count < 0)");
                    return n >= s.Length ? s : s.Substring(s.Length - n, n);
                }
                case "MID":
                {
                    RequireArgs(name, args, 3, 3);
                    string s = ToStr(args[0].Evaluate(in ctx));
                    int start = ToInt(args[1].Evaluate(in ctx));
                    int count = ToInt(args[2].Evaluate(in ctx));
                    if (start < 1 || count < 0) throw new FormulaException("#VALUE! (MID invalid start/count)");
                    int idx0 = start - 1;
                    if (idx0 >= s.Length || count == 0) return string.Empty;
                    int take = Math.Min(count, s.Length - idx0);
                    return s.Substring(idx0, take);
                }
                case "LEN":
                {
                    RequireArgs(name, args, 1, 1);
                    return (double)ToStr(args[0].Evaluate(in ctx)).Length;
                }
                case "FIND":
                {
                    RequireArgs(name, args, 2, 3);
                    string find = ToStr(args[0].Evaluate(in ctx));
                    string within = ToStr(args[1].Evaluate(in ctx));
                    int start = args.Count >= 3 ? ToInt(args[2].Evaluate(in ctx)) : 1;
                    if (start < 1 || start > within.Length + 1) throw new FormulaException("#VALUE! (FIND start out of range)");
                    int idx = within.IndexOf(find, start - 1, StringComparison.Ordinal);
                    if (idx < 0) throw new FormulaException($"#VALUE! (FIND '{find}' not found)");
                    return (double)(idx + 1);
                }
                case "SEARCH":
                {
                    RequireArgs(name, args, 2, 3);
                    string find = ToStr(args[0].Evaluate(in ctx));
                    string within = ToStr(args[1].Evaluate(in ctx));
                    int start = args.Count >= 3 ? ToInt(args[2].Evaluate(in ctx)) : 1;
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
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string oldText = ToStr(args[1].Evaluate(in ctx));
                    string newText = ToStr(args[2].Evaluate(in ctx));
                    if (string.IsNullOrEmpty(oldText)) return text;

                    if (args.Count < 4)
                        return text.Replace(oldText, newText);

                    int instance = ToInt(args[3].Evaluate(in ctx));
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
                    string oldText = ToStr(args[0].Evaluate(in ctx));
                    int start = ToInt(args[1].Evaluate(in ctx));
                    int numChars = ToInt(args[2].Evaluate(in ctx));
                    string newText = ToStr(args[3].Evaluate(in ctx));
                    if (start < 1 || numChars < 0) throw new FormulaException("#VALUE! (REPLACE invalid start/count)");
                    int idx0 = Math.Min(start - 1, oldText.Length);
                    int delLen = Math.Min(numChars, oldText.Length - idx0);
                    return oldText.Substring(0, idx0) + newText + oldText.Substring(idx0 + delLen);
                }
                case "UPPER":
                {
                    RequireArgs(name, args, 1, 1);
                    return ToStr(args[0].Evaluate(in ctx)).ToUpperInvariant();
                }
                case "LOWER":
                {
                    RequireArgs(name, args, 1, 1);
                    return ToStr(args[0].Evaluate(in ctx)).ToLowerInvariant();
                }
                case "PROPER":
                {
                    RequireArgs(name, args, 1, 1);
                    string s = ToStr(args[0].Evaluate(in ctx)).ToLowerInvariant();
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
                }
                case "TRIM":
                {
                    RequireArgs(name, args, 1, 1);
                    string s = ToStr(args[0].Evaluate(in ctx)).Trim();
                    return Regex.Replace(s, @"\s+", " ");
                }
                case "CONCAT":
                case "CONCATENATE":
                {
                    var sb = new StringBuilder();
                    foreach (var a in args) sb.Append(ToStr(a.Evaluate(in ctx)));
                    return sb.ToString();
                }
                case "REPT":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    int times = ToInt(args[1].Evaluate(in ctx));
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
                        ToStr(args[0].Evaluate(in ctx)),
                        ToStr(args[1].Evaluate(in ctx)),
                        StringComparison.Ordinal);
                }
                case "SPLIT":
                {
                    RequireArgs(name, args, 3, 3);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string delim = ToStr(args[1].Evaluate(in ctx));
                    int index = ToInt(args[2].Evaluate(in ctx));
                    if (string.IsNullOrEmpty(delim) || index == 0)
                        throw new FormulaException("#VALUE! (SPLIT invalid delimiter or 0 index)");

                    string[] parts = text.Split(new[] { delim }, StringSplitOptions.None);
                    int idx0 = index > 0 ? index - 1 : parts.Length + index;
                    if (idx0 < 0 || idx0 >= parts.Length)
                        throw new FormulaException($"#VALUE! (SPLIT index {index} out of bounds)");
                    return parts[idx0];
                }
                case "PADLEFT":
                {
                    RequireArgs(name, args, 2, 3);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    int width = ToInt(args[1].Evaluate(in ctx));
                    if (width < 0) throw new FormulaException("#VALUE! (PADLEFT width < 0)");
                    string padStr = args.Count >= 3 ? ToStr(args[2].Evaluate(in ctx)) : " ";
                    char padChar = string.IsNullOrEmpty(padStr) ? ' ' : padStr[0];
                    return text.PadLeft(width, padChar);
                }
                case "PADRIGHT":
                {
                    RequireArgs(name, args, 2, 3);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    int width = ToInt(args[1].Evaluate(in ctx));
                    if (width < 0) throw new FormulaException("#VALUE! (PADRIGHT width < 0)");
                    string padStr = args.Count >= 3 ? ToStr(args[2].Evaluate(in ctx)) : " ";
                    char padChar = string.IsNullOrEmpty(padStr) ? ' ' : padStr[0];
                    return text.PadRight(width, padChar);
                }
                case "REGEXEXTRACT":
                {
                    RequireArgs(name, args, 2, 3);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string pattern = ToStr(args[1].Evaluate(in ctx));
                    int group = args.Count >= 3 ? ToInt(args[2].Evaluate(in ctx)) : 0;
                    if (group < 0) throw new FormulaException("#VALUE! (REGEXEXTRACT group < 0)");
                    var rx = GetCachedRegex(pattern);
                    var m = rx.Match(text);
                    if (!m.Success || group >= m.Groups.Count || !m.Groups[group].Success)
                        return string.Empty;
                    return m.Groups[group].Value;
                }
                case "REGEXREPLACE":
                {
                    RequireArgs(name, args, 3, 3);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string pattern = ToStr(args[1].Evaluate(in ctx));
                    string replacement = ToStr(args[2].Evaluate(in ctx));
                    return GetCachedRegex(pattern).Replace(text, replacement);
                }
                case "REGEXMATCH":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string pattern = ToStr(args[1].Evaluate(in ctx));
                    return GetCachedRegex(pattern).IsMatch(text);
                }
                case "CONTAINS":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string sub = ToStr(args[1].Evaluate(in ctx));
                    return text.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                case "STARTSWITH":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string prefix = ToStr(args[1].Evaluate(in ctx));
                    return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }
                case "ENDSWITH":
                {
                    RequireArgs(name, args, 2, 2);
                    string text = ToStr(args[0].Evaluate(in ctx));
                    string suffix = ToStr(args[1].Evaluate(in ctx));
                    return text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
                }

                // Numeric / Formatting functions
                case "VALUE":
                {
                    RequireArgs(name, args, 1, 1);
                    return ToNumber(args[0].Evaluate(in ctx));
                }
                case "TEXT":
                {
                    RequireArgs(name, args, 2, 2);
                    object raw = args[0].Evaluate(in ctx);
                    string fmt = ToStr(args[1].Evaluate(in ctx));
                    if (TryParseNumber(raw, out double num))
                        return num.ToString(fmt, CultureInfo.InvariantCulture);
                    if (DateTime.TryParse(ToStr(raw), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                        return dt.ToString(fmt, CultureInfo.InvariantCulture);
                    return ToStr(raw);
                }
                case "ROUND":
                {
                    RequireArgs(name, args, 2, 2);
                    double val = ToNumber(args[0].Evaluate(in ctx));
                    int digits = ToInt(args[1].Evaluate(in ctx));
                    if (digits >= 0) return Math.Round(val, digits, MidpointRounding.AwayFromZero);
                    double factor = Math.Pow(10, -digits);
                    return Math.Round(val / factor, 0, MidpointRounding.AwayFromZero) * factor;
                }
                case "ABS":
                {
                    RequireArgs(name, args, 1, 1);
                    return Math.Abs(ToNumber(args[0].Evaluate(in ctx)));
                }
                case "INT":
                {
                    RequireArgs(name, args, 1, 1);
                    return Math.Floor(ToNumber(args[0].Evaluate(in ctx)));
                }
                case "MIN":
                {
                    RequireArgs(name, args, 1, 64);
                    double m = double.PositiveInfinity;
                    foreach (var a in args) m = Math.Min(m, ToNumber(a.Evaluate(in ctx)));
                    return m;
                }
                case "MAX":
                {
                    RequireArgs(name, args, 1, 64);
                    double m = double.NegativeInfinity;
                    foreach (var a in args) m = Math.Max(m, ToNumber(a.Evaluate(in ctx)));
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
