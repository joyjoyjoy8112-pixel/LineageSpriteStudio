AUTOHUNT UI STAGE1

This is the requested STEP 1 implementation.

What it does:
- Uses the client's existing HighRankUI window/frame as the visual base.
- Injects a new visible AutoHunt_Stage1Window.
- Keeps the original rank window intact.
- Shows the auto-hunt layout immediately for visual confirmation.
- Uses Korean captions without storing Korean source text directly, avoiding PowerShell 5 encoding corruption.

Included layout:
- Title: auto-hunt settings
- Tabs: basic / hunting ground / potion / skill
- Hunting ground
- Potion name only (no item code shown)
- Potion HP 50
- Return HP 20
- Resume HP 90
- Auto return / auto shop
- Auto-hunt status
- Attack/buff/defense skill use
- Skill selection placeholders
- Bottom buttons: defaults / save / start / stop

Important:
- Stage1 is layout only.
- Buttons intentionally use Dummy events.
- Server, DB, auto-hunt logic, and button-open linkage are NOT changed yet.

Apply:
1. Close game and launcher.
2. Copy AUTOHUNT_UI_STAGE1.ps1 and RUN_AUTOHUNT_UI_STAGE1.bat into the client folder containing UI.idx/UI.pak.
3. Run RUN_AUTOHUNT_UI_STAGE1.bat.
4. Wait for PATCH COMPLETE.
5. Start the game and take a screenshot of the displayed window.

Restore:
Run RESTORE_AUTOHUNT_UI_STAGE1.bat in the client folder.
