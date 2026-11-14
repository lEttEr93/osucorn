namespace OsuCorn.Models;

/// <summary>
/// 一套预设里的一行：某个模式（mania 还要分键数）要用什么皮肤 / 流速。
/// 这些行**不需要手动启用** —— corn 用 tosu 识别当前模式与键数，自动挑对应的那一行。
/// </summary>
public sealed class Profile
{
    /// <summary>osu / taiko / fruits / mania。</summary>
    public string Mode { get; set; } = "mania";

    /// <summary>mania 的键数（1–10）；其它模式写 0。</summary>
    public int Keys { get; set; }

    /// <summary>目标皮肤名，必须和 osu! 里显示的完全一致；留空 = 这行不动皮肤。</summary>
    public string? Skin { get; set; }

    /// <summary>目标 mania 流速（1–40）；留空 = 这行不动流速。快捷键每次 ±1，所以只落在整数档。</summary>
    public double? ScrollSpeed { get; set; }

    /// <summary>行标签，比如「mania 4K」「osu!std」。</summary>
    public string Label => Mode == "mania"
        ? $"mania {Keys}K"
        : Mode switch
        {
            "osu" => "osu!std",
            "taiko" => "taiko",
            "fruits" => "catch",
            _ => Mode
        };

    /// <summary>这一行管不管流速（只有 mania 有）。</summary>
    public bool SupportsScrollSpeed => Mode == "mania";

    /// <summary>流速输入框里显示的文本。</summary>
    public string ScrollSpeedText
    {
        get => ScrollSpeed is { } value ? value.ToString("0.#") : string.Empty;
        set
        {
            string text = value.Trim();
            ScrollSpeed = double.TryParse(text, out double parsed) ? Math.Clamp(parsed, 1, 40) : null;
        }
    }
}

/// <summary>
/// 一套预设：名字 + 各模式的行。程序里可以有多套，其中一套是「启用中」，
/// corn 只按启用中那套工作。
/// </summary>
public sealed class Preset
{
    public string Name { get; set; } = "默认";

    public List<Profile> Profiles { get; set; } = [];

    /// <summary>建一套默认的「5 行」：mania 4K / 7K + 另外三个模式。</summary>
    public static Preset CreateDefault(string name) => new()
    {
        Name = name,
        Profiles =
        [
            new Profile { Mode = "mania", Keys = 4 },
            new Profile { Mode = "mania", Keys = 7 },
            new Profile { Mode = "osu", Keys = 0 },
            new Profile { Mode = "taiko", Keys = 0 },
            new Profile { Mode = "fruits", Keys = 0 }
        ]
    };

    /// <summary>按模式与键数找对应行；mania 找不到该键数时退回该模式的第一行。</summary>
    public Profile? Match(string mode, int keys)
    {
        Profile? exact = Profiles.FirstOrDefault(profile =>
            profile.Mode == mode && (mode != "mania" || profile.Keys == keys));
        return exact ?? Profiles.FirstOrDefault(profile => profile.Mode == mode);
    }
}

/// <summary>presets.json 的根。</summary>
public sealed class PresetFile
{
    /// <summary>启用中的预设名。</summary>
    public string ActivePreset { get; set; } = "默认";

    public List<Preset> Presets { get; set; } = [];
}
