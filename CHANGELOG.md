# Changelog

## v4.0.0 (2026-06-09)

Maintenance and robustness release. No change to the basic workflow (group archives,
`mps_only` archives, owner tagging, `_NO_MPR_found` marking); existing CLI and GUI usage
is preserved.

### Added
- **Folder-aware `.mps` matching.** A group now only considers `.mps` files in its own
  folder or an ancestor folder. This matches the typical BioLogic layout (the `.mps` in the
  experiment root, data in technique sub-folders) and prevents picking an `.mps` from a
  sibling experiment folder.
- **Zero-score tagging.** If the best `.mps` in the folder context shares no leading
  characters with the group (score `0`), it is still bundled, but the archive name is tagged
  `_zeroScoreMps` for review.
- **Collision-safe `mps_only` names.** Two `.mps` files with the same filename in different
  folders no longer overwrite each other; the second archive gets a `_2`, `_3`, ... counter
  and both contents are kept.
- **`--overwrite` / `--no-overwrite` flag.** Overwriting existing output archives stays on
  by default; `--overwrite=false` (alias `--no-overwrite`) skips any archive that would
  replace an existing file and reports it on the console.
- **More golden-file tests:** multi-experiment input, cross-folder matching, the zero-score
  case, and the filename-collision case.

### Changed
- **Refactored `Main`** into focused functions (`ResolveInput`, `PrepareWorkingDirectory`,
  `ResolveOutputDirectory`, `ResolveContentDirectory`, `CreateGroupArchives`,
  `CreateMpsOnlyArchives`, `CleanupWorkingDirectory`) so the core logic is easier to test.

### Fixed
- Cross-folder `.mps` mis-selection: a group in one experiment folder could receive an
  `.mps` from another folder via the filename tie-break.
- Silent loss of `mps_only` archives on filename collisions across folders.

---

## v3.0.0 (2026-05-29)

- Accepts `.zip`, `.tar`, and raw folder input (folder input usable via CLI).
- Smart grouping by base filename with per-group `tar.gz` archives; `_NO_MPR_found` marking.
- Filename-similarity `.mps` matching; separate `mps_only` archives; owner tagging.
- Relative input path handling fixed; temporary directories always cleaned up; TAR
  extraction paths validated against path traversal.
- xUnit golden-file process tests added.
