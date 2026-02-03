using System.Text;
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

public sealed record WarningDto(
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

public sealed record WarningResult(
    int ScriptCount,
    int WarningCount,
    List<WarningDto> Warnings
);

public sealed class AnalyzerService
{
    public AnalyzeResult AnalyzeFile(string xmlPath, AllowlistConfig allowlist, string logPath, Action<int>? onProgress = null)
    {
        var knownNamespaceRoots = XmlCwReader.ReadAllNamespaceNames(xmlPath);

        var builtinRoots = new HashSet<string>(allowlist.Roots, StringComparer.Ordinal);
        foreach (var js in JsGlobals)
        {
            builtinRoots.Add(js);
        }

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

            if (scriptCount % 10 == 0)
            {
                onProgress?.Invoke(scriptCount);
            }
        }

        writer.WriteLine();
        writer.WriteLine($"Toplam Script: {scriptCount}");
        writer.WriteLine($"Toplam Issue : {issueCount}");

        return new AnalyzeResult(scriptCount, issueCount, issues);
    }

    public WarningResult AnalyzeWarnings(string xmlPath, AllowlistConfig allowlist, Action<int>? onProgress = null)
    {
        _ = allowlist;
        var analyzer = new CwWarningAnalyzer();

        int scriptCount = 0;
        int warningCount = 0;
        var warnings = new List<WarningDto>();

        foreach (var script in XmlCwReader.ReadScripts(xmlPath))
        {
            scriptCount++;

            foreach (var warning in analyzer.Analyze(script))
            {
                warningCount++;
                var snippet = IssueFormatter.GetLineSnippet(script, warning.Line);
                warnings.Add(new WarningDto(
                    warning.Rule,
                    warning.FullName,
                    warning.Line,
                    warning.Column,
                    warning.Message,
                    snippet
                ));
            }

            if (scriptCount % 10 == 0)
            {
                onProgress?.Invoke(scriptCount);
            }
        }

        return new WarningResult(scriptCount, warningCount, warnings);
    }

    public static readonly string[] JsGlobals =
    {
        "Math", "Date", "String", "Number", "Boolean", "Object", "Array", "JSON", "RegExp", "Error", "isNaN",
        "parseInt", "parseFloat", "decodeURI", "decodeURIComponent", "encodeURI", "encodeURIComponent", "Infinity", "NaN",
        "undefined"
    };
}
