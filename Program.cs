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

            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

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
                        entry.WriteToDirectory(tempDir, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                    }
                }
            }
            else
            {
                Console.WriteLine("Unsupported file format.");
                return;
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
                string baseName = Path.GetFileName(group.Key); // just the base name
                CompressionType compression = CompressionType.GZip;
                string tarFile = Path.Combine(outputDir, $"part_{baseName}_{counter}.tar.{GetExtension(compression)}");

                using (var stream = File.Create(tarFile))
                using (var writer = WriterFactory.Open(stream, ArchiveType.Tar, compression))
                {
                    if (mpsFile.Length >= 1)
                    {
                        string mpsFileName = Path.GetFileName(mpsFile[0]);
                        writer.Write(mpsFileName, mpsFile[0]);
                    }
                    foreach (string file in group)
                    {
                        // string relativePath = Path.GetRelativePath(tempDir, file);
                        // writer.Write(relativePath, file); // with folder remain 
                        // Console.WriteLine($"Added: {relativePath}");
                        if (Path.GetExtension(file) != ".mps")
                        {
                            string fileName = Path.GetFileName(file); // no folders
                            writer.Write(fileName, file);             // flatten structure
                            Console.WriteLine($"Added: {fileName}");
                        }
                    }
                }

                Console.WriteLine($"Created {tarFile}");
                counter++;
            }

            // Create single MPS-only archive
            if (mpsFile.Length >= 1)
            {
                string mpsFileName = Path.GetFileName(mpsFile[0]);
                string mpsFileNameWoExt = Path.GetFileNameWithoutExtension(mpsFile[0]);
                string mpsArchive = Path.Combine(outputDir, $"part_{mpsFileNameWoExt}_mps_only.tar.{GetExtension(CompressionType.GZip)}");

                using (var stream = File.Create(mpsArchive))
                using (var writer = WriterFactory.Open(stream, ArchiveType.Tar, CompressionType.GZip))
                {
                    writer.Write(mpsFileName, mpsFile[0]);
                }

                Console.WriteLine($"Created {mpsArchive}");
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
