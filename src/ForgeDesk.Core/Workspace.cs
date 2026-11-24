using System.Collections.ObjectModel;
using ForgeDesk.Core.Configuration;
using ForgeDesk.Core.Models;
using ForgeDesk.Core.Urls;

namespace ForgeDesk.Core;

/// <summary>
/// All user-facing operations on projects, mappings and per-project desk state.
/// UI code calls these instead of mutating the model directly so invariants hold
/// (contiguous ordering, valid selection, sensible active project).
/// None of these operations ever contacts a provider: removing a mapping or project
/// only forgets the local pointer.
/// </summary>
public sealed class Workspace : ObservableObject
{
    private Project? _activeProject;

    public Workspace(AppConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        ConfigNormalizer.Normalize(config);
        _activeProject = config.Projects.FirstOrDefault(p => p.Id == config.ActiveProjectId && !p.IsArchived);
    }

    public AppConfig Config { get; }

    public ObservableCollection<Project> Projects => Config.Projects;

    public Preferences Preferences => Config.Preferences;

    /// <summary>Non-archived projects in tab order.</summary>
    public IReadOnlyList<Project> VisibleProjects => Projects.Where(p => !p.IsArchived).ToList();

    public Project? ActiveProject
    {
        get => _activeProject;
        private set
        {
            if (Set(ref _activeProject, value))
            {
                Config.ActiveProjectId = value?.Id;
            }
        }
    }

    // ---------------------------------------------------------------- projects

    public Project CreateProject(string name, string? color = null)
    {
        var project = new Project
        {
            Name = ConfigNormalizer.CleanName(name, ConfigNormalizer.DefaultProjectName),
            Color = NormalizeColor(color),
            Order = Projects.Count,
        };
        Projects.Add(project);
        return project;
    }

    public void RenameProject(Project project, string name)
    {
        EnsureKnown(project);
        var clean = ConfigNormalizer.CleanName(name, string.Empty);
        if (clean.Length == 0)
        {
            throw new ArgumentException("A project needs a name.", nameof(name));
        }

        project.Name = clean;
    }

    public void SetProjectColor(Project project, string? color)
    {
        EnsureKnown(project);
        project.Color = NormalizeColor(color);
    }

    /// <summary>
    /// Moves a project one step left (-1) or right (+1) among projects with the same
    /// archived state, so archived projects never get in the way of reordering tabs.
    /// </summary>
    public bool MoveProject(Project project, int delta)
    {
        EnsureKnown(project);
        if (delta == 0)
        {
            return false;
        }

        var peers = Projects.Where(p => p.IsArchived == project.IsArchived).ToList();
        var index = peers.IndexOf(project);
        var targetIndex = Math.Clamp(index + delta, 0, peers.Count - 1);
        if (targetIndex == index)
        {
            return false;
        }

        var target = peers[targetIndex];
        Projects.Move(Projects.IndexOf(project), Projects.IndexOf(target));
        RenumberProjects();
        return true;
    }

    /// <summary>Moves a project to an absolute position in the full project list.</summary>
    public void MoveProjectTo(Project project, int newIndex)
    {
        EnsureKnown(project);
        newIndex = Math.Clamp(newIndex, 0, Projects.Count - 1);
        var oldIndex = Projects.IndexOf(project);
        if (oldIndex != newIndex)
        {
            Projects.Move(oldIndex, newIndex);
            RenumberProjects();
        }
    }

    public void SetArchived(Project project, bool archived)
    {
        EnsureKnown(project);
        if (project.IsArchived == archived)
        {
            return;
        }

        var fallback = archived && ActiveProject == project ? NeighbourOf(project) : null;
        project.IsArchived = archived;
        if (archived && ActiveProject == project)
        {
            ActiveProject = fallback;
        }
        else if (!archived && ActiveProject is null)
        {
            ActiveProject = project;
        }
    }

    /// <summary>Forgets a project and its mappings. Nothing is changed on ChatGPT or Claude.</summary>
    public void RemoveProject(Project project)
    {
        EnsureKnown(project);
        var fallback = ActiveProject == project ? NeighbourOf(project) : ActiveProject;
        Projects.Remove(project);
        RenumberProjects();
        ActiveProject = fallback;
    }

    public void ActivateProject(Project? project)
    {
        if (project is not null)
        {
            EnsureKnown(project);
            if (project.IsArchived)
            {
                throw new InvalidOperationException("Archived projects can't be opened. Restore it first.");
            }
        }

        ActiveProject = project;
    }

    /// <summary>Activates the visible project at a 0-based tab position, if it exists.</summary>
    public bool ActivateProjectAt(int index)
    {
        var visible = VisibleProjects;
        if (index < 0 || index >= visible.Count)
        {
            return false;
        }

        ActivateProject(visible[index]);
        return true;
    }

    /// <summary>Cycles through visible projects (wrapping around).</summary>
    public bool ActivateRelative(int delta)
    {
        var visible = VisibleProjects;
        if (visible.Count == 0)
        {
            return false;
        }

        var index = ActiveProject is null ? -1 : visible.ToList().IndexOf(ActiveProject);
        if (index < 0)
        {
            ActivateProject(visible[0]);
            return true;
        }

        var next = ((index + delta) % visible.Count + visible.Count) % visible.Count;
        ActivateProject(visible[next]);
        return next != index;
    }

    /// <summary>Makes sure the active project is valid after bulk changes such as an import.</summary>
    public void EnsureActiveProject()
    {
        if (ActiveProject is null || !Projects.Contains(ActiveProject) || ActiveProject.IsArchived)
        {
            ActiveProject = Projects.FirstOrDefault(p => p.Id == Config.ActiveProjectId && !p.IsArchived)
                ?? VisibleProjects.FirstOrDefault();
        }
    }

    // ---------------------------------------------------------------- mappings

    /// <summary>
    /// Adds a mapping. The provider is taken from the link itself.
    /// Throws <see cref="ArgumentException"/> with a user-readable message if invalid.
    /// </summary>
    public ConversationMapping AddConversation(Project project, string name, string url)
    {
        EnsureKnown(project);
        var check = ConversationUrl.Check(url);
        if (!check.IsValid)
        {
            throw new ArgumentException(check.Error, nameof(url));
        }

        var provider = check.Provider!.Value;
        var existing = FindMapping(project, provider, check.NormalizedUrl!);
        if (existing is not null)
        {
            throw new ArgumentException($"This conversation is already mapped in \"{project.Name}\" as \"{existing.Name}\".", nameof(url));
        }

        var list = project.Conversations(provider);
        var mapping = new ConversationMapping
        {
            Provider = provider,
            Name = ConfigNormalizer.CleanName(name, $"{provider.DisplayName()} conversation {list.Count + 1}"),
            Url = check.NormalizedUrl!,
            Order = list.Count,
        };
        list.Add(mapping);
        return mapping;
    }

    public void UpdateConversation(Project project, ConversationMapping mapping, string name, string url)
    {
        EnsureKnown(project);
        var list = project.Conversations(mapping.Provider);
        if (!list.Contains(mapping))
        {
            throw new InvalidOperationException("That conversation isn't part of this project.");
        }

        var cleanName = ConfigNormalizer.CleanName(name, string.Empty);
        if (cleanName.Length == 0)
        {
            throw new ArgumentException("A conversation needs a name.", nameof(name));
        }

        var check = ConversationUrl.Check(url);
        if (!check.IsValid)
        {
            throw new ArgumentException(check.Error, nameof(url));
        }

        if (check.Provider != mapping.Provider)
        {
            throw new ArgumentException($"This is a {check.Provider!.Value.DisplayName()} link, but this mapping is on the {mapping.Provider.DisplayName()} side.", nameof(url));
        }

        var duplicate = FindMapping(project, mapping.Provider, check.NormalizedUrl!);
        if (duplicate is not null && duplicate != mapping)
        {
            throw new ArgumentException($"This conversation is already mapped in \"{project.Name}\" as \"{duplicate.Name}\".", nameof(url));
        }

        mapping.Name = cleanName;
        mapping.Url = check.NormalizedUrl!;
    }

    public void RenameConversation(Project project, ConversationMapping mapping, string name) =>
        UpdateConversation(project, mapping, name, mapping.Url);

    /// <summary>Forgets a mapping. The conversation itself is untouched on the provider's site.</summary>
    public void RemoveConversation(Project project, ConversationMapping mapping)
    {
        EnsureKnown(project);
        var list = project.Conversations(mapping.Provider);
        var index = list.IndexOf(mapping);
        if (index < 0)
        {
            return;
        }

        var wasSelected = project.Desk.GetSelectedId(mapping.Provider) == mapping.Id;
        list.RemoveAt(index);
        RenumberMappings(list);
        if (wasSelected)
        {
            var fallback = list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)];
            project.Desk.SetSelectedId(mapping.Provider, fallback?.Id);
        }
    }

    public bool MoveConversation(Project project, ConversationMapping mapping, int delta)
    {
        EnsureKnown(project);
        var list = project.Conversations(mapping.Provider);
        var index = list.IndexOf(mapping);
        if (index < 0)
        {
            return false;
        }

        var target = Math.Clamp(index + delta, 0, list.Count - 1);
        if (target == index)
        {
            return false;
        }

        list.Move(index, target);
        RenumberMappings(list);
        return true;
    }

    public ConversationMapping? FindMapping(Project project, Provider provider, string url)
    {
        var key = ConversationUrl.GetKey(provider, url);
        return project.Conversations(provider).FirstOrDefault(m => ConversationUrl.GetKey(provider, m.Url) == key);
    }

    /// <summary>Projects (other than <paramref name="except"/>) that also map the same conversation.</summary>
    public IReadOnlyList<Project> ProjectsMapping(Provider provider, string url, Project? except = null)
    {
        var key = ConversationUrl.GetKey(provider, url);
        return Projects
            .Where(p => p != except && p.Conversations(provider).Any(m => ConversationUrl.GetKey(provider, m.Url) == key))
            .ToList();
    }

    // ---------------------------------------------------------------- desk state

    /// <summary>
    /// The conversation a side should show for a project: the last selected one if it still
    /// exists, otherwise the first mapping, otherwise none.
    /// </summary>
    public ConversationMapping? ResolveSelection(Project project, Provider provider)
    {
        var list = project.Conversations(provider);
        var id = project.Desk.GetSelectedId(provider);
        return list.FirstOrDefault(m => m.Id == id) ?? list.FirstOrDefault();
    }

    /// <summary>Selects a conversation on one side. The other side is never affected.</summary>
    public void SelectConversation(Project project, Provider provider, ConversationMapping? mapping)
    {
        EnsureKnown(project);
        if (mapping is not null && !project.Conversations(provider).Contains(mapping))
        {
            throw new InvalidOperationException("That conversation isn't mapped on this side of the project.");
        }

        project.Desk.SetSelectedId(provider, mapping?.Id);
    }

    public void SetSplitRatio(Project project, double ratio)
    {
        EnsureKnown(project);
        project.Desk.SplitRatio = ratio;
    }

    public void SetFocus(Project project, DeskFocus focus)
    {
        EnsureKnown(project);
        project.Desk.Focus = focus;
    }

    /// <summary>Maximises a side, or restores the split if that side is already maximised.</summary>
    public DeskFocus ToggleFocus(Project project, Provider provider)
    {
        EnsureKnown(project);
        var wanted = ProjectDesk.FocusFor(provider);
        project.Desk.Focus = project.Desk.Focus == wanted ? DeskFocus.None : wanted;
        return project.Desk.Focus;
    }

    // ---------------------------------------------------------------- import

    public ImportSummary Import(ImportPreview preview, ImportMode mode)
    {
        var summary = ConfigTransfer.Apply(Config, preview, mode);
        EnsureActiveProject();
        return summary;
    }

    // ---------------------------------------------------------------- helpers

    private Project? NeighbourOf(Project project)
    {
        var visible = VisibleProjects.ToList();
        var index = visible.IndexOf(project);
        if (index < 0)
        {
            return visible.FirstOrDefault();
        }

        if (index + 1 < visible.Count)
        {
            return visible[index + 1];
        }

        return index > 0 ? visible[index - 1] : null;
    }

    private void RenumberProjects()
    {
        for (var i = 0; i < Projects.Count; i++)
        {
            Projects[i].Order = i;
        }
    }

    private static void RenumberMappings(IList<ConversationMapping> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            list[i].Order = i;
        }
    }

    private static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var text = color.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        return ConfigNormalizer.IsValidColor(text)
            ? text.ToUpperInvariant()
            : throw new ArgumentException("Colours must look like #RRGGBB.", nameof(color));
    }

    private void EnsureKnown(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!Projects.Contains(project))
        {
            throw new InvalidOperationException("That project is no longer part of ForgeDesk.");
        }
    }
}
