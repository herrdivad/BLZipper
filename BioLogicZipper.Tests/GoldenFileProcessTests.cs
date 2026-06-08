using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using Xunit;

namespace BioLogicZipper.Tests;

public sealed class GoldenFileProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

    [Theory]
    [InlineData("Messprotokoll_1.zip", "MP1")]
    [InlineData("Messprotokoll_2.zip", "MP2")]
    [InlineData("Multi_MP.zip", "MultiMP")]
    // Expected to FAIL until the .mps matching is fixed: across folders the tool picks
    // an .mps from the wrong folder. Cell_02's group must contain A01_run_v2.mps, but
    // the current tie-break selects Cell_01's shorter-named A01_run.mps instead.
    [InlineData("CrossFolder_noRealData.zip", "CrossFolder")]
    public void CliProcess_CreatesExpectedGoldenFiles(string inputArchiveName, string expectedOutputSet)
    {
        string repositoryRoot = FindRepositoryRoot();
        string appAssembly = FindBuiltAppAssembly(repositoryRoot);
        string inputArchive = Path.Combine(repositoryRoot, "TestFiles", "Input", inputArchiveName);
        string expectedOutputDirectory = Path.Combine(repositoryRoot, "TestFiles", "Output", expectedOutputSet);
        string tempRoot = Path.Combine(Path.GetTempPath(), "BioLogicZipper.Tests", Guid.NewGuid().ToString("N"));
        string actualOutputDirectory = Path.Combine(tempRoot, "Actual");

        Directory.CreateDirectory(actualOutputDirectory);

        try
        {
            ProcessResult result = RunBioLogicZipper(repositoryRoot, appAssembly, inputArchive, actualOutputDirectory);

            Assert.True(result.ExitCode == 0, $"""
                BioLogicZipper exited with code {result.ExitCode}.

                STDOUT:
                {result.StandardOutput}

                STDERR:
                {result.StandardError}
                """);

            AssertOutputMatches(expectedOutputDirectory, actualOutputDirectory, Path.Combine(tempRoot, "Compare"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static ProcessResult RunBioLogicZipper(
        string repositoryRoot,
        string appAssembly,
        string inputArchive,
        string actualOutputDirectory)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetDotnetExecutable(),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = repositoryRoot,
            }
        };

        process.StartInfo.ArgumentList.Add(appAssembly);
        process.StartInfo.ArgumentList.Add(inputArchive);
        process.StartInfo.ArgumentList.Add(actualOutputDirectory);

        process.Start();

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(ProcessTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"BioLogicZipper did not exit within {ProcessTimeout}.");
        }

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static void AssertOutputMatches(
        string expectedOutputDirectory,
        string actualOutputDirectory,
        string comparisonDirectory)
    {
        string[] expectedFileNames = GetTarGzFileNames(expectedOutputDirectory);
        string[] actualFileNames = GetTarGzFileNames(actualOutputDirectory);

        Assert.Equal(expectedFileNames, actualFileNames);

        for (int i = 0; i < expectedFileNames.Length; i++)
        {
            string fileName = expectedFileNames[i];
            string expectedFile = Path.Combine(expectedOutputDirectory, fileName);
            string actualFile = Path.Combine(actualOutputDirectory, fileName);

            if (FileComparison.FilesAreEqual(expectedFile, actualFile))
                continue;

            AssertTarGzContentsMatch(
                expectedFile,
                actualFile,
                Path.Combine(comparisonDirectory, i.ToString()),
                fileName);
        }
    }

    private static void AssertTarGzContentsMatch(
        string expectedArchive,
        string actualArchive,
        string comparisonDirectory,
        string archiveFileName)
    {
        string expectedExtractDirectory = Path.Combine(comparisonDirectory, "Expected");
        string actualExtractDirectory = Path.Combine(comparisonDirectory, "Actual");

        ExtractTarGz(expectedArchive, expectedExtractDirectory);
        ExtractTarGz(actualArchive, actualExtractDirectory);

        string[] expectedFileNames = GetRelativeFileNames(expectedExtractDirectory);
        string[] actualFileNames = GetRelativeFileNames(actualExtractDirectory);

        Assert.Equal(expectedFileNames, actualFileNames);

        foreach (string fileName in expectedFileNames)
        {
            string expectedFile = Path.Combine(expectedExtractDirectory, fileName);
            string actualFile = Path.Combine(actualExtractDirectory, fileName);

            Assert.True(
                FileComparison.FilesAreEqual(expectedFile, actualFile),
                $"Generated archive content differs from golden file: {archiveFileName}::{fileName}");
        }
    }

    private static void ExtractTarGz(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        using FileStream archiveStream = File.OpenRead(archivePath);
        using GZipStream gzipStream = new(archiveStream, CompressionMode.Decompress);
        using TarReader tarReader = new(gzipStream);

        TarEntry? entry;

        while ((entry = tarReader.GetNextEntry()) != null)
        {
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(GetSafeDestinationPath(destinationDirectory, entry.Name));
                continue;
            }

            if (entry.DataStream == null)
                continue;

            string destinationPath = GetSafeDestinationPath(destinationDirectory, entry.Name);
            string? parentDirectory = Path.GetDirectoryName(destinationPath);

            if (parentDirectory != null)
                Directory.CreateDirectory(parentDirectory);

            using FileStream outputStream = File.Create(destinationPath);
            entry.DataStream.CopyTo(outputStream);
        }
    }

    private static string GetSafeDestinationPath(string destinationDirectory, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
            throw new InvalidDataException($"Unsafe tar entry path: {entryName}");

        string fullDestinationDirectory = Path.GetFullPath(destinationDirectory);

        if (!fullDestinationDirectory.EndsWith(Path.DirectorySeparatorChar))
            fullDestinationDirectory += Path.DirectorySeparatorChar;

        string normalizedEntryName = entryName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string fullDestinationPath = Path.GetFullPath(Path.Combine(fullDestinationDirectory, normalizedEntryName));

        if (!fullDestinationPath.StartsWith(fullDestinationDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsafe tar entry path: {entryName}");

        return fullDestinationPath;
    }

    private static string[] GetTarGzFileNames(string directory)
    {
        return Directory
            .GetFiles(directory, "*.tar.gz", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetRelativeFileNames(string directory)
    {
        return Directory
            .GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BioLogicZipper.csproj"))
                && Directory.Exists(Path.Combine(directory.FullName, "TestFiles")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root from test output directory.");
    }

    private static string FindBuiltAppAssembly(string repositoryRoot)
    {
        string configuration = GetBuildConfiguration();
        string expectedAssembly = Path.Combine(repositoryRoot, "bin", configuration, "net8.0-windows", "BioLogicZipper.dll");

        if (File.Exists(expectedAssembly))
            return expectedAssembly;

        string binDirectory = Path.Combine(repositoryRoot, "bin");

        if (Directory.Exists(binDirectory))
        {
            string? newestAssembly = Directory
                .GetFiles(binDirectory, "BioLogicZipper.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (newestAssembly != null)
                return newestAssembly;
        }

        throw new FileNotFoundException(
            $"Could not find built BioLogicZipper.dll. Build BioLogicZipper.csproj before running tests. Expected path: {expectedAssembly}");
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string GetDotnetExecutable()
    {
        return Environment.GetEnvironmentVariable("BIOLOGICZIPPER_DOTNET") ?? "dotnet";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
