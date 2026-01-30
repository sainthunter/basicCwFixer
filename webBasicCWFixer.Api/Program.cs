using Microsoft.AspNetCore.Http.Features;
using webBasicCWFixer.Analyzer;
using webBasicCWFixer.Api.Allowlist;
using webBasicCWFixer.Api.Jobs;

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
builder.Services.AddHostedService<JobWorker>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// ---- ENDPOINTS ----

// Upload + start job
app.MapPost("/api/analyze", async (
    IFormFile file,
    JobStore store,
    JobQueue queue,
    AllowlistService allowlist) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest("Dosya boş.");

    var cfg = allowlist.Load();
    var maxBytes = cfg.MaxUploadMb * 1024L * 1024L;

    if (file.Length > maxBytes)
        return Results.BadRequest($"Dosya çok büyük. Max {cfg.MaxUploadMb}MB.");

    var jobId = Guid.NewGuid().ToString("N");
    var job = store.Create(jobId);

    var tempXml = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_{jobId}.xml");
    job.XmlPath = tempXml;
    job.Message = "Upload alındı";
    job.Progress = 1;

    await using (var fs = File.Create(tempXml))
        await file.CopyToAsync(fs);

    job.Message = "Kuyruğa alındı";
    job.Progress = 3;

    await queue.EnqueueAsync(jobId);

    return Results.Ok(new { jobId });
})
.Accepts<IFormFile>("multipart/form-data")
.DisableAntiforgery();


// Job status
app.MapGet("/api/jobs/{jobId}", (string jobId, JobStore store) =>
{
    if (!store.TryGet(jobId, out var job) || job is null)
        return Results.NotFound();

    return Results.Ok(new
    {
        job.JobId,
        status = job.Status.ToString(),
        job.Progress,
        job.Message,
        job.ScriptCount,
        job.IssueCount,
        hasLog = !string.IsNullOrWhiteSpace(job.LogPath) && File.Exists(job.LogPath),
        error = job.Error
    });
});

// Issues paging
app.MapGet("/api/jobs/{jobId}/issues", (string jobId, int page, int pageSize, JobStore store) =>
{
    page = page <= 0 ? 1 : page;
    pageSize = pageSize is <= 0 or > 500 ? 100 : pageSize;

    if (!store.TryGet(jobId, out var job) || job is null)
        return Results.NotFound();

    var total = job.Issues.Count;
    var items = job.Issues
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToList();

    return Results.Ok(new { total, page, pageSize, items });
});

// Log download
app.MapGet("/api/jobs/{jobId}/log", (string jobId, JobStore store) =>
{
    if (!store.TryGet(jobId, out var job) || job is null)
        return Results.NotFound();

    if (string.IsNullOrWhiteSpace(job.LogPath) || !File.Exists(job.LogPath))
        return Results.NotFound("Log yok.");

    return Results.File(job.LogPath, "text/plain", $"webBasicCWFixer_{jobId}.log");
});

// Allowlist get
app.MapGet("/api/allowlist", (AllowlistService svc) =>
{
    return Results.File(svc.PathOnDisk, "application/json");
});

app.MapPut("/api/allowlist", async (
    AllowlistDto dto,
    AllowlistService allowlistSvc) =>
{
    // normalize & distinct
    var rootsList = (dto.Roots ?? new List<string>())
        .Select(x => (x ?? "").Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    var regexFlagsList = (dto.RegexFlags ?? new List<string>())
        .Select(x => (x ?? "").Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    var skipList = (dto.SkipIdentifiers ?? new List<string>())
        .Select(x => (x ?? "").Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    var maxMb = dto.MaxUploadMb <= 0 ? 90 : dto.MaxUploadMb;

    // cfg record/init-only ise: with kullan
    var cfg = allowlistSvc.Load();

    // cfg.Roots tipi büyük olasılıkla HashSet<string> / ISet<string>
    var newCfg = cfg with
    {
        Roots = rootsList.ToHashSet(StringComparer.Ordinal),
        RegexFlags = regexFlagsList.ToHashSet(StringComparer.Ordinal),
        SkipIdentifiers = skipList.ToHashSet(StringComparer.Ordinal),
        MaxUploadMb = maxMb
    };

    await allowlistSvc.SaveAsync(newCfg);

    // DTO'ya dönerken set -> list dönüşümü (CS0029 fix)
    return Results.Ok(new AllowlistDto
    {
        Roots = newCfg.Roots.ToList(),
        RegexFlags = newCfg.RegexFlags.ToList(),
        SkipIdentifiers = newCfg.SkipIdentifiers.ToList(),
        MaxUploadMb = newCfg.MaxUploadMb
    });
})
.Accepts<AllowlistDto>("application/json")
.DisableAntiforgery();



app.MapPost("/api/allowlist/roots", async (
    AddRootRequest req,
    AllowlistService allowlistSvc) =>
{
    var v = (req.Value ?? "").Trim();
    if (v.Length == 0) return Results.BadRequest("Value boş olamaz.");

    var cfg = allowlistSvc.Load();

    // yeni set üret (record/init-only dostu)
    var roots = new HashSet<string>(cfg.Roots, StringComparer.Ordinal);
    roots.Add(v);

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

app.MapDelete("/api/allowlist/roots/{value}", async (
    string value,
    AllowlistService allowlistSvc) =>
{
    var v = (value ?? "").Trim();
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