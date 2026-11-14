using System.IO;
using System.Text;

namespace OsuCorn.Services;

/// <summary>
/// 极简日志：写 <c>data/corn.log</c> 并在控制台可选输出。
/// 后台工具最重要的可诊断性就是这一件事——出事时能看出「刚才发了什么」。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "corn.log");

    public static event Action<string>? LineAdded;

    public static string FilePath => Path;

    public static void Info(string message) => write("INFO", message);

    public static void Error(string message, Exception? exception = null) =>
        write("ERROR", exception is null ? message : $"{message}：{exception.GetType().Name}: {exception.Message}");

    private static void write(string level, string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
        LineAdded?.Invoke(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写不进去不能影响功能。
        }
    }
}
