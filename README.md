# ForgeDesk

**One desk. Two assistants. Your projects.**

ForgeDesk is a lightweight Windows desktop app that puts the official **ChatGPT** (left) and **Claude** (right) websites side by side and organises your *existing* conversations by project. Pick a project, pick one of its ChatGPT conversations and one of its Claude conversations, and carry on working. Switch projects and your desk comes back exactly as you left it.

It is a chat handler — not a project manager, not an AI client. It uses the real provider websites (no APIs, no scraping, no recreated chat UI).

> Status: Prototype 001 — "The Two Desks". See [docs/PLAYTEST.md](docs/PLAYTEST.md) for the hands-on checklist.

## Download and run

1. Open the latest successful **Windows build** run under the repository's **Actions** tab.
2. Download the **ForgeDesk-win-x64** artifact (a ZIP), extract it anywhere, run `ForgeDesk.exe`.

No installer, no Visual Studio, no .NET install needed (the build is self-contained).

**Prerequisite:** the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/). It ships with Windows 11 and most up-to-date Windows 10 PCs. If it is missing, ForgeDesk says so and links to the installer.

Tagged builds (`git tag v0.1.0 && git push --tags`) also attach `ForgeDesk-win-x64.zip` to a GitHub Release.

## Using ForgeDesk

| | |
|---|---|
| **Projects** | Tabs along the top. `+` creates one. Right-click a tab to edit, reorder, archive or remove. ⌄ lists all projects (and archived ones). |
| **Mapping a conversation** | In a side's empty state or its `⋯` menu: **Add conversation…** and paste the conversation link (`chatgpt.com/c/…`, `claude.ai/chat/…`). The provider is detected from the link. Or **Browse ChatGPT/Claude**, open the conversation there, and choose **Map this page…** — the name is suggested from the page title. |
| **Picking conversations** | Each side's dropdown lists only that project's mapped conversations. The two sides are independent. |
| **Layout** | Drag the splitter (double-click = 50/50). Maximise a side with its ⤢ button or F11; press again to restore. Each project remembers its own split, maximised side and selections. |
| **Wandered off?** | If you click to another conversation inside the website, ForgeDesk shows a slim bar: **Return** takes you back to the mapped conversation; **Map this page…** adds it. Mappings are never changed or added silently. |
| **Manage everything** | Menu `⋯` → **Manage projects…**: rename, colour, reorder, archive, remove, and edit both conversation lists. |
| **Export / import** | Menu `⋯` → Export / Import configuration. Exports contain projects, mappings and preferences only — never sign-ins, cookies or conversation contents. Import can merge or replace (a backup is written before replacing). |

### Keyboard

| Keys | Action |
|---|---|
| `Ctrl+1` … `Ctrl+9` | Switch to project 1–9 |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` (or `Ctrl+PgDn` / `Ctrl+PgUp`) | Next / previous project |
| `Alt+1` … `Alt+9` | Conversation 1–9 on the active side |
| `Alt+↓` / `Alt+↑` | Next / previous conversation on the active side |
| `F6` | Switch active side (in maximised mode: maximise the other side) |
| `F11` | Maximise / restore the active side |
| `F5` | Reload the active side |

The active side is the one you last clicked or typed in. Normal browser keys work inside each side (copy/paste, `Ctrl+F`, `Ctrl+P`, zoom — zoom is remembered per provider). Shortcuts never use `Ctrl+Alt`, so AltGr characters (`@ { [ ] } \ € $`) on Nordic/German/etc. layouts keep working.

## Sessions, background conversations and resources

* **One persistent browser profile** (`%LOCALAPPDATA%\ForgeDesk\WebView2`) shared by both sides. You sign in once per provider; sign-ins survive restarts. ForgeDesk never sees or stores passwords or tokens — the profile is managed (and encrypted) by WebView2.
* **One live browser instance per conversation.** Switching conversation or project hides the current instance instead of closing it, so a response that is still streaming keeps going, and scroll position and unsent drafts are preserved. Coming back shows the same instance — no reload.
* **A conversation mapped in several projects shares one instance.**
* **Bounded resources.** Each side keeps up to *N* instances (default 6). Above that, hidden instances are released — least recently used first, and **only after they have been hidden for at least 10 minutes** and are not playing audio. A conversation you just switched away from is never closed immediately. Both values are in menu `⋯` → **Background conversations**. Instances load lazily: at start-up only the active project's two conversations load.
* **Not guaranteed:** whether a provider keeps generating in a hidden page is up to the provider's site and Chromium's background-page policies. Hidden pages keep their network connections, but timers are throttled. Hands-on testing is needed — see the playtest checklist.

## Sign-in notes

* Email sign-in (code/magic link) and password sign-in work like in Edge.
* "Continue with Google/Apple/Microsoft" opens the provider's sign-in popup in a small in-app window (title shows the site's host) that shares the session.
* **Google may refuse sign-in inside embedded browsers** ("This browser or app may not be secure"). ForgeDesk does **not** disguise itself to get around that. If it happens, sign in with the provider's email option (both ChatGPT and Claude support email login to the same account), or another identity provider.
* Links inside answers open in your default browser.

## Data and privacy

| Path (under `%LOCALAPPDATA%\ForgeDesk`) | Contents |
|---|---|
| `config.json` (+ `config.json.bak`) | Projects, mappings, per-project desk state, window placement, preferences. Written atomically; the previous good copy is kept. A damaged file is set aside (`config.corrupt-*.json`) and the backup restored. |
| `WebView2\` | Embedded browser profile (provider sign-in cookies, site data). |
| `logs\forgedesk.log` | Diagnostics (no page contents; URLs logged without query strings). |
| `backups\` | Automatic export written before an import replaces your projects. |

`--data-dir <folder>` (or `FORGEDESK_DATA_DIR`) uses another data folder, e.g. for a portable USB setup.

## Architecture

```
src/ForgeDesk.Core      net10.0 class library — no UI, fully unit-tested on any OS
  Models/               AppConfig, Project, ConversationMapping, ProjectDesk, Preferences (INotifyPropertyChanged)
  Urls/ConversationUrl  link validation, provider detection, normalisation, conversation identity
  Configuration/        ConfigStore (atomic JSON + backup + recovery), ConfigNormalizer, ChangeTracker (autosave), ConfigTransfer (export/import)
  Workspace             all project/mapping/selection/layout operations and their invariants
  Browsers/BrowserPool  per-side browser-instance lifecycle policy (LRU, grace period, busy veto)
src/ForgeDesk           net10.0-windows WPF app
  MainWindow            project tabs, desk grid + splitter, shortcuts, menus, autosave, import/export
  Views/PaneView        one side: header, conversation dropdown, notice bar, WebView2 host, empty states
  Browser/              shared WebView2 environment, BrowserView (one conversation), popup/new-window policy
  Views/*Dialog, ProjectsWindow   configuration UI
  Themes/               light/dark palettes + control styles (WPF Fluent theme for menus/scrollbars)
  SmokeTest             unattended end-to-end check run by CI on a real Windows machine
tests/ForgeDesk.Core.Tests   xUnit
```

Technology: C# 14 / .NET 10 LTS, WPF, Microsoft WebView2 (`Microsoft.Web.WebView2` 1.0.4314.35), System.Text.Json. No other dependencies.

## Building from source

```bash
dotnet test tests/ForgeDesk.Core.Tests
```

```bash
dotnet publish src/ForgeDesk/ForgeDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o artifacts/ForgeDesk
```

The WPF project compiles on Linux/macOS too (`EnableWindowsTargeting`), but only runs on Windows.

CI (`.github/workflows/windows-build.yml`, `windows-latest`): unit tests → self-contained single-file publish → `ForgeDesk-win-x64` artifact → **smoke test** that launches the real app on the runner (`ForgeDesk.exe --smoke-test <folder>`), loads both websites signed out, and checks project switching, instance reuse, split/maximise restoration, drift detection, dialogs, themes, instance release and persistence. Its report and screenshots are uploaded as the `smoke-test-results` artifact and summarised on the run page.
