using System.IO;

namespace BioLogicZipper.Tests;

internal static class FileComparison
{
    public static bool FilesAreEqual(string path1, string path2)
    {
        FileInfo file1 = new(path1);
        FileInfo file2 = new(path2);

        if (file1.Length != file2.Length)
            return false;

        const int bufferSize = 1024 * 1024;

        using FileStream fs1 = file1.OpenRead();
        using FileStream fs2 = file2.OpenRead();

        byte[] buffer1 = new byte[bufferSize];
        byte[] buffer2 = new byte[bufferSize];

        int read1;

        while ((read1 = fs1.Read(buffer1, 0, buffer1.Length)) > 0)
        {
            int read2 = fs2.Read(buffer2, 0, buffer2.Length);

            if (read1 != read2)
                return false;

            for (int i = 0; i < read1; i++)
            {
                if (buffer1[i] != buffer2[i])
                    return false;
            }
        }

        return true;
    }
}
