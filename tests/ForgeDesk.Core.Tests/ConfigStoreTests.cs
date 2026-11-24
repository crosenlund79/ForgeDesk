using ForgeDesk.Core.Configuration;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Missing_configuration_starts_fresh()
    {
        using var dir = new TempDir();
        var result = new ConfigStore(dir.Path).Load();
        Assert.Equal(ConfigLoadOutcome.Fresh, result.Outcome);
        Assert.Empty(result.Config.Projects);
        Assert.Null(result.Config.ActiveProjectId);
    }

    [Fact]
    public void Everything_the_desk_needs_survives_a_save_and_load()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(Path.Combine(dir.Path, "nested"));
        var (ws, hyper, sirens) = TestData.HyperCarsDesk();
        ws.SelectConversation(hyper, Provider.ChatGpt, hyper.ChatGptConversations[3]);
        ws.SelectConversation(hyper, Provider.Claude, hyper.ClaudeConversations[2]);
        ws.SetSplitRatio(hyper, 0.62);
        ws.ToggleFocus(hyper, Provider.ChatGpt);
        ws.SetArchived(sirens, true);
        ws.MoveConversation(hyper, hyper.ClaudeConversations[2], -2);
        ws.Config.Window = new WindowPlacement { Left = 10, Top = 20, Width = 3440, Height = 1400, Maximized = true };
        ws.Preferences.Theme = ThemePreference.Dark;
        ws.Preferences.MaxBrowsersPerProvider = 9;
        ws.Preferences.ClaudeZoom = 1.25;

        store.Save(ws.Config);
        var loaded = store.Load();

        Assert.Equal(ConfigLoadOutcome.Loaded, loaded.Outcome);
        var reopened = new Workspace(loaded.Config);
        var p = Assert.Single(reopened.VisibleProjects);
        Assert.Equal("HyperCars", p.Name);
        Assert.Equal("#E5484D", p.Color);
        Assert.Same(p, reopened.ActiveProject);
        Assert.Equal("Release planning", reopened.ResolveSelection(p, Provider.ChatGpt)!.Name);
        Assert.Equal("AI refinement", reopened.ResolveSelection(p, Provider.Claude)!.Name);
        Assert.Equal(["AI refinement", "Main implementation", "Multiplayer development"], p.ClaudeConversations.Select(m => m.Name));
        Assert.Equal(0.62, p.Desk.SplitRatio, 3);
        Assert.Equal(DeskFocus.ChatGpt, p.Desk.Focus);
        Assert.True(reopened.Projects.Single(x => x.Name == "Sirens").IsArchived);
        Assert.Equal(3440, reopened.Config.Window!.Width);
        Assert.True(reopened.Config.Window.Maximized);
        Assert.Equal(ThemePreference.Dark, reopened.Preferences.Theme);
        Assert.Equal(9, reopened.Preferences.MaxBrowsersPerProvider);
        Assert.Equal(1.25, reopened.Preferences.ClaudeZoom);
        Assert.Equal(hyper.ChatGptConversations.Select(m => m.Id), p.ChatGptConversations.Select(m => m.Id));
    }

    [Fact]
    public void Saved_json_is_readable_and_contains_no_secrets()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        var (ws, _, _) = TestData.HyperCarsDesk();
        store.Save(ws.Config);

        var json = File.ReadAllText(store.ConfigPath);
        Assert.Contains("\"chatGptConversations\"", json);
        Assert.Contains("\"provider\": \"chatgpt\"", json);
        Assert.Contains("\"provider\": \"claude\"", json);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saves_are_atomic_and_keep_a_backup()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        store.Save(ws.Config);
        ws.RenameProject(hyper, "HyperCars 2");
        store.Save(ws.Config);

        Assert.False(File.Exists(store.ConfigPath + ".tmp"));
        Assert.True(File.Exists(store.BackupPath));
        Assert.Contains("HyperCars 2", File.ReadAllText(store.ConfigPath));
        Assert.DoesNotContain("HyperCars 2", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void Corrupted_configuration_is_quarantined_and_backup_restored()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        store.Save(ws.Config);
        ws.RenameProject(hyper, "Newest");
        store.Save(ws.Config);
        File.WriteAllText(store.ConfigPath, "{ \"projects\": [ { \"name\": ");

        var result = store.Load();

        Assert.Equal(ConfigLoadOutcome.RecoveredFromBackup, result.Outcome);
        Assert.NotNull(result.QuarantinedFile);
        Assert.True(File.Exists(result.QuarantinedFile));
        Assert.Contains(result.Config.Projects, p => p.Name == "HyperCars");
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Corrupted_configuration_without_backup_starts_fresh_but_keeps_the_file()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        File.WriteAllText(store.ConfigPath, "this is not json");

        var result = store.Load();

        Assert.Equal(ConfigLoadOutcome.ResetAfterCorruption, result.Outcome);
        Assert.Empty(result.Config.Projects);
        Assert.True(File.Exists(result.QuarantinedFile));
        Assert.Equal("this is not json", File.ReadAllText(result.QuarantinedFile!));
    }

    [Fact]
    public void Hand_edited_configuration_is_repaired()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        File.WriteAllText(store.ConfigPath, """
            {
              // comments and trailing commas are tolerated
              "projects": [
                { "id": "b", "name": "Second", "order": 5, "color": "not-a-colour",
                  "chatGptConversations": [
                    { "id": "x", "name": "Misplaced", "url": "https://claude.ai/chat/0000000a-0000-4000-8000-00000000000a", "order": 0 },
                    { "id": "x", "name": "Dup id", "url": "chat.openai.com/c/0000000b-0000-4000-8000-00000000000b", "order": 1 },
                    { "name": "", "url": "not a link", "order": 2 },
                  ],
                  "desk": { "splitRatio": 7 } },
                { "id": "a", "name": "  ", "order": 1 },
                { "id": "a", "name": "Duplicate id", "order": 9 },
              ],
              "activeProjectId": "missing",
              "window": { "left": 0, "top": 0, "width": -5, "height": 10 },
            }
            """);

        var result = store.Load();
        var config = result.Config;

        Assert.Equal(["Untitled project", "Second", "Duplicate id"], config.Projects.Select(p => p.Name));
        Assert.Equal(3, config.Projects.Select(p => p.Id).Distinct().Count());
        var second = config.Projects[1];
        Assert.Null(second.Color);
        Assert.Equal(ProjectDesk.MaxSplitRatio, second.Desk.SplitRatio);
        Assert.Equal("Misplaced", Assert.Single(second.ClaudeConversations).Name);
        Assert.Equal(2, second.ChatGptConversations.Count);
        Assert.Equal("https://chatgpt.com/c/0000000b-0000-4000-8000-00000000000b", second.ChatGptConversations[0].Url);
        Assert.Equal("not a link", second.ChatGptConversations[1].Url);
        Assert.Equal(config.Projects[0].Id, config.ActiveProjectId);
        Assert.Null(config.Window);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Change_tracker_reports_every_persisted_change()
    {
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        using var tracker = new ChangeTracker(ws.Config);

        var count = tracker.ChangeCount;
        ws.RenameProject(hyper, "Renamed");
        Assert.True(tracker.ChangeCount > count);

        count = tracker.ChangeCount;
        var added = ws.CreateProject("New");
        Assert.True(tracker.ChangeCount > count);

        count = tracker.ChangeCount;
        var mapping = ws.AddConversation(added, "Fresh", TestData.Claude(77));
        Assert.True(tracker.ChangeCount > count);

        count = tracker.ChangeCount;
        mapping.Name = "Edited via binding";
        Assert.True(tracker.ChangeCount > count);

        count = tracker.ChangeCount;
        ws.SetSplitRatio(added, 0.3);
        Assert.True(tracker.ChangeCount > count);

        count = tracker.ChangeCount;
        ws.ActivateProject(added);
        Assert.True(tracker.ChangeCount > count);

        count = tracker.ChangeCount;
        ws.Preferences.Theme = ThemePreference.Light;
        Assert.True(tracker.ChangeCount > count);
    }
}
