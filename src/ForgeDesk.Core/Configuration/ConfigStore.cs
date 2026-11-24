using System.Text;
using System.Text.Json;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Configuration;

public enum ConfigLoadOutcome
{
    /// <summary>No configuration existed; a fresh one was created.</summary>
    Fresh,

    /// <summary>The configuration file was read normally.</summary>
    Loaded,

    /// <summary>The main file was unreadable; the previous good copy was used.</summary>
    RecoveredFromBackup,

    /// <summary>Neither the main file nor the backup could be read; started fresh.</summary>
    ResetAfterCorruption,
}

public sealed record ConfigLoadResult(
    AppConfig Config,
    ConfigLoadOutcome Outcome,
    IReadOnlyList<string> Warnings,
    string? QuarantinedFile);

/// <summary>
/// Reads and writes config.json in the application's local data directory.
/// Writes are atomic (temp file + replace) and keep the previous good copy as config.json.bak.
/// </summary>
public sealed class ConfigStore
{
    public const string FileName = "config.json";

    private readonly object _gate = new();

    public ConfigStore(string directory)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public string Directory { get; }

    public string ConfigPath => Path.Combine(Directory, FileName);

    public string BackupPath => ConfigPath + ".bak";

    private string TempPath => ConfigPath + ".tmp";

    public ConfigLoadResult Load()
    {
        lock (_gate)
        {
            var warnings = new List<string>();
            if (!File.Exists(ConfigPath))
            {
                if (File.Exists(BackupPath) && TryRead(BackupPath, out var fromBackupOnly, out _))
                {
                    warnings.AddRange(ConfigNormalizer.Normalize(fromBackupOnly!));
                    warnings.Insert(0, "The settings file was missing; restored the previous copy.");
                    return new ConfigLoadResult(fromBackupOnly!, ConfigLoadOutcome.RecoveredFromBackup, warnings, null);
                }

                var fresh = new AppConfig();
                ConfigNormalizer.Normalize(fresh);
                return new ConfigLoadResult(fresh, ConfigLoadOutcome.Fresh, warnings, null);
            }

            if (TryRead(ConfigPath, out var config, out var error))
            {
                if (config!.SchemaVersion > AppConfig.CurrentSchemaVersion)
                {
                    var keep = Path.Combine(Directory, $"config.schema{config.SchemaVersion}.json");
                    TryCopy(ConfigPath, keep);
                    warnings.Add($"Settings were written by a newer ForgeDesk (format {config.SchemaVersion}). A copy was kept as {Path.GetFileName(keep)}.");
                }

                warnings.AddRange(ConfigNormalizer.Normalize(config));
                return new ConfigLoadResult(config, ConfigLoadOutcome.Loaded, warnings, null);
            }

            // The main file is damaged: keep it for inspection, never overwrite it blindly.
            var quarantine = Path.Combine(Directory, $"config.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            TryMove(ConfigPath, quarantine);
            warnings.Add($"The settings file could not be read ({error}). It was kept as {Path.GetFileName(quarantine)}.");

            if (File.Exists(BackupPath) && TryRead(BackupPath, out var backup, out _))
            {
                warnings.AddRange(ConfigNormalizer.Normalize(backup!));
                warnings.Add("The previous good copy of your settings was restored.");
                return new ConfigLoadResult(backup!, ConfigLoadOutcome.RecoveredFromBackup, warnings, quarantine);
            }

            var reset = new AppConfig();
            ConfigNormalizer.Normalize(reset);
            return new ConfigLoadResult(reset, ConfigLoadOutcome.ResetAfterCorruption, warnings, quarantine);
        }
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var json = Serialize(config);
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(ConfigPath))
            {
                File.Replace(TempPath, ConfigPath, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(TempPath, ConfigPath, overwrite: true);
            }
        }
    }

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, ConfigJson.Options);

    public static AppConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfig>(json, ConfigJson.Options)
        ?? throw new JsonException("The settings file is empty.");

    private static bool TryRead(string path, out AppConfig? config, out string? error)
    {
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            config = Deserialize(json);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            config = null;
            error = ex.Message;
            return false;
        }
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: if the file can't be moved, the next save overwrites it.
        }
    }

    private static void TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
