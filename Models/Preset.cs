namespace OsuCorn.Models;

using System.Text.Json.Serialization;

/// <summary>mania 键数选项：0 表示「其它键数」兜底行。</summary>
public sealed record KeyOption(int Keys, string Label);

/// <summary>
/// 一套预设里的一行：某个模式（mania 还要分键数）要用什么皮肤 / 流速。
/// 这些行**不需要手动启用** —— corn 用 tosu 识别当前模式与键数，自动挑对应的那一行。
/// </summary>
public sealed class Profile
{
    /// <summary>osu!mania 常见的键数；0 = 「其它键数」兜底。</summary>
    public static readonly IReadOnlyList<KeyOption> AllKeyOptions =
    [
        new(4, "4K"), new(7, "7K"), new(1, "1K"), new(2, "2K"), new(3, "3K"), new(5, "5K"),
        new(6, "6K"), new(8, "8K"), new(9, "9K"), new(10, "10K"),
        new(12, "12K"), new(14, "14K"), new(16, "16K"), new(18, "18K"), new(20, "20K"),
        new(0, "其它键数")
    ];

    /// <summary>osu / taiko / fruits / mania。</summary>
    public string Mode { get; set; } = "mania";

    /// <summary>mania 的键数（0 = 其它键数兜底）；其它模式写 0。</summary>
    public int Keys { get; set; }

    /// <summary>目标皮肤名，必须和 osu! 里显示的完全一致；留空 = 这行不动皮肤。</summary>
    public string? Skin { get; set; }

    /// <summary>目标 mania 流速（1–40）；留空 = 这行不动流速。快捷键每次 ±1，所以只落在整数档。</summary>
    public double? ScrollSpeed { get; set; }

    /// <summary>行标签，比如「mania 4K」「osu!std」。</summary>
    [JsonIgnore]
    public string Label => Mode == "mania"
        ? $"mania {(Keys == 0 ? "其它" : $"{Keys}K")}"
        : Mode switch
        {
            "osu" => "osu!std",
            "taiko" => "taiko",
            "fruits" => "catch",
            _ => Mode
        };

    /// <summary>是不是 mania 行（决定键数下拉框与流速框是否可用）。</summary>
    [JsonIgnore]
    public bool IsMania => Mode == "mania";

    /// <summary>这一行管不管流速（只有 mania 有）。</summary>
    [JsonIgnore]
    public bool SupportsScrollSpeed => Mode == "mania";

    /// <summary>下拉框的选项来源（供 XAML 绑定）。</summary>
    [JsonIgnore]
    public IReadOnlyList<KeyOption> KeyOptions => AllKeyOptions;

    /// <summary>键数下拉框绑这个（0 显示成「其它键数」）。</summary>
    [JsonIgnore]
    public int KeysForCombo
    {
        get => Keys;
        set => Keys = value;
    }

    /// <summary>流速输入框里显示的文本。</summary>
    [JsonIgnore]
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

    /// <summary>建一套默认预设：mania 给 4K / 7K / 其它键数，另外三个模式各一行。</summary>
    public static Preset CreateDefault(string name) => new()
    {
        Name = name,
        Profiles =
        [
            new Profile { Mode = "mania", Keys = 4 },
            new Profile { Mode = "mania", Keys = 7 },
            new Profile { Mode = "mania", Keys = 0 },
            new Profile { Mode = "osu", Keys = 0 },
            new Profile { Mode = "taiko", Keys = 0 },
            new Profile { Mode = "fruits", Keys = 0 }
        ]
    };

    /// <summary>
    /// 按模式与键数找对应行：
    /// mania 先精确匹配键数，没有就用「其它键数」（Keys == 0）那行，再没有就用第一条 mania 行。
    /// </summary>
    public Profile? Match(string mode, int keys)
    {
        if (mode == "mania")
        {
            Profile? exact = Profiles.FirstOrDefault(profile => profile.Mode == "mania" && profile.Keys == keys && keys > 0);
            if (exact is not null)
                return exact;

            Profile? fallback = Profiles.FirstOrDefault(profile => profile.Mode == "mania" && profile.Keys == 0);
            if (fallback is not null)
                return fallback;

            return Profiles.FirstOrDefault(profile => profile.Mode == "mania");
        }

        return Profiles.FirstOrDefault(profile => profile.Mode == mode);
    }
}

/// <summary>presets.json 的根。</summary>
public sealed class PresetFile
{
    /// <summary>启用中的预设名。</summary>
    public string ActivePreset { get; set; } = "默认";

    public List<Preset> Presets { get; set; } = [];
}
