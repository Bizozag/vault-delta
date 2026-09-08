# macOS Dual-Architecture Release Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Produce self-contained `osx-arm64` and `osx-x64` Vault Delta application bundles with deterministic metadata, portable ZIP archives, checksums, and macOS CI validation.

**Architecture:** A Bash release script publishes the existing Avalonia desktop project once per runtime identifier, wraps each output in a standard `Vault Delta.app/Contents` hierarchy, renders a versioned `Info.plist`, adds release metadata and an application icon, then validates and archives the bundle. Windows can independently cross-publish both RIDs for static inspection, while a macOS runner performs Mach-O and native-architecture launch checks. Signing, notarization, hardened runtime, and DMG creation remain isolated in Task 25.

**Tech Stack:** C#/.NET 10, Avalonia 12, Bash, macOS `plutil`/`file`/`ditto`, GitHub Actions.

---

### Task 1: Define the application bundle metadata

**Files:**

- Create: `packaging/macos/Info.plist`
- Create: `packaging/macos/README.txt`

**Steps:**

1. Add a tokenized property-list template with bundle identifier `io.vaultdelta.app`, executable `VaultDelta`, semantic version fields, high-resolution capability, and application category.
2. Declare `VaultDelta.icns` as the bundle icon and document that Task 24 uses an unsigned preview bundle.
3. Validate the generated property list with `plutil -lint` on macOS and with XML parsing during cross-platform static checks.

### Task 2: Build both self-contained application bundles

**Files:**

- Create: `scripts/publish-macos.sh`

**Steps:**

1. Parse version, configuration, output-root, verification, and smoke-test switches without adding a public application CLI.
2. Refuse to overwrite an existing app directory or archive.
3. Run the repository verification script once, then publish `osx-arm64` and `osx-x64` self-contained outputs.
4. Assemble `Contents/MacOS`, `Contents/Resources`, `Contents/Info.plist`, and `Contents/PkgInfo` in a temporary staging directory.
5. Generate a valid preview `.icns`, write `release.json`, and check required managed/runtime files and executable permissions.
6. On macOS, validate plist and Mach-O architecture; launch only the bundle matching the current host architecture for a three-second smoke test.
7. Atomically publish each staging directory, create a ZIP preserving one versioned package directory containing the `.app`, and write SHA-256 checksums.

### Task 3: Add macOS release documentation

**Files:**

- Create: `docs/user-guide/macos-installation.md`

**Steps:**

1. Explain Apple Silicon versus Intel package selection, ZIP verification, complete extraction, and unsigned preview Gatekeeper behavior.
2. Document patch generation, transfer, baseline Gate, apply, transaction journal, and rollback flow.
3. Record Task 24 boundaries: no universal binary, signing/notarization/DMG deferred to Task 25, and real external-volume acceptance still required.

### Task 4: Add macOS CI release gates

**Files:**

- Modify: `.github/workflows/ci.yml`

**Steps:**

1. Add a macOS packaging job after normal verification.
2. Run the packaging script for both architectures from a clean checkout.
3. Upload the two ZIPs, checksum file, and unpacked metadata as CI artifacts for inspection.

### Task 5: Verify and commit

**Steps:**

1. Run shell syntax validation and XML template parsing locally.
2. Cross-publish both RIDs from Windows and validate required files, metadata substitutions, ZIP topology, and hashes without claiming a macOS launch result.
3. Run the full Release verification suite.
4. Run `git diff --check` and review the staged diff.
5. Commit as `build: package macos desktop releases`.
