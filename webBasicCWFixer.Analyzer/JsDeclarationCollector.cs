using System.Text.RegularExpressions;

namespace webBasicCWFixer.Analyzer;

internal static class JsDeclarationCollector
{
    public static HashSet<string> Collect(string code)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var scanner = new JsScanner(code);

        while (scanner.MoveNext())
        {
            if (scanner.Kind != JsTokenKind.Identifier) continue;

            var t = scanner.Text;

            if (t is "var" or "let" or "const")
            {
                while (scanner.MoveNextNonTrivia())
                {
                    if (scanner.Kind == JsTokenKind.Identifier)
                    {
                        declared.Add(scanner.Text);

                        while (scanner.MoveNext())
                        {
                            if (scanner.Kind == JsTokenKind.Punct && scanner.Text == ",")
                            {
                                break;
                            }
                            if (scanner.Kind == JsTokenKind.Punct && (scanner.Text == ";" || scanner.Text == ")"))
                            {
                                goto endVar;
                            }
                        }
                        continue;
                    }

                    if (scanner.Kind == JsTokenKind.Punct && scanner.Text == ";")
                    {
                        break;
                    }
                }
            endVar:
                continue;
            }

            if (t == "function")
            {
                var snap = scanner.Snapshot();
                if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
                {
                    declared.Add(scanner.Text);
                }
                else
                {
                    scanner.Restore(snap);
                }

                AddFunctionParams(scanner, declared);
                continue;
            }

            if (t == "class")
            {
                if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
                {
                    declared.Add(scanner.Text);
                }
                continue;
            }

            if (t == "catch")
            {
                var snap = scanner.Snapshot();
                if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Punct && scanner.Text == "(")
                {
                    if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
                    {
                        declared.Add(scanner.Text);
                    }
                }
                else
                {
                    scanner.Restore(snap);
                }
                continue;
            }
        }
        foreach (var name in DeclaredByRegex(code))
        {
            declared.Add(name);
        }

        return declared;
    }

    private static IEnumerable<string> DeclaredByRegex(string code)
    {
        var rx = new Regex(@"\b(?:var|let|const)\s+([A-Za-z_$][\w$]*)", RegexOptions.Compiled);
        foreach (Match m in rx.Matches(code))
        {
            var id = m.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    private static void AddFunctionParams(JsScanner scanner, HashSet<string> declared)
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
                    declared.Add(id);
                }
            }
        }
    }
}
