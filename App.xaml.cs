using System.Windows;

namespace OsuCorn;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 先把内置字体装上，XAML 里的 {DynamicResource UiFont} 才有东西可解析。
        Services.Typography.Apply();

        // 后台工具最怕「闪退但什么都没留下」：把所有未处理异常都写进 corn.log。
        DispatcherUnhandledException += (_, args) =>
        {
            Services.AppLog.Error("未处理的界面异常", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Services.AppLog.Error("未处理的线程异常", args.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.AppLog.Error("未观察到的任务异常", args.Exception);
            args.SetObserved();
        };

        Services.AppLog.Info($"osu! corn 启动｜参数：{string.Join(' ', e.Args)}");
        base.OnStartup(e);
    }
}
