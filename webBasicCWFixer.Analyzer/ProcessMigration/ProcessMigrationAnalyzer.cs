using System.Text;

namespace webBasicCWFixer.Analyzer.ProcessMigration;

public sealed class ProcessMigrationAnalyzer
{
    private const string DefaultRefType = "unknown";

    public ProcessMigrationSummary Analyze(
        string inputPath,
        string outputPath,
        OutputFormat format = OutputFormat.Csv,
        bool debug = false)
    {
        var latest = BuildLatestIndex(inputPath);
        var summary = ValidateLatestProcesses(inputPath, outputPath, format, latest, debug);
        return summary;
    }

    private static Dictionary<string, VersionTuple> BuildLatestIndex(string inputPath)
    {
        var latest = new Dictionary<string, VersionTuple>(StringComparer.Ordinal);
        using var fs = File.OpenRead(inputPath);
        using var reader = new StreamReader(fs, Encoding.UTF8, true, bufferSize: 1024 * 64);

        bool inProcess = false;
        bool gotProcessName = false;
        long lineNumber = 0;
        bool readingName = false;
        var nameBuffer = new StringBuilder();

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line == null) break;
            lineNumber++;

            if (!inProcess)
            {
                if (line.Contains("<Process", StringComparison.OrdinalIgnoreCase))
                {
                    inProcess = true;
                    gotProcessName = false;
                    readingName = false;
                    nameBuffer.Clear();
                }
                continue;
            }

            if (line.Contains("</Process>", StringComparison.OrdinalIgnoreCase))
            {
                inProcess = false;
                gotProcessName = false;
                readingName = false;
                nameBuffer.Clear();
                continue;
            }

            if (gotProcessName)
            {
                continue;
            }

            if (!readingName && line.Contains("<name>", StringComparison.OrdinalIgnoreCase))
            {
                readingName = true;
                nameBuffer.Append(line);
            }
            else if (readingName)
            {
                nameBuffer.Append(line);
            }

            if (readingName && line.Contains("</name>", StringComparison.OrdinalIgnoreCase))
            {
                var fullName = ExtractTagValue(nameBuffer.ToString(), "name");
                if (!string.IsNullOrWhiteSpace(fullName)
                    && ProcessNameParser.TryParse(fullName, out var baseName, out var version))
                {
                    if (!latest.TryGetValue(baseName, out var existing) || version.CompareTo(existing) > 0)
                    {
                        latest[baseName] = version;
                    }
                }

                gotProcessName = true;
                readingName = false;
                nameBuffer.Clear();
            }
        }

        return latest;
    }

    private static ProcessMigrationSummary ValidateLatestProcesses(
        string inputPath,
        string outputPath,
        OutputFormat format,
        Dictionary<string, VersionTuple> latest,
        bool debug)
    {
        using var fs = File.OpenRead(inputPath);
        using var reader = new StreamReader(fs, Encoding.UTF8, true, bufferSize: 1024 * 64);
        using var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(outStream, new UTF8Encoding(false));

        var output = FindingWriter.Create(writer, format);
        output.WriteHeader();

        bool inProcess = false;
        bool gotProcessName = false;
        bool isLatestProcess = false;
        long lineNumber = 0;
        bool readingName = false;
        var nameBuffer = new StringBuilder();

        string parentFullName = "";
        string parentBaseName = "";
        VersionTuple parentVersion = VersionTuple.Empty;

        var refIndex = new Dictionary<string, TargetRefCollection>(StringComparer.Ordinal);
        var activityStack = new Stack<ActivityContext>();

        int processesSeen = 0;
        int latestSeen = 0;
        int findingCount = 0;

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line == null) break;
            lineNumber++;

            if (!inProcess)
            {
                if (line.Contains("<Process", StringComparison.OrdinalIgnoreCase))
                {
                    inProcess = true;
                    gotProcessName = false;
                    isLatestProcess = false;
                    readingName = false;
                    nameBuffer.Clear();
                    refIndex.Clear();
                    activityStack.Clear();
                }
                continue;
            }

            if (line.Contains("</Process>", StringComparison.OrdinalIgnoreCase))
            {
                if (isLatestProcess)
                {
                    findingCount += EvaluateProcess(
                        parentFullName,
                        parentBaseName,
                        parentVersion,
                        latest,
                        refIndex,
                        output);
                }

                inProcess = false;
                gotProcessName = false;
                isLatestProcess = false;
                readingName = false;
                nameBuffer.Clear();
                refIndex.Clear();
                activityStack.Clear();
                continue;
            }

            if (!gotProcessName)
            {
                if (!readingName && line.Contains("<name>", StringComparison.OrdinalIgnoreCase))
                {
                    readingName = true;
                    nameBuffer.Append(line);
                }
                else if (readingName)
                {
                    nameBuffer.Append(line);
                }

                if (readingName && line.Contains("</name>", StringComparison.OrdinalIgnoreCase))
                {
                    var fullName = ExtractTagValue(nameBuffer.ToString(), "name");
                    if (!string.IsNullOrWhiteSpace(fullName)
                        && ProcessNameParser.TryParse(fullName, out var baseName, out var version))
                    {
                        parentFullName = fullName;
                        parentBaseName = baseName;
                        parentVersion = version;
                        processesSeen++;
                        isLatestProcess = latest.TryGetValue(parentBaseName, out var latestVersion)
                            && parentVersion.CompareTo(latestVersion) == 0;
                        if (isLatestProcess)
                        {
                            latestSeen++;
                        }
                    }
                    else
                    {
                        parentFullName = fullName ?? string.Empty;
                        parentBaseName = string.Empty;
                        parentVersion = VersionTuple.Empty;
                        isLatestProcess = false;
                    }

                    gotProcessName = true;
                    readingName = false;
                    nameBuffer.Clear();
                }

                continue;
            }

            if (!isLatestProcess)
            {
                continue;
            }

            if (line.Contains("<activity", StringComparison.OrdinalIgnoreCase))
            {
                var activityType = ExtractAttributeValue(line, "type");
                var activityName = ExtractAttributeValue(line, "name");
                var activityId = ExtractAttributeValue(line, "guid");

                activityStack.Push(new ActivityContext(
                    activityType ?? string.Empty,
                    activityName ?? string.Empty,
                    activityId ?? string.Empty));
            }

            if (line.Contains("</activity>", StringComparison.OrdinalIgnoreCase))
            {
                if (activityStack.Count > 0)
                {
                    activityStack.Pop();
                }
            }

            if (activityStack.Count > 0 && line.Contains("<type>", StringComparison.OrdinalIgnoreCase))
            {
                var typeValue = ExtractTagValue(line, "type");
                if (!string.IsNullOrWhiteSpace(typeValue))
                {
                    var current = activityStack.Pop();
                    activityStack.Push(current with { Type = typeValue });
                }
            }

            if (activityStack.Count > 0 && line.Contains("<name>", StringComparison.OrdinalIgnoreCase))
            {
                var nameValue = ExtractTagValue(line, "name");
                if (!string.IsNullOrWhiteSpace(nameValue))
                {
                    var current = activityStack.Pop();
                    activityStack.Push(current with { Name = nameValue });
                }
            }

            if (line.Contains("<element", StringComparison.OrdinalIgnoreCase) && line.Contains("name=", StringComparison.OrdinalIgnoreCase))
            {
                var elementName = ExtractAttributeValue(line, "name");
                if (string.IsNullOrWhiteSpace(elementName))
                {
                    continue;
                }

                if (!elementName.Contains("proc_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ProcessNameParser.TryParseReference(elementName, out var targetFullName, out var targetBaseName, out var targetVersion))
                {
                    continue;
                }

                var refType = DefaultRefType;
                string location = $"line:{lineNumber}";
                if (activityStack.Count > 0)
                {
                    var ctx = activityStack.Peek();
                    if (!string.IsNullOrWhiteSpace(ctx.Type))
                    {
                        refType = ctx.Type;
                    }
                    var activityLabel = !string.IsNullOrWhiteSpace(ctx.Name) ? ctx.Name : ctx.Id;
                    if (!string.IsNullOrWhiteSpace(activityLabel))
                    {
                        location = $"line:{lineNumber} activity:{activityLabel} type:{refType}";
                    }
                }

                if (!refIndex.TryGetValue(targetBaseName, out var collection))
                {
                    collection = new TargetRefCollection(targetBaseName);
                    refIndex[targetBaseName] = collection;
                }

                collection.Add(new TargetReference(
                    targetFullName,
                    targetBaseName,
                    targetVersion,
                    refType,
                    location));
            }
        }

        output.Flush();
        return new ProcessMigrationSummary(processesSeen, latestSeen, findingCount, debug);
    }

    private static int EvaluateProcess(
        string parentFullName,
        string parentBaseName,
        VersionTuple parentVersion,
        Dictionary<string, VersionTuple> latest,
        Dictionary<string, TargetRefCollection> refIndex,
        FindingWriter output)
    {
        int findingCount = 0;

        foreach (var entry in refIndex.Values)
        {
            var versions = entry.GetDistinctVersions();
            if (versions.Count > 1)
            {
                foreach (var reference in entry.References)
                {
                    findingCount += WriteFinding(
                        output,
                        parentFullName,
                        parentBaseName,
                        parentVersion,
                        reference,
                        latest,
                        "HIGH",
                        "mixed_versions_in_same_parent");
                }
            }

            if (entry.TryGetSpawnJoinMismatch(out var mismatch))
            {
                foreach (var reference in mismatch)
                {
                    findingCount += WriteFinding(
                        output,
                        parentFullName,
                        parentBaseName,
                        parentVersion,
                        reference,
                        latest,
                        "HIGH",
                        "spawn_join_mismatch");
                }
            }

            foreach (var reference in entry.References)
            {
                if (!latest.TryGetValue(reference.TargetBaseName, out var expected))
                {
                    findingCount += WriteFinding(
                        output,
                        parentFullName,
                        parentBaseName,
                        parentVersion,
                        reference,
                        latest,
                        "LOW",
                        "unknown_target_process");
                    continue;
                }

                if (reference.TargetVersion.CompareTo(expected) != 0)
                {
                    var severity = reference.RefType is "join" or "wait" ? "HIGH" : "MEDIUM";
                    findingCount += WriteFinding(
                        output,
                        parentFullName,
                        parentBaseName,
                        parentVersion,
                        reference,
                        latest,
                        severity,
                        "target_latest_exists_but_old_ref");
                }
            }
        }

        return findingCount;
    }

    private static int WriteFinding(
        FindingWriter output,
        string parentFullName,
        string parentBaseName,
        VersionTuple parentVersion,
        TargetReference reference,
        Dictionary<string, VersionTuple> latest,
        string severity,
        string reason)
    {
        latest.TryGetValue(reference.TargetBaseName, out var expected);
        var finding = new ProcessMigrationFinding(
            parentFullName,
            parentBaseName,
            parentVersion.ToDisplay(),
            reference.RefType,
            reference.TargetFullName,
            reference.TargetBaseName,
            reference.TargetVersion.ToDisplay(),
            expected.ToDisplay(),
            severity,
            reference.Location,
            reason);

        output.WriteFinding(finding);
        return 1;
    }

    private static string ExtractAttributeValue(string line, string attribute)
    {
        var token = attribute + "=\"";
        var start = line.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        start += token.Length;
        var end = line.IndexOf('"', start);
        if (end < 0) return string.Empty;
        return line.Substring(start, end - start);
    }

    private static string ExtractTagValue(string text, string tagName)
    {
        var open = "<" + tagName + ">";
        var close = "</" + tagName + ">";
        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        start += open.Length;
        var end = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return string.Empty;
        return text.Substring(start, end - start).Trim();
    }

    private sealed record ActivityContext(string Type, string Name, string Id);

    private sealed record TargetReference(
        string TargetFullName,
        string TargetBaseName,
        VersionTuple TargetVersion,
        string RefType,
        string Location);

    private sealed class TargetRefCollection
    {
        public TargetRefCollection(string baseName)
        {
            BaseName = baseName;
        }

        public string BaseName { get; }
        public List<TargetReference> References { get; } = new();

        public void Add(TargetReference reference)
        {
            References.Add(reference);
        }

        public HashSet<VersionTuple> GetDistinctVersions()
        {
            return References.Select(r => r.TargetVersion).ToHashSet();
        }

        public bool TryGetSpawnJoinMismatch(out List<TargetReference> mismatch)
        {
            mismatch = new List<TargetReference>();
            var spawnVersions = References
                .Where(r => r.RefType == "spawn")
                .Select(r => r.TargetVersion)
                .Distinct()
                .ToList();

            var joinVersions = References
                .Where(r => r.RefType is "join" or "wait")
                .Select(r => r.TargetVersion)
                .Distinct()
                .ToList();

            if (spawnVersions.Count == 0 || joinVersions.Count == 0)
            {
                return false;
            }

            var mismatchFound = spawnVersions.Any(spawn => joinVersions.Any(join => spawn.CompareTo(join) != 0));
            if (!mismatchFound)
            {
                return false;
            }

            mismatch.AddRange(References.Where(r => r.RefType is "join" or "wait"));
            mismatch.AddRange(References.Where(r => r.RefType == "spawn"));
            return true;
        }
    }
}

public sealed record ProcessMigrationSummary(int ProcessCount, int LatestProcessCount, int FindingCount, bool Debug);

public enum OutputFormat
{
    Csv,
    Jsonl
}

internal static class ProcessNameParser
{
    public static bool TryParse(string fullName, out string baseName, out VersionTuple version)
    {
        baseName = string.Empty;
        version = VersionTuple.Empty;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return false;
        }

        var trimmed = fullName.Trim();
        if (TrySplitVersion(trimmed, out baseName, out version))
        {
            return true;
        }

        return false;
    }

    public static bool TryParseReference(string elementName, out string fullName, out string baseName, out VersionTuple version)
    {
        fullName = string.Empty;
        baseName = string.Empty;
        version = VersionTuple.Empty;

        if (string.IsNullOrWhiteSpace(elementName))
        {
            return false;
        }

        var value = elementName.Trim();
        var colon = value.LastIndexOf(':');
        if (colon >= 0 && colon < value.Length - 1)
        {
            value = value[(colon + 1)..];
        }

        if (!TrySplitVersion(value, out baseName, out version))
        {
            return false;
        }

        fullName = value;
        return true;
    }

    private static bool TrySplitVersion(string value, out string baseName, out VersionTuple version)
    {
        baseName = string.Empty;
        version = VersionTuple.Empty;

        var vIndex = value.LastIndexOf("_v_", StringComparison.OrdinalIgnoreCase);
        var altIndex = value.LastIndexOf("_v", StringComparison.OrdinalIgnoreCase);

        int index;
        int offset;
        if (vIndex >= 0)
        {
            index = vIndex;
            offset = 3;
        }
        else if (altIndex >= 0)
        {
            index = altIndex;
            offset = 2;
        }
        else
        {
            return false;
        }

        if (index <= 0)
        {
            return false;
        }

        var versionPart = value[(index + offset)..];
        if (string.IsNullOrWhiteSpace(versionPart))
        {
            return false;
        }

        var numbers = versionPart.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<int>();
        foreach (var number in numbers)
        {
            if (!int.TryParse(number, out var parsed))
            {
                return false;
            }
            parts.Add(parsed);
        }

        if (parts.Count == 0)
        {
            return false;
        }

        baseName = value.Substring(0, index);
        version = new VersionTuple(parts);
        return true;
    }
}

internal readonly record struct VersionTuple(IReadOnlyList<int> Parts) : IComparable<VersionTuple>
{
    public static readonly VersionTuple Empty = new(Array.Empty<int>());

    public int CompareTo(VersionTuple other)
    {
        var max = Math.Max(Parts.Count, other.Parts.Count);
        for (int i = 0; i < max; i++)
        {
            var left = i < Parts.Count ? Parts[i] : 0;
            var right = i < other.Parts.Count ? other.Parts[i] : 0;
            var cmp = left.CompareTo(right);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return 0;
    }

    public string ToDisplay()
    {
        if (Parts.Count == 0)
        {
            return string.Empty;
        }

        return string.Join('.', Parts);
    }
}

internal abstract class FindingWriter
{
    public static FindingWriter Create(StreamWriter writer, OutputFormat format)
        => format == OutputFormat.Jsonl
            ? new JsonlFindingWriter(writer)
            : new CsvFindingWriter(writer);

    public abstract void WriteHeader();
    public abstract void WriteFinding(ProcessMigrationFinding finding);
    public abstract void Flush();
}

internal sealed class CsvFindingWriter : FindingWriter
{
    private readonly StreamWriter _writer;

    public CsvFindingWriter(StreamWriter writer)
    {
        _writer = writer;
    }

    public override void WriteHeader()
    {
        _writer.WriteLine(string.Join(',', new[]
        {
            "parentProcessName",
            "parentBaseName",
            "parentVersion",
            "refType",
            "targetProcessName",
            "targetBaseName",
            "referencedVersion",
            "expectedVersion",
            "severity",
            "location",
            "reason"
        }));
    }

    public override void WriteFinding(ProcessMigrationFinding finding)
    {
        _writer.WriteLine(string.Join(',', new[]
        {
            Escape(finding.ParentProcessName),
            Escape(finding.ParentBaseName),
            Escape(finding.ParentVersion),
            Escape(finding.RefType),
            Escape(finding.TargetProcessName),
            Escape(finding.TargetBaseName),
            Escape(finding.ReferencedVersion),
            Escape(finding.ExpectedVersion),
            Escape(finding.Severity),
            Escape(finding.Location),
            Escape(finding.Reason)
        }));
    }

    public override void Flush() => _writer.Flush();

    private static string Escape(string value)
    {
        if (value == null)
        {
            return "";
        }

        var escaped = value.Replace("\"", "\"\"");
        if (escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n'))
        {
            return "\"" + escaped + "\"";
        }

        return escaped;
    }
}

internal sealed class JsonlFindingWriter : FindingWriter
{
    private readonly StreamWriter _writer;

    public JsonlFindingWriter(StreamWriter writer)
    {
        _writer = writer;
    }

    public override void WriteHeader()
    {
    }

    public override void WriteFinding(ProcessMigrationFinding finding)
    {
        var line = System.Text.Json.JsonSerializer.Serialize(finding);
        _writer.WriteLine(line);
    }

    public override void Flush() => _writer.Flush();
}
