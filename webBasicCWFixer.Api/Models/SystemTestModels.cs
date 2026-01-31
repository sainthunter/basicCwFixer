namespace webBasicCWFixer.Api.Models;

public sealed record SystemTestCheck(string Name, bool Success, string Message);

public sealed record SystemTestResponse(bool Success, List<SystemTestCheck> Checks, DateTimeOffset RanAt);
