using System.Diagnostics;

namespace Kiji.Generation;

/// <summary>
/// Copies static files from a source directory to an output directory.
/// </summary>
public static class StaticFileCopier
{
    /// <summary>
    /// Copies all files from <paramref name="staticDirectory"/> to <paramref name="outputDirectory"/>,
    /// preserving the directory structure. Skipped when the static directory does not exist.
    /// </summary>
    public static async Task CopyAsync(string staticDirectory, string outputDirectory)
    {
        if (!Directory.Exists(staticDirectory))
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        var files = Directory.EnumerateFiles(staticDirectory, "*", SearchOption.AllDirectories).ToArray();

        var copies = new (string Source, string Destination)[files.Length];
        var destinationDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Length; i++)
        {
            var relativePath = Path.GetRelativePath(staticDirectory, files[i]);
            var destination = Path.Combine(outputDirectory, relativePath);
            copies[i] = (files[i], destination);
            destinationDirectories.Add(Path.GetDirectoryName(destination)!);
        }

        foreach (var directory in destinationDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        await Parallel.ForEachAsync(
            copies,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            static (copy, _) =>
            {
                // File.Copy takes the kernel copy fast path (CopyFileEx on Windows),
                // which beats streaming through managed buffers.
                File.Copy(copy.Source, copy.Destination, overwrite: true);
                BuildOutput.Detail($"Copied: {copy.Destination}");
                return ValueTask.CompletedTask;
            });

        BuildOutput.Info($"Copied {files.Length} static files in {stopwatch.ElapsedMilliseconds} ms.");
    }
}
