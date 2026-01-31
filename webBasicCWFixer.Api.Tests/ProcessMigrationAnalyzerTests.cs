using System.Text;
using webBasicCWFixer.Analyzer.ProcessMigration;
using Xunit;

namespace webBasicCWFixer.Api.Tests;

public sealed class ProcessMigrationAnalyzerTests
{
    [Fact]
    public void DetectsSpawnJoinMismatchOnLatestProcess()
    {
        var xml = """
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
              <Process>
                <name>proc_Parent_v1_1</name>
                <activity type="join" name="joinOld">
                  <element name="ns:proc_SubX_v1_1">{CCC}</element>
                </activity>
              </Process>
            </ConceptWaveMetadata>
            """;

        var input = Path.Combine(Path.GetTempPath(), $"proc_migration_{Guid.NewGuid():N}.xml");
        var output = Path.Combine(Path.GetTempPath(), $"proc_migration_{Guid.NewGuid():N}.csv");
        File.WriteAllText(input, xml, Encoding.UTF8);

        var analyzer = new ProcessMigrationAnalyzer();
        analyzer.Analyze(input, output, OutputFormat.Csv, debug: false);

        var lines = File.ReadAllLines(output);
        Assert.Contains(lines, line => line.Contains("spawn_join_mismatch"));
        Assert.Contains(lines, line => line.Contains("target_latest_exists_but_old_ref"));

        File.Delete(input);
        File.Delete(output);
    }
}
