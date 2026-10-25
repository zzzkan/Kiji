using Microsoft.Extensions.Options;

namespace Kiji.Assets;

/// <summary>Rejects image settings the default processor cannot encode.</summary>
internal sealed class ImageProcessorOptionsValidator : IValidateOptions<ImageProcessorOptions>
{
    public ValidateOptionsResult Validate(string? name, ImageProcessorOptions options)
    {
        if (name != Options.DefaultName)
        {
            return ValidateOptionsResult.Skip;
        }

        List<string> failures = [];
        if (options.Quality is < 1 or > 100)
        {
            failures.Add($"ImageProcessorOptions.Quality must be between 1 and 100, but was {options.Quality}.");
        }
        if (options.MaxWidth < 1)
        {
            failures.Add($"ImageProcessorOptions.MaxWidth must be positive, but was {options.MaxWidth}.");
        }
        foreach (var width in options.Widths?.Where(static width => width < 1).Distinct() ?? [])
        {
            failures.Add($"ImageProcessorOptions.Widths must contain only positive widths, but contained {width}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
