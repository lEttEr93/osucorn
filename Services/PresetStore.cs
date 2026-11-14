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

    /// <summary>读整份配置；文件不存在或坏掉时给一套默认的「默认」预设。</summary>
    public PresetFile Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                PresetFile seed = DefaultFile();
                Save(seed);
                AppLog.Info($"预设：没有 {path}，已写一份示例");
                return seed;
            }

            PresetFile file = JsonSerializer.Deserialize<PresetFile>(File.ReadAllText(path), Options) ?? DefaultFile();
            if (file.Presets.Count == 0)
                file = DefaultFile();
            normalize(file);
            return file;
        }
        catch (Exception exception)
        {
            AppLog.Error("预设读取失败", exception);
            return DefaultFile();
        }
    }

    public void Save(PresetFile file)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
        }
        catch (Exception exception)
        {
            AppLog.Error("预设保存失败", exception);
        }
    }

    /// <summary>补齐每一套预设里缺的模式行（手改文件删漏了一行也能兜住）。</summary>
    private static void normalize(PresetFile file)
    {
        foreach (Preset preset in file.Presets)
        {
            preset.Profiles ??= [];
            foreach (Profile row in Preset.CreateDefault(preset.Name).Profiles)
            {
                bool exists = preset.Profiles.Any(profile =>
                    profile.Mode == row.Mode && (row.Mode != "mania" || profile.Keys == row.Keys));
                if (!exists)
                    preset.Profiles.Add(row);
            }
        }

        if (file.Presets.All(preset => preset.Name != file.ActivePreset))
            file.ActivePreset = file.Presets[0].Name;
    }

    private static PresetFile DefaultFile() => new()
    {
        ActivePreset = "默认",
        Presets = [Preset.CreateDefault("默认")]
    };
}
