using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OsuCorn.Services;

/// <summary>
/// osu! 窗口与前台判定。合成按键只会送到**当前前台窗口**，
/// 所以每次发键前都必须确认前台就是游戏，否则按键会落到别的窗口上。
/// </summary>
public static class GameWindow
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);

    /// <summary>找到 osu! 的主窗口（没有就返回 Zero）。</summary>
    public static IntPtr FindOsuWindow()
    {
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || process.HasExited)
                    continue;
                string title = process.ProcessName;
                if (title.Equals("osu!", StringComparison.OrdinalIgnoreCase) ||
                    title.Equals("osu", StringComparison.OrdinalIgnoreCase))
                    return process.MainWindowHandle;
            }
            catch
            {
                // 进程在枚举过程中退出是正常的。
            }
            finally
            {
                process.Dispose();
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>当前前台窗口是不是 osu!。</summary>
    public static bool IsOsuForeground()
    {
        IntPtr osu = FindOsuWindow();
        return osu != IntPtr.Zero && GetForegroundWindow() == osu;
    }

    /// <summary>前台窗口的标题（日志用，排查「键发到哪去了」）。</summary>
    public static string ForegroundTitle()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return string.Empty;
        int length = GetWindowTextLength(foreground);
        if (length <= 0)
            return string.Empty;
        System.Text.StringBuilder builder = new(length + 1);
        _ = GetWindowText(foreground, builder, builder.Capacity);
        return builder.ToString();
    }
}
