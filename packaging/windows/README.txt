Vault Delta for Windows
=======================

Vault Delta creates and applies offline incremental ZIP patches for Obsidian vaults.

Package contents
----------------
- VaultDelta.exe: the complete self-contained application. Start here.
- README.txt: this guide.
- release.json: build provenance and version information.

Runtime dependencies are bundled inside VaultDelta.exe and are extracted to the
current user's temporary runtime cache when required. Keep these three package
files together when archiving or verifying a release.

Quick start
-----------
1. Extract the complete ZIP to a local folder.
2. Run VaultDelta.exe. Administrator permission is not required.
3. To create a patch, compare an old snapshot with the updated snapshot, review the changes, and choose Generate ZIP Patch.
4. Transfer only the generated ZIP to the target computer.
5. Open the patch in Patch & Recovery, select the old target vault, run the baseline check, then apply.

Recovery
--------
Vault Delta stores transaction journals and backups beside the target vault in a hidden folder named:

.<vault-name>.vaultdelta-transactions

If an apply operation is interrupted, open its journal.json from Patch & Recovery and run recovery. Keep this folder until the update has been verified.

Safety
------
- Close Obsidian or pause other programs that write to the vault before scanning or applying.
- Keep an independent backup for important vaults.
- Do not mix files from different Vault Delta release ZIPs.
- This preview package is not code-signed. Verify the SHA256SUMS.txt file before use.

Full instructions are in docs/user-guide/windows-installation.md in the source repository.
