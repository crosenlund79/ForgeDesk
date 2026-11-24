using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ForgeDesk.Browser;
using ForgeDesk.Core;
using ForgeDesk.Core.Configuration;
using ForgeDesk.Core.Models;
using ForgeDesk.Infrastructure;
using ForgeDesk.Views;

namespace ForgeDesk;

/// <summary>
/// Unattended end-to-end check used by CI on a real Windows machine:
///   ForgeDesk.exe --smoke-test &lt;output folder&gt;
/// It starts the real UI with a throwaway data folder, loads both provider websites
/// (signed out), exercises project switching, instance reuse, split/focus restoration,
/// drift detection, dialogs, themes and persistence, then writes report.json and screenshots.
/// Exit code 0 means every critical check passed. Provider page results are informational
/// because CI machines are signed out and may be challenged by the providers' bot protection.
/// </summary>
internal static class SmokeTest
{
    private const string FakeId = "00000000-0000-4000-8000-000000000000";
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(40);

    public static void Seed(Workspace workspace)
    {
        if (workspace.Projects.Count > 0)
        {
            return;
        }

        var alpha = workspace.CreateProject("Alpha", "#3E63DD");
        workspace.AddConversation(alpha, "ChatGPT home", "https://chatgpt.com/");
        workspace.AddConversation(alpha, "GPTs", "https://chatgpt.com/gpts");
        workspace.AddConversation(alpha, "Claude home", "https://claude.ai/");
        workspace.AddConversation(alpha, "Recents", "https://claude.ai/recents");

        var beta = workspace.CreateProject("Beta", "#E5484D");
        workspace.AddConversation(beta, "Conversation link (signed out)", $"https://chatgpt.com/c/{FakeId}");
        workspace.AddConversation(beta, "Claude conversation link (signed out)", $"https://claude.ai/chat/{FakeId}");

        workspace.CreateProject("Gamma (empty)", "#46A758");
        workspace.ActivateProject(alpha);
    }

    public static async Task<int> RunAsync(MainWindow window, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var report = new Report();
        report.Info["os"] = Environment.OSVersion.VersionString;
        report.Info["started"] = DateTimeOffset.Now.ToString("O");
        var ws = window.Workspace;

        try
        {
            ThemeManager.Apply(ThemePreference.Dark);
            window.WindowState = WindowState.Maximized;
            window.Activate();
            await WaitFor(() => window.IsLoaded, TimeSpan.FromSeconds(20));
            report.Check("Main window loaded", window.IsLoaded, critical: true);

            var runtime = BrowserEnvironment.TryGetRuntimeVersion();
            report.Info["webView2Runtime"] = runtime;
            report.Check("WebView2 Runtime available", runtime is not null, critical: true, runtime);
            if (runtime is null)
            {
                await Screenshot(window, outputDirectory, "00-runtime-missing", report);
                return Finish(report, outputDirectory);
            }

            var alpha = ws.Projects.First(p => p.Name == "Alpha");
            var beta = ws.Projects.First(p => p.Name == "Beta");
            var gamma = ws.Projects.First(p => p.Name.StartsWith("Gamma", StringComparison.Ordinal));
            var left = window.LeftPane;
            var right = window.RightPane;

            // 1. Both providers load on the first project.
            var alphaLeft = await CurrentViewLoaded(left, report, "Alpha · ChatGPT");
            var alphaRight = await CurrentViewLoaded(right, report, "Alpha · Claude");
            report.Check("Both sides created a browser view", alphaLeft is not null && alphaRight is not null, critical: true);
            if (alphaLeft is null || alphaRight is null)
            {
                return Finish(report, outputDirectory);
            }

            report.Info["userAgent"] = alphaLeft.WebView.CoreWebView2?.Settings.UserAgent;
            await Task.Delay(4000); // let pages paint (or show their challenge pages)
            await Screenshot(window, outputDirectory, "01-alpha-desk", report);
            await Preview(alphaLeft, outputDirectory, "01-alpha-chatgpt-page", report);
            await Preview(alphaRight, outputDirectory, "01-alpha-claude-page", report);
            var alphaLeftNavigations = alphaLeft.DocumentNavigations;
            var alphaRightNavigations = alphaRight.DocumentNavigations;

            // 2. Independent selection on one side.
            var claudeSelectionBefore = alpha.Desk.SelectedClaudeId;
            report.Info["alphaChatGptNotice"] = left.NoticeMessage;
            report.Info["alphaClaudeNotice"] = right.NoticeMessage;
            var gpts = alpha.ChatGptConversations[1];
            left.SelectMapping(gpts);
            await Task.Delay(300);
            var gptsView = left.CurrentView;
            report.Check("Selecting a ChatGPT conversation leaves Claude untouched",
                ReferenceEquals(right.CurrentView, alphaRight) && alpha.Desk.SelectedClaudeId == claudeSelectionBefore &&
                alpha.Desk.SelectedChatGptId == gpts.Id && !ReferenceEquals(gptsView, alphaLeft), critical: true);
            if (gptsView is not null)
            {
                await WaitForNavigation(gptsView);
            }

            left.SelectMapping(alpha.ChatGptConversations[0]);
            await Task.Delay(300);
            report.Check("Switching back to a conversation reuses its live instance",
                ReferenceEquals(left.CurrentView, alphaLeft), critical: true);
            // Informational: the page itself may redirect (e.g. bot checks) while hidden.
            report.Check("…without reloading the page", alphaLeft.DocumentNavigations == alphaLeftNavigations, critical: false,
                $"document navigations {alphaLeftNavigations} → {alphaLeft.DocumentNavigations}");

            // 3. Split ratio and project switching.
            ws.SetSplitRatio(alpha, 0.5);
            window.ApplyDeskLayout();
            ws.ActivateProject(beta);
            await Task.Delay(300);
            var betaLeft = await CurrentViewLoaded(left, report, "Beta · ChatGPT (signed-out conversation link)");
            var betaRight = await CurrentViewLoaded(right, report, "Beta · Claude (signed-out conversation link)");
            report.Check("Switching project shows that project's conversations",
                betaLeft is not null && !ReferenceEquals(betaLeft, alphaLeft) && betaRight is not null && !ReferenceEquals(betaRight, alphaRight), critical: true);
            ws.SetSplitRatio(beta, 0.3);
            window.ApplyDeskLayout();
            await Task.Delay(2500);
            report.Info["betaChatGptDrifted"] = betaLeft?.IsDrifted;
            report.Info["betaClaudeDrifted"] = betaRight?.IsDrifted;
            report.Info["betaChatGptNotice"] = left.NoticeMessage;
            report.Info["betaClaudeNotice"] = right.NoticeMessage;
            await Screenshot(window, outputDirectory, "02-beta-desk-30-70", report);

            ws.ActivateProject(alpha);
            await Task.Delay(500);
            report.Check("Returning to a project restores the same live browser instances",
                ReferenceEquals(left.CurrentView, alphaLeft) && ReferenceEquals(right.CurrentView, alphaRight), critical: true);
            report.Check("…without reloading either page",
                alphaLeft.DocumentNavigations == alphaLeftNavigations && alphaRight.DocumentNavigations == alphaRightNavigations, critical: false,
                $"ChatGPT document navigations {alphaLeftNavigations}→{alphaLeft.DocumentNavigations}, Claude {alphaRightNavigations}→{alphaRight.DocumentNavigations}");
            report.Check("Hidden instances stay alive while another project is shown",
                left.Pool.Count >= 3 && right.Pool.Count >= 2, critical: true, $"ChatGPT pool {left.Pool.Count}, Claude pool {right.Pool.Count}");
            report.Check("Split proportions restored per project (Alpha 50/50)", Math.Abs(window.MeasuredSplitRatio - 0.5) < 0.03, critical: true, $"{window.MeasuredSplitRatio:F3}");
            ws.ActivateProject(beta);
            await Task.Delay(500);
            report.Check("Split proportions restored per project (Beta 30/70)", Math.Abs(window.MeasuredSplitRatio - 0.3) < 0.03, critical: true, $"{window.MeasuredSplitRatio:F3}");
            ws.ActivateProject(alpha);
            await Task.Delay(300);

            // 4. Focus (maximise) mode.
            window.ToggleFocus(Provider.Claude);
            await Task.Delay(400);
            report.Check("Maximise Claude hides the ChatGPT side", left.Visibility != Visibility.Visible && right.Visibility == Visibility.Visible, critical: true);
            await Screenshot(window, outputDirectory, "03-alpha-claude-maximised", report);
            window.ToggleFocus(Provider.Claude);
            await Task.Delay(400);
            report.Check("Restore returns to the previous split", left.Visibility == Visibility.Visible && Math.Abs(window.MeasuredSplitRatio - 0.5) < 0.03, critical: true);

            // 5. Navigation drift and "Return".
            alphaLeft.WebView.CoreWebView2?.Navigate("https://chatgpt.com/gpts");
            await WaitFor(() => alphaLeft.IsDrifted, TimeSpan.FromSeconds(15));
            report.Check("Navigating inside the page to another page is detected (drift)", alphaLeft.IsDrifted, critical: true, Log.Safe(alphaLeft.CurrentUrl));
            report.Check("Drift does not change the mapping", alpha.ChatGptConversations[0].Url == "https://chatgpt.com/" && alpha.ChatGptConversations.Count == 2, critical: true);
            await Task.Delay(1500);
            await Screenshot(window, outputDirectory, "04-drift-notice", report);
            alphaLeft.NavigateHome();
            await WaitFor(() => !alphaLeft.IsDrifted, TimeSpan.FromSeconds(15));
            report.Check("“Return” goes back to the mapped conversation", !alphaLeft.IsDrifted, critical: true, Log.Safe(alphaLeft.CurrentUrl));

            // 5b. Background streaming: a hidden instance keeps receiving a streamed response.
            await BackgroundStreamingCheck(window, alphaLeft, alpha, beta, report);

            // 6. Empty project state.
            ws.ActivateProject(gamma);
            await Task.Delay(800);
            report.Check("Empty project shows empty states, no browser", left.CurrentView is null && right.CurrentView is null, critical: true);
            await Screenshot(window, outputDirectory, "05-empty-project", report);

            // 7. Keyboard shortcuts.
            window.HandleShortcut(System.Windows.Input.Key.D1, System.Windows.Input.ModifierKeys.Control);
            await Task.Delay(300);
            report.Check("Ctrl+1 opens the first project", ws.ActiveProject == alpha, critical: true);
            window.HandleShortcut(System.Windows.Input.Key.Tab, System.Windows.Input.ModifierKeys.Control);
            await Task.Delay(300);
            report.Check("Ctrl+Tab opens the next project", ws.ActiveProject == beta, critical: true);
            var altGrIgnored = !window.HandleShortcut(System.Windows.Input.Key.D2, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt);
            report.Check("AltGr (Ctrl+Alt) combinations are left to the page", altGrIgnored && ws.ActiveProject == beta, critical: true);
            ws.ActivateProject(alpha);
            await Task.Delay(300);

            // 8. Dialogs and windows render without errors.
            await ShowAndCapture(new ProjectsWindow(window, ws, alpha), outputDirectory, "06-projects-window", report);
            await ShowAndCapture(ConversationDialog.ForPreview(window, ws, alpha, Provider.ChatGpt, "https://claude.ai/chat/" + FakeId, "Main implementation"), outputDirectory, "07-conversation-dialog", report);
            await ShowAndCapture(ProjectDialog.ForPreview(window, ws), outputDirectory, "08-project-dialog", report);
            await ShowAndCapture(ChoiceDialog.Build(window, "Keyboard shortcuts", "The active side is the one you last clicked or typed in.",
                [new ChoiceDialog.Choice("ok", "OK", IsPrimary: true)], MainWindow.ShortcutsText), outputDirectory, "09-shortcuts", report);

            // 9. Light theme.
            ThemeManager.Apply(ThemePreference.Light);
            await Task.Delay(2500);
            await Screenshot(window, outputDirectory, "10-light-theme", report);
            ThemeManager.Apply(ThemePreference.Dark);
            report.Check("Theme switching", true, critical: true);

            // 10. Browser instance lifecycle: lowering the limit releases hidden instances only.
            var before = left.Pool.Count;
            ws.Preferences.KeepHiddenBrowsersMinutes = 0;
            ws.Preferences.MaxBrowsersPerProvider = 1;
            left.TrimPool();
            right.TrimPool();
            report.Check("Hidden instances above the limit are released; the visible one is kept",
                left.Pool.Count == 1 && ReferenceEquals(left.CurrentView, alphaLeft) && right.Pool.Count == 1, critical: true,
                $"ChatGPT pool {before} → {left.Pool.Count}");
            ws.Preferences.MaxBrowsersPerProvider = Preferences.DefaultMaxBrowsersPerProvider;
            ws.Preferences.KeepHiddenBrowsersMinutes = Preferences.DefaultKeepHiddenMinutes;

            // 11. Persistence round-trip through the real settings file.
            left.SelectMapping(alpha.ChatGptConversations[1]);
            ws.SetSplitRatio(alpha, 0.42);
            report.Check("Settings saved", window.SaveNow(), critical: true);
            var reloaded = new ConfigStore(window.Paths.ConfigDirectory).Load();
            var reAlpha = reloaded.Config.Projects.FirstOrDefault(p => p.Id == alpha.Id);
            report.Check("Settings reload with selections and split intact",
                reloaded.Outcome == ConfigLoadOutcome.Loaded && reAlpha is not null &&
                reAlpha.Desk.SelectedChatGptId == alpha.ChatGptConversations[1].Id &&
                Math.Abs(reAlpha.Desk.SplitRatio - 0.42) < 0.001 && reloaded.Config.ActiveProjectId == alpha.Id,
                critical: true);
            var configText = File.ReadAllText(Path.Combine(window.Paths.ConfigDirectory, ConfigStore.FileName));
            report.Check("Settings file contains no cookies or tokens",
                !configText.Contains("cookie", StringComparison.OrdinalIgnoreCase) && !configText.Contains("token", StringComparison.OrdinalIgnoreCase), critical: true);
        }
        catch (Exception ex)
        {
            report.Info["exception"] = ex.ToString();
            report.Check("No unexpected exceptions", false, critical: true, ex.Message);
        }

        return Finish(report, outputDirectory);
    }

    /// <summary>
    /// Serves a local page that consumes a slowly streamed HTTP response (like a chat answer),
    /// hides that browser instance by switching project, and verifies the stream and the page's
    /// scripts kept running while hidden. This checks the browser-level mechanism ForgeDesk relies
    /// on for background generation; whether a provider's own site keeps generating is separate.
    /// </summary>
    private static async Task BackgroundStreamingCheck(MainWindow window, BrowserView view, Project shownProject, Project otherProject, Report report)
    {
        const int Chunks = 40;
        const int ChunkDelayMs = 300;
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            report.Check("Hidden instance keeps streaming (local test server)", false, critical: false, "could not start local server: " + ex.Message);
            return;
        }

        const string Page = """
            <!doctype html><title>stream test</title><body style="background:#202020;color:#ddd;font:14px sans-serif">
            <p id="s">streaming…</p>
            <script>
            window.ticks = 0; window.hiddenTicks = 0; window.chunks = 0; window.hiddenChunks = 0; window.done = false;
            setInterval(() => { window.ticks++; if (document.hidden) window.hiddenTicks++; }, 500);
            (async () => {
              const reader = (await fetch('/stream')).body.getReader();
              for (;;) {
                const { done } = await reader.read();
                if (done) break;
                window.chunks++; if (document.hidden) window.hiddenChunks++;
                document.getElementById('s').textContent = 'chunks: ' + window.chunks;
              }
              window.done = true;
            })();
            </script>
            """;

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var response = context.Response;
                        if (context.Request.Url?.AbsolutePath == "/stream")
                        {
                            response.SendChunked = true;
                            response.ContentType = "text/plain";
                            for (var i = 0; i < Chunks; i++)
                            {
                                var bytes = System.Text.Encoding.UTF8.GetBytes($"chunk {i}\n");
                                await response.OutputStream.WriteAsync(bytes);
                                await response.OutputStream.FlushAsync();
                                await Task.Delay(ChunkDelayMs);
                            }
                        }
                        else
                        {
                            response.ContentType = "text/html; charset=utf-8";
                            var bytes = System.Text.Encoding.UTF8.GetBytes(Page);
                            await response.OutputStream.WriteAsync(bytes);
                        }

                        response.Close();
                    }
                    catch (Exception)
                    {
                    }
                });
            }
        });

        var core = view.WebView.CoreWebView2;
        if (core is null)
        {
            report.Check("Hidden instance keeps streaming (local test server)", false, critical: true, "browser not ready");
            return;
        }

        core.Navigate($"http://localhost:{port}/");
        await WaitFor(() => view.CurrentUrl?.StartsWith($"http://localhost:{port}", StringComparison.Ordinal) == true && !view.IsLoading, TimeSpan.FromSeconds(15));
        await Task.Delay(1000);
        var before = await ReadState(core);

        window.Workspace.ActivateProject(otherProject);   // hides the instance
        await Task.Delay(7000);
        var whileHidden = await ReadState(core);
        window.Workspace.ActivateProject(shownProject);   // shows it again
        await Task.Delay(ChunkDelayMs * Chunks);           // let the stream finish
        var after = await ReadState(core);

        report.Info["streamBefore"] = before;
        report.Info["streamWhileHidden"] = whileHidden;
        report.Info["streamAfter"] = after;
        using var hiddenDoc = JsonDocument.Parse(whileHidden ?? "{}");
        var root = hiddenDoc.RootElement;
        var wasHidden = root.TryGetProperty("visibility", out var vis) && vis.GetString() == "hidden";
        var hiddenChunks = root.TryGetProperty("hiddenChunks", out var hc) ? hc.GetInt32() : 0;
        var hiddenTicks = root.TryGetProperty("hiddenTicks", out var ht) ? ht.GetInt32() : 0;
        report.Check("Hidden instance really is hidden while another project is shown", wasHidden, critical: true, whileHidden);
        report.Check("Hidden instance keeps receiving a streamed response", hiddenChunks >= 10, critical: true, $"{hiddenChunks} chunks arrived while hidden");
        report.Check("Hidden instance keeps running page scripts (timers may be throttled)", hiddenTicks >= 3, critical: false, $"{hiddenTicks} timer ticks while hidden");
        using var afterDoc = JsonDocument.Parse(after ?? "{}");
        report.Check("Stream completed; page state intact after showing it again",
            afterDoc.RootElement.TryGetProperty("done", out var done) && done.GetBoolean() &&
            afterDoc.RootElement.TryGetProperty("chunks", out var total) && total.GetInt32() == Chunks, critical: true, after);

        listener.Stop();
        view.NavigateHome();
        await WaitFor(() => !view.IsDrifted && !view.IsLoading, TimeSpan.FromSeconds(15));
    }

    private static async Task<string?> ReadState(Microsoft.Web.WebView2.Core.CoreWebView2 core)
    {
        try
        {
            var json = await core.ExecuteScriptAsync(
                "JSON.stringify({ticks: window.ticks, hiddenTicks: window.hiddenTicks, chunks: window.chunks, hiddenChunks: window.hiddenChunks, done: window.done, visibility: document.visibilityState})");
            // ExecuteScriptAsync returns the JSON encoding of the (string) result.
            return JsonSerializer.Deserialize<string>(json);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<BrowserView?> CurrentViewLoaded(PaneView pane, Report report, string label)
    {
        await WaitFor(() => pane.CurrentView is not null, TimeSpan.FromSeconds(10));
        var view = pane.CurrentView;
        if (view is null)
        {
            report.Pages[label] = new { created = false };
            return null;
        }

        var completed = await WaitForNavigation(view);
        await Task.Delay(1500); // allow client-side redirects to settle
        report.Pages[label] = new
        {
            created = true,
            navigationCompleted = completed,
            succeeded = view.LastNavigationSucceeded,
            httpStatus = view.LastHttpStatus,
            webError = view.LastWebErrorStatus,
            finalPage = Log.Safe(view.CurrentUrl),
            title = view.Title,
            drifted = view.IsDrifted,
            crashed = view.IsCrashed,
            error = view.ErrorText,
        };
        return view;
    }

    private static async Task<bool> WaitForNavigation(BrowserView view)
    {
        var finished = await Task.WhenAny(view.FirstNavigationCompleted.Task, Task.Delay(NavigationTimeout));
        return finished == view.FirstNavigationCompleted.Task;
    }

    private static async Task WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(100);
        }
    }

    private static async Task ShowAndCapture(Window dialog, string folder, string name, Report report)
    {
        try
        {
            dialog.Show();
            await Task.Delay(700);
            await Screenshot(dialog, folder, name, report);
            dialog.Close();
            report.Check($"Window renders: {name}", true, critical: true);
        }
        catch (Exception ex)
        {
            report.Check($"Window renders: {name}", false, critical: true, ex.Message);
        }
    }

    private static async Task Preview(BrowserView view, string folder, string name, Report report)
    {
        var error = await view.CapturePreviewAsync(Path.Combine(folder, name + ".png"));
        if (error is null)
        {
            report.Screenshots.Add(name + ".png");
        }
    }

    private static async Task Screenshot(Window window, string folder, string name, Report report)
    {
        await Task.Delay(150);
        try
        {
            window.Activate();
            var hwnd = new WindowInteropHelper(window).Handle;
            if (!GetWindowRect(hwnd, out var rect))
            {
                return;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            var screen = GetDC(IntPtr.Zero);
            var memory = CreateCompatibleDC(screen);
            var bitmap = CreateCompatibleBitmap(screen, width, height);
            var old = SelectObject(memory, bitmap);
            BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020 | 0x40000000);
            SelectObject(memory, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            await using var stream = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(stream);
            report.Screenshots.Add(name + ".png");
        }
        catch (Exception ex)
        {
            report.Info[$"screenshotError:{name}"] = ex.Message;
        }
    }

    private static int Finish(Report report, string folder)
    {
        report.Info["finished"] = DateTimeOffset.Now.ToString("O");
        var failed = report.Checks.Where(c => c.Critical && !c.Passed).ToList();
        report.Info["result"] = failed.Count == 0 ? "PASS" : $"FAIL ({failed.Count} critical checks failed)";
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(folder, "report.json"), json);
        Log.Info($"Smoke test finished: {report.Info["result"]}");
        return failed.Count == 0 ? 0 : 1;
    }

    private sealed class Report
    {
        public Dictionary<string, object?> Info { get; } = [];

        public List<CheckResult> Checks { get; } = [];

        public Dictionary<string, object> Pages { get; } = [];

        public List<string> Screenshots { get; } = [];

        public void Check(string name, bool passed, bool critical, string? detail = null) =>
            Checks.Add(new CheckResult(name, passed, critical, detail));
    }

    private sealed record CheckResult(string Name, bool Passed, bool Critical, string? Detail);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, int rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
}
