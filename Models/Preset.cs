namespace OsuCorn.Models;

/// <summary>
/// 一个预设：某个模式（mania 还要分键数）下希望用哪套皮肤 / 流速。
///
/// 这个工具**不碰任何配置文件**：皮肤靠「下一个 / 上一个皮肤」快捷键走过去，
/// 流速靠 F4 / F3 一格一格加减，全是游戏自带的操作。
/// </summary>
public sealed class Preset
{
    /// <summary>显示名，比如「4K 低流速」「7K 高流速」。</summary>
    public string Name { get; set; } = "新预设";

    /// <summary>osu / taiko / fruits / mania。</summary>
    public string Mode { get; set; } = "mania";

    /// <summary>mania 的键数（1–10）；其它模式写 0。</summary>
    public int Keys { get; set; }

    /// <summary>目标皮肤名，必须和 osu! 里显示的完全一致。</summary>
    public string? Skin { get; set; }

    /// <summary>目标 mania 流速（1–40）。快捷键每次 ±1，所以只会落在整数档上。</summary>
    public double? ScrollSpeed { get; set; }

    /// <summary>备注，界面上显示在名字旁边。</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>模式 + 键数的短标签，给卡片用。</summary>
    public string ModeBadge => Mode == "mania"
        ? $"mania {Keys}K"
        : Mode switch
        {
            "osu" => "osu!std",
            "taiko" => "taiko",
            "fruits" => "catch",
            _ => Mode
        };

    /// <summary>皮肤 / 流速的摘要。</summary>
    public string Summary
    {
        get
        {
            List<string> parts = [];
            if (ScrollSpeed is { } speed)
                parts.Add($"流速 {speed:0.#}");
            if (!string.IsNullOrWhiteSpace(Skin))
                parts.Add($"皮肤 {Skin}");
            return parts.Count == 0 ? "（空预设）" : string.Join("  ·  ", parts);
        }
    }
}

/// <summary>presets.json 的根。</summary>
public sealed class PresetFile
{
    public List<Preset> Presets { get; set; } = [];
}
