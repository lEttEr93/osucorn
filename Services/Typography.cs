using System.Windows;
using System.Windows.Media;

namespace OsuCorn.Services;

/// <summary>
/// 界面字体：内置 <b>Outfit</b>（SIL OFL 1.1，见 <c>Fonts/ATTRIBUTION.md</c>），
/// 和 osu! Roam 用的是同一套，观感保持一致。
///
/// Outfit 没有汉字字形：中文由 WPF 逐字符回退到系统字体（<see cref="SystemFontName"/>）。
/// 所以 App.xaml 里的 <c>UiFont</c> 是「家族列表」而不是单一字体名。
/// </summary>
public static class Typography
{
    private const string EmbeddedFontsUri = "pack://application:,,,/Fonts/";
    private const string FamilyName = "Outfit";
    private const string SystemFontName = "Microsoft YaHei UI";
    public const string ResourceKey = "UiFont";

    private static bool loaded;

    /// <summary>把字体装进 <see cref="Application.Resources"/>；必须在创建窗口之前调用。</summary>
    public static void Apply()
    {
        if (loaded)
            return;
        loaded = true;

        FontFamily family = loadEmbedded();
        Application.Current.Resources[ResourceKey] = family;
        AppLog.Info($"字体：{family.FamilyNames.Values.FirstOrDefault() ?? FamilyName} 已加载");
    }

    private static FontFamily loadEmbedded()
    {
        try
        {
            ICollection<FontFamily> families = Fonts.GetFontFamilies(new Uri(EmbeddedFontsUri));
            FontFamily? found = families.FirstOrDefault(candidate =>
                candidate.FamilyNames.Values.Any(name => name.Equals(FamilyName, StringComparison.OrdinalIgnoreCase)));
            if (found is not null)
                return new FontFamily($"{FamilyName}, {SystemFontName}");
        }
        catch (Exception exception)
        {
            AppLog.Info($"字体：读取 {EmbeddedFontsUri} 失败 {exception.GetType().Name}: {exception.Message}");
        }

        return new FontFamily(SystemFontName);
    }
}
