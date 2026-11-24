using System.IO;
using System.Text;

namespace ForgeDesk.Infrastructure;

/// <summary>
/// Tiny append-only diagnostics log (logs\forgedesk.log, rolled at 1 MB).
/// Never logs page contents, credentials or full URLs with query strings.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "forgedesk.log");
            Roll();
        }
        catch (Exception)
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>host + path only, to keep OAuth codes and other query data out of the log.</summary>
    public static string Safe(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url is null ? "(none)" : "(unparseable)";
        }

        return uri.Scheme is "http" or "https" ? $"{uri.Host}{uri.AbsolutePath}" : uri.Scheme + ":";
    }

    private static void Write(string level, string message, Exception? ex)
    {
        if (_path is null)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(' ').Append(level).Append(' ').Append(message);
        if (ex is not null)
        {
            line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        }

        lock (Gate)
        {
            try
            {
                File.AppendAllText(_path, line.AppendLine().ToString(), Encoding.UTF8);
                Roll();
            }
            catch (Exception)
            {
                // Logging must never take the app down.
            }
        }
    }

    private static void Roll()
    {
        if (_path is null)
        {
            return;
        }

        var info = new FileInfo(_path);
        if (info.Exists && info.Length > MaxBytes)
        {
            File.Move(_path, _path + ".1", overwrite: true);
        }
    }
}
