ORIGINAL BOT WINDOW TEST

This test does NOT attach BotOpenUI to a random test button.

It activates the exact original client objects:
- BotOpenWindow
- BotOpenButton
- MouseEvent=BotOpenUI

Settings:
- X=620 Y=380
- image=29999
- Activate=1
- Top=1 / MostTop=1
- tooltip=ORIGINAL BOT

Why:
BotOpenUI was confirmed to do nothing when attached to ordinary visible buttons.
This checks whether the client handler is bound specifically to the original native object/name.

Apply:
1. Close game and launcher.
2. Copy ORIGINAL_BOT_WINDOW_TEST.ps1 and RUN_ORIGINAL_BOT_TEST.bat into the client folder.
3. Run RUN_ORIGINAL_BOT_TEST.bat.
4. Wait for PATCH COMPLETE.
5. Start game.
6. Find the icon near X=620 Y=380.
7. Hover it: tooltip must say ORIGINAL BOT.
8. Click it and report the result.

If this exact original object is also silent, the next step is client event-handler/binary/packet analysis, not more XML button layout testing.
