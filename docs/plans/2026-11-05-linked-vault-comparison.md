# Linked Vault Comparison Implementation Plan

**Goal:** Compare vaults containing development junctions through an explicit skip option, preserve actionable failure details, and keep large comparisons usable.

**Architecture:** Strict link rejection stays the default. An opt-in metadata pass finds linked paths in both snapshots and excludes the union (including descendants) from both sides before hashing; links are never followed. The result exposes exclusions to the UI and a companion report. Risk classification indexes path/type conflicts once per immutable diff set, and the review list uses virtualization.

**Tech Stack:** C# / .NET 10, Avalonia, xUnit, PowerShell, GitHub releases.

## Design choices

- Adopt explicit skipping with visible exclusions. Silently skipping links would misrepresent completeness; dereferencing links could leave the vault or cycle and would change link semantics.
- Preserve root and relative-path diagnostics for link, changing-file, read and permission failures.
- Apply exclusions symmetrically: a link in one snapshot must not cause deletion of a normal directory at the same path in the other snapshot.
- Display excluded paths and explain that link relationships require separate restoration. Preserve the previously delivered task-specific junction script separately.

## Task 1: Regression coverage and comparison changes

Files: Application/Snapshots/SnapshotScanner.cs and SnapshotScanException.cs; Application/Compare/CompareRequest.cs, CompareResult.cs, CompareWorkflow.cs; Infrastructure/FileSystem/LocalFileSystem.cs; corresponding Application and EndToEnd tests.

1. Add regressions for strict failure path, opt-in links, symmetric subtree exclusion and no link hashing.
2. Add metadata discovery and immutable exclusions, preserving cancellation and strict defaults.
3. Preserve path/root/cause for failures; reject linked snapshot roots.
4. Run focused tests.

## Task 2: Desktop and large-vault behavior

Files: Desktop/Services/ICompareService.cs and LocalCompareService.cs; Desktop/ViewModels/CompareWorkspaceViewModel.cs; Desktop/MainWindow.axaml; Application/Compare/DifferenceRiskClassifier.cs; desktop and performance tests.

1. Add skip-links checkbox, exclusion summary/list, actionable errors and exclusion report on package creation.
2. Invalidate results when scope changes and ignore late progress after completion.
3. Index path/type risks once per diff set and virtualize the change list.
4. Test and render both error and successful skipped-link states, plus a large diff.

## Task 3: Validate and release 0.1.11

1. Rescan the user's directories read-only and compare the resulting operations to the already verified package.
2. Run dotnet restore then scripts/verify.ps1 (format, build, tests).
3. Update version references and Windows instructions; commit code and build a self-contained Windows ZIP with scripts/publish-windows.ps1.
4. Verify executable startup, push source and publish the Windows release and checksum to the requested GitHub repository. Do not upload vault content.
