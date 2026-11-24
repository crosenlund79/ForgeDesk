namespace ForgeDesk.Core;

/// <summary>
/// Where ForgeDesk keeps its local data. By default everything lives under
/// %LOCALAPPDATA%\ForgeDesk:
///   config.json          – projects, mappings, desk state, preferences
///   WebView2\            – the embedded browser profile (provider sign-in cookies live here,
///                          managed and encrypted by WebView2, never by ForgeDesk's config)
///   logs\forgedesk.log   – diagnostics
/// </summary>
public sealed class AppPaths
{
    public const string DataDirEnvironmentVariable = "FORGEDESK_DATA_DIR";

    public AppPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A data directory is required.", nameof(root));
        }

        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ConfigDirectory => Root;

    public string BrowserDataDirectory => Path.Combine(Root, "WebView2");

    public string LogDirectory => Path.Combine(Root, "logs");

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgeDesk");

    /// <summary>Command-line override, then environment variable, then the default.</summary>
    public static AppPaths Resolve(string? commandLineOverride)
    {
        if (!string.IsNullOrWhiteSpace(commandLineOverride))
        {
            return new AppPaths(commandLineOverride);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        return new AppPaths(string.IsNullOrWhiteSpace(fromEnvironment) ? DefaultRoot : fromEnvironment);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BrowserDataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
