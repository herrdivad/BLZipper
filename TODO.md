# TODO

## High Priority

- Fix relative input path handling. `Path.GetDirectoryName("data.zip")` can return an empty value, which makes `Directory.CreateDirectory(outputDir)` fail. Normalize input with `Path.GetFullPath(...)` and fall back to `Directory.GetCurrentDirectory()` when needed.

- ~~Ensure temporary directories are always cleaned up. Archive processing should use a `try/finally` around extraction and packaging. Keep the original temp root separately so cleanup still removes it after entering a single-root archive folder.~~

## Medium Priority

- Improve `.mps` matching. Matching currently considers only the group base name and can select an `.mps` file from another folder when multiple experiment folders exist. Include folder context, and avoid selecting a file when the best score is `0`.

- Prevent silent archive overwrites. `File.Create(...)` overwrites existing output archives, and `mps_only` archives can collide when equal `.mps` names exist in different folders. Add collision-safe naming or fail with a clear error.

- ~~Validate TAR extraction paths. `ExtractFullPath = true` should be guarded against archive entries containing `../` or absolute paths so extracted files cannot escape the temporary directory.~~

## Low Priority

- ~~Replace extension comparisons using `ToLower()` with `string.Equals(..., StringComparison.OrdinalIgnoreCase)` to avoid unnecessary allocations and culture-sensitive behavior.~~

- Refactor `Main` into smaller functions such as `ResolveInput`, `ExtractArchive`, `ResolveOutputDirectory`, `CreateGroupArchives`, and `CreateMpsOnlyArchives`. This will make the core logic easier to test.

## Verification

- Run `dotnet build BioLogicZipper.sln --no-restore` after installing or switching to an environment with the .NET SDK available.

- Add representative manual tests for `.zip`, `.tar`, raw folders, multiple `.mps` files, missing `.mpr` files, owner-prefixed names, and custom output directories.
