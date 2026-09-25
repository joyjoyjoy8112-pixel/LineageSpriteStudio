AUTOHUNT UI STAGE1 V2

Fix for the empty second Character Ranking window seen in Stage1.

Why V1 was blank:
The window clone itself rendered, but newly injected generic controls were not on the same native rendering layer as the visible ranking controls.

V2:
- reuses the exact existing visible ranking title/tab controls
- changes the second window title to the auto-hunt title
- uses the same known-visible tab control as the template for all settings rows
- places all new rows inside the same native tab rendering layer
- moves ranking list controls off-screen in the cloned window
- does not change server/DB logic

Apply:
1. Close game/launcher.
2. Copy the V2 PS1/BAT into the client folder containing UI.idx/UI.pak.
3. Run RUN_AUTOHUNT_UI_STAGE1_V2.bat.
4. Wait for PATCH COMPLETE.
5. Start game and open Character Ranking.
6. Send a screenshot of the second window.

This is still STEP 1 only: visual UI construction.
