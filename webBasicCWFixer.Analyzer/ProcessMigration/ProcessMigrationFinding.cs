namespace webBasicCWFixer.Analyzer.ProcessMigration;

public sealed record ProcessMigrationFinding(
    string ParentProcessName,
    string ParentBaseName,
    string ParentVersion,
    string RefType,
    string TargetProcessName,
    string TargetBaseName,
    string ReferencedVersion,
    string ExpectedVersion,
    string Severity,
    string Location,
    string Reason
);
