using webBasicCWFixer.Analyzer;
using Xunit;

namespace webBasicCWFixer.Api.Tests;

public sealed class AnalyzerServiceTests
{
    [Fact]
    public void AnalyzeFileReadsScriptsAndWritesLog()
    {
        var xmlContent = """
            <root>
              <namespace name=\"TestNs\" />
              <Script>
                <name>TestScript</name>
                <namespace name=\"TestNs\" />
                <script><![CDATA[
                  var a = 1;
                  if (a == 1) { }
                ]]></script>
              </Script>
            </root>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_analyzer_{Guid.NewGuid():N}.xml");
        var logPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_analyzer_{Guid.NewGuid():N}.log");

        File.WriteAllText(xmlPath, xmlContent);

        var service = new AnalyzerService();
        var cfg = new AllowlistConfig(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            90
        );

        var result = service.AnalyzeFile(xmlPath, cfg, logPath);

        Assert.Equal(1, result.ScriptCount);
        Assert.True(File.Exists(logPath));

        File.Delete(xmlPath);
        File.Delete(logPath);
    }
}
