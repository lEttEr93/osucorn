using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OsuCorn.Models;
using OsuCorn.Services;

namespace OsuCorn;

/// <summary>
/// 界面外壳：先只有「看」的部分 —— 预设列表、当前状态、日志。
///
/// 引擎（tosu 读状态 / 快捷键发送 / 键数解析）是下一步的事；这一版点「应用」只会在日志里
/// 留一条记录，用来确认界面和数据结构没问题。
/// </summary>
public partial class MainWindow : Window
{
    private readonly PresetStore store = new();
    private List<Preset> presets = [];

    public MainWindow()
    {
        InitializeComponent();

        presets = store.Load();
        PresetList.ItemsSource = presets;
        PresetCountText.Text = $"{presets.Count} 个";
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";

        AppLog.LineAdded += line => Dispatcher.BeginInvoke(() => LogText.Text = line);
        LogText.Text = $"预设文件：{store.FilePath}";

        // 开发用：--snapshot=<png> 渲染一张就走，方便确认配色/排版没跑偏。
        if (environmentSnapshotPath() is { } path)
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(300);
                capture(path);
                Close();
            };
        }
    }

    private void ApplyPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Preset preset })
            return;

        // 占位：真正的应用逻辑（切皮肤 / 调流速）在引擎里，下一步接上。
        AppLog.Info($"应用预设《{preset.Name}》（{preset.ModeBadge}）：{preset.Summary} —— 引擎未接入，仅记录");
        StateText.Text = $"待应用：{preset.Name}";
        DetailText.Text = preset.Summary;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string? environmentSnapshotPath()
    {
        foreach (string argument in Environment.GetCommandLineArgs())
            if (argument.StartsWith("--snapshot=", StringComparison.OrdinalIgnoreCase))
                return argument["--snapshot=".Length..].Trim('"');
        return null;
    }

    /// <summary>把窗口内容渲染成 PNG（2 倍像素，便于看清字）。</summary>
    private void capture(string path)
    {
        try
        {
            UpdateLayout();
            double scale = 2;
            RenderTargetBitmap bitmap = new(
                (int)(ActualWidth * scale), (int)(ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(this);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(path);
            encoder.Save(stream);
            AppLog.Info($"截图已保存：{path}（{bitmap.PixelWidth}×{bitmap.PixelHeight}）");
        }
        catch (Exception exception)
        {
            AppLog.Error("截图失败", exception);
        }
    }
}
