using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Tests;

internal static class TestData
{
    public static string ChatGpt(int n) => $"https://chatgpt.com/c/{Guid(n)}";

    public static string Claude(int n) => $"https://claude.ai/chat/{Guid(n)}";

    public static string Guid(int n) => $"{n:x8}-0000-4000-8000-{n:x12}";

    /// <summary>The HyperCars example from the product brief.</summary>
    public static (Workspace Workspace, Project HyperCars, Project Sirens) HyperCarsDesk()
    {
        var ws = new Workspace(new AppConfig());
        var hyper = ws.CreateProject("HyperCars", "#E5484D");
        ws.AddConversation(hyper, "Overall development", ChatGpt(1));
        ws.AddConversation(hyper, "Visual design", ChatGpt(2));
        ws.AddConversation(hyper, "AI and racing mechanics", ChatGpt(3));
        ws.AddConversation(hyper, "Release planning", ChatGpt(4));
        ws.AddConversation(hyper, "Main implementation", Claude(1));
        ws.AddConversation(hyper, "Multiplayer development", Claude(2));
        ws.AddConversation(hyper, "AI refinement", Claude(3));

        var sirens = ws.CreateProject("Sirens");
        ws.AddConversation(sirens, "Story", ChatGpt(10));
        ws.AddConversation(sirens, "Engine", Claude(10));
        ws.ActivateProject(hyper);
        return (ws, hyper, sirens);
    }
}

/// <summary>Temporary directory deleted at the end of a test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgedesk-tests-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
