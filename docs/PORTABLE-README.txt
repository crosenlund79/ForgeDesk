ForgeDesk — One desk. Two assistants. Your projects.
====================================================

Start:  double-click ForgeDesk.exe  (no installation needed)

Requirement
-----------
ForgeDesk shows the real chatgpt.com and claude.ai websites using the
Microsoft Edge WebView2 Runtime. It is already part of Windows 11 and of
most up-to-date Windows 10 PCs. If ForgeDesk says it is missing, install the
"Evergreen Bootstrapper" from:
  https://developer.microsoft.com/microsoft-edge/webview2/

Keep the files in this folder together (ForgeDesk.exe needs the DLLs next to it).
You can put the folder anywhere, e.g. C:\Tools\ForgeDesk.

Your data
---------
%LOCALAPPDATA%\ForgeDesk\config.json   projects, conversation mappings, layout
%LOCALAPPDATA%\ForgeDesk\WebView2\     the embedded browser profile (your
                                       ChatGPT / Claude sign-ins live here)
%LOCALAPPDATA%\ForgeDesk\logs\         diagnostics

ForgeDesk never stores your passwords and never reads, copies or uploads your
conversations. Removing a project or mapping only removes it from ForgeDesk;
nothing is deleted on ChatGPT or Claude.

To start completely fresh, close ForgeDesk and delete %LOCALAPPDATA%\ForgeDesk.

Keyboard
--------
Ctrl+1…9            switch project            Alt+1…9   conversation on active side
Ctrl+Tab            next project              Alt+↓/↑   next/previous conversation
F6                  switch side               F11       maximise / restore side
F5                  reload side
