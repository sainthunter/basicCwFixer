namespace webBasicCWFixer.Analyzer;

internal static class JsRules
{
    private static readonly HashSet<string> JsKeywords = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "return", "try", "catch", "finally",
        "throw",
        "var", "let", "const", "function", "class", "new", "this", "super", "typeof", "instanceof", "in", "of", "delete",
        "void",
        "true", "false", "null", "undefined", "await", "async", "yield", "import", "export", "default", "get", "set"
    };

    private static bool IsRegexFlag(string source, int identifierStartIndex, string id)
    {
        if (id.Length != 1) return false;

        const string flags = "gimsuyd";
        if (!flags.Contains(id[0])) return false;

        for (int i = identifierStartIndex - 1; i >= 0; i--)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c)) continue;
            return c == '/';
        }

        return false;
    }

    private static bool IsDeclarationIdentifier(string source, int identifierStartIndex)
    {
        int i = identifierStartIndex - 1;

        while (i >= 0 && char.IsWhiteSpace(source[i])) i--;

        int end = i;
        while (i >= 0 && char.IsLetter(source[i])) i--;

        var word = source.Substring(i + 1, end - i);

        return word == "var" || word == "let" || word == "const";
    }

    private static bool IsPrecededByDot(string source, int identifierStartIndex)
    {
        for (int i = identifierStartIndex - 1; i >= 0; i--)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c)) continue;
            return c == '.';
        }
        return false;
    }

    public static IEnumerable<LintIssue> FindSingleEqualsInConditions(XmlCwReader.ScriptBlock script)
    {
        var scanner = new JsScanner(script.ScriptBody);

        while (scanner.MoveNext())
        {
            if (scanner.Kind != JsTokenKind.Identifier) continue;

            var kw = scanner.Text;
            if (kw != "if" && kw != "while" && kw != "for") continue;

            var snap = scanner.Snapshot();
            if (!scanner.MoveNextNonTrivia() || scanner.Kind != JsTokenKind.Punct || scanner.Text != "(")
            {
                scanner.Restore(snap);
                continue;
            }

            if (kw == "for")
            {
                int depth = 1;
                bool inTest = false;
                int semicolonsAtTopLevel = 0;

                while (scanner.MoveNext())
                {
                    if (scanner.Kind == JsTokenKind.Punct)
                    {
                        if (scanner.Text == "(") depth++;
                        else if (scanner.Text == ")")
                        {
                            depth--;
                            if (depth == 0) break;
                        }
                        else if (depth == 1 && scanner.Text == ";")
                        {
                            semicolonsAtTopLevel++;
                            inTest = semicolonsAtTopLevel == 1;
                            if (semicolonsAtTopLevel == 2)
                            {
                                inTest = false;
                            }
                        }
                        else if (inTest && scanner.Text == "=")
                        {
                            if (IsBadSingleEquals(script.ScriptBody, scanner.StartIndex))
                            {
                                yield return new LintIssue(
                                    script.FullName,
                                    "single-equals-in-condition",
                                    "Koşul içinde tek '=' (atama) tespit edildi. Muhtemelen '==' veya '===' olmalı.",
                                    scanner.Line,
                                    scanner.Column
                                );
                            }
                        }
                    }
                }

                continue;
            }

            {
                int depth = 1;
                while (scanner.MoveNext())
                {
                    if (scanner.Kind != JsTokenKind.Punct) continue;

                    if (scanner.Text == "(") depth++;
                    else if (scanner.Text == ")")
                    {
                        depth--;
                        if (depth == 0) break;
                    }
                    else if (scanner.Text == "=")
                    {
                        if (IsBadSingleEquals(script.ScriptBody, scanner.StartIndex))
                        {
                            yield return new LintIssue(
                                script.FullName,
                                "single-equals-in-condition",
                                "Koşul içinde tek '=' (atama) tespit edildi. Muhtemelen '==' veya '===' olmalı.",
                                scanner.Line,
                                scanner.Column
                            );
                        }
                    }
                }
            }
        }
    }

    public static IEnumerable<LintIssue> FindNoUndefRoots(XmlCwReader.ScriptBlock script, HashSet<string> declared)
    {
        var scanner = new JsScanner(script.ScriptBody);

        while (scanner.MoveNext())
        {
            if (scanner.Kind != JsTokenKind.Identifier) continue;

            var id = scanner.Text;

            if (IsRegexFlag(script.ScriptBody, scanner.StartIndex, id))
            {
                continue;
            }

            if (IsDeclarationIdentifier(script.ScriptBody, scanner.StartIndex))
            {
                continue;
            }

            if (IsPrecededByDot(script.ScriptBody, scanner.StartIndex))
            {
                continue;
            }

            if (id == "this")
            {
                continue;
            }

            if (JsKeywords.Contains(id)) continue;

            if (scanner.NextNonTriviaIsColon()) continue;

            if (declared.Contains(id)) continue;

            yield return new LintIssue(
                script.FullName,
                "no-undef-root",
                $"Tanımsız root tespit edildi: '{id}'. (namespace/builtin/param/local declare listesinde yok.)",
                scanner.Line,
                scanner.Column
            );
        }
    }

    private static bool IsBadSingleEquals(string source, int eqIndex)
    {
        char prev = '\0';
        char next = '\0';

        for (int i = eqIndex - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(source[i]))
            {
                prev = source[i];
                break;
            }
        }

        for (int i = eqIndex + 1; i < source.Length; i++)
        {
            if (!char.IsWhiteSpace(source[i]))
            {
                next = source[i];
                break;
            }
        }

        if (prev == '=') return false;
        if (next == '=') return false;
        if (prev == '!') return false;
        if (prev == '<' || prev == '>') return false;
        if ("+-*/%&|^?".Contains(prev)) return false;
        if (next == '>') return false;

        return true;
    }
}
