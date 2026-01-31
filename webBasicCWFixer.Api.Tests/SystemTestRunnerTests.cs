using webBasicCWFixer.Analyzer;
using webBasicCWFixer.Api.Allowlist;
using webBasicCWFixer.Api.SystemTests;
using Xunit;

namespace webBasicCWFixer.Api.Tests;

public sealed class SystemTestRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsSuccessWithChecks()
    {
        var env = new TestWebHostEnvironment();
        var allowlistService = new AllowlistService(env);
        var analyzerService = new AnalyzerService();
        var runner = new SystemTestRunner(allowlistService, analyzerService);

        var result = await runner.RunAsync();

        Assert.NotNull(result);
        Assert.True(result.Checks.Count >= 2);
    }
}
