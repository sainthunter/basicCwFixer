namespace webBasicCWFixer.Api.Models;

public sealed class AddRootRequest
{
    public string Value { get; set; } = "";
}

public sealed class AllowlistDto
{
    public List<string> Roots { get; set; } = new();
    public List<string> RegexFlags { get; set; } = new();
    public List<string> SkipIdentifiers { get; set; } = new();
    public int MaxUploadMb { get; set; } = 90;
}
