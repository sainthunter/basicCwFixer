using System.Text.Json;
using webBasicCWFixer.Analyzer;
using System.Threading.Tasks;
using System.IO;

namespace webBasicCWFixer.Api.Allowlist;

public sealed class AllowlistService
{
    private readonly string _path;

    public AllowlistService(IWebHostEnvironment env)
    {
        // Docker’da /data mount edeceğiz; localde de proje klasöründe durabilir
        var dataDir = Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "allowlist.json");

        if (!File.Exists(_path))
        {
            var defaultCfg = new AllowlistConfig(
                Roots: new HashSet<string>(StringComparer.Ordinal)
                {
                    "Global","Finder","Document","DataStructure","tt_Common","eval",
                    "Calendar","UserProfile","Process","AVM","DataObjectList","cwt_on","Catalog","RegExp","cwt_catalog",
                    "cwt_pcoe","cwt_pcapi","cwt_on_ovr","CwfError","FileWriter","uiOrder","FileReader"
                },
                RegexFlags: new HashSet<string>(StringComparer.Ordinal) { "g", "i", "m", "s", "u", "y", "d" },
                SkipIdentifiers: new HashSet<string>(StringComparer.Ordinal) { "this" },
                MaxUploadMb: 90
            );

            File.WriteAllText(_path, JsonSerializer.Serialize(defaultCfg, new JsonSerializerOptions { WriteIndented = true }));
        }
    }


    public AllowlistConfig Load()
    {
        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<AllowlistConfig>(json) ?? throw new InvalidOperationException("allowlist.json okunamadı");
    }

    public void Save(AllowlistConfig cfg)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task SaveAsync(AllowlistConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(_path, json);
    }
    public string PathOnDisk => _path;


}
