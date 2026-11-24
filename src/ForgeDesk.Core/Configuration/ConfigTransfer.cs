using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Configuration;

/// <summary>The portable export file: projects, mappings and preferences only.</summary>
public sealed class ConfigExport
{
    public const string FormatName = "forgedesk-export";

    public string Format { get; set; } = FormatName;

    public int SchemaVersion { get; set; } = AppConfig.CurrentSchemaVersion;

    public DateTimeOffset ExportedAt { get; set; }

    public string? ExportedBy { get; set; }

    public ObservableCollection<Project> Projects { get; set; } = [];

    public Preferences? Preferences { get; set; }
}

public enum ImportMode
{
    /// <summary>Keep existing projects; add new ones and new mappings to matching projects.</summary>
    Merge,

    /// <summary>Replace all projects (and preferences, if present) with the imported ones.</summary>
    Replace,
}

public sealed record ImportPreview(ConfigExport Data, IReadOnlyList<string> Warnings)
{
    public int ProjectCount => Data.Projects.Count;

    public int ConversationCount => Data.Projects.Sum(p => p.ChatGptConversations.Count + p.ClaudeConversations.Count);
}

public sealed record ImportSummary(
    int ProjectsAdded,
    int ProjectsMerged,
    int ConversationsAdded,
    int ConversationsSkipped,
    IReadOnlyList<string> Warnings);

public sealed class ConfigImportException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Export and import of configuration. Exports never contain window placement, browser
/// data, cookies, credentials or conversation contents — ForgeDesk does not hold any of those
/// in its configuration in the first place.
/// </summary>
public static class ConfigTransfer
{
    public static string Export(AppConfig config, string? exportedBy = null, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var export = new ConfigExport
        {
            ExportedAt = now ?? DateTimeOffset.Now,
            ExportedBy = exportedBy,
            Projects = new ObservableCollection<Project>(config.Projects.Select(CloneProject)),
            Preferences = config.Preferences.Clone(),
        };
        return JsonSerializer.Serialize(export, ConfigJson.Options);
    }

    /// <summary>
    /// Parses an export file (or a raw config.json) and normalises it.
    /// Throws <see cref="ConfigImportException"/> with a user-readable message on failure.
    /// </summary>
    public static ImportPreview Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ConfigImportException("The file is empty.");
        }

        ConfigExport? data;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !TryGetProperty(doc.RootElement, "projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
            {
                throw new ConfigImportException("This file doesn't contain ForgeDesk projects.");
            }

            if (TryGetProperty(doc.RootElement, "format", out var format) &&
                format.ValueKind == JsonValueKind.String &&
                !string.Equals(format.GetString(), ConfigExport.FormatName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigImportException("This file isn't a ForgeDesk configuration export.");
            }

            data = JsonSerializer.Deserialize<ConfigExport>(json, ConfigJson.Options);
        }
        catch (JsonException ex)
        {
            throw new ConfigImportException($"The file isn't valid ForgeDesk JSON ({ex.Message}).", ex);
        }

        if (data is null)
        {
            throw new ConfigImportException("The file is empty.");
        }

        if (data.SchemaVersion > AppConfig.CurrentSchemaVersion)
        {
            throw new ConfigImportException("This file was exported by a newer version of ForgeDesk.");
        }

        data.Projects ??= [];
        var warnings = new List<string>();
        ConfigNormalizer.NormalizeProjects(data.Projects, warnings);
        return new ImportPreview(data, warnings);
    }

    /// <summary>Applies parsed import data to the live configuration.</summary>
    public static ImportSummary Apply(AppConfig config, ImportPreview preview, ImportMode mode)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(preview);
        var warnings = new List<string>(preview.Warnings);
        var incoming = preview.Data.Projects.Select(CloneProject).ToList();

        if (mode == ImportMode.Replace)
        {
            config.Projects.Clear();
            foreach (var project in incoming)
            {
                config.Projects.Add(project);
            }

            Renumber(config.Projects);
            if (preview.Data.Preferences is { } prefs)
            {
                CopyPreferences(prefs, config.Preferences);
            }

            var count = incoming.Sum(p => p.ChatGptConversations.Count + p.ClaudeConversations.Count);
            return new ImportSummary(incoming.Count, 0, count, 0, warnings);
        }

        int added = 0, merged = 0, conversationsAdded = 0, skipped = 0;
        foreach (var project in incoming)
        {
            var existing = config.Projects.FirstOrDefault(p => p.Id == project.Id);
            if (existing is null)
            {
                project.Order = config.Projects.Count;
                config.Projects.Add(project);
                added++;
                conversationsAdded += project.ChatGptConversations.Count + project.ClaudeConversations.Count;
                continue;
            }

            merged++;
            foreach (var provider in ProviderInfo.All)
            {
                var target = existing.Conversations(provider);
                foreach (var mapping in project.Conversations(provider))
                {
                    var key = Urls.ConversationUrl.GetKey(provider, mapping.Url);
                    if (target.Any(m => m.Id == mapping.Id || Urls.ConversationUrl.GetKey(provider, m.Url) == key))
                    {
                        skipped++;
                        continue;
                    }

                    var copy = mapping.Clone();
                    copy.Order = target.Count;
                    target.Add(copy);
                    conversationsAdded++;
                }
            }
        }

        Renumber(config.Projects);
        return new ImportSummary(added, merged, conversationsAdded, skipped, warnings);
    }

    internal static Project CloneProject(Project source)
    {
        var project = new Project
        {
            Id = source.Id,
            Name = source.Name,
            Color = source.Color,
            Order = source.Order,
            IsArchived = source.IsArchived,
            Desk = new ProjectDesk
            {
                SelectedChatGptId = source.Desk.SelectedChatGptId,
                SelectedClaudeId = source.Desk.SelectedClaudeId,
                SplitRatio = source.Desk.SplitRatio,
                Focus = source.Desk.Focus,
            },
        };

        foreach (var m in source.ChatGptConversations)
        {
            project.ChatGptConversations.Add(m.Clone());
        }

        foreach (var m in source.ClaudeConversations)
        {
            project.ClaudeConversations.Add(m.Clone());
        }

        return project;
    }

    private static void Renumber(IList<Project> projects)
    {
        for (var i = 0; i < projects.Count; i++)
        {
            projects[i].Order = i;
        }
    }

    private static void CopyPreferences(Preferences from, Preferences to)
    {
        to.Theme = from.Theme;
        to.MaxBrowsersPerProvider = from.MaxBrowsersPerProvider;
        to.KeepHiddenBrowsersMinutes = from.KeepHiddenBrowsersMinutes;
        to.ChatGptZoom = from.ChatGptZoom;
        to.ClaudeZoom = from.ClaudeZoom;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
