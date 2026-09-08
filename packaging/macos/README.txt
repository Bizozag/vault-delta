Vault Delta for macOS
=====================

Vault Delta creates and applies offline incremental ZIP patches for Obsidian vaults.

Package selection
-----------------
- Apple Silicon (M1 or newer): use the osx-arm64 package.
- Intel Mac: use the osx-x64 package.

Quick start
-----------
1. Extract the complete ZIP.
2. Move Vault Delta.app to Applications or another local folder.
3. Open Vault Delta.app.
4. Compare an old snapshot with the updated snapshot and generate a ZIP patch.
5. Transfer only the patch ZIP to the target Mac, run the baseline check, then apply.

Recovery
--------
Transaction journals and backups are stored beside the target vault in:

.<vault-name>.vaultdelta-transactions

If apply is interrupted, select the operation's journal.json in Patch & Recovery and run recovery. Keep the transaction folder until the update has been verified.

Preview safety
--------------
- Close Obsidian or pause other writers before scanning or applying.
- Keep an independent backup for important vaults.
- This Task 24 preview is not signed or notarized. Verify SHA256SUMS.txt and use only a package from a trusted source.
- Developer ID signing, notarization, hardened runtime validation, and DMG packaging are added in Task 25.
