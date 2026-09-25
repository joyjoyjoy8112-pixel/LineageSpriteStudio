AUTO BOT EVENT TEST

Purpose:
Test BotOpenUI only on button structures already confirmed visible/clickable.

Mappings:
2  = BotOpenUI
5  = OpenActionUI   (control)
7  = BotOpenUI
9  = BotOpenUI
10 = OpenActionUI   (control)
17 = BotOpenUI
20 = OpenActionUI   (control)

Expected control check:
5, 10, 20 should now open the Action window.
If they do, this patch is definitely applied.

What matters:
Tell us what happens when clicking 2, 7, 9, 17.
If any one opens the auto-hunt UI, that structure/event pair is the final solution.

Apply:
1. Close game/launcher.
2. Copy AUTO_BOT_EVENT_TEST.ps1 and RUN_AUTO_BOT_EVENT_TEST.bat to the client folder with UI.idx/UI.pak.
3. Run RUN_AUTO_BOT_EVENT_TEST.bat.
4. Wait for PATCH COMPLETE.
5. Start the game and click 2,5,7,9,10,17,20.
