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
builder.Services.AddSingleton<webBasicCWFixer.Analyzer.ProcessMigration.ProcessMigrationAnalyzer>();
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
app.MapProcessMigrationEndpoints();


app.Run();
