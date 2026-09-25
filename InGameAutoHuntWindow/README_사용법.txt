AUTOHUNT INGAME WINDOW V1

This version matches the requested direction:
- NOT an external EXE
- NOT tied to Character Ranking
- creates a separate top-level window inside the Lineage game UI
- window name: AutoHuntSettingsWindow
- injected into MainButtonUI.xml, which is loaded in the game world
- uses the rank frame only as a visual template
- Korean text is serialized as CP949 to avoid the mojibake seen in the previous test

V1 behavior:
The new auto-hunt window is intentionally visible immediately after entering the world.
You do NOT click Character Ranking.
This first verifies that the independent in-game window itself renders correctly.

Buttons are visual only in V1.
After this window renders correctly, the next patch will:
1) start hidden
2) open/close from the AUTO button
3) connect Save / Start / Stop and setting changes

Apply:
1. Close game/launcher.
2. Put AUTOHUNT_INGAME_WINDOW_V1.ps1 and RUN_AUTOHUNT_INGAME_WINDOW_V1.bat next to UI.idx/UI.pak.
3. Run RUN_AUTOHUNT_INGAME_WINDOW_V1.bat.
4. Wait for PATCH COMPLETE.
5. Start the game and enter the world.
6. Do not open Character Ranking. The new window should already be visible.
