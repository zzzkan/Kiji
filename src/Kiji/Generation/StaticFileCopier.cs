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

        foreach (var file in Directory.EnumerateFiles(staticDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            await CopyFileAsync(staticDirectory, outputDirectory, file);
        }

        foreach (var directory in Directory.EnumerateDirectories(staticDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                await CopyFileAsync(staticDirectory, outputDirectory, file);
            }
        }

        Console.WriteLine($"Static files copied from: {staticDirectory}");
    }

    private static async Task CopyFileAsync(string staticDirectory, string outputDirectory, string file)
    {
        var relativePath = Path.GetRelativePath(staticDirectory, file);
        var dest = Path.Combine(outputDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        using var src = File.OpenRead(file);
        using var dst = File.Create(dest);
        await src.CopyToAsync(dst);
    }
}
