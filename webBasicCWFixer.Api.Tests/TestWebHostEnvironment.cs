using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace webBasicCWFixer.Api.Tests;

public sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public TestWebHostEnvironment()
    {
        ContentRootPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(ContentRootPath);
    }

    public string ApplicationName { get; set; } = "webBasicCWFixer.Api.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
