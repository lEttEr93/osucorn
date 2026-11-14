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
                AppLog.Info($"状态：{state.Summary}" + (state.KeyCount is { } keys ? $"｜键数 {keys}" : string.Empty));
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
        // 流速要等「加载结束、真正进游戏」之后再调：tosu 的 beatmap.time.live 在加载阶段是 0，
        // 开局后才开始走。留 150ms 余量，免得砸在读条/开场动画上。
        bool inPlay = state.StateName == "play" && !state.Paused && state.LiveTime > 150;

        // 皮肤：选歌界面 + 目标非空 + 名字还没对上
        if (inSelect && !string.IsNullOrWhiteSpace(row.Skin) &&
            !string.Equals(state.SkinName, row.Skin, StringComparison.Ordinal))
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
                                   state.LiveTime > 150 && state.ScrollSpeed is not null => applySpeed(action),
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
            return true; // 这条放弃，别卡住队列
        }

        if (!skinOrder.TryGetValue(current, out int currentIndex))
            currentIndex = 0;

        int total = skinOrder.Count;
        // 两个方向都可用（Ctrl+Shift+T 下一个 / Ctrl+Shift+E 上一个），走环上更近的那一半。
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

        return true;
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

    // ---- 皮肤环形列表 ----

    private readonly Dictionary<string, int> skinOrder = new(StringComparer.Ordinal);

    /// <summary>
    /// 启动时扫一圈皮肤：连按「下一个皮肤」直到回到起点，把途中的名字按顺序记下来，
    /// 结束时正好停在起始皮肤上（不改变玩家当前皮肤）。
    /// 需要游戏在前台，所以由 <see cref="ScanSkinsIfNeeded"/> 在合适的时机调用。
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
        skinOrder[start] = 0;
        int index = 1;
        for (int guard = 0; guard < 60; guard += 1)
        {
            if (!canSendNow())
                return false;
            KeySender.TapNextSkin();
            await Task.Delay(150);
            TosuState read = await tosu.ReadAsync();
            string name = read.SkinName;
            if (name.Length == 0 || string.Equals(name, start, StringComparison.Ordinal))
                break;
            if (skinOrder.ContainsKey(name))
                break; // 重名或提前闭环，停手
            skinOrder[name] = index++;
        }

        AppLog.Info($"扫皮肤完成：{skinOrder.Count} 个 —— {string.Join(" / ", skinOrder.Keys.Take(8))}");
        Notice?.Invoke($"已建立皮肤列表（{skinOrder.Count} 个）");
        return true;
    }

    private readonly record struct PendingAction(ActionKind Kind, string Text, double Value);

    private enum ActionKind
    {
        Skin,
        Speed
    }
}
