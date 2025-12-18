using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;

namespace BioLogicZipper
{
    class Program
    {
        static string GetExtension(CompressionType c) => c switch
        {
            CompressionType.GZip => "gz",
            CompressionType.BZip2 => "bz2",
            CompressionType.Xz => "xz",
            CompressionType.None => "tar", // no suffix
            _ => c.ToString().ToLower()
        };

        static string? SelectBestMatchingMps(string groupBaseName, string[] mpsFiles)
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
                .Select(mps => new { Path = mps, Score = Score(mps) })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => Path.GetFileName(x.Path).Length)
                .First()
                .Path;
        }

        static string GetOwner(string baseName)
        {
            int underscoreIndex = baseName.IndexOf('_');

            if (underscoreIndex <= 0)
                return "";

            string candidate = baseName.Substring(0, underscoreIndex);

            if (candidate.Contains(' '))
                return "";

            return candidate+"_";
        }


        [STAThread]
        static void Main(string[] args)
        {
            string archivePath = "";

            if (args.Length == 0)
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
                    return;
                }
            }
            else
            {
                archivePath = args[0];
            }

            // This will be the directory that contains the files to process
            string tempDir;
            bool usesTempDir = false;

            // 1) CLI: if user passes a folder → use it directly
            if (Directory.Exists(archivePath))
            {
                tempDir = archivePath;
                usesTempDir = false;
                Console.WriteLine($"Using existing directory as input: {tempDir}");
            }
            else
            {
                // 2) Otherwise: assume it's an archive that must be extracted
                tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                usesTempDir = true;

                Console.WriteLine($"Extracting to temp directory: {tempDir}");

                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archivePath, tempDir);
                }
                else if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = TarArchive.Open(archivePath);
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
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
                    return;
                }
            }

            string[] subDirs = Directory.GetDirectories(tempDir);
            string[] subFiles = Directory.GetFiles(tempDir);
            string baseDir = Path.GetDirectoryName(archivePath);
            string outputDir = baseDir;

            if (args.Length > 1)
            {
                // Using args[1] direct, if a absolute path
                outputDir = Path.IsPathRooted(args[1]) ? args[1] : Path.Combine(baseDir, args[1]);
            }

            Directory.CreateDirectory(outputDir);

            // Special case: ZIP contains a single root folder (e.g., same name as ZIP file)
            // var files = Directory.GetFiles(subDirs[0]);
            // var dirs = Directory.GetDirectories(subDirs[0]);
            if (subDirs.Length == 1 && subFiles.Length == 0)
            {
                // Dive into that folder instead
                tempDir = subDirs[0];
                subDirs = Directory.GetDirectories(tempDir);
            }

            string[] allFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            string[] mpsFile = Directory.GetFiles(tempDir, "*.mps", SearchOption.AllDirectories);

            // Group files by filename without extension
            var fileGroups = allFiles
                .Where(f => Path.GetExtension(f).ToLower() != ".mps")
                .GroupBy(f => Path.Combine(Path.GetDirectoryName(f) ?? "", Path.GetFileNameWithoutExtension(f)));

            int counter = 1;

            foreach (var group in fileGroups)
            {
                string baseName = Path.GetFileName(group.Key);

                string owner = GetOwner(baseName);

                // Determine unfinished_mps_only before creating the output file
                bool unfinished_mps_only = !group.Any(f =>
                    string.Equals(Path.GetExtension(f), ".mpr", StringComparison.OrdinalIgnoreCase));

                CompressionType compression = CompressionType.GZip;

                string suffix = unfinished_mps_only ? "_NO_MPR_found" : "";
                string tarFile = Path.Combine(
                    outputDir,
                    $"{owner}part_{baseName}_{counter}{suffix}.tar.{GetExtension(compression)}"
                );

                using (var stream = File.Create(tarFile))
                using (var writer = WriterFactory.Open(stream, ArchiveType.Tar, compression))
                {
                    if (mpsFile.Length >= 1)
                    {
                        var selectedMps = SelectBestMatchingMps(baseName, mpsFile);
                        if (selectedMps != null)
                        {
                            string mpsFileName = Path.GetFileName(selectedMps);
                            writer.Write(mpsFileName, selectedMps);
                        }
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
                counter++;
            }


            // Create single MPS-only archive
            if (mpsFile.Length >= 1)
                for (int i = 0; i < mpsFile.Length; i++)
                {
                    {
                        string mpsFileName = Path.GetFileName(mpsFile[i]);
                        string mpsFileNameWoExt = Path.GetFileNameWithoutExtension(mpsFile[i]);

                        string owner = GetOwner(mpsFileNameWoExt);

                        string mpsArchive = Path.Combine(outputDir, $"{owner}part_{mpsFileNameWoExt}_mps_only.tar.{GetExtension(CompressionType.GZip)}");

                        using (var stream = File.Create(mpsArchive))
                        using (var writer = WriterFactory.Open(stream, ArchiveType.Tar, CompressionType.GZip))
                        {
                            writer.Write(mpsFileName, mpsFile[i]);
                        }

                        Console.WriteLine($"Created {mpsArchive}");
                    }
                }

            if (usesTempDir && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                    Console.WriteLine($"Temporary folder {tempDir} cleaned up.");
                }
                catch
                {
                    Console.WriteLine("Warning: Could not remove temporary folder.");
                }
            }


            if (args.Length == 0)
            {
                Console.WriteLine("Done.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
    }

}
