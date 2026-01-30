using System.Net;
using System.Text.RegularExpressions;
using System.Xml;

namespace webBasicCWFixer.Analyzer;

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
                {
                    set.Add(ns.Trim());
                }
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
            sub.Read();

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
                    if (!string.IsNullOrWhiteSpace(n))
                    {
                        nsName = n.Trim();
                    }

                    _ = sub.ReadInnerXml();
                }
                else if (sub.Name.Equals("parameter", StringComparison.OrdinalIgnoreCase))
                {
                    using var psub = sub.ReadSubtree();
                    psub.Read();
                    while (psub.Read())
                    {
                        if (psub.NodeType == XmlNodeType.Element &&
                            psub.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                        {
                            var p = (psub.ReadElementContentAsString() ?? "").Trim();
                            if (IsValidIdentifier(p))
                            {
                                parameters.Add(p);
                            }
                        }
                    }
                }
                else if (sub.Name.Equals("script", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = sub.ReadInnerXml() ?? "";
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
