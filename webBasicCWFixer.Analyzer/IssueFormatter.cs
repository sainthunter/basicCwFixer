namespace webBasicCWFixer.Analyzer;

internal static class IssueFormatter
{
    public static string GetLineSnippet(XmlCwReader.ScriptBlock script, int line1Based, int maxLen = 180)
    {
        if (line1Based <= 0) return "";
        var lines = script.ScriptBody.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        if (line1Based > lines.Length) return "";
        var s = lines[line1Based - 1].Trim();
        if (s.Length > maxLen)
        {
            s = s[..maxLen] + "…";
        }
        return s;
    }
}
