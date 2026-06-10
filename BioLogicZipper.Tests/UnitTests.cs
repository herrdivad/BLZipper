using BioLogicZipper;
using SharpCompress.Common;
using Xunit;

namespace BioLogicZipper.Tests;

// Unit / function tests for the pure helper methods that were extracted from Main.
// These call the internal helpers directly (exposed via InternalsVisibleTo), in
// contrast to GoldenFileProcessTests which exercise the whole CLI as a subprocess.

public sealed class ParseOptionsTests
{
    [Fact]
    public void NoArguments_DefaultsToEmptyPositionalAndOverwriteTrue()
    {
        Program.CliOptions options = Program.ParseOptions([]);

        Assert.Empty(options.Positional);
        Assert.True(options.Overwrite);
    }

    [Fact]
    public void NoOverwriteFlag_DisablesOverwrite()
    {
        Program.CliOptions options = Program.ParseOptions(["--no-overwrite"]);

        Assert.False(options.Overwrite);
        Assert.Empty(options.Positional);
    }

    [Theory]
    [InlineData("--overwrite", true)]
    [InlineData("--OVERWRITE", true)]
    [InlineData("--overwrite=true", true)]
    [InlineData("--overwrite=false", false)]
    [InlineData("--overwrite=False", false)]
    [InlineData("--overwrite=0", false)]
    [InlineData("--overwrite=1", true)]
    public void OverwriteFlagVariants_AreParsed(string flag, bool expected)
    {
        Program.CliOptions options = Program.ParseOptions([flag]);

        Assert.Equal(expected, options.Overwrite);
    }

    [Fact]
    public void PositionalArguments_ArePreservedInOrderAndFlagsRemoved()
    {
        Program.CliOptions options = Program.ParseOptions(
            ["input.zip", "--no-overwrite", "outDir"]);

        Assert.Equal(new[] { "input.zip", "outDir" }, options.Positional);
        Assert.False(options.Overwrite);
    }
}

public sealed class GetExtensionTests
{
    [Theory]
    [InlineData(CompressionType.GZip, "gz")]
    [InlineData(CompressionType.BZip2, "bz2")]
    [InlineData(CompressionType.Xz, "xz")]
    [InlineData(CompressionType.None, "tar")]
    public void MapsCompressionTypeToExtension(CompressionType type, string expected)
    {
        Assert.Equal(expected, Program.GetExtension(type));
    }
}

public sealed class SelectBestMatchingMpsTests
{
    [Fact]
    public void NullOrEmptyCandidates_ReturnsNull()
    {
        Assert.Null(Program.SelectBestMatchingMps("group", null!));
        Assert.Null(Program.SelectBestMatchingMps("group", []));
    }

    [Fact]
    public void PrefersHighestPrefixScore()
    {
        string[] candidates = [@"C:\data\B99_other.mps", @"C:\data\A01_run.mps"];

        Program.MpsMatch? match = Program.SelectBestMatchingMps("A01_run", candidates);

        Assert.NotNull(match);
        Assert.Equal(@"C:\data\A01_run.mps", match!.Path);
        Assert.True(match.Score > 0);
    }

    [Fact]
    public void OnTie_PrefersShorterFileName()
    {
        // Both share the leading "AB" with the group, producing the same score;
        // the shorter file name wins the tie-break.
        string[] candidates = [@"C:\data\ABxyz.mps", @"C:\data\ABz.mps"];

        Program.MpsMatch? match = Program.SelectBestMatchingMps("AB", candidates);

        Assert.NotNull(match);
        Assert.Equal(@"C:\data\ABz.mps", match!.Path);
    }

    [Fact]
    public void ZeroSharedPrefix_StillReturnsMatchWithScoreZero()
    {
        // The caller (CreateGroupArchives) is responsible for tagging a zero-score match;
        // the selector itself must still return a candidate rather than null.
        string[] candidates = [@"C:\data\ZZZZ.mps"];

        Program.MpsMatch? match = Program.SelectBestMatchingMps("AAAA", candidates);

        Assert.NotNull(match);
        Assert.Equal(0, match!.Score);
    }
}

public sealed class GetOwnerTests
{
    [Theory]
    [InlineData("FK155_JLGU_GE", "FK155_")]
    [InlineData("EL_setup", "EL_")]
    public void ExtractsOwnerPrefixUpToFirstUnderscore(string baseName, string expected)
    {
        Assert.Equal(expected, Program.GetOwner(baseName));
    }

    [Theory]
    [InlineData("nounderscore")]   // no underscore at all
    [InlineData("_leading")]       // underscore at index 0
    [InlineData("my data_value")]  // owner candidate contains a space
    public void ReturnsEmptyWhenNoValidOwnerPrefix(string baseName)
    {
        Assert.Equal("", Program.GetOwner(baseName));
    }
}

public sealed class IsSafeArchiveEntryPathTests
{
    private static readonly string Destination =
        Path.Combine(Path.GetTempPath(), "BioLogicZipper.SafePathTests");

    [Theory]
    [InlineData("data.mpr")]
    [InlineData("sub/data.mpr")]
    [InlineData("sub/../within.txt")] // normalizes back inside the destination
    public void AllowsRelativePathsThatStayInsideDestination(string entry)
    {
        Assert.True(Program.IsSafeArchiveEntryPath(Destination, entry));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute/path.txt")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsTraversalAbsoluteAndEmptyPaths(string? entry)
    {
        Assert.False(Program.IsSafeArchiveEntryPath(Destination, entry));
    }
}

public sealed class IsSameOrAncestorDirectoryTests
{
    [Fact]
    public void SameDirectory_IsTrue()
    {
        string dir = Path.Combine(Path.GetTempPath(), "exp", "data");
        Assert.True(Program.IsSameOrAncestorDirectory(dir, dir));
    }

    [Fact]
    public void AncestorDirectory_IsTrue()
    {
        string ancestor = Path.Combine(Path.GetTempPath(), "exp");
        string group = Path.Combine(Path.GetTempPath(), "exp", "technique", "data");
        Assert.True(Program.IsSameOrAncestorDirectory(ancestor, group));
    }

    [Fact]
    public void SiblingDirectory_IsFalse()
    {
        string candidate = Path.Combine(Path.GetTempPath(), "exp", "cell_01");
        string group = Path.Combine(Path.GetTempPath(), "exp", "cell_02");
        Assert.False(Program.IsSameOrAncestorDirectory(candidate, group));
    }

    [Fact]
    public void DescendantDirectory_IsFalse()
    {
        // candidate is *below* the group, so it is neither the group nor an ancestor.
        string candidate = Path.Combine(Path.GetTempPath(), "exp", "data", "deeper");
        string group = Path.Combine(Path.GetTempPath(), "exp", "data");
        Assert.False(Program.IsSameOrAncestorDirectory(candidate, group));
    }
}

public sealed class ResolveArchiveTargetTests
{
    [Fact]
    public void FirstUse_ProducesUnsuffixedName()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string path = Program.ResolveArchiveTarget("out", "stem", "gz", used);

        Assert.Equal("stem.tar.gz", Path.GetFileName(path));
    }

    [Fact]
    public void RepeatedStem_GetsIncrementingCounter()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string first = Program.ResolveArchiveTarget("out", "stem", "gz", used);
        string second = Program.ResolveArchiveTarget("out", "stem", "gz", used);
        string third = Program.ResolveArchiveTarget("out", "stem", "gz", used);

        Assert.Equal("stem.tar.gz", Path.GetFileName(first));
        Assert.Equal("stem_2.tar.gz", Path.GetFileName(second));
        Assert.Equal("stem_3.tar.gz", Path.GetFileName(third));
    }

    [Fact]
    public void DifferentStems_DoNotCollide()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string a = Program.ResolveArchiveTarget("out", "alpha", "gz", used);
        string b = Program.ResolveArchiveTarget("out", "beta", "gz", used);

        Assert.Equal("alpha.tar.gz", Path.GetFileName(a));
        Assert.Equal("beta.tar.gz", Path.GetFileName(b));
    }
}

public sealed class ResolveOutputDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "BioLogicZipper.OutDirTests", Guid.NewGuid().ToString("N"));

    public ResolveOutputDirectoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void WithoutSecondArgument_UsesArchiveDirectory()
    {
        string archivePath = Path.Combine(_root, "data.zip");

        string outputDir = Program.ResolveOutputDirectory([archivePath], archivePath);

        Assert.Equal(_root, outputDir);
        Assert.True(Directory.Exists(outputDir));
    }

    [Fact]
    public void RelativeSecondArgument_IsCombinedWithArchiveDirectory()
    {
        string archivePath = Path.Combine(_root, "data.zip");

        string outputDir = Program.ResolveOutputDirectory(
            [archivePath, "results"], archivePath);

        Assert.Equal(Path.Combine(_root, "results"), outputDir);
        Assert.True(Directory.Exists(outputDir));
    }

    [Fact]
    public void AbsoluteSecondArgument_IsUsedDirectly()
    {
        string archivePath = Path.Combine(_root, "data.zip");
        string absoluteOut = Path.Combine(_root, "absolute_out");

        string outputDir = Program.ResolveOutputDirectory(
            [archivePath, absoluteOut], archivePath);

        Assert.Equal(absoluteOut, outputDir);
        Assert.True(Directory.Exists(outputDir));
    }
}

public sealed class ResolveContentDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "BioLogicZipper.ContentDirTests", Guid.NewGuid().ToString("N"));

    public ResolveContentDirectoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SingleSubdirectoryNoFiles_DivesIntoSubdirectory()
    {
        string sub = Path.Combine(_root, "experiment");
        Directory.CreateDirectory(sub);

        string content = Program.ResolveContentDirectory(_root);

        Assert.Equal(sub, content);
    }

    [Fact]
    public void MultipleSubdirectories_StaysAtRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "exp1"));
        Directory.CreateDirectory(Path.Combine(_root, "exp2"));

        string content = Program.ResolveContentDirectory(_root);

        Assert.Equal(_root, content);
    }

    [Fact]
    public void SubdirectoryWithLooseFile_StaysAtRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "experiment"));
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "x");

        string content = Program.ResolveContentDirectory(_root);

        Assert.Equal(_root, content);
    }
}
