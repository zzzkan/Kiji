using Kiji.Assets;

namespace Kiji.Imaging.Tests;

public sealed class RepeatedVariantImageProcessor : IImageProcessor
{
    public string CacheIdentity => "repeated-variant-v1";

    public async Task<ProcessedImageInfo> ProcessAsync(string sourceFilePath, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var fileName = Path.Combine("nested", "same.svg");
        Directory.CreateDirectory(Path.Combine(outputDirectory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), "<svg/>", cancellationToken);
        return new ProcessedImageInfo
        {
            OriginalWidth = 2,
            OriginalHeight = 2,
            Variants = [new(fileName, 1), new(fileName, 2)]
        };
    }
}
