using System.IO;
using System.Text.Json;
using OsuCorn.Models;

namespace OsuCorn.Services;

/// <summary>
/// presets.json 的读写。文件跟着 exe 走（便携），第一次运行没有就写一份示例。
/// 手改这个文件也行 —— 界面只是它的一个编辑器。
/// </summary>
public sealed class PresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string path = System.IO.Path.Combine(AppContext.BaseDirectory, "presets.json");

    public string FilePath => path;

    public List<Preset> Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                List<Preset> seed = defaultPresets();
                Save(seed);
                AppLog.Info($"预设：没有 {path}，已写一份示例");
                return seed;
            }

            PresetFile? file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path), Options);
            return file?.Presets ?? [];
        }
        catch (Exception exception)
        {
            AppLog.Error("预设读取失败", exception);
            return [];
        }
    }

    public void Save(IEnumerable<Preset> presets)
    {
        try
        {
            PresetFile file = new() { Presets = [.. presets] };
            File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
        }
        catch (Exception exception)
        {
            AppLog.Error("预设保存失败", exception);
        }
    }

    /// <summary>第一次运行的示例：覆盖四个模式，mania 再分 4K / 7K。</summary>
    private static List<Preset> defaultPresets() =>
    [
        new() { Name = "4K 惯用", Mode = "mania", Keys = 4, ScrollSpeed = 30, Note = "皮肤留空就先不动" },
        new() { Name = "7K 惯用", Mode = "mania", Keys = 7, ScrollSpeed = 34, Note = "皮肤留空就先不动" },
        new() { Name = "std 干净界面", Mode = "osu", Keys = 0, Note = "只切皮肤" },
        new() { Name = "taiko", Mode = "taiko", Keys = 0 },
        new() { Name = "catch", Mode = "fruits", Keys = 0 }
    ];
}
