AUTO DIAG20 V2

This build fixes the PowerShell 5 encoding/path error from V1.
All executable script text and generated filenames are ASCII-only.

IMPORTANT:
The previous V1 failed only while writing the final report, after UI.idx/UI.pak had already been updated.
V2 is safe to run again on that partially-patched state.

Steps:
1. Close the game and launcher completely.
2. Copy AUTO_DIAG20_PATCH_V2.ps1 and RUN_AUTO_DIAG20_V2.bat into the client folder that contains UI.idx and UI.pak.
3. Run RUN_AUTO_DIAG20_V2.bat.
4. Wait for PATCH COMPLETE.
5. Start the game.
6. Report visible button numbers and click results.

This patch:
- uses existing image ID 29999 for all 20 test buttons
- adds UI text labels 1 through 20
- adds TEST 1 through TEST 20 tooltips
- automatically backs up UI.idx and UI.pak
- does not modify server files, database, Data.pak, or Image00.pak
