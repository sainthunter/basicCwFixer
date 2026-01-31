using Microsoft.AspNetCore.Http.Features;
using webBasicCWFixer.Analyzer;
using webBasicCWFixer.Api.Allowlist;
using webBasicCWFixer.Api.Endpoints;
using webBasicCWFixer.Api.Jobs;
using webBasicCWFixer.Api.SystemTests;

var builder = WebApplication.CreateBuilder(args);

// 90MB limit (server side)
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 90 * 1024L * 1024L;
});
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 90 * 1024L * 1024L;
});

builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddSingleton<AllowlistService>();
builder.Services.AddSingleton<AnalyzerService>();
builder.Services.AddSingleton<SystemTestRunner>();
builder.Services.AddHostedService<JobWorker>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapAnalyzeEndpoints();
app.MapJobEndpoints();
app.MapAllowlistEndpoints();
app.MapSystemTestEndpoints();

app.MapDelete("/api/allowlist/roots", async (
    HttpRequest request,
    AllowlistService allowlistSvc) =>
{
    string? rawValue = request.Query["value"];
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        var payload = await request.ReadFromJsonAsync<AddRootRequest>();
        rawValue = payload?.Value;
    }

    var v = (rawValue ?? "").Trim();
    if (v.Length == 0) return Results.BadRequest("Value boş olamaz.");

    var cfg = allowlistSvc.Load();

    var roots = new HashSet<string>(cfg.Roots, StringComparer.Ordinal);
    var removed = roots.Remove(v);
    if (!removed) return Results.NotFound("Bulunamadı.");

    var newCfg = cfg with { Roots = roots };

    await allowlistSvc.SaveAsync(newCfg);

    return Results.Ok(new AllowlistDto
    {
        Roots = newCfg.Roots.ToList(),
        RegexFlags = newCfg.RegexFlags.ToList(),
        SkipIdentifiers = newCfg.SkipIdentifiers.ToList(),
        MaxUploadMb = newCfg.MaxUploadMb
    });
})
.Accepts<AddRootRequest>("application/json")
.DisableAntiforgery();

app.MapPost("/api/system-test", async (
    AllowlistService allowlistSvc,
    AnalyzerService analyzerSvc) =>
{
    var checks = new List<SystemTestCheck>();
    var success = true;
    AllowlistConfig? originalConfig = null;

    try
    {
        originalConfig = allowlistSvc.Load();
        var testRoot = $"SystemTest_{Guid.NewGuid():N}";

        var addedRoots = new HashSet<string>(originalConfig.Roots, StringComparer.Ordinal)
        {
            testRoot
        };

        var addCfg = originalConfig with { Roots = addedRoots };
        await allowlistSvc.SaveAsync(addCfg);

        var reloadAfterAdd = allowlistSvc.Load();
        if (!reloadAfterAdd.Roots.Contains(testRoot))
        {
            throw new InvalidOperationException("Allowlist'e root eklenemedi.");
        }

        var removedRoots = new HashSet<string>(reloadAfterAdd.Roots, StringComparer.Ordinal);
        removedRoots.Remove(testRoot);
        var removeCfg = reloadAfterAdd with { Roots = removedRoots };
        await allowlistSvc.SaveAsync(removeCfg);

        var reloadAfterDelete = allowlistSvc.Load();
        if (reloadAfterDelete.Roots.Contains(testRoot))
        {
            throw new InvalidOperationException("Allowlist'ten root silinemedi.");
        }

        checks.Add(new SystemTestCheck(
            "Allowlist: add + delete",
            true,
            "Allowlist ekleme/silme doğrulandı."
        ));
    }
    catch (Exception ex)
    {
        success = false;
        checks.Add(new SystemTestCheck(
            "Allowlist: add + delete",
            false,
            ex.Message
        ));
    }
    finally
    {
        if (originalConfig is not null)
        {
            await allowlistSvc.SaveAsync(originalConfig);
        }
    }

    try
    {
        var xmlContent = """
            <root>
              <namespace name="TestNs" />
              <Script>
                <name>TestScript</name>
                <namespace name="TestNs" />
                <script><![CDATA[
                  var a = 1;
                  if (a == 1) { }
                ]]></script>
              </Script>
            </root>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_systemtest_{Guid.NewGuid():N}.xml");
        var logPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_systemtest_{Guid.NewGuid():N}.log");

        await File.WriteAllTextAsync(xmlPath, xmlContent);

        var allowlistCfg = allowlistSvc.Load();
        var result = analyzerSvc.AnalyzeFile(xmlPath, allowlistCfg, logPath);

        if (result.ScriptCount != 1)
        {
            throw new InvalidOperationException($"Beklenmeyen script sayısı: {result.ScriptCount}.");
        }

        if (!File.Exists(logPath))
        {
            throw new InvalidOperationException("Log dosyası üretilemedi.");
        }

        checks.Add(new SystemTestCheck(
            "Analyzer: sample XML",
            true,
            $"ScriptCount={result.ScriptCount}, IssueCount={result.IssueCount}"
        ));

        File.Delete(xmlPath);
        File.Delete(logPath);
    }
    catch (Exception ex)
    {
        success = false;
        checks.Add(new SystemTestCheck(
            "Analyzer: sample XML",
            false,
            ex.Message
        ));
    }

    return Results.Ok(new SystemTestResponse(success, checks, DateTimeOffset.UtcNow));
})
.DisableAntiforgery();


app.Run();

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

public sealed record SystemTestCheck(string Name, bool Success, string Message);

public sealed record SystemTestResponse(bool Success, List<SystemTestCheck> Checks, DateTimeOffset RanAt);
