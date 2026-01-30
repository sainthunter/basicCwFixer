using webBasicCWFixer.Api.Allowlist;
using webBasicCWFixer.Api.Models;

namespace webBasicCWFixer.Api.Endpoints;

public static class AllowlistEndpoints
{
    public static IEndpointRouteBuilder MapAllowlistEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/allowlist", (AllowlistService svc) =>
        {
            return Results.File(svc.PathOnDisk, "application/json");
        });

        app.MapPut("/api/allowlist", async (
            AllowlistDto dto,
            AllowlistService allowlistSvc) =>
        {
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

            var cfg = allowlistSvc.Load();

            var newCfg = cfg with
            {
                Roots = rootsList.ToHashSet(StringComparer.Ordinal),
                RegexFlags = regexFlagsList.ToHashSet(StringComparer.Ordinal),
                SkipIdentifiers = skipList.ToHashSet(StringComparer.Ordinal),
                MaxUploadMb = maxMb
            };

            await allowlistSvc.SaveAsync(newCfg);

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

        return app;
    }
}
