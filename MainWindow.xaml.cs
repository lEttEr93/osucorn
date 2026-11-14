using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OsuCorn.Models;
using OsuCorn.Services;

namespace OsuCorn;

/// <summary>
/// 界面外壳：预设列表 + 当前状态 + 日志。
///
/// 预设的新增 / 删除在这里完成并立刻落盘（presets.json 是唯一事实来源）；
/// 「应用」按钮暂时只写日志 —— 引擎（tosu 读状态 / 快捷键发送 / 键数解析）还没接。
/// </summary>
public partial class MainWindow : Window
{
    private readonly PresetStore store = new();
    private readonly ObservableCollection<Preset> presets = [];

    public MainWindow()
    {
        InitializeComponent();

        foreach (Preset preset in store.Load())
            presets.Add(preset);
        PresetList.ItemsSource = presets;
        updateCount();
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

    private void updateCount() => PresetCountText.Text = $"{presets.Count} 个";

    private void savePresets() => store.Save(presets);

    private void ApplyPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Preset preset })
            return;

        AppLog.Info($"应用预设《{preset.Name}》（{preset.ModeBadge}）：{preset.Summary} —— 引擎未接入，仅记录");
        StateText.Text = $"待应用：{preset.Name}";
        DetailText.Text = preset.Summary;
    }

    /// <summary>新增一个空预设：名字自动取「新预设 N」，其余留空，先落盘，细节改 presets.json。</summary>
    private void AddPresetButton_Click(object sender, RoutedEventArgs e)
    {
        int index = 1;
        while (presets.Any(item => item.Name == $"新预设 {index}"))
            index += 1;

        Preset preset = new()
        {
            Name = $"新预设 {index}",
            Mode = "mania",
            Keys = 4,
            Note = "皮肤 / 流速留空 = 这项不动"
        };
        presets.Add(preset);
        savePresets();
        updateCount();
        AppLog.Info($"已添加预设《{preset.Name}》—— 具体皮肤 / 流速先手动改 presets.json");
        LogText.Text = $"已添加《{preset.Name}》（{store.FilePath}）";
    }

    /// <summary>删除一个预设：先确认，再从列表和文件里一起去掉。</summary>
    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Preset preset })
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"删除预设「{preset.Name}」？",
            "osu! corn",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        presets.Remove(preset);
        savePresets();
        updateCount();
        AppLog.Info($"已删除预设《{preset.Name}》");
        LogText.Text = $"已删除《{preset.Name}》，剩 {presets.Count} 个";
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
