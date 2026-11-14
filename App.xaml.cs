using System.Windows;

namespace OsuCorn;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 先把内置字体装上，XAML 里的 {DynamicResource UiFont} 才有东西可解析。
        Services.Typography.Apply();
        Services.AppLog.Info($"osu! corn 启动｜参数：{string.Join(' ', e.Args)}");
        base.OnStartup(e);
    }
}
