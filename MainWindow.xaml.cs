using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OsuCorn.Models;
using OsuCorn.Services;

namespace OsuCorn;

/// <summary>
/// 界面外壳。
///
/// 模型：**多套预设，其中一套「启用中」**；每套预设里有 5 行模式配置
/// （mania 4K / 7K、osu!std、taiko、catch）。这些行不是手动启用的 ——
/// corn 用 tosu 识别当前模式与键数，自动挑对应的一行去做事。
///
/// 这一版只做界面与数据：读 / 写 presets.json、切启用中的预设、编辑单元格。
/// 引擎（tosu 读状态 / 快捷键发送 / 键数解析）还没接。
/// </summary>
public partial class MainWindow : Window
{
    private readonly PresetStore store = new();
    private readonly TosuClient tosu = new();
    private readonly ObservableCollection<Preset> presets = [];
    private readonly ObservableCollection<Profile> rows = [];
    private PresetFile file = new();
    private bool loading; // 初始化 / 切换预设期间不要触发保存
    private Engine? engine;
    private TrayIcon? tray;
    private bool firstPresenceSeen;

    public MainWindow()
    {
        InitializeComponent();

        file = store.Load();
        foreach (Preset preset in file.Presets)
            presets.Add(preset);

        PresetChips.ItemsSource = presets;
        ProfileList.ItemsSource = rows;
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";

        AppLog.LineAdded += line => Dispatcher.BeginInvoke(() => LogText.Text = line);
        LogText.Text = $"预设文件：{store.FilePath}";

        selectActivePreset(file.ActivePreset);

        Loaded += (_, _) => startEngine();

        // 开发用：--snapshot=<png> 渲染一张就走。
        if (environmentSnapshotPath() is { } path)
        {
            Loaded += async (_, _) =>
            {
                await Task.Delay(700);
                capture(path);
                Close();
            };
        }
    }

    private void startEngine()
    {
        tray = new TrayIcon(this);

        engine = new Engine(tosu, () => activePreset);
        engine.StateUpdated += state => Dispatcher.BeginInvoke(() => applyState(state));
        engine.Notice += message => Dispatcher.BeginInvoke(() => DetailText.Text = message);
        engine.GamePresenceChanged += running => Dispatcher.BeginInvoke(() =>
        {
            // 调试阶段先**不隐藏**窗口：先确认「切皮肤 / 调流速」都对，再开自动收起。
            if (running && !firstPresenceSeen)
                AppLog.Info("检测到 osu! 在运行（调试阶段不隐藏窗口）");
            firstPresenceSeen = running;

            if (!running && !IsVisible)
            {
                Show();
                Activate();
            }
        });
        engine.Start();

        // 皮肤列表要游戏在前台才能扫，所以等第一拍有状态再试（不行就下一拍继续试）。
        _ = scanSkinsWhenReadyAsync();
    }

    private async Task scanSkinsWhenReadyAsync()
    {
        for (int attempt = 0; attempt < 600 && engine is not null && !engine.skinsScanned; attempt += 1)
        {
            await Task.Delay(500);
            if (engine is null || engine.skinsScanned)
                return;
            if (await engine.ScanSkinsIfNeededAsync())
                return;
        }
    }

    private void applyState(TosuState state)
    {
        ConnectionText.Text = state.Connected ? "tosu 已连接" : "tosu 未连接";
        StateText.Text = state.Summary;

        Profile? active = activePreset?.Match(state.ModeName.Length == 0 ? "?" : state.ModeName, state.KeyCount ?? 0);
        if (state.Connected)
        {
            string row = active is null ? "这套预设里没有对应模式的行" : $"{active.Label}：皮肤 {describe(active.Skin)}，流速 {describe(active.ScrollSpeedText)}";
            DetailText.Text = $"会自动套用 → {row}｜前台：{(GameWindow.IsOsuForeground() ? "osu!" : GameWindow.ForegroundTitle())}";
        }
        else
        {
            DetailText.Text = $"等待 {tosu.Endpoint}（tosu 没起来时这里会一直是未连接）";
        }
        updateSkinTableText();
    }

    /// <summary>当前启用中的预设。</summary>
    private Preset? activePreset => presets.FirstOrDefault(preset => preset.Name == file.ActivePreset);

    /// <summary>按名字切换启用中的预设：更新 chips、配置表、文件。</summary>
    private void selectActivePreset(string name)
    {
        loading = true;
        try
        {
            if (presets.All(preset => preset.Name != name) && presets.Count > 0)
                name = presets[0].Name;
            file.ActivePreset = name;

            Preset? preset = activePreset;
            rows.Clear();
            if (preset is not null)
            {
                foreach (Profile profile in preset.Profiles)
                    rows.Add(profile);
            }

            ProfilesHeader.Text = preset is null
                ? "配置"
                : $"「{preset.Name}」的配置（自动匹配当前模式，不用手动选）";
        }
        finally
        {
            loading = false;
        }
    }

    private void PresetChip_Checked(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not FrameworkElement { Tag: Preset preset })
            return;
        if (file.ActivePreset == preset.Name)
            return;

        selectActivePreset(preset.Name);
        store.Save(file);
        AppLog.Info($"启用预设《{preset.Name}》");
        LogText.Text = $"已启用《{preset.Name}》";
    }

    /// <summary>新增一套预设：5 行全空，接上引擎后按 tosu 识别的模式自动填。</summary>
    private void AddPresetButton_Click(object sender, RoutedEventArgs e)
    {
        int index = 1;
        while (presets.Any(item => item.Name == $"预设 {index}"))
            index += 1;

        Preset preset = Preset.CreateDefault($"预设 {index}");
        presets.Add(preset);
        selectActivePreset(preset.Name);
        store.Save(file);
        AppLog.Info($"已添加预设《{preset.Name}》（5 行空配置）");
        LogText.Text = $"已添加《{preset.Name}》并启用";

        // ItemsControl 里的 RadioButton 不会自己跟着重建，刷新一次让新 chip 显示为选中。
        PresetChips.ItemsSource = null;
        PresetChips.ItemsSource = presets;
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        Preset? preset = activePreset;
        if (preset is null)
            return;
        if (presets.Count <= 1)
        {
            MessageBox.Show("至少要留一套预设。", "osu! corn", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult answer = MessageBox.Show(
            $"删除预设「{preset.Name}」？",
            "osu! corn",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        int removed = presets.IndexOf(preset);
        presets.Remove(preset);
        selectActivePreset(presets[Math.Min(removed, presets.Count - 1)].Name);
        store.Save(file);
        AppLog.Info($"已删除预设《{preset.Name}》");
        LogText.Text = $"已删除《{preset.Name}》，剩 {presets.Count} 套";

        PresetChips.ItemsSource = null;
        PresetChips.ItemsSource = presets;
    }

    /// <summary>mania 行的键数下拉框改完：落盘并把标签刷一下。</summary>
    private void ProfileKeys_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || sender is not FrameworkElement { Tag: Profile profile })
            return;

        store.Save(file);
        AppLog.Info($"{profile.Label}：皮肤={describe(profile.Skin)}，流速={profile.ScrollSpeedText}");
        LogText.Text = $"{profile.Label} 已保存";
    }

    /// <summary>「取当前」：把这一行填成 osu! 现在的皮肤 / 流速（皮肤名通常很长，手打不现实）。</summary>
    private void UseCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Profile profile })
            return;

        TosuState state = engine?.State ?? TosuState.Disconnected;
        if (!state.Connected)
        {
            LogText.Text = "tosu 未连接，取不到当前值";
            return;
        }

        List<string> filled = [];
        if (state.SkinName.Length > 0)
        {
            profile.Skin = state.SkinName;
            filled.Add($"皮肤 {state.SkinName}");
        }

        if (profile.SupportsScrollSpeed && state.ScrollSpeed is > 0 and { } speed)
        {
            profile.ScrollSpeed = speed;
            filled.Add($"流速 {speed:0.#}");
        }

        store.Save(file);
        refreshRows();
        AppLog.Info($"{profile.Label} 取当前：{string.Join("，", filled)}");
        LogText.Text = filled.Count == 0
            ? "当前没有可取的皮肤 / 流速"
            : $"{profile.Label} 已填：{string.Join("，", filled)}";
    }

    /// <summary>重建配置表（Profile 没有 INotifyPropertyChanged，改完值要靠重建立即显示）。</summary>
    private void refreshRows()
    {
        loading = true;
        try
        {
            Preset? preset = activePreset;
            rows.Clear();
            if (preset is null)
                return;
            foreach (Profile profile in preset.Profiles)
                rows.Add(profile);
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>编辑单元格（皮肤 / 流速）后落盘。</summary>
    private void ProfileField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (loading || sender is not FrameworkElement { Tag: Profile profile })
            return;

        store.Save(file);
        AppLog.Info($"{profile.Label}：皮肤={describe(profile.Skin)}，流速={profile.ScrollSpeedText}");
        LogText.Text = $"{profile.Label} 已保存";
    }

    private static string describe(string? text) => string.IsNullOrWhiteSpace(text) ? "不动" : text;

    /// <summary>调试图：不管预设，排队一次「流速 +1」，用来确认合成按键真的能到游戏。</summary>
    private void NudgeButton_Click(object sender, RoutedEventArgs e) => engine?.RequestSpeedNudge(1);

    /// <summary>底栏显示皮肤表建到第几步了。</summary>
    private void updateSkinTableText()
    {
        int count = engine?.SkinCount ?? 0;
        SkinTableText.Text = count == 0 ? "皮肤表：未建立" : $"皮肤表：{count} 个";
        if (count > 0)
            SkinTableText.ToolTip = engine!.SkinListText;
    }

    /// <summary>清掉皮肤表重扫（玩家自己换过皮肤、或切不到位时用）。</summary>
    private void RescanButton_Click(object sender, RoutedEventArgs e)
    {
        engine?.ResetSkins();
        updateSkinTableText();
        LogText.Text = "皮肤表已清空，下一轮重扫（需要游戏在前台）";
        _ = scanSkinsWhenReadyAsync();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        engine?.Dispose();
        tray?.Dispose();
        tray = null;
        engine = null;
        base.OnClosed(e);
    }

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
