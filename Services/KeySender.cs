using System.Runtime.InteropServices;

namespace OsuCorn.Services;

/// <summary>
/// 合成按键（只发游戏自带的快捷键，不碰任何配置文件）。
///
/// 三个约定：
/// 1. 按键只会送到**当前前台窗口**，所以每次发之前必须确认前台是 osu!（由调用方检查）；
/// 2. 一次「按下 → 保持 <see cref="HoldMilliseconds"/> → 抬起」，两次之间隔 <see cref="GapMilliseconds"/>；
/// 3. 中途发现前台变了就立刻停手，并把「已经发了几个」告诉调用方。
/// </summary>
public static class KeySender
{
    private const uint KeyUp = 0x2;

    /// <summary>按下后保持多久。太短可能被游戏合并成一帧里的一次输入。</summary>
    public const int HoldMilliseconds = 20;

    /// <summary>两次按键之间的间隔。</summary>
    public const int GapMilliseconds = 12;

    /// <summary>
    /// Ctrl+Shift 组合键的节奏：先按住修饰键、**停一会儿**让游戏确认按下了修饰键，
    /// 再点按字母，最后才松开修饰键。直接一口气按下松开容易变成「单独按了一个 T」。
    /// </summary>
    public const int ModifierHoldMilliseconds = 80;
    public const int LetterHoldMilliseconds = 60;
    public const int ModifierReleaseDelayMilliseconds = 40;

    public const byte F3 = 0x72;
    public const byte F4 = 0x73;
    private const byte VkControl = 0x11;
    private const byte VkShift = 0x10;
    private const byte VkT = 0x54;
    private const byte VkE = 0x45;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    /// <summary>按一下某个键。</summary>
    public static void Tap(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        Thread.Sleep(HoldMilliseconds);
        keybd_event(virtualKey, 0, KeyUp, UIntPtr.Zero);
    }

    /// <summary>
    /// Ctrl+Shift+某个键（皮肤切换）：**修饰键长按、字母点按**。
    /// 顺序：按下 Ctrl、Shift → 等 80ms（让游戏确认修饰键已按下）→ 按下字母、保持 60ms、抬起
    /// → 等 40ms → 松开 Shift、Ctrl。这样才像人手按，不会退化成「单独按了一个字母」。
    /// </summary>
    public static void TapWithControlShift(byte virtualKey)
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkShift, 0, 0, UIntPtr.Zero);
        Thread.Sleep(ModifierHoldMilliseconds);

        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        Thread.Sleep(LetterHoldMilliseconds);
        keybd_event(virtualKey, 0, KeyUp, UIntPtr.Zero);
        Thread.Sleep(ModifierReleaseDelayMilliseconds);

        keybd_event(VkShift, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
    }

    public static void TapNextSkin() => TapWithControlShift(VkT);

    public static void TapPreviousSkin() => TapWithControlShift(VkE);

    /// <summary>
    /// 连发 N 次同一个键，返回实际发出几次（前台被抢走就提前停）。
    /// <paramref name="stillForeground"/> 每次发之前调用，返回 false 立即停止。
    /// </summary>
    public static int Burst(byte virtualKey, int count, Func<bool> stillForeground)
    {
        int sent = 0;
        for (int index = 0; index < count; index += 1)
        {
            if (!stillForeground())
                return sent;
            Tap(virtualKey);
            sent += 1;
            Thread.Sleep(GapMilliseconds);
        }
        return sent;
    }
}
