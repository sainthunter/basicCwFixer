using System.Text.RegularExpressions;

namespace webBasicCWFixer.Analyzer;

internal static class JsWarningRules
{
    private static readonly HashSet<string> JsKeywords = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "return", "try", "catch", "finally",
        "throw",
        "var", "let", "const", "function", "class", "new", "this", "super", "typeof", "instanceof", "in", "of", "delete",
        "void",
        "true", "false", "null", "undefined", "await", "async", "yield", "import", "export", "default", "get", "set"
    };

    public static IEnumerable<LintIssue> FindWarnings(XmlCwReader.ScriptBlock script)
    {
        var declarations = CollectDeclarations(script);
        var used = CollectUsages(script.ScriptBody);

        var declaredOnce = new Dictionary<string, JsDeclaration>(StringComparer.Ordinal);
        foreach (var decl in declarations)
        {
            if (declaredOnce.TryGetValue(decl.Name, out var previous))
            {
                yield return new LintIssue(
                    script.FullName,
                    "duplicate-declaration",
                    $"'{decl.Name}' için tekrar eden deklarasyon. İlk tanım: L{previous.Line}:{previous.Column}",
                    decl.Line,
                    decl.Column
                );
                continue;
            }

            declaredOnce.Add(decl.Name, decl);
        }

        foreach (var decl in declarations)
        {
            if (used.Contains(decl.Name)) continue;

            var (rule, message) = decl.Kind switch
            {
                JsDeclarationKind.Parameter => ("unused-parameter", $"Kullanılmayan parametre: '{decl.Name}'"),
                JsDeclarationKind.Function => ("unused-function", $"Kullanılmayan fonksiyon: '{decl.Name}'"),
                _ => ("unused-variable", $"Kullanılmayan değişken: '{decl.Name}'")
            };

            yield return new LintIssue(
                script.FullName,
                rule,
                message,
                decl.Line,
                decl.Column
            );
        }
    }

    private static List<JsDeclaration> CollectDeclarations(XmlCwReader.ScriptBlock script)
    {
        var declarations = new List<JsDeclaration>();

        foreach (var p in script.Parameters)
        {
            declarations.Add(new JsDeclaration(p, JsDeclarationKind.Parameter, 1, 1));
        }

        var scanner = new JsScanner(script.ScriptBody);

        while (scanner.MoveNext())
        {
            if (scanner.Kind != JsTokenKind.Identifier) continue;

            var t = scanner.Text;

            if (t is "var" or "let" or "const")
            {
                CaptureVarList(scanner, declarations);
                continue;
            }

            if (t == "function")
            {
                var snap = scanner.Snapshot();
                if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
                {
                    declarations.Add(new JsDeclaration(scanner.Text, JsDeclarationKind.Function, scanner.Line, scanner.Column));
                }
                else
                {
                    scanner.Restore(snap);
                }

                CaptureFunctionParams(scanner, declarations);
                continue;
            }

            if (t == "catch")
            {
                CaptureCatchParam(scanner, declarations);
            }
        }

        return declarations;
    }

    private static HashSet<string> CollectUsages(string code)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var scanner = new JsScanner(code);
        bool awaitingFunctionParams = false;
        bool inFunctionParams = false;
        int functionParamDepth = 0;
        bool awaitingCatchParams = false;
        bool inCatchParams = false;
        int catchParamDepth = 0;

        while (scanner.MoveNext())
        {
            if (scanner.Kind == JsTokenKind.Punct)
            {
                if (awaitingFunctionParams && scanner.Text == "(")
                {
                    inFunctionParams = true;
                    functionParamDepth = 1;
                    awaitingFunctionParams = false;
                    continue;
                }

                if (inFunctionParams)
                {
                    if (scanner.Text == "(") functionParamDepth++;
                    else if (scanner.Text == ")")
                    {
                        functionParamDepth--;
                        if (functionParamDepth == 0) inFunctionParams = false;
                    }
                    continue;
                }

                if (awaitingCatchParams && scanner.Text == "(")
                {
                    inCatchParams = true;
                    catchParamDepth = 1;
                    awaitingCatchParams = false;
                    continue;
                }

                if (inCatchParams)
                {
                    if (scanner.Text == "(") catchParamDepth++;
                    else if (scanner.Text == ")")
                    {
                        catchParamDepth--;
                        if (catchParamDepth == 0) inCatchParams = false;
                    }
                    continue;
                }
            }

            if (scanner.Kind != JsTokenKind.Identifier) continue;

            var id = scanner.Text;

            if (id == "function")
            {
                awaitingFunctionParams = true;
                continue;
            }

            if (id == "catch")
            {
                awaitingCatchParams = true;
                continue;
            }

            if (awaitingFunctionParams)
            {
                continue;
            }

            if (inFunctionParams || inCatchParams)
            {
                continue;
            }

            if (IsRegexFlag(code, scanner.StartIndex, id))
            {
                continue;
            }

            if (IsDeclarationIdentifier(code, scanner.StartIndex))
            {
                continue;
            }

            if (scanner.PrevNonTriviaIsDot())
            {
                continue;
            }

            if (id == "this")
            {
                continue;
            }

            if (JsKeywords.Contains(id)) continue;

            if (scanner.NextNonTriviaIsColon()) continue;

            used.Add(id);
        }

        return used;
    }

    private static void CaptureVarList(JsScanner scanner, List<JsDeclaration> declarations)
    {
        while (scanner.MoveNextNonTrivia())
        {
            if (scanner.Kind == JsTokenKind.Identifier)
            {
                declarations.Add(new JsDeclaration(scanner.Text, JsDeclarationKind.Variable, scanner.Line, scanner.Column));

                while (scanner.MoveNext())
                {
                    if (scanner.Kind == JsTokenKind.Punct && scanner.Text == ",")
                    {
                        break;
                    }

                    if (scanner.Kind == JsTokenKind.Punct && (scanner.Text == ";" || scanner.Text == ")"))
                    {
                        return;
                    }
                }
                continue;
            }

            if (scanner.Kind == JsTokenKind.Punct && scanner.Text == ";")
            {
                return;
            }
        }
    }

    private static void CaptureFunctionParams(JsScanner scanner, List<JsDeclaration> declarations)
    {
        var snap = scanner.Snapshot();
        if (!scanner.MoveNextNonTrivia() || scanner.Kind != JsTokenKind.Punct || scanner.Text != "(")
        {
            scanner.Restore(snap);
            return;
        }

        int depth = 1;
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
            }

            if (scanner.Kind == JsTokenKind.Identifier)
            {
                var id = scanner.Text;
                if (Regex.IsMatch(id, @"^[A-Za-z_$][\w$]*$"))
                {
                    declarations.Add(new JsDeclaration(id, JsDeclarationKind.Parameter, scanner.Line, scanner.Column));
                }
            }
        }
    }

    private static void CaptureCatchParam(JsScanner scanner, List<JsDeclaration> declarations)
    {
        var snap = scanner.Snapshot();
        if (!scanner.MoveNextNonTrivia() || scanner.Kind != JsTokenKind.Punct || scanner.Text != "(")
        {
            scanner.Restore(snap);
            return;
        }

        if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
        {
            declarations.Add(new JsDeclaration(scanner.Text, JsDeclarationKind.Variable, scanner.Line, scanner.Column));
        }
    }

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
}

internal enum JsDeclarationKind
{
    Variable,
    Parameter,
    Function
}

internal sealed record JsDeclaration(string Name, JsDeclarationKind Kind, int Line, int Column);
