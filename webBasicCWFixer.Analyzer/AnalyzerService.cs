using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using static webBasicCWFixer.Analyzer.XmlCwReader;

namespace webBasicCWFixer.Analyzer;

public sealed record AllowlistConfig(
    HashSet<string> Roots,
    HashSet<string> RegexFlags,
    HashSet<string> SkipIdentifiers,
    int MaxUploadMb = 90
);

public sealed record IssueDto(
    string Rule,
    string FullName,
    int Line,
    int Column,
    string Message,
    string Snippet
);

public sealed record AnalyzeResult(
    int ScriptCount,
    int IssueCount,
    List<IssueDto> Issues
);

public sealed class AnalyzerService
{
    public AnalyzeResult AnalyzeFile(string xmlPath, AllowlistConfig allowlist, string logPath, Action<int>? onProgress = null)
    {
        // 1) XML’den namespace root’larını çek
        var knownNamespaceRoots = XmlCwReader.ReadAllNamespaceNames(xmlPath);

        // 2) Builtin root’lar = allowlist roots + JS globals
        var builtinRoots = new HashSet<string>(allowlist.Roots, StringComparer.Ordinal);
        foreach (var js in JsGlobals) builtinRoots.Add(js);

        var analyzer = new CwAnalyzer(knownNamespaceRoots, builtinRoots);

        using var fs = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(fs, new UTF8Encoding(false));

        int scriptCount = 0;
        int issueCount = 0;
        var issues = new List<IssueDto>();

        foreach (var script in XmlCwReader.ReadScripts(xmlPath))
        {
            scriptCount++;

            foreach (var issue in analyzer.Analyze(script))
            {
                issueCount++;
                var snippet = IssueFormatter.GetLineSnippet(script, issue.Line);

                writer.WriteLine($"[{issue.Rule}] {issue.FullName} L{issue.Line}:{issue.Column} - {issue.Message} | {snippet}");

                issues.Add(new IssueDto(
                    issue.Rule,
                    issue.FullName,
                    issue.Line,
                    issue.Column,
                    issue.Message,
                    snippet
                ));
            }

            // progress: kaba ama işe yarar (her script sonunda güncelle)
            if (scriptCount % 10 == 0)
                onProgress?.Invoke(scriptCount);
        }

        writer.WriteLine();
        writer.WriteLine($"Toplam Script: {scriptCount}");
        writer.WriteLine($"Toplam Issue : {issueCount}");

        return new AnalyzeResult(scriptCount, issueCount, issues);
    }

    // Burayı senin mevcut dosyandaki JsGlobals listesinden aynen taşıyacağız.
    public static readonly string[] JsGlobals =
    {
        "Math","Date","String","Number","Boolean","Object","Array","JSON","RegExp","Error","isNaN","parseInt","parseFloat",
        "decodeURI","decodeURIComponent","encodeURI","encodeURIComponent","Infinity","NaN","undefined"
    };
}
internal static class XmlCwReader
{
    public static HashSet<string> ReadAllNamespaceNames(string xmlPath)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        using var reader = XmlReader.Create(xmlPath, new XmlReaderSettings
        {
            IgnoreComments = false,
            IgnoreWhitespace = false,
            DtdProcessing = DtdProcessing.Ignore
        });

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element &&
                reader.Name.Equals("namespace", StringComparison.OrdinalIgnoreCase))
            {
                var ns = reader.GetAttribute("name");
                if (!string.IsNullOrWhiteSpace(ns))
                    set.Add(ns.Trim());
            }
        }

        return set;
    }

    public static IEnumerable<ScriptBlock> ReadScripts(string xmlPath)
    {
        using var reader = XmlReader.Create(xmlPath, new XmlReaderSettings
        {
            IgnoreComments = false,
            IgnoreWhitespace = false,
            DtdProcessing = DtdProcessing.Ignore
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (!reader.Name.Equals("Script", StringComparison.OrdinalIgnoreCase)) continue;

            string? scriptName = null;
            string? nsName = null;
            var parameters = new List<string>();
            string? body = null;

            using var sub = reader.ReadSubtree();
            sub.Read(); // Script root

            while (sub.Read())
            {
                if (sub.NodeType != XmlNodeType.Element) continue;

                if (sub.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    scriptName = (sub.ReadElementContentAsString() ?? "").Trim();
                }
                else if (sub.Name.Equals("namespace", StringComparison.OrdinalIgnoreCase))
                {
                    var n = sub.GetAttribute("name");
                    if (!string.IsNullOrWhiteSpace(n)) nsName = n.Trim();
                    _ = sub.ReadInnerXml(); // consume
                }
                else if (sub.Name.Equals("parameter", StringComparison.OrdinalIgnoreCase))
                {
                    using var psub = sub.ReadSubtree();
                    psub.Read(); // parameter
                    while (psub.Read())
                    {
                        if (psub.NodeType == XmlNodeType.Element &&
                            psub.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                        {
                            var p = (psub.ReadElementContentAsString() ?? "").Trim();
                            if (IsValidIdentifier(p))
                                parameters.Add(p);
                        }
                    }
                }
                else if (sub.Name.Equals("script", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = sub.ReadInnerXml() ?? "";
                    // HTML entity decode: &#033;= => !=, &#34; => ", &lt; => <, vb.
                    body = WebUtility.HtmlDecode(raw);
                }
            }

            if (string.IsNullOrWhiteSpace(body)) continue;

            scriptName ??= "(unnamedScript)";
            nsName ??= "(noNamespace)";
            var fullName = $"{nsName}.{scriptName}";

            yield return new ScriptBlock(
                nsName,
                scriptName,
                fullName,
                parameters,
                NormalizeNewlines(body)
            );
        }
    }

    internal sealed record ScriptBlock(
        string NamespaceName,
        string ScriptName,
        string FullName,
        List<string> Parameters,
        string ScriptBody
    );



    private static bool IsValidIdentifier(string s)
        => Regex.IsMatch(s ?? "", @"^[A-Za-z_$][\w$]*$");

    private static string NormalizeNewlines(string s)
        => (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
}

internal static class IssueFormatter
{
    public static string GetLineSnippet(ScriptBlock script, int line1Based, int maxLen = 180)
    {
        if (line1Based <= 0) return "";
        var lines = script.ScriptBody.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (line1Based > lines.Length) return "";
        var s = lines[line1Based - 1].Trim();
        if (s.Length > maxLen) s = s[..maxLen] + "…";
        return s;
    }
}
internal sealed class CwAnalyzer
{
    private readonly HashSet<string> _knownNamespaceRoots;
    private readonly HashSet<string> _builtinRoots;

    public CwAnalyzer(HashSet<string> knownNamespaceRoots, HashSet<string> builtinRoots)
    {
        _knownNamespaceRoots = knownNamespaceRoots;
        _builtinRoots = builtinRoots;
    }

    public IEnumerable<LintIssue> Analyze(ScriptBlock script)
    {
        // Declared set: params + builtins + namespaces + local declarations
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in script.Parameters) declared.Add(p);
        foreach (var b in _builtinRoots) declared.Add(b);
        foreach (var ns in _knownNamespaceRoots) declared.Add(ns);

        // local var/let/const + function/class + catch param
        foreach (var d in JsDeclarationCollector.Collect(script.ScriptBody))
            declared.Add(d);

        // rules
        foreach (var i in JsRules.FindSingleEqualsInConditions(script))
            yield return i;

        foreach (var i in JsRules.FindNoUndefRoots(script, declared))
            yield return i;
    }
}

internal static class JsRules
{
    private static bool IsRegexFlag(string source, int identifierStartIndex, string id)
    {
        // genelde g/i/m/s/u/y/d tek harf veya bazen "gi" gibi birleşik olur,
        // fakat bizim scanner bunu tek tek identifier olarak yakalıyor (g gibi).
        if (id.Length != 1) return false;

        // desteklenen flag'ler
        const string flags = "gimsuyd";
        if (!flags.Contains(id[0])) return false;

        // identifier'dan önceki anlamlı karakter '/' ise regex literal kapanışı olabilir: /.../g
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
        // identifier'dan geriye doğru bak:
        // var | let | const <identifier>
        int i = identifierStartIndex - 1;

        // boşlukları atla
        while (i >= 0 && char.IsWhiteSpace(source[i])) i--;

        // identifier'dan önce "var / let / const" var mı?
        // geriye doğru kelime topla
        int end = i;
        while (i >= 0 && char.IsLetter(source[i])) i--;

        var word = source.Substring(i + 1, end - i);

        return word == "var" || word == "let" || word == "const";
    }

    private static bool IsPrecededByDot(string source, int identifierStartIndex)
    {
        // identifierStartIndex: identifier'ın başladığı index
        // geriye doğru boşlukları atla, '.' varsa property'dir
        for (int i = identifierStartIndex - 1; i >= 0; i--)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c)) continue;
            return c == '.';
        }
        return false;
    }

    public static IEnumerable<LintIssue> FindSingleEqualsInConditions(ScriptBlock script)
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
                // for ( init ; test ; update )
                // init bölümünü atla: ilk ';'e kadar
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
                            // 1. ';' sonrası test bölümüne girdik, 2. ';' ile test biter
                            inTest = (semicolonsAtTopLevel == 1);
                            if (semicolonsAtTopLevel == 2)
                                inTest = false;
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

            // if/while: tüm (...) condition kabul et
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


    public static IEnumerable<LintIssue> FindNoUndefRoots(ScriptBlock script, HashSet<string> declared)
    {
        var scanner = new JsScanner(script.ScriptBody);

        while (scanner.MoveNext())
        {
            if (scanner.Kind != JsTokenKind.Identifier) continue;

            var id = scanner.Text;

            if (IsRegexFlag(script.ScriptBody, scanner.StartIndex, id))
                continue;

            if (IsDeclarationIdentifier(script.ScriptBody, scanner.StartIndex))
                continue;

            // this.* ve obj.* gibi property isimlerini kontrol etmeyelim
            if (IsPrecededByDot(script.ScriptBody, scanner.StartIndex))
                continue;

            if (id == "this")
                continue;

            if (JsKeywords.Contains(id)) continue;

            //// obj.prop -> prop'u ignore etmek için: önceki non-trivia '.' ise bu property'dir
            //if (scanner.PrevNonTriviaIsDot()) continue;

            // object literal key: { foo: 1 } -> foo'yu ignore
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

        // önceki anlamlı karakter
        for (int i = eqIndex - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(source[i]))
            {
                prev = source[i];
                break;
            }
        }

        // sonraki anlamlı karakter
        for (int i = eqIndex + 1; i < source.Length; i++)
        {
            if (!char.IsWhiteSpace(source[i]))
            {
                next = source[i];
                break;
            }
        }

        // == veya === operatörünün 2./3. '=' karakteri
        if (prev == '=') return false;

        // == veya === (ilk '=' için)
        if (next == '=') return false;

        // != veya !== (ilk '=' için)
        if (prev == '!') return false;

        // <= >=
        if (prev == '<' || prev == '>') return false;

        // += -= *= /= %= &= |= ^= ??=
        if ("+-*/%&|^?".Contains(prev)) return false;

        // arrow function =>
        if (next == '>') return false;

        // GERÇEK atama '='
        return true;
    }




    private static readonly HashSet<string> JsKeywords = new(StringComparer.Ordinal)
    {
        "if","else","for","while","do","switch","case","break","continue","return","try","catch","finally","throw",
        "var","let","const","function","class","new","this","super","typeof","instanceof","in","of","delete","void",
        "true","false","null","undefined","await","async","yield","import","export","default","get","set"
    };
}

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

            // var/let/const a, b, c
            if (t is "var" or "let" or "const")
            {
                while (scanner.MoveNextNonTrivia())
                {
                    if (scanner.Kind == JsTokenKind.Identifier)
                    {
                        declared.Add(scanner.Text);

                        // var a = 1, b = 2;
                        // ileri: ',' görünce sonraki identifier'ı al
                        while (scanner.MoveNext())
                        {
                            if (scanner.Kind == JsTokenKind.Punct && scanner.Text == ",")
                                break;
                            if (scanner.Kind == JsTokenKind.Punct && (scanner.Text == ";" || scanner.Text == ")"))
                                goto endVar;
                        }
                        continue;
                    }

                    if (scanner.Kind == JsTokenKind.Punct && scanner.Text == ";")
                        break;
                }
            endVar:
                continue;
            }

            // function foo(...) { ... }
            if (t == "function")
            {
                var snap = scanner.Snapshot();
                if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
                    declared.Add(scanner.Text);
                else
                    scanner.Restore(snap);

                AddFunctionParams(scanner, declared);
                continue;
            }

            // class Foo
            if (t == "class")
            {
                if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
                    declared.Add(scanner.Text);
                continue;
            }

            // catch (ex)
            if (t == "catch")
            {
                var snap = scanner.Snapshot();
                if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Punct && scanner.Text == "(")
                {
                    if (scanner.MoveNextNonTrivia() && scanner.Kind == JsTokenKind.Identifier)
                        declared.Add(scanner.Text);
                }
                else scanner.Restore(snap);
                continue;
            }
        }
        foreach (var name in DeclaredByRegex(code))
            declared.Add(name);

        return declared;
    }
    private static IEnumerable<string> DeclaredByRegex(string code)
    {
        // var a = ..., let b=..., const c=...
        // birden fazla tanım: var a=1, b=2;  -> a ve b’yi de yakalamaya çalışırız
        // Not: Bu basit bir fallback; false-positive riskini düşük tutacak şekilde yazıldı.
        var rx = new Regex(@"\b(?:var|let|const)\s+([A-Za-z_$][\w$]*)", RegexOptions.Compiled);
        foreach (Match m in rx.Matches(code))
        {
            var id = m.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(id))
                yield return id;
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
                    declared.Add(id);
            }
        }
    }
}

internal sealed class JsScanner
{
    private readonly string _s;
    private int _i;

    // current token
    public JsTokenKind Kind { get; private set; }
    public string Text { get; private set; } = "";
    public int StartIndex { get; private set; }
    public int Line { get; private set; } = 1;
    public int Column { get; private set; } = 1;

    private int _line = 1;
    private int _col = 1;

    private JsTokenKind _prevNonTriviaKind = JsTokenKind.Unknown;
    private string _prevNonTriviaText = "";

    public JsScanner(string s)
    {
        _s = s ?? "";
        _i = 0;
    }

    public bool MoveNext() => ReadNextToken(includeTrivia: true);

    public bool MoveNextNonTrivia()
    {
        while (ReadNextToken(includeTrivia: true))
        {
            if (Kind != JsTokenKind.Trivia) return true;
        }
        return false;
    }

    public bool PrevNonTriviaIsDot()
        => _prevNonTriviaKind == JsTokenKind.Punct && _prevNonTriviaText == ".";

    public bool NextNonTriviaIsColon()
    {
        var snap = Snapshot();
        try
        {
            if (!MoveNextNonTrivia()) return false;
            return Kind == JsTokenKind.Punct && Text == ":";
        }
        finally
        {
            Restore(snap);
        }
    }

    public ScannerSnapshot Snapshot() => new(_i, _line, _col, _prevNonTriviaKind, _prevNonTriviaText);

    public void Restore(ScannerSnapshot s)
    {
        _i = s.I;
        _line = s.Line;
        _col = s.Col;
        _prevNonTriviaKind = s.PrevKind;
        _prevNonTriviaText = s.PrevText;
        Kind = JsTokenKind.Unknown;
        Text = "";
        StartIndex = 0;
        Line = _line;
        Column = _col;
    }

    private bool ReadNextToken(bool includeTrivia)
    {
        if (_i >= _s.Length) return false;

        StartIndex = _i;
        Line = _line;
        Column = _col;

        char c = _s[_i];

        // whitespace
        if (char.IsWhiteSpace(c))
        {
            var start = _i;
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
            {
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Trivia;
            Text = _s.Substring(start, _i - start);
            return includeTrivia;
        }

        // line comment //
        if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
        {
            var start = _i;
            _i += 2; _col += 2;
            while (_i < _s.Length && _s[_i] != '\n')
            {
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Trivia;
            Text = _s.Substring(start, _i - start);
            return includeTrivia;
        }

        // block comment /* */
        if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
        {
            var start = _i;
            _i += 2; _col += 2;
            while (_i < _s.Length)
            {
                if (_s[_i] == '*' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                {
                    _i += 2; _col += 2;
                    break;
                }
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Trivia;
            Text = _s.Substring(start, _i - start);
            return includeTrivia;
        }

        // strings ' " `
        if (c is '\'' or '"' or '`')
        {
            var quote = c;
            var start = _i;
            Advance(c); _i++;
            while (_i < _s.Length)
            {
                var ch = _s[_i];
                if (ch == '\\')
                {
                    Advance(ch); _i++;
                    if (_i < _s.Length) { Advance(_s[_i]); _i++; }
                    continue;
                }
                if (ch == quote)
                {
                    Advance(ch); _i++;
                    break;
                }
                Advance(ch); _i++;
            }
            Kind = JsTokenKind.String;
            Text = _s.Substring(start, _i - start);
            UpdatePrevNonTrivia();
            return true;
        }

        // identifier
        if (IsIdentStart(c))
        {
            var start = _i;
            Advance(c); _i++;
            while (_i < _s.Length && IsIdentPart(_s[_i]))
            {
                Advance(_s[_i]); _i++;
            }
            Kind = JsTokenKind.Identifier;
            Text = _s.Substring(start, _i - start);
            UpdatePrevNonTrivia();
            return true;
        }

        // number
        if (char.IsDigit(c))
        {
            var start = _i;
            Advance(c); _i++;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
            {
                Advance(_s[_i]); _i++;
            }
            Kind = JsTokenKind.Number;
            Text = _s.Substring(start, _i - start);
            UpdatePrevNonTrivia();
            return true;
        }

        // punctuation (single-char is enough for our rules)
        Kind = JsTokenKind.Punct;
        Text = c.ToString();
        Advance(c); _i++;
        UpdatePrevNonTrivia();
        return true;
    }

    private void UpdatePrevNonTrivia()
    {
        if (Kind == JsTokenKind.Trivia) return;
        _prevNonTriviaKind = Kind;
        _prevNonTriviaText = Text;
    }

    private void Advance(char c)
    {
        if (c == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
    private static bool IsIdentPart(char c) => IsIdentStart(c) || char.IsDigit(c);
}
internal sealed record LintIssue(
    string FullName,
    string Rule,
    string Message,
    int Line,
    int Column
);


internal enum JsTokenKind { Trivia, Identifier, Number, String, Punct, Unknown }
internal readonly record struct ScannerSnapshot(int I, int Line, int Col, JsTokenKind PrevKind, string PrevText);