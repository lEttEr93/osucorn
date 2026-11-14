using System.Net.Http;
using System.Text.Json;

namespace OsuCorn.Services;

/// <summary>
/// 从 tosu 读到的当前状态（只读，不做任何修改）。
/// 字段都允许缺：tosu 版本之间字段名略有出入，缺了就当作未知。
/// </summary>
public sealed record TosuState(
    bool Connected,
    string Client,
    bool Focused,
    bool Paused,
    string StateName,
    string ModeName,
    double? ScrollSpeed,
    string SkinName,
    int BeatmapId,
    int BeatmapSetId,
    int? KeyCount,
    string KeyCountSource,
    string Fingerprint)
{
    public static readonly TosuState Disconnected = new(
        false, string.Empty, false, false, string.Empty, string.Empty,
        null, string.Empty, 0, 0, null, string.Empty, string.Empty);

    /// <summary>能不能动手（发快捷键）的前置条件：lazer、游戏在前台、没暂停。</summary>
    public bool ReadyToSend => Connected && Client == "lazer" && Focused && !Paused;

    /// <summary>状态卡的摘要行。</summary>
    public string Summary
    {
        get
        {
            if (!Connected)
                return "tosu 未连接";
            if (Client != "lazer")
                return $"客户端：{Client}（只支持 lazer）";

            List<string> parts = [StateName.Length > 0 ? StateName : "?"];
            if (ModeName.Length > 0)
                parts.Add(KeyCount is { } keys ? $"{ModeName} {keys}K" : ModeName);
            if (ScrollSpeed is { } speed)
                parts.Add($"流速 {speed:0.0}");
            if (SkinName.Length > 0)
                parts.Add($"皮肤 {Shorten(SkinName, 22)}");
            parts.Add(Focused ? "前台" : "后台");
            if (Paused)
                parts.Add("暂停");
            return string.Join("  ·  ", parts);
        }
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>
/// tosu 客户端：GET <c>http://127.0.0.1:24050/json/v2</c>，只读。
/// 端口可在以后做成配置；现在沿用 tosu 默认端口。
/// </summary>
public sealed class TosuClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly string endpoint;
    private string loggedShape = string.Empty;

    public TosuClient(string? url = null)
    {
        endpoint = (url ?? "http://127.0.0.1:24050/json/v2").TrimEnd('/');
        if (!endpoint.EndsWith("/json/v2", StringComparison.OrdinalIgnoreCase))
            endpoint += "/json/v2";
    }

    public string Endpoint => endpoint;

    public async Task<TosuState> ReadAsync(CancellationToken token = default)
    {
        try
        {
            string json = await Http.GetStringAsync(endpoint, token).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            return parse(document.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or TaskCanceledException
                                              or JsonException)
        {
            return TosuState.Disconnected;
        }
    }

    private TosuState parse(JsonElement root)
    {
        string client = text(root, "client");
        bool focused = flag(root, "game", "focused");
        bool paused = flag(root, "game", "paused");
        string state = text(root, "state", "name");
        string mode = text(root, "settings", "mode", "name");
        double? speed = number(root, "settings", "mania", "scrollSpeed");
        string skin = text(root, "settings", "skin", "name");
        int beatmapId = (int)(number(root, "beatmap", "id") ?? 0);
        int set = (int)(number(root, "beatmap", "set") ?? number(root, "beatmap", "setId") ?? 0);

        // 键数：tosu 的值可能出现在几个位置，按可能性依次试；都拿不到就留给上层去读谱面。
        (int? keys, string source) = keyCount(root, mode);

        // 首次连上时把实际字段形状记进日志 —— 后面调字段名有据可查。
        string shape = describeShape(root);
        if (shape != loggedShape)
        {
            loggedShape = shape;
            AppLog.Info($"tosu 字段：{shape}");
        }

        return new TosuState(true, client, focused, paused, state, mode, speed, skin, beatmapId, set, keys, source,
            $"{state}|{mode}|{keys}|{beatmapId}|{skin}|{speed}|{focused}|{paused}");
    }

    private static (int? Keys, string Source) keyCount(JsonElement root, string mode)
    {
        if (!string.Equals(mode, "mania", StringComparison.OrdinalIgnoreCase))
            return (null, string.Empty);

        // 注意：tosu 的 stats.cs 是个对象 { original, converted }，不是数字。
        // std 谱面在 mania 模式下会被转换，**converted 才是实际键数**；原生 mania 谱两者相同。
        foreach ((string[] path, string name) in new (string[], string)[]
                 {
                     (["beatmap", "stats", "cs", "converted"], "stats.cs.converted"),
                     (["beatmap", "stats", "cs", "original"], "stats.cs.original"),
                     (["beatmap", "stats", "cs"], "stats.cs"),
                     (["beatmap", "cs", "converted"], "beatmap.cs.converted"),
                     (["beatmap", "cs"], "beatmap.cs"),
                     (["beatmap", "stats", "keys"], "beatmap.stats.keys"),
                     (["beatmap", "keys"], "beatmap.keys")
                 })
        {
            if (number(root, path) is { } value && value is >= 1 and <= 18)
                return ((int)Math.Round(value), name);
        }

        return (null, "未找到");
    }

    /// <summary>把 payload 里出现过的关键节点与字段名摊平，便于对照 tosu 版本。</summary>
    private static string describeShape(JsonElement root)
    {
        List<string> parts = [];
        foreach (string node in new[] { "beatmap", "state", "settings" })
        {
            if (!root.TryGetProperty(node, out JsonElement element) || element.ValueKind != JsonValueKind.Object)
                continue;
            List<string> names = [.. element.EnumerateObject().Select(property => property.Name).Take(14)];
            parts.Add($"{node}[{string.Join(',', names)}]");
        }

        if (root.TryGetProperty("beatmap", out JsonElement beatmap) &&
            beatmap.TryGetProperty("stats", out JsonElement stats) && stats.ValueKind == JsonValueKind.Object)
            parts.Add($"beatmap.stats[{string.Join(',', stats.EnumerateObject().Select(property => property.Name))}]");

        return string.Join("  ", parts);
    }

    private static string text(JsonElement root, params string[] path)
    {
        return tryGet(root, path) is { ValueKind: JsonValueKind.String } element ? element.GetString() ?? string.Empty : string.Empty;
    }

    private static bool flag(JsonElement root, params string[] path)
    {
        return tryGet(root, path) is { } element &&
               element.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               element.GetBoolean();
    }

    private static double? number(JsonElement root, params string[] path)
    {
        JsonElement element = tryGet(root, path);
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), out double parsed) => parsed,
            _ => null
        };
    }

    private static JsonElement tryGet(JsonElement root, string[] path)
    {
        JsonElement current = root;
        foreach (string name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return default;
        }
        return current;
    }
}
