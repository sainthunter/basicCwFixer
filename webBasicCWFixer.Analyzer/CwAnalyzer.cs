namespace webBasicCWFixer.Analyzer;

internal sealed class CwAnalyzer
{
    private readonly HashSet<string> _knownNamespaceRoots;
    private readonly HashSet<string> _builtinRoots;

    public CwAnalyzer(HashSet<string> knownNamespaceRoots, HashSet<string> builtinRoots)
    {
        _knownNamespaceRoots = knownNamespaceRoots;
        _builtinRoots = builtinRoots;
    }

    public IEnumerable<LintIssue> Analyze(XmlCwReader.ScriptBlock script)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in script.Parameters)
        {
            declared.Add(p);
        }

        foreach (var b in _builtinRoots)
        {
            declared.Add(b);
        }

        foreach (var ns in _knownNamespaceRoots)
        {
            declared.Add(ns);
        }

        foreach (var d in JsDeclarationCollector.Collect(script.ScriptBody))
        {
            declared.Add(d);
        }

        foreach (var i in JsRules.FindSingleEqualsInConditions(script))
        {
            yield return i;
        }

        foreach (var i in JsRules.FindNoUndefRoots(script, declared))
        {
            yield return i;
        }
    }
}
