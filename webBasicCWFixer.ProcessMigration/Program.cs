using webBasicCWFixer.Analyzer.ProcessMigration;

string? input = null;
string? output = null;
OutputFormat format = OutputFormat.Csv;
bool debug = false;

for (int i = 0; i < args.Length; i++)
{
    var arg = args[i];
    if (arg == "--input" && i + 1 < args.Length)
    {
        input = args[++i];
        continue;
    }
    if (arg == "--output" && i + 1 < args.Length)
    {
        output = args[++i];
        continue;
    }
    if (arg == "--format" && i + 1 < args.Length)
    {
        var fmt = args[++i];
        if (string.Equals(fmt, "jsonl", StringComparison.OrdinalIgnoreCase))
        {
            format = OutputFormat.Jsonl;
        }
        else
        {
            format = OutputFormat.Csv;
        }
        continue;
    }
    if (arg == "--debug")
    {
        debug = true;
    }
}

if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
{
    Console.WriteLine("Usage: --input <path> --output <path> [--format csv|jsonl] [--debug]");
    return;
}

var analyzer = new ProcessMigrationAnalyzer();
var summary = analyzer.Analyze(input, output, format, debug);

if (debug)
{
    Console.WriteLine($"Processes: {summary.ProcessCount}");
    Console.WriteLine($"Latest processes: {summary.LatestProcessCount}");
    Console.WriteLine($"Findings: {summary.FindingCount}");
}
