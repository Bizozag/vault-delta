# Obsidian vault E2E fixture

`Baseline` and `Target` are fixed blueprints for the full offline delta workflow.

Files ending in `.base64` are decoded during test materialization, with the suffix removed. This keeps binary attachment bytes reviewable and identical on Windows and macOS.

The fixture intentionally changes `.trash/Discarded.md` and `.obsidian/workspace.json`; both must be included in snapshot, patch, apply, and rollback behavior so the default workflow covers every regular file.
