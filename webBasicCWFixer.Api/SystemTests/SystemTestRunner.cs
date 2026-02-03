using webBasicCWFixer.Analyzer;
using webBasicCWFixer.Analyzer.ProcessMigration;
using webBasicCWFixer.Api.Allowlist;
using webBasicCWFixer.Api.Models;

namespace webBasicCWFixer.Api.SystemTests;

public sealed class SystemTestRunner
{
    private readonly AllowlistService _allowlistService;
    private readonly AnalyzerService _analyzerService;

    public SystemTestRunner(AllowlistService allowlistService, AnalyzerService analyzerService)
    {
        _allowlistService = allowlistService;
        _analyzerService = analyzerService;
    }

    public async Task<SystemTestResponse> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<SystemTestCheck>();
        var success = true;
        AllowlistConfig? originalConfig = null;

        string[]? cleanupPaths = null;

        string? warningXmlPath = null;
        try
        {
            originalConfig = _allowlistService.Load();
            var testRoot = $"SystemTest_{Guid.NewGuid():N}";

            var addedRoots = new HashSet<string>(originalConfig.Roots, StringComparer.Ordinal)
            {
                testRoot
            };

            var addCfg = originalConfig with { Roots = addedRoots };
            await _allowlistService.SaveAsync(addCfg);

            var reloadAfterAdd = _allowlistService.Load();
            if (!reloadAfterAdd.Roots.Contains(testRoot))
            {
                throw new InvalidOperationException("Allowlist'e root eklenemedi.");
            }

            var removedRoots = new HashSet<string>(reloadAfterAdd.Roots, StringComparer.Ordinal);
            removedRoots.Remove(testRoot);
            var removeCfg = reloadAfterAdd with { Roots = removedRoots };
            await _allowlistService.SaveAsync(removeCfg);

            var reloadAfterDelete = _allowlistService.Load();
            if (reloadAfterDelete.Roots.Contains(testRoot))
            {
                throw new InvalidOperationException("Allowlist'ten root silinemedi.");
            }

            checks.Add(new SystemTestCheck(
                "Allowlist: add + delete",
                true,
                "Allowlist ekleme/silme doğrulandı."
            ));
        }
        catch (Exception ex)
        {
            success = false;
            checks.Add(new SystemTestCheck(
                "Allowlist: add + delete",
                false,
                ex.Message
            ));
        }
        finally
        {
            if (originalConfig is not null)
            {
                await _allowlistService.SaveAsync(originalConfig);
            }
        }

        try
        {
            var xmlContent = """
                <root>
                  <namespace name="TestNs" />
                  <Script>
                    <name>TestScript</name>
                    <namespace name="TestNs" />
                    <script><![CDATA[
                      var a = 1;
                      if (a == 1) { }
                    ]]></script>
                  </Script>
                </root>
                """;

            var xmlPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_systemtest_{Guid.NewGuid():N}.xml");
            var logPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_systemtest_{Guid.NewGuid():N}.log");

            await File.WriteAllTextAsync(xmlPath, xmlContent, cancellationToken);

            var allowlistCfg = _allowlistService.Load();
            var result = _analyzerService.AnalyzeFile(xmlPath, allowlistCfg, logPath);

            if (result.ScriptCount != 1)
            {
                throw new InvalidOperationException($"Beklenmeyen script sayısı: {result.ScriptCount}.");
            }

            if (!File.Exists(logPath))
            {
                throw new InvalidOperationException("Log dosyası üretilemedi.");
            }

            checks.Add(new SystemTestCheck(
                "Analyzer: sample XML",
                true,
                $"ScriptCount={result.ScriptCount}, IssueCount={result.IssueCount}"
            ));

            File.Delete(xmlPath);
            File.Delete(logPath);
        }
        catch (Exception ex)
        {
            success = false;
            checks.Add(new SystemTestCheck(
                "Analyzer: sample XML",
                false,
                ex.Message
            ));
        }

        try
        {
            var warningXml = """
                <root>
                  <namespace name="TestNs" />
                  <Script>
                    <name>WarnScript</name>
                    <namespace name="TestNs" />
                    <parameter>
                      <name>unusedParam</name>
                    </parameter>
                    <script><![CDATA[
                      var usedVar = 1;
                      var unusedVar = 2;
                      var dupVar = 10;
                      var dupVar = 20;
                      function unusedFunc(arg1) {
                        return arg1 + usedVar;
                      }
                      if (usedVar > 0) {
                        usedVar = usedVar + 1;
                      }
                    ]]></script>
                  </Script>
                </root>
                """;

            warningXmlPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_systemtest_warn_{Guid.NewGuid():N}.xml");

            await File.WriteAllTextAsync(warningXmlPath, warningXml, cancellationToken);

            var allowlistCfg = _allowlistService.Load();
            var result = _analyzerService.AnalyzeWarnings(warningXmlPath, allowlistCfg);

            if (result.WarningCount <= 0)
            {
                throw new InvalidOperationException("Uyarı analizi beklenen bulguları üretmedi.");
            }

            if (!result.Warnings.Any(w => w.Rule == "unused-variable" || w.Rule == "unused-parameter" || w.Rule == "unused-function"))
            {
                throw new InvalidOperationException("Uyarı analizi beklenen kural türlerini üretmedi.");
            }

            checks.Add(new SystemTestCheck(
                "Warnings: sample XML",
                true,
                $"WarningCount={result.WarningCount}"
            ));

        }
        catch (Exception ex)
        {
            success = false;
            checks.Add(new SystemTestCheck(
                "Warnings: sample XML",
                false,
                ex.Message
            ));
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(warningXmlPath) && File.Exists(warningXmlPath))
            {
                File.Delete(warningXmlPath);
            }
        }

        try
        {
            var migrationXml = """
                <ConceptWaveMetadata>
                  <Process>
                    <name>proc_SubX_v1_1</name>
                  </Process>
                  <Process>
                    <name>proc_SubX_v1_2</name>
                  </Process>
                  <Process>
                    <name>proc_Parent_v1_2</name>
                    <activity type="spawn" name="spawnSub">
                      <element name="ns:proc_SubX_v1_2">{AAA}</element>
                    </activity>
                    <activity type="join" name="joinSub">
                      <element name="ns:proc_SubX_v1_1">{BBB}</element>
                    </activity>
                  </Process>
                </ConceptWaveMetadata>
                """;

            var inputPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_migration_{Guid.NewGuid():N}.xml");
            var outputPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_migration_{Guid.NewGuid():N}.csv");
            cleanupPaths = new[] { inputPath, outputPath };

            await File.WriteAllTextAsync(inputPath, migrationXml, cancellationToken);

            var analyzer = new ProcessMigrationAnalyzer();
            var summary = analyzer.Analyze(inputPath, outputPath, OutputFormat.Csv, debug: false);

            if (summary.ProcessCount <= 0 || summary.LatestProcessCount <= 0)
            {
                throw new InvalidOperationException("Process migration analizinde beklenmeyen özet değerleri döndü.");
            }

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException("Process migration çıktısı üretilemedi.");
            }

            var outputLines = await File.ReadAllLinesAsync(outputPath, cancellationToken);
            if (!outputLines.Any(line => line.Contains("spawn_join_mismatch", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Process migration çıktısında beklenen bulgu yok.");
            }

            checks.Add(new SystemTestCheck(
                "Process migration: sample XML",
                true,
                $"ProcessCount={summary.ProcessCount}, LatestProcessCount={summary.LatestProcessCount}, FindingCount={summary.FindingCount}"
            ));
        }
        catch (Exception ex)
        {
            success = false;
            checks.Add(new SystemTestCheck(
                "Process migration: sample XML",
                false,
                ex.Message
            ));
        }
        finally
        {
            foreach (var path in cleanupPaths ?? Array.Empty<string>())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // ignore cleanup failures
                }
            }
        }

        return new SystemTestResponse(success, checks, DateTimeOffset.UtcNow);
    }
}
