namespace webBasicCWFixer.Analyzer;

internal sealed class CwWarningAnalyzer
{
    public IEnumerable<LintIssue> Analyze(XmlCwReader.ScriptBlock script)
    {
        foreach (var issue in JsWarningRules.FindWarnings(script))
        {
            yield return issue;
        }
    }
}
