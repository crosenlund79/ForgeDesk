using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ForgeDesk.Core.Models;
using ForgeDesk.Core.Urls;

namespace ForgeDesk.Core.Configuration;

/// <summary>
/// Repairs configuration loaded from disk or imported from a file so the rest of the
/// application can rely on its invariants (unique ids, contiguous order, mappings on the
/// correct side). Never drops a user's mapping silently: problems are reported.
/// </summary>
public static partial class ConfigNormalizer
{
    public const int MaxNameLength = 80;
    public const string DefaultProjectName = "Untitled project";

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorRegex();

    public static bool IsValidColor(string? color) => color is not null && ColorRegex().IsMatch(color);

    public static string CleanName(string? name, string fallback)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return fallback;
        }

        return text.Length > MaxNameLength ? text[..MaxNameLength].TrimEnd() : text;
    }

    /// <summary>Normalises the whole configuration in place and returns any warnings.</summary>
    public static IReadOnlyList<string> Normalize(AppConfig config)
    {
        var warnings = new List<string>();
        config.Projects ??= [];
        config.Preferences ??= new Preferences();
        NormalizeProjects(config.Projects, warnings);

        var visible = config.Projects.Where(p => !p.IsArchived).ToList();
        if (config.ActiveProjectId is null || visible.All(p => p.Id != config.ActiveProjectId))
        {
            config.ActiveProjectId = visible.FirstOrDefault()?.Id;
        }

        if (config.Window is not null && !config.Window.IsUsable)
        {
            config.Window = null;
        }

        config.SchemaVersion = AppConfig.CurrentSchemaVersion;
        return warnings;
    }

    internal static void NormalizeProjects(ObservableCollection<Project> projects, List<string> warnings)
    {
        var nulls = projects.Where(p => p is null).ToList();
        foreach (var n in nulls)
        {
            projects.Remove(n);
        }

        var sorted = projects
            .Select((p, index) => (p, index))
            .OrderBy(x => x.p.Order)
            .ThenBy(x => x.index)
            .Select(x => x.p)
            .ToList();

        var seenProjectIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < sorted.Count; i++)
        {
            var project = sorted[i];
            if (string.IsNullOrWhiteSpace(project.Id) || !seenProjectIds.Add(project.Id))
            {
                project.Id = Ids.New();
                seenProjectIds.Add(project.Id);
            }

            project.Name = CleanName(project.Name, DefaultProjectName);
            if (project.Color is not null && !IsValidColor(project.Color))
            {
                warnings.Add($"Project \"{project.Name}\": ignored invalid colour \"{project.Color}\".");
                project.Color = null;
            }

            project.Order = i;
            project.Desk ??= new ProjectDesk();
            project.Desk.SplitRatio = ProjectDesk.ClampRatio(project.Desk.SplitRatio);
            if (!Enum.IsDefined(project.Desk.Focus))
            {
                project.Desk.Focus = DeskFocus.None;
            }

            project.ChatGptConversations ??= [];
            project.ClaudeConversations ??= [];
            NormalizeMappings(project, warnings);
        }

        // Re-order the collection itself so list order always matches Order.
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = projects.IndexOf(sorted[i]);
            if (current != i)
            {
                projects.Move(current, i);
            }
        }
    }

    private static void NormalizeMappings(Project project, List<string> warnings)
    {
        // Move mappings whose link clearly belongs to the other provider to the right side.
        foreach (var provider in ProviderInfo.All)
        {
            var list = project.Conversations(provider);
            foreach (var mapping in list.Where(m => m is not null).ToList())
            {
                var check = ConversationUrl.Check(mapping.Url);
                if (check.IsValid && check.Provider != provider)
                {
                    list.Remove(mapping);
                    project.Conversations(check.Provider!.Value).Add(mapping);
                    warnings.Add($"Project \"{project.Name}\": moved \"{mapping.Name}\" to the {check.Provider.Value.DisplayName()} side.");
                }
            }
        }

        foreach (var provider in ProviderInfo.All)
        {
            var list = project.Conversations(provider);
            foreach (var n in list.Where(m => m is null).ToList())
            {
                list.Remove(n);
            }

            var sorted = list
                .Select((m, index) => (m, index))
                .OrderBy(x => x.m.Order)
                .ThenBy(x => x.index)
                .Select(x => x.m)
                .ToList();

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < sorted.Count; i++)
            {
                var mapping = sorted[i];
                if (string.IsNullOrWhiteSpace(mapping.Id) || !seenIds.Add(mapping.Id))
                {
                    mapping.Id = Ids.New();
                    seenIds.Add(mapping.Id);
                }

                mapping.Provider = provider;
                mapping.Order = i;
                mapping.Name = CleanName(mapping.Name, $"{provider.DisplayName()} conversation {i + 1}");

                var check = ConversationUrl.Check(mapping.Url);
                if (check.IsValid)
                {
                    mapping.Url = check.NormalizedUrl!;
                }
                else
                {
                    warnings.Add($"Project \"{project.Name}\": \"{mapping.Name}\" has an invalid link and will need editing.");
                }
            }

            for (var i = 0; i < sorted.Count; i++)
            {
                var current = list.IndexOf(sorted[i]);
                if (current != i)
                {
                    list.Move(current, i);
                }
            }
        }
    }
}
