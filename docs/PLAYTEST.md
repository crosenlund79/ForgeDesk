# ForgeDesk — hands-on playtest (Prototype 001)

Things that can only be verified with your real accounts on your own Windows PC. Tick them off and note anything odd (menu `⋯` → **Open data folder** → `logs\forgedesk.log` helps).

## 1. First start and sign-in
- [ ] Extract the `ForgeDesk-win-x64` artifact, run `ForgeDesk.exe`. The welcome screen appears.
- [ ] **Create a project** (e.g. HyperCars, pick a colour).
- [ ] Left side → **Open ChatGPT** → sign in. Right side → **Open Claude** → sign in.
  - If you use "Continue with Google": does the popup work, or does Google refuse the embedded browser? (If refused, use the email option.)
- [ ] Close ForgeDesk, start it again: both sides are still signed in.
- [ ] With a conversation mapped while signed out, the side says "Sign in to … to open …"; after signing in, the mapped conversation opens by itself.

## 2. Mapping conversations
- [ ] Paste a ChatGPT conversation link via **Add conversation…**; the dialog says "ChatGPT conversation".
- [ ] Paste a Claude link while on the ChatGPT side: it says it will be added to the Claude side.
- [ ] Paste an invalid link (e.g. `https://example.com`): clear error, **Add** disabled.
- [ ] **Browse ChatGPT** → open a conversation → **Map this page…**: name is pre-filled from the title; the page doesn't reload after mapping.
- [ ] Map 3–4 conversations per side, as in the HyperCars example. Each dropdown shows only that project's conversations, with Alt+1… hints.

## 3. Daily workflow
- [ ] Send a message in each side; responses stream normally.
- [ ] Attach a file (paperclip and drag-and-drop from Explorer) on both sides.
- [ ] Paste an image from the clipboard into each composer.
- [ ] Copy text from a ChatGPT answer and paste it into Claude (and back).
- [ ] Scroll long conversations; use Ctrl+F inside a side.
- [ ] Ctrl+Plus/Minus zoom on one side; restart — the zoom is remembered.

## 4. Background generation (the important one)
- [ ] Ask ChatGPT for a long answer, and while it streams switch to another ChatGPT conversation of the same project. Wait ~30 s, switch back: did the answer complete?
- [ ] Same, but switch to a **different project** instead. Then switch back.
- [ ] Repeat both with Claude.
- [ ] Switching back shows the same scroll position and any unsent draft text.

## 5. Project switching and restoration
- [ ] Two projects with different selections; drag the splitter to e.g. 30/70 in one. Ctrl+1 / Ctrl+2 back and forth: selections and split come back exactly; no page reloads.
- [ ] Maximise a side (F11 or ⤢), switch project and back: still maximised; F11 restores the previous split.
- [ ] Restart ForgeDesk: same active project, selections, split, window size/position.
- [ ] Ultrawide (3440×1440 / 5120×1440): maximise the window; both sides usable; splitter comfortable.

## 6. Drift
- [ ] Inside ChatGPT, click a different conversation in its sidebar: the amber bar "You've moved away from …" appears. **Return** goes back. The mapping is unchanged.
- [ ] Same in Claude.

## 7. Configuration
- [ ] Manage projects: rename, recolour, reorder, archive (tab disappears; restorable from ⌄), remove (confirmation says nothing is deleted on the providers).
- [ ] Map the same conversation in two projects; both work.
- [ ] Export configuration; open the JSON: only projects/mappings/preferences, no cookies or tokens.
- [ ] Import it back with **Merge** (nothing duplicated) and **Replace**.

## 8. Recovery
- [ ] Disconnect the network, reload a side: an error bar with **Reload**; reconnect and reload works.
- [ ] Links in answers open in your default browser.
- [ ] Leave ~8 conversations open per side for 15+ minutes, check Task Manager memory; hidden ones beyond the limit are released (menu `⋯` → Background conversations).
