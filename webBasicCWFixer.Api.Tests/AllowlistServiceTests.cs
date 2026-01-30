using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using webBasicCWFixer.Analyzer;
using webBasicCWFixer.Api.Allowlist;
using Xunit;

namespace webBasicCWFixer.Api.Tests;

public sealed class AllowlistServiceTests
{
    [Fact]
    public void SaveAndLoadPersistsRoots()
    {
        var env = new TestWebHostEnvironment();
        var service = new AllowlistService(env);

        var cfg = service.Load();
        var root = $"TestRoot_{Guid.NewGuid():N}";
        var newCfg = cfg with
        {
            Roots = new HashSet<string>(cfg.Roots, StringComparer.Ordinal) { root }
        };

        service.Save(newCfg);
        var reload = service.Load();

        Assert.Contains(root, reload.Roots);
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
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
}
