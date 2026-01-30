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

}
