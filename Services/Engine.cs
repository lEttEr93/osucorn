using OsuCorn.Models;

namespace OsuCorn.Services;

/// <summary>
/// 引擎：轮询 tosu → 判断现在该做什么 → **排队** → 等 osu! 回到前台再发键。
///
/// 为什么一定要排队：corn 的窗口被点击时前台就是 corn 自己，这时候发键会打到 corn 上。
/// 所以任何动作都先记进队列，等「前台是 osu! 且 tosu 报告 focused」再执行。
///
/// 两类动作：
///   · 皮肤：选歌界面（selectPlay）切到这一档预设的皮肤；
///   · 流速：进入游玩（play 且未暂停）后，一次性把 F3/F4 发完再读回校验。
/// 两者都会记住「已经做过的目标」，避免同一状态下反复发键。
/// </summary>
public sealed class Engine : IDisposable
{
    private readonly TosuClient tosu;
    private readonly Func<Preset?> activePreset;
    private readonly System.Windows.Threading.DispatcherTimer timer;
    private readonly Queue<PendingAction> pending = new();

    private bool polling;
    private bool gameRunning;
    private string lastStateFingerprint = string.Empty;
    private double lastLiveTime;
    private bool liveClockRunning;
    private bool wasPlaying;
    private string lastSpeedKey = string.Empty;
    private string lastSkinKey = string.Empty;
    private TosuState state = TosuState.Disconnected;

    /// <summary>每次读到状态都会回调（UI 线程）。</summary>
    public event Action<TosuState>? StateUpdated;

    /// <summary>给状态卡第二行用的一句话。</summary>
    public event Action<string>? Notice;

    /// <summary>游戏出现 / 消失。</summary>
    public event Action<bool>? GamePresenceChanged;

    public Engine(TosuClient tosu, Func<Preset?> activePreset)
    {
        this.tosu = tosu;
        this.activePreset = activePreset;
        timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        timer.Tick += async (_, _) => await tickAsync();
    }

    public TosuState State => state;

    public void Start()
    {
        timer.Start();
        _ = tickAsync();
    }

    public void Dispose() => timer.Stop();

    private async Task tickAsync()
    {
        if (polling)
            return;
        polling = true;
        try
        {
            state = await tosu.ReadAsync();
            StateUpdated?.Invoke(state);

            // tosu 的 beatmap.time.live 在选歌界面是**试听进度**（可能是 27 秒），加载阶段还停在上一次的值。
            // 所以「进游戏了」不能只看 state==play，要看 live 有没有真的在往前走：
            //   · 刚进 play 的那一拍只记基准（加载时 live 冻住不动，差值会是 0）
            //   · 下一拍 live 至少前进 100ms，才认为时钟真的在走
            if (state.StateName == "play")
            {
                if (!wasPlaying)
                {
                    lastLiveTime = state.LiveTime;
                    wasPlaying = true;
                    liveClockRunning = false; // 这一拍不加判断，等下一拍对比
                }
                else
                {
                    liveClockRunning = state.LiveTime - lastLiveTime >= 100;
                    lastLiveTime = state.LiveTime;
                }
            }
            else
            {
                wasPlaying = false;
                liveClockRunning = false;
            }

            bool running = state.Connected && state.Client == "lazer";
            if (running != gameRunning)
            {
                gameRunning = running;
                GamePresenceChanged?.Invoke(running);
            }

            collectActions();
            flushPending();

            if (state.Fingerprint != lastStateFingerprint)
            {
                lastStateFingerprint = state.Fingerprint;
                Profile? row = activePreset()?.Match(state.ModeName, state.KeyCount ?? 0);
                AppLog.Info($"状态：{state.Summary}" + (state.KeyCount is { } keys ? $"｜键数 {keys}" : string.Empty)
                            + $"｜live={state.LiveTime:0} 时钟在走={liveClockRunning}"
                            + (row is null ? "｜没有对应行" : $"｜该行皮肤={row.Skin ?? "空"} 流速={row.ScrollSpeedText switch { "" => "空", var text => text }}"));
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("轮询出错", exception);
        }
        finally
        {
            polling = false;
        }
    }

    /// <summary>根据当前状态决定要不要排队新动作。</summary>
    private void collectActions()
    {
        if (!state.Connected || state.Client != "lazer")
            return;

        Preset? preset = activePreset();
        Profile? row = preset?.Match(state.ModeName, state.KeyCount ?? 0);
        if (row is null)
        {
            Notice?.Invoke("这套预设里没有对应模式的行");
            return;
        }

        bool inSelect = state.StateName is "selectPlay" or "menu";
        // 流速要等「加载结束、真正进游戏」之后再调：判据是 live 时钟在往前走（见 tickAsync）。
        bool inPlay = state.StateName == "play" && !state.Paused && liveClockRunning;

        // 皮肤：选歌界面 + 目标非空 + 名字还没对上
        if (inSelect && !string.IsNullOrWhiteSpace(row.Skin) &&
            !string.Equals(state.SkinName, row.Skin, StringComparison.Ordinal) &&
            !blockedSkins.Contains(row.Skin))
        {
            string key = $"{row.Skin}|{state.SkinName}";
            if (key != lastSkinKey && !hasPending(ActionKind.Skin))
            {
                lastSkinKey = key;
                enqueue(ActionKind.Skin, row.Skin, 0);
                Notice?.Invoke($"排队：切皮肤 → {row.Skin}（等 osu! 回到前台）");
            }
        }

        // 流速：游玩中 + 目标非空 + 当前值没到
        if (inPlay && row.ScrollSpeed is { } target)
        {
            double current = state.ScrollSpeed ?? 0;
            if (current > 0 && Math.Abs(current - target) >= 0.5)
            {
                string key = $"{target}|{Math.Round(current, 1)}";
                if (key != lastSpeedKey && !hasPending(ActionKind.Speed))
                {
                    lastSpeedKey = key;
                    enqueue(ActionKind.Speed, string.Empty, target);
                    Notice?.Invoke($"排队：流速 {current:0.0} → {target:0.0}（等 osu! 回到前台）");
                }
            }
        }
    }

    private bool hasPending(ActionKind kind) => pending.Any(action => action.Kind == kind);

    private void enqueue(ActionKind kind, string text, double value)
    {
        pending.Enqueue(new PendingAction(kind, text, value));
        AppLog.Info($"排队 {kind}：{text}{value:0.#}");
    }

    /// <summary>把队列里能做的做掉；做不了就留着等下一拍。</summary>
    private void flushPending()
    {
        if (pending.Count == 0)
            return;
        if (!canSendNow())
        {
            Notice?.Invoke($"待执行 {pending.Count} 项：{whyCannotSend()}");
            return;
        }

        int guard = 0;
        while (pending.Count > 0 && guard++ < 8)
        {
            PendingAction action = pending.Peek();
            bool executed = action.Kind switch
            {
                ActionKind.Skin when state.StateName is "selectPlay" or "menu" => applySkin(action),
                ActionKind.Speed when state.StateName == "play" && !state.Paused &&
                                   liveClockRunning && state.ScrollSpeed is not null => applySpeed(action),
                _ => false
            };

            if (!executed)
            {
                // 状态不对（比如已经开局了）就先留着，别丢掉。
                Notice?.Invoke($"待执行 {pending.Count} 项：状态不合适（{state.StateName}）");
                return;
            }

            pending.Dequeue();
        }
    }

    /// <summary>发不出去的具体原因，写进状态卡与日志。</summary>
    private string whyCannotSend()
    {
        List<string> reasons = [];
        if (!state.Connected) reasons.Add("tosu 未连接");
        if (state.Client != "lazer") reasons.Add($"客户端 {state.Client}");
        if (!state.Focused) reasons.Add("游戏未聚焦");
        if (state.Paused) reasons.Add("游戏暂停中");
        if (!GameWindow.IsOsuForeground()) reasons.Add($"前台是「{GameWindow.ForegroundTitle()}」");
        return reasons.Count == 0 ? "未知原因" : string.Join("，", reasons);
    }

    private bool canSendNow() =>
        state.Connected && state.Client == "lazer" && state.Focused && !state.Paused && GameWindow.IsOsuForeground();

    /// <summary>调试用：不管预设，直接排一个「把流速 +N 格」的动作（N 可正可负）。</summary>
    public void RequestSpeedNudge(int steps)
    {
        double current = state.ScrollSpeed ?? 0;
        if (current <= 0)
        {
            Notice?.Invoke("拿不到当前流速（要先游玩一次）");
            return;
        }

        double target = Math.Clamp(current + steps, 1, 40);
        pending.Enqueue(new PendingAction(ActionKind.Speed, $"nudge{steps}", target));
        AppLog.Info($"调试：排队流速 {current:0.0} → {target:0.0}");
        Notice?.Invoke($"调试：已排队 流速 {current:0.0} → {target:0.0}（{whyCannotSend()}）");
    }

    /// <summary>
    /// 切皮肤：先转一圈把环形列表建出来（只在需要时做一次），
    /// 再按「目标与当前的环形距离」选正/反方向走最短路径，最后读回确认。
    /// </summary>
    private bool applySkin(PendingAction action)
    {
        string target = action.Text;
        string current = state.SkinName;
        if (string.Equals(current, target, StringComparison.Ordinal))
            return true;

        if (!skinOrder.TryGetValue(target, out int targetIndex))
        {
            Notice?.Invoke($"没有这个皮肤：{target}");
            AppLog.Info($"切皮肤失败：列表里没有「{target}」（已知 {skinOrder.Count} 个）");
            blockSkin(target, "不在皮肤列表里");
            return true; // 这条放弃，别卡住队列
        }

        if (!skinOrder.TryGetValue(current, out int currentIndex))
        {
            // 当前皮肤不在表里（玩家自己换了皮肤 / 表过期），重新扫一次再决定。
            AppLog.Info($"当前皮肤「{current}」不在列表里，重扫皮肤表");
            skinOrder.Clear();
            Notice?.Invoke("皮肤表和当前皮肤对不上，下轮重扫");
            return false;
        }

        int total = skinOrder.Count;
        // 两个方向都可用（Ctrl+Shift+T 下一个 / Ctrl+Shift+E 上一个），走环上更近的一半。
        int forward = (targetIndex - currentIndex + total) % total;
        int backward = (currentIndex - targetIndex + total) % total;
        bool useForward = forward <= backward;
        int steps = useForward ? forward : backward;
        AppLog.Info($"切皮肤：{current} → {target}（{(useForward ? "下一个" : "上一个")} × {steps}，共 {total} 个）");
        Notice?.Invoke($"切皮肤 {current} → {target}（{(useForward ? "下一个" : "上一个")} × {steps}）");

        for (int index = 0; index < steps; index += 1)
        {
            if (!canSendNow())
            {
                AppLog.Info($"切皮肤中断：前台变了（已发 {index}/{steps}）");
                return false;
            }
            KeySender.TapNextSkin();
            Thread.Sleep(120);
        }

        // 读回确认：没到就补一次，再不到就认输并**禁止**再试这个目标 —— 否则会一直切不停。
        string? landed = waitForSkinName(target, timeoutMs: 600, expectTarget: true);
        if (landed is null && !string.Equals(landed, target, StringComparison.Ordinal))
        {
            AppLog.Info($"切皮肤没到目标，补一次（当前 {landed ?? "未知"}）");
            if (landed is not null && skinOrder.TryGetValue(landed, out int landedIndex))
            {
                int fixForward = (targetIndex - landedIndex + total) % total;
                int fixBackward = (landedIndex - targetIndex + total) % total;
                bool fixUseForward = fixForward <= fixBackward;
                int fixSteps = fixUseForward ? fixForward : fixBackward;
                for (int index = 0; index < fixSteps && canSendNow(); index += 1)
                {
                    if (fixUseForward)
                        KeySender.TapNextSkin();
                    else
                        KeySender.TapPreviousSkin();
                    Thread.Sleep(120);
                }
                landed = waitForSkinName(target, timeoutMs: 600, expectTarget: true);
            }
        }

        if (string.Equals(landed, target, StringComparison.Ordinal))
        {
            AppLog.Info($"切皮肤完成：{current} → {landed}");
            Notice?.Invoke($"已切到 {target}");
            return true;
        }

        blockSkin(target, $"落了 {landed ?? "未知"} 而不是目标");
        return true;
    }

    /// <summary>轮询读皮肤名，直到它变成目标（或超时）；返回最后一次读到的名字。</summary>
    private string? waitForSkinName(string target, int timeoutMs, bool expectTarget)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = tosu.ReadAsync().GetAwaiter().GetResult().SkinName;
            if (string.Equals(last, target, StringComparison.Ordinal))
                return last;
            Thread.Sleep(120);
        }
        return last;
    }

    /// <summary>这个皮肤目标就到此为止：记下来，别再来回切。</summary>
    private void blockSkin(string target, string reason)
    {
        if (blockedSkins.Add(target))
            AppLog.Info($"放弃切到「{target}」：{reason}（本次运行不再尝试）");
        Notice?.Invoke($"切皮肤放弃：{target}（{reason}）");
    }

    /// <summary>调流速：算出差值，一次性发完，读回校验，差一格再补一把。</summary>
    private bool applySpeed(PendingAction action)
    {
        double target = action.Value;
        double current = state.ScrollSpeed ?? 0;
        if (current <= 0)
        {
            AppLog.Info("调流速跳过：读不到当前流速");
            return false;
        }

        int delta = (int)Math.Round(target - current);
        if (delta == 0)
            return true;
        // 夹在 1–40，别把游戏顶到边界外面
        delta = delta > 0 ? Math.Min(delta, 40 - (int)Math.Round(current)) : Math.Max(delta, 1 - (int)Math.Round(current));

        byte key = delta > 0 ? KeySender.F4 : KeySender.F3;
        int count = Math.Abs(delta);
        AppLog.Info($"调流速：{current:0.0} → {target:0.0}（发 {count} 次 {(delta > 0 ? "F4" : "F3")}，前台可发={canSendNow()}）");
        int sent = KeySender.Burst(key, count, canSendNow);

        Thread.Sleep(180);
        double after = tosu.ReadAsync().GetAwaiter().GetResult().ScrollSpeed ?? current;
        AppLog.Info($"调流速：发了 {sent}/{count}，读回 {after:0.0}");
        if (sent == count && Math.Abs(after - target) >= 0.5)
        {
            int fix = (int)Math.Round(target - after);
            if (fix != 0)
            {
                AppLog.Info($"流速差 {after:0.0}，补发 {Math.Abs(fix)} 次");
                KeySender.Burst(fix > 0 ? KeySender.F4 : KeySender.F3, Math.Abs(fix), canSendNow);
                Thread.Sleep(180);
                after = tosu.ReadAsync().GetAwaiter().GetResult().ScrollSpeed ?? after;
                AppLog.Info($"调流速：补发后 {after:0.0}");
            }
        }

        Notice?.Invoke(sent == count
            ? $"流速 {current:0.0} → {after:0.0}（目标 {target:0.0}）"
            : $"流速只发了 {sent}/{count}，前台被抢走了");
        return true;
    }

    /// <summary>皮肤表里认到几个（0 = 还没建立）。</summary>
    public int SkinCount => skinOrder.Count;

    /// <summary>皮肤表的内容，排查用。</summary>
    public string SkinListText => skinOrder.Count == 0
        ? "未建立"
        : string.Join(" / ", skinOrder.OrderBy(pair => pair.Value).Select(pair => pair.Key));

    /// <summary>清掉皮肤表并解除放弃名单，强制重扫。</summary>
    public void ResetSkins()
    {
        skinOrder.Clear();
        blockedSkins.Clear();
        AppLog.Info("皮肤表已清空，准备重扫");
        Notice?.Invoke("皮肤表已清空，下一轮重扫");
    }

    // ---- 皮肤环形列表 ----

    private readonly Dictionary<string, int> skinOrder = new(StringComparer.Ordinal);
    private readonly HashSet<string> blockedSkins = new(StringComparer.Ordinal);

    /// <summary>
    /// 扫一圈皮肤建立环形列表。
    ///
    /// 关键：皮肤切换是**异步加载**，按下到 tosu 里名字变化有延迟（大皮肤更久）。
    /// 所以每按一次都要**轮询等名字真的变了**再记录；等不到就补按，补几次还不变就放弃本次扫描。
    /// 宁可没有列表，也不要用一张错的表去算步数 —— 那正是「皮肤停不下来」的原因。
    /// </summary>
    public bool skinsScanned => skinOrder.Count > 0;

    public async Task<bool> ScanSkinsIfNeededAsync()
    {
        if (skinOrder.Count > 0 || !canSendNow())
            return false;

        string start = state.SkinName;
        if (start.Length == 0)
            return false;

        AppLog.Info($"扫皮肤：从「{start}」开始");
        Dictionary<string, int> found = new(StringComparer.Ordinal) { [start] = 0 };
        string current = start;

        for (int index = 1; index <= 60; index += 1)
        {
            string? next = null;
            for (int attempt = 0; attempt < 3 && next is null; attempt += 1)
            {
                if (!canSendNow())
                {
                    AppLog.Info("扫皮肤中断：前台变了");
                    return false;
                }

                KeySender.TapNextSkin();
                next = await waitForSkinChangeAsync(current, timeoutMs: 1000);
            }

            if (next is null)
            {
                AppLog.Info($"扫皮肤放弃：在「{current}」上连按 3 次名字都没变");
                return false;
            }

            if (string.Equals(next, start, StringComparison.Ordinal))
                break; // 转完一圈，正好回到起点

            if (found.ContainsKey(next))
            {
                AppLog.Info($"扫皮肤：又见到「{next}」（可能有同名皮肤），按这一圈算");
                break;
            }

            found[next] = index;
            current = next;
        }

        if (found.Count < 2)
        {
            AppLog.Info($"扫皮肤失败：只认到 {found.Count} 个，不采信");
            return false;
        }

        skinOrder.Clear();
        foreach (KeyValuePair<string, int> pair in found)
            skinOrder[pair.Key] = pair.Value;

        AppLog.Info($"扫皮肤完成：{skinOrder.Count} 个 —— {string.Join(" / ", skinOrder.Keys.Take(8))}");
        Notice?.Invoke($"已建立皮肤列表（{skinOrder.Count} 个）");
        return true;
    }

    /// <summary>按一次之后，轮询等 tosu 里的皮肤名变化；超时返回 null。</summary>
    private async Task<string?> waitForSkinChangeAsync(string current, int timeoutMs)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(120);
            TosuState read = await tosu.ReadAsync();
            if (read.SkinName.Length > 0 && !string.Equals(read.SkinName, current, StringComparison.Ordinal))
                return read.SkinName;
        }
        return null;
    }

    private readonly record struct PendingAction(ActionKind Kind, string Text, double Value);

    private enum ActionKind
    {
        Skin,
        Speed
    }
}
