using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Tests;

public class WorkspaceTests
{
    [Fact]
    public void New_workspace_has_no_hardcoded_projects()
    {
        var ws = new Workspace(new AppConfig());
        Assert.Empty(ws.Projects);
        Assert.Null(ws.ActiveProject);
    }

    [Fact]
    public void Creating_projects_assigns_ids_order_and_cleans_names()
    {
        var ws = new Workspace(new AppConfig());
        var a = ws.CreateProject("  HyperCars  ", "e5484d");
        var b = ws.CreateProject("", null);

        Assert.Equal("HyperCars", a.Name);
        Assert.Equal("#E5484D", a.Color);
        Assert.Equal("Untitled project", b.Name);
        Assert.Null(b.Color);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal([0, 1], ws.Projects.Select(p => p.Order));
        Assert.Throws<ArgumentException>(() => ws.CreateProject("Bad", "red"));
    }

    [Fact]
    public void Renaming_requires_a_name()
    {
        var ws = new Workspace(new AppConfig());
        var p = ws.CreateProject("Old");
        ws.RenameProject(p, " Ember & Ash ");
        Assert.Equal("Ember & Ash", p.Name);
        Assert.Throws<ArgumentException>(() => ws.RenameProject(p, "   "));
        Assert.Equal("Ember & Ash", p.Name);
    }

    [Fact]
    public void Projects_can_be_reordered()
    {
        var ws = new Workspace(new AppConfig());
        var a = ws.CreateProject("A");
        var b = ws.CreateProject("B");
        var c = ws.CreateProject("C");

        Assert.True(ws.MoveProject(c, -1));
        Assert.Equal(["A", "C", "B"], ws.Projects.Select(p => p.Name));
        Assert.False(ws.MoveProject(a, -1));
        ws.MoveProjectTo(a, 2);
        Assert.Equal(["C", "B", "A"], ws.Projects.Select(p => p.Name));
        Assert.Equal([0, 1, 2], ws.Projects.Select(p => p.Order));
        Assert.Same(b, ws.Projects[1]);
    }

    [Fact]
    public void Reordering_skips_over_archived_projects()
    {
        var ws = new Workspace(new AppConfig());
        var a = ws.CreateProject("A");
        var hidden = ws.CreateProject("Hidden");
        var c = ws.CreateProject("C");
        ws.SetArchived(hidden, true);

        ws.MoveProject(c, -1);
        Assert.Equal(["C", "A"], ws.VisibleProjects.Select(p => p.Name));
        Assert.Contains(hidden, ws.Projects);
    }

    [Fact]
    public void Archiving_the_active_project_activates_a_neighbour_and_restore_works()
    {
        var ws = new Workspace(new AppConfig());
        var a = ws.CreateProject("A");
        var b = ws.CreateProject("B");
        ws.ActivateProject(a);

        ws.SetArchived(a, true);
        Assert.Same(b, ws.ActiveProject);
        Assert.DoesNotContain(a, ws.VisibleProjects);
        Assert.Throws<InvalidOperationException>(() => ws.ActivateProject(a));

        ws.SetArchived(a, false);
        Assert.Contains(a, ws.VisibleProjects);
        Assert.Equal(["A", "B"], ws.VisibleProjects.Select(p => p.Name));
    }

    [Fact]
    public void Removing_projects_keeps_a_sensible_active_project()
    {
        var ws = new Workspace(new AppConfig());
        var a = ws.CreateProject("A");
        var b = ws.CreateProject("B");
        var c = ws.CreateProject("C");
        ws.ActivateProject(c);

        ws.RemoveProject(c);
        Assert.Same(b, ws.ActiveProject);
        ws.RemoveProject(a);
        Assert.Same(b, ws.ActiveProject);
        ws.RemoveProject(b);
        Assert.Null(ws.ActiveProject);
        Assert.Empty(ws.Projects);
        Assert.Null(ws.Config.ActiveProjectId);
    }

    [Fact]
    public void Conversations_are_added_to_the_side_matching_their_link()
    {
        var ws = new Workspace(new AppConfig());
        var p = ws.CreateProject("HyperCars");
        var gpt = ws.AddConversation(p, "Overall development", TestData.ChatGpt(1));
        var claude = ws.AddConversation(p, "Main implementation", TestData.Claude(1));

        Assert.Equal(Provider.ChatGpt, gpt.Provider);
        Assert.Equal(Provider.Claude, claude.Provider);
        Assert.Single(p.ChatGptConversations);
        Assert.Single(p.ClaudeConversations);
        Assert.NotEqual(gpt.Id, claude.Id);
    }

    [Fact]
    public void Invalid_or_duplicate_conversations_are_rejected()
    {
        var ws = new Workspace(new AppConfig());
        var p = ws.CreateProject("P");
        ws.AddConversation(p, "First", TestData.ChatGpt(1));

        var invalid = Assert.Throws<ArgumentException>(() => ws.AddConversation(p, "Bad", "https://example.com/chat"));
        Assert.Contains("ChatGPT", invalid.Message);
        var duplicate = Assert.Throws<ArgumentException>(() => ws.AddConversation(p, "Again", TestData.ChatGpt(1) + "?model=x"));
        Assert.Contains("already mapped", duplicate.Message);
        Assert.Single(p.ChatGptConversations);
    }

    [Fact]
    public void A_conversation_can_be_mapped_into_several_projects()
    {
        var ws = new Workspace(new AppConfig());
        var a = ws.CreateProject("A");
        var b = ws.CreateProject("B");
        var m1 = ws.AddConversation(a, "Shared", TestData.Claude(5));
        var m2 = ws.AddConversation(b, "Shared too", TestData.Claude(5));

        Assert.NotEqual(m1.Id, m2.Id);
        Assert.Equal([b], ws.ProjectsMapping(Provider.Claude, TestData.Claude(5), except: a));
    }

    [Fact]
    public void Conversations_can_be_renamed_edited_reordered_and_removed()
    {
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        var list = hyper.ChatGptConversations;
        var visual = list[1];

        ws.RenameConversation(hyper, visual, "Art direction");
        Assert.Equal("Art direction", visual.Name);

        ws.UpdateConversation(hyper, visual, "Art direction", TestData.ChatGpt(99));
        Assert.Equal(TestData.ChatGpt(99), visual.Url);
        Assert.Throws<ArgumentException>(() => ws.UpdateConversation(hyper, visual, "x", TestData.Claude(1)));
        Assert.Throws<ArgumentException>(() => ws.UpdateConversation(hyper, visual, "x", TestData.ChatGpt(1)));
        Assert.Throws<ArgumentException>(() => ws.UpdateConversation(hyper, visual, " ", TestData.ChatGpt(99)));

        Assert.True(ws.MoveConversation(hyper, visual, 2));
        Assert.Equal(["Overall development", "AI and racing mechanics", "Release planning", "Art direction"], list.Select(m => m.Name));
        Assert.Equal([0, 1, 2, 3], list.Select(m => m.Order));

        ws.RemoveConversation(hyper, visual);
        Assert.Equal(3, list.Count);
        Assert.Equal([0, 1, 2], list.Select(m => m.Order));
    }

    [Fact]
    public void Selection_is_independent_per_side()
    {
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        var release = hyper.ChatGptConversations[3];
        var multiplayer = hyper.ClaudeConversations[1];

        ws.SelectConversation(hyper, Provider.ChatGpt, release);
        Assert.Same(release, ws.ResolveSelection(hyper, Provider.ChatGpt));
        Assert.Same(hyper.ClaudeConversations[0], ws.ResolveSelection(hyper, Provider.Claude));

        ws.SelectConversation(hyper, Provider.Claude, multiplayer);
        Assert.Same(release, ws.ResolveSelection(hyper, Provider.ChatGpt));
        Assert.Same(multiplayer, ws.ResolveSelection(hyper, Provider.Claude));

        Assert.Throws<InvalidOperationException>(() => ws.SelectConversation(hyper, Provider.Claude, release));
    }

    [Fact]
    public void Dropdowns_only_contain_the_projects_own_mappings()
    {
        var (_, hyper, sirens) = TestData.HyperCarsDesk();
        Assert.Equal(4, hyper.Conversations(Provider.ChatGpt).Count);
        Assert.Equal(3, hyper.Conversations(Provider.Claude).Count);
        Assert.Equal(["Story"], sirens.Conversations(Provider.ChatGpt).Select(m => m.Name));
        Assert.Equal(["Engine"], sirens.Conversations(Provider.Claude).Select(m => m.Name));
    }

    [Fact]
    public void Switching_projects_restores_each_projects_desk()
    {
        var (ws, hyper, sirens) = TestData.HyperCarsDesk();
        ws.SelectConversation(hyper, Provider.ChatGpt, hyper.ChatGptConversations[2]);
        ws.SelectConversation(hyper, Provider.Claude, hyper.ClaudeConversations[1]);
        ws.SetSplitRatio(hyper, 0.35);
        ws.ToggleFocus(hyper, Provider.Claude);

        ws.ActivateProject(sirens);
        ws.SetSplitRatio(sirens, 0.7);
        Assert.Equal("Story", ws.ResolveSelection(sirens, Provider.ChatGpt)!.Name);

        ws.ActivateProject(hyper);
        Assert.Same(hyper, ws.ActiveProject);
        Assert.Equal("AI and racing mechanics", ws.ResolveSelection(hyper, Provider.ChatGpt)!.Name);
        Assert.Equal("Multiplayer development", ws.ResolveSelection(hyper, Provider.Claude)!.Name);
        Assert.Equal(0.35, hyper.Desk.SplitRatio, 3);
        Assert.Equal(DeskFocus.Claude, hyper.Desk.Focus);
        Assert.Equal(0.7, sirens.Desk.SplitRatio, 3);
        Assert.Equal(DeskFocus.None, sirens.Desk.Focus);
    }

    [Fact]
    public void Focus_toggles_between_maximised_and_split()
    {
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        ws.SetSplitRatio(hyper, 0.4);
        Assert.Equal(DeskFocus.ChatGpt, ws.ToggleFocus(hyper, Provider.ChatGpt));
        Assert.Equal(DeskFocus.Claude, ws.ToggleFocus(hyper, Provider.Claude));
        Assert.Equal(DeskFocus.None, ws.ToggleFocus(hyper, Provider.Claude));
        Assert.Equal(0.4, hyper.Desk.SplitRatio, 3);
    }

    [Fact]
    public void Split_ratio_is_clamped()
    {
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        ws.SetSplitRatio(hyper, 0.0);
        Assert.Equal(ProjectDesk.MinSplitRatio, hyper.Desk.SplitRatio);
        ws.SetSplitRatio(hyper, double.NaN);
        Assert.Equal(ProjectDesk.DefaultSplitRatio, hyper.Desk.SplitRatio);
        ws.SetSplitRatio(hyper, 4);
        Assert.Equal(ProjectDesk.MaxSplitRatio, hyper.Desk.SplitRatio);
    }

    [Fact]
    public void Removing_the_selected_mapping_falls_back_to_a_neighbour()
    {
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        var selected = hyper.ChatGptConversations[1];
        ws.SelectConversation(hyper, Provider.ChatGpt, selected);

        ws.RemoveConversation(hyper, selected);
        Assert.Equal("AI and racing mechanics", ws.ResolveSelection(hyper, Provider.ChatGpt)!.Name);

        foreach (var m in hyper.ChatGptConversations.ToList())
        {
            ws.RemoveConversation(hyper, m);
        }

        Assert.Null(ws.ResolveSelection(hyper, Provider.ChatGpt));
        Assert.Null(hyper.Desk.SelectedChatGptId);
        Assert.NotNull(ws.ResolveSelection(hyper, Provider.Claude));
    }

    [Fact]
    public void Stale_selection_ids_fall_back_to_the_first_mapping()
    {
        var (ws, hyper, _) = TestData.HyperCarsDesk();
        hyper.Desk.SelectedClaudeId = "does-not-exist";
        Assert.Same(hyper.ClaudeConversations[0], ws.ResolveSelection(hyper, Provider.Claude));
    }

    [Fact]
    public void Project_shortcuts_activate_by_position_and_cycle()
    {
        var ws = new Workspace(new AppConfig());
        var a = ws.CreateProject("A");
        var b = ws.CreateProject("B");
        var c = ws.CreateProject("C");
        ws.SetArchived(b, true);

        Assert.True(ws.ActivateProjectAt(1));
        Assert.Same(c, ws.ActiveProject);
        Assert.False(ws.ActivateProjectAt(5));
        ws.ActivateRelative(+1);
        Assert.Same(a, ws.ActiveProject);
        ws.ActivateRelative(-1);
        Assert.Same(c, ws.ActiveProject);
    }

    [Fact]
    public void There_is_no_artificial_limit_on_projects_or_mappings()
    {
        var ws = new Workspace(new AppConfig());
        for (var i = 0; i < 150; i++)
        {
            var p = ws.CreateProject($"Project {i}");
            for (var j = 0; j < 40; j++)
            {
                ws.AddConversation(p, $"G{j}", TestData.ChatGpt(i * 1000 + j));
                ws.AddConversation(p, $"C{j}", TestData.Claude(i * 1000 + j));
            }
        }

        Assert.Equal(150, ws.Projects.Count);
        Assert.All(ws.Projects, p => Assert.Equal(40, p.ClaudeConversations.Count));
    }
}
