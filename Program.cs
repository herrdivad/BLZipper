using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace BioLogicZipper
{
    class Program
    {
        // Tracks how the working directory was obtained so it can be cleaned up afterwards.
        sealed class ExtractionContext
        {
            public bool UsesTempDir;
            public string? TempRootDir;
        }

        // A candidate .mps file together with its prefix-match score against a group.
        internal sealed record MpsMatch(string Path, int Score);

        // Parsed command-line options: positional arguments (input, optional output dir)
        // separated from flags.
        internal sealed class CliOptions
        {
            public string[] Positional = Array.Empty<string>();
            public bool Overwrite = true;
        }

        // Splits flags from positional arguments. Supports --overwrite[=true|false] and
        // --no-overwrite; overwriting existing output archives is enabled by default.
        internal static CliOptions ParseOptions(string[] args)
        {
            var positional = new List<string>();
            bool overwrite = true;

            foreach (string arg in args)
            {
                if (string.Equals(arg, "--no-overwrite", StringComparison.OrdinalIgnoreCase))
                {
                    overwrite = false;
                }
                else if (string.Equals(arg, "--overwrite", StringComparison.OrdinalIgnoreCase))
                {
                    overwrite = true;
                }
                else if (arg.StartsWith("--overwrite=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = arg.Substring("--overwrite=".Length);
                    overwrite = !(string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) || value == "0");
                }
                else
                {
                    positional.Add(arg);
                }
            }

            return new CliOptions { Positional = positional.ToArray(), Overwrite = overwrite };
        }

        internal static string GetExtension(CompressionType c) => c switch
        {
            CompressionType.GZip => "gz",
            CompressionType.BZip2 => "bz2",
            CompressionType.Xz => "xz",
            CompressionType.None => "tar", // no suffix
            _ => c.ToString().ToLower()
        };

        internal static MpsMatch? SelectBestMatchingMps(string groupBaseName, string[] mpsFiles)
        {
            if (mpsFiles == null || mpsFiles.Length == 0)
                return null;

            string groupName = Path.GetFileNameWithoutExtension(groupBaseName);

            int Score(string mpsPath)
            {
                string mpsName = Path.GetFileNameWithoutExtension(mpsPath);
                int score = 0;

                int len = Math.Min(groupName.Length, mpsName.Length);

                for (int i = 0; i < len; i++)
                {
                    if (groupName[i] == mpsName[i])
                    {
                        score += (i < 10) ? 2 : 1; // double weight for first 10 chars
                    }
                    else
                    {
                        break; // stop at first mismatch
                    }
                }

                return score;
            }

            return mpsFiles
                .Select(mps => new MpsMatch(mps, Score(mps)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => Path.GetFileName(x.Path).Length)
                .First();
        }

        /// <summary>
        /// Guards against Zip-Slip: confirms that an archive entry will be written
        /// strictly inside <paramref name="destinationDir"/> when extracted.
        /// A legitimate entry is always relative to the archive root, so two kinds of
        /// malicious keys are rejected before <c>Path.Combine</c> can act on them:
        /// <list type="bullet">
        /// <item><description>Absolute (rooted) paths such as <c>C:\Windows\System32\evil.dll</c>
        /// or <c>/etc/cron.d/backdoor</c>. <c>Path.Combine(dir, absolute)</c> discards
        /// <paramref name="destinationDir"/> entirely and writes wherever the entry points.</description></item>
        /// <item><description>Traversal paths such as <c>../../etc/passwd</c> that climb out of
        /// the destination; caught by normalizing with <c>GetFullPath</c> and checking the prefix.</description></item>
        /// </list>
        /// </summary>
        /// <param name="destinationDir">The directory the entry must stay within.</param>
        /// <param name="entryPath">The archive entry key/path to validate.</param>
        /// <returns><c>true</c> only when the resolved path stays inside the destination.</returns>
        internal static bool IsSafeArchiveEntryPath(string destinationDir, string? entryPath)
        {
            if (string.IsNullOrWhiteSpace(entryPath) || Path.IsPathRooted(entryPath))
                return false;

            string fullDestinationDir = Path.GetFullPath(destinationDir);
            if (!fullDestinationDir.EndsWith(Path.DirectorySeparatorChar))
                fullDestinationDir += Path.DirectorySeparatorChar;

            string fullEntryPath = Path.GetFullPath(Path.Combine(fullDestinationDir, entryPath));
            return fullEntryPath.StartsWith(fullDestinationDir, StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetOwner(string baseName)
        {
            int underscoreIndex = baseName.IndexOf('_');

            if (underscoreIndex <= 0)
                return "";

            string candidate = baseName.Substring(0, underscoreIndex);

            if (candidate.Contains(' '))
                return "";

            return candidate+"_";
        }

        // True when candidateDir is the same folder as groupDir or an ancestor of it.
        // Used to keep .mps matching within a group's own folder branch (the .mps may live
        // in an experiment root above the data), while excluding sibling experiment folders.
        internal static bool IsSameOrAncestorDirectory(string candidateDir, string groupDir)
        {
            string candidate = Path.GetFullPath(candidateDir);
            string group = Path.GetFullPath(groupDir);

            if (string.Equals(candidate, group, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!candidate.EndsWith(Path.DirectorySeparatorChar))
                candidate += Path.DirectorySeparatorChar;

            return group.StartsWith(candidate, StringComparison.OrdinalIgnoreCase);
        }

        // Ensures the archive name is unique within this run: appends a "_2", "_3", ...
        // counter only when the desired name was already produced this run, so two equal
        // names (e.g. same .mps filename in different folders) cannot clobber each other.
        // usedArchivePaths records every claimed path for this run.
        internal static string ResolveArchiveTarget(string outputDir, string stem, string ext, HashSet<string> usedArchivePaths)
        {
            string finalPath = Path.Combine(outputDir, $"{stem}.tar.{ext}");

            int n = 2;
            while (usedArchivePaths.Contains(finalPath))
            {
                finalPath = Path.Combine(outputDir, $"{stem}_{n}.tar.{ext}");
                n++;
            }

            usedArchivePaths.Add(finalPath);
            return finalPath;
        }

        // Resolves the input archive/folder path from the positional args or a GUI dialog.
        // Returns null when the user cancels the dialog.
        static string? ResolveInput(string[] positional)
        {
            string archivePath;

            if (positional.Length == 0)
            {
                // No CLI args – open GUI dialog
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "Archive files (*.zip;*.tar)|*.zip;*.tar";
                ofd.Title = "Select an archive file";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    archivePath = ofd.FileName;
                }
                else
                {
                    Console.WriteLine("No file selected.");
                    return null;
                }
            }
            else
            {
                archivePath = positional[0];
            }

            return Path.GetFullPath(archivePath);
        }

        // Provides the directory that contains the files to process. Folders are used
        // directly; archives are extracted into an isolated temp directory recorded in
        // the context so it can be cleaned up later. Returns null when the input is
        // neither a supported archive nor a directory.
        static string? PrepareWorkingDirectory(string archivePath, ExtractionContext context)
        {
            // 1) CLI: if user passes a folder → use it directly
            if (Directory.Exists(archivePath))
            {
                Console.WriteLine($"Using existing directory as input: {archivePath}");
                return archivePath;
            }

            // 2) Otherwise: assume it's an archive that must be extracted
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            context.TempRootDir = tempDir;
            context.UsesTempDir = true;
            Directory.CreateDirectory(tempDir);

            Console.WriteLine($"Extracting to temp directory: {tempDir}");

            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, tempDir);
            }
            else if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        if (!IsSafeArchiveEntryPath(tempDir, entry.Key))
                            throw new InvalidDataException($"Unsafe archive entry path: {entry.Key}");

                        entry.WriteToDirectory(tempDir, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
            }
            else
            {
                Console.WriteLine("Unsupported file format and not a directory.");
                return null;
            }

            return tempDir;
        }

        // Determines the output directory, honoring an optional second positional argument.
        internal static string ResolveOutputDirectory(string[] positional, string archivePath)
        {
            string baseDir = Path.GetDirectoryName(archivePath) ?? Directory.GetCurrentDirectory();
            string outputDir = baseDir;

            if (positional.Length > 1)
            {
                // Using positional[1] direct, if a absolute path
                outputDir = Path.IsPathRooted(positional[1]) ? positional[1] : Path.Combine(baseDir, positional[1]);
            }

            Directory.CreateDirectory(outputDir);
            return outputDir;
        }

        // Special case: the archive contains a single root folder (e.g., same name as the
        // ZIP file) → dive into that folder instead of treating it as the content root.
        internal static string ResolveContentDirectory(string tempDir)
        {
            string[] subDirs = Directory.GetDirectories(tempDir);
            string[] subFiles = Directory.GetFiles(tempDir);

            if (subDirs.Length == 1 && subFiles.Length == 0)
                return subDirs[0];

            return tempDir;
        }

        // Groups non-.mps files by base filename and writes one TAR archive per group,
        // adding the best-matching .mps file to each group.
        static void CreateGroupArchives(string contentDir, string[] mpsFile, string outputDir, bool overwrite, HashSet<string> usedArchivePaths)
        {
            string[] allFiles = Directory.GetFiles(contentDir, "*", SearchOption.AllDirectories);

            // Group files by filename without extension
            var fileGroups = allFiles
                .Where(f => !string.Equals(Path.GetExtension(f), ".mps", StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => Path.Combine(Path.GetDirectoryName(f) ?? "", Path.GetFileNameWithoutExtension(f)));

            int counter = 1;

            foreach (var group in fileGroups)
            {
                string baseName = Path.GetFileName(group.Key);

                string owner = GetOwner(baseName);

                string groupDir = Path.GetDirectoryName(group.Key) ?? "";

                // Determine unfinished_mps_only before creating the output file
                bool unfinished_mps_only = !group.Any(f =>
                    string.Equals(Path.GetExtension(f), ".mpr", StringComparison.OrdinalIgnoreCase));

                // Folder context: only consider .mps files in the group's own folder or an
                // ancestor folder. This keeps the typical BioLogic layout working (the .mps
                // sits in the experiment root, data in technique sub-folders) while preventing
                // selection of an .mps from a sibling experiment folder.
                string[] candidateMps = mpsFile
                    .Where(m => IsSameOrAncestorDirectory(Path.GetDirectoryName(m) ?? "", groupDir))
                    .ToArray();

                MpsMatch? mpsMatch = SelectBestMatchingMps(baseName, candidateMps);

                // A score of 0 means the selected .mps shares no leading characters with the
                // group; it is still bundled, but the name is tagged so it can be reviewed.
                bool zeroScoreMps = mpsMatch != null && mpsMatch.Score == 0;

                CompressionType compression = CompressionType.GZip;

                string suffix = "";
                if (unfinished_mps_only)
                    suffix += "_NO_MPR_found";
                if (zeroScoreMps)
                    suffix += "_zeroScoreMps";

                string ext = GetExtension(compression);
                string stem = $"{owner}part_{baseName}_{counter}{suffix}";
                counter++;

                string tarFile = ResolveArchiveTarget(outputDir, stem, ext, usedArchivePaths);
                if (!overwrite && File.Exists(tarFile))
                {
                    Console.WriteLine($"Skipped existing archive (overwrite disabled): {tarFile}");
                    continue;
                }

                using (var stream = File.Create(tarFile))
                using (var writer = WriterFactory.OpenWriter(stream, ArchiveType.Tar, new WriterOptions(compression)))
                {
                    if (mpsMatch != null)
                    {
                        string mpsFileName = Path.GetFileName(mpsMatch.Path);
                        writer.Write(mpsFileName, mpsMatch.Path);
                    }

                    foreach (string file in group)
                    {
                        if (!string.Equals(Path.GetExtension(file), ".mps", StringComparison.OrdinalIgnoreCase))
                        {
                            string fileName = Path.GetFileName(file);
                            writer.Write(fileName, file);
                            Console.WriteLine($"Added: {fileName}");
                        }
                    }
                }

                Console.WriteLine($"Created {tarFile}");
            }
        }

        // Packs every .mps file into its own dedicated archive.
        static void CreateMpsOnlyArchives(string[] mpsFile, string outputDir, bool overwrite, HashSet<string> usedArchivePaths)
        {
            if (mpsFile.Length >= 1)
                for (int i = 0; i < mpsFile.Length; i++)
                {
                    {
                        string mpsFileName = Path.GetFileName(mpsFile[i]);
                        string mpsFileNameWoExt = Path.GetFileNameWithoutExtension(mpsFile[i]);

                        string owner = GetOwner(mpsFileNameWoExt);

                        string ext = GetExtension(CompressionType.GZip);
                        string stem = $"{owner}part_{mpsFileNameWoExt}_mps_only";

                        // Same .mps filename in different folders would otherwise overwrite each
                        // other; ResolveArchiveTarget appends a counter only on a real collision.
                        string mpsArchive = ResolveArchiveTarget(outputDir, stem, ext, usedArchivePaths);
                        if (!overwrite && File.Exists(mpsArchive))
                        {
                            Console.WriteLine($"Skipped existing archive (overwrite disabled): {mpsArchive}");
                            continue;
                        }

                        using (var stream = File.Create(mpsArchive))
                        using (var writer = WriterFactory.OpenWriter(stream, ArchiveType.Tar, new WriterOptions(CompressionType.GZip)))
                        {
                            writer.Write(mpsFileName, mpsFile[i]);
                        }

                        Console.WriteLine($"Created {mpsArchive}");
                    }
                }
        }

        // Removes the temporary working directory when the input was an extracted archive.
        static void CleanupWorkingDirectory(ExtractionContext context)
        {
            if (context.UsesTempDir && context.TempRootDir != null && Directory.Exists(context.TempRootDir))
            {
                try
                {
                    Directory.Delete(context.TempRootDir, true);
                    Console.WriteLine($"Temporary folder {context.TempRootDir} cleaned up.");
                }
                catch
                {
                    Console.WriteLine("Warning: Could not remove temporary folder.");
                }
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            CliOptions options = ParseOptions(args);

            string? archivePath = ResolveInput(options.Positional);
            if (archivePath == null)
                return;

            var context = new ExtractionContext();

            try
            {
                string? tempDir = PrepareWorkingDirectory(archivePath, context);
                if (tempDir == null)
                    return;

                string outputDir = ResolveOutputDirectory(options.Positional, archivePath);
                string contentDir = ResolveContentDirectory(tempDir);

                string[] mpsFile = Directory.GetFiles(contentDir, "*.mps", SearchOption.AllDirectories);

                // Tracks output archive names already produced this run so equal names get a
                // counter suffix instead of silently overwriting each other.
                var usedArchivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                CreateGroupArchives(contentDir, mpsFile, outputDir, options.Overwrite, usedArchivePaths);
                CreateMpsOnlyArchives(mpsFile, outputDir, options.Overwrite, usedArchivePaths);

                if (options.Positional.Length == 0)
                {
                    Console.WriteLine("Done.");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
            }
            finally
            {
                CleanupWorkingDirectory(context);
            }
        }
    }

}
