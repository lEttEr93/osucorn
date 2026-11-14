using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace OsuCorn.Services;

/// <summary>
/// 托盘图标。corn 在游戏出现后会把自己藏起来，没有托盘图标就等于「打开后消失」，
/// 所以双击图标 = 打开配置，右键 = 退出。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Window window;

    public TrayIcon(Window window)
    {
        this.window = window;

        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("打开配置", null, (_, _) => showWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            icon.Visible = false;
            Application.Current.Shutdown();
        });

        Icon? applicationIcon = null;
        try
        {
            if (Environment.ProcessPath is { } path)
                applicationIcon = Icon.ExtractAssociatedIcon(path);
        }
        catch
        {
            // 拿不到 exe 图标就退化成系统默认图标。
        }

        icon = new Forms.NotifyIcon
        {
            Text = "osu! corn",
            Visible = true,
            Icon = applicationIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => showWindow();
        AppLog.Info("托盘图标已创建");
    }

    private void showWindow()
    {
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
