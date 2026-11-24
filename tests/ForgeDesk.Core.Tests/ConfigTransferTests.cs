using ForgeDesk.Core.Configuration;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Tests;

public class ConfigTransferTests
{
    [Fact]
    public void Export_contains_projects_and_preferences_but_not_machine_state()
    {
        var (ws, _, _) = TestData.HyperCarsDesk();
        ws.Config.Window = new WindowPlacement { Left = 1, Top = 2, Width = 5120, Height = 1440 };

        var json = ConfigTransfer.Export(ws.Config, "ForgeDesk 0.1.0", DateTimeOffset.Parse("2026-11-24T10:00:00Z"));

        Assert.Contains("\"format\": \"forgedesk-export\"", json);
        Assert.Contains("Overall development", json);
        Assert.Contains("\"preferences\"", json);
        Assert.DoesNotContain("\"window\"", json);
        Assert.DoesNotContain("5120", json);
        Assert.DoesNotContain("activeProjectId", json);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_then_replace_import_round_trips()
    {
        var (source, _, _) = TestData.HyperCarsDesk();
        source.Preferences.MaxBrowsersPerProvider = 3;
        var json = ConfigTransfer.Export(source.Config);

        var target = new Workspace(new AppConfig());
        target.CreateProject("Will be replaced");
        var preview = ConfigTransfer.Parse(json);
        Assert.Equal(2, preview.ProjectCount);
        Assert.Equal(9, preview.ConversationCount);

        var summary = target.Import(preview, ImportMode.Replace);

        Assert.Equal(2, summary.ProjectsAdded);
        Assert.Equal(["HyperCars", "Sirens"], target.Projects.Select(p => p.Name));
        Assert.Equal(source.Projects[0].ChatGptConversations.Select(m => (m.Id, m.Name, m.Url)),
                     target.Projects[0].ChatGptConversations.Select(m => (m.Id, m.Name, m.Url)));
        Assert.Equal(3, target.Preferences.MaxBrowsersPerProvider);
        Assert.NotNull(target.ActiveProject);
    }

    [Fact]
    public void Merge_import_adds_new_projects_and_only_new_mappings()
    {
        var (source, hyper, _) = TestData.HyperCarsDesk();
        var json = ConfigTransfer.Export(source.Config);

        // Target already has HyperCars (same id) with one of the mappings, plus its own project.
        var target = new Workspace(new AppConfig());
        var mine = target.CreateProject("WorkTrace");
        var existing = target.CreateProject("HyperCars (local)");
        existing.Id = hyper.Id;
        target.AddConversation(existing, "Already here", TestData.ChatGpt(1));
        target.Preferences.MaxBrowsersPerProvider = 12;

        var summary = target.Import(ConfigTransfer.Parse(json), ImportMode.Merge);

        Assert.Equal(1, summary.ProjectsAdded);
        Assert.Equal(1, summary.ProjectsMerged);
        Assert.Equal(1, summary.ConversationsSkipped);
        Assert.Equal(["WorkTrace", "HyperCars (local)", "Sirens"], target.Projects.Select(p => p.Name));
        Assert.Equal(4, existing.ChatGptConversations.Count);
        Assert.Equal("Already here", existing.ChatGptConversations[0].Name);
        Assert.Equal(3, existing.ClaudeConversations.Count);
        Assert.Equal(12, target.Preferences.MaxBrowsersPerProvider);
        Assert.Contains(mine, target.Projects);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"something\": 1 }")]
    [InlineData("{ \"format\": \"other-app\", \"projects\": [] }")]
    [InlineData("{ \"format\": \"forgedesk-export\", \"schemaVersion\": 99, \"projects\": [] }")]
    public void Bad_import_files_are_rejected_with_a_readable_error(string json)
    {
        var ex = Assert.Throws<ConfigImportException>(() => ConfigTransfer.Parse(json));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void A_raw_config_file_can_also_be_imported()
    {
        var (source, _, _) = TestData.HyperCarsDesk();
        var raw = ConfigStore.Serialize(source.Config);
        var preview = ConfigTransfer.Parse(raw);
        Assert.Equal(2, preview.ProjectCount);
    }

    [Fact]
    public void Import_does_not_share_objects_with_the_parsed_file()
    {
        var (source, _, _) = TestData.HyperCarsDesk();
        var preview = ConfigTransfer.Parse(ConfigTransfer.Export(source.Config));
        var target = new Workspace(new AppConfig());
        target.Import(preview, ImportMode.Replace);
        target.Projects[0].Name = "Changed";
        Assert.Equal("HyperCars", preview.Data.Projects[0].Name);
    }
}
