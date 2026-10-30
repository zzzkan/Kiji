using Microsoft.Extensions.Logging;

namespace Kiji;

/// <summary>
/// Kiji's own progress messages. Summaries are Information and per-file details are Debug,
/// which <c>"Logging": { "LogLevel": { "Kiji.Build": "Debug" } }</c> enables.
/// </summary>
internal static partial class BuildLoggerExtensions
{
    /// <summary>The category of these messages, also the key that configures their level.</summary>
    internal const string CategoryName = "Kiji.Build";

    [LoggerMessage(1, LogLevel.Information, "Static files: {Copied} copied, {Unchanged} unchanged.")]
    internal static partial void StaticFilesSynced(this ILogger logger, int copied, int unchanged);

    [LoggerMessage(2, LogLevel.Information, "Generated {Written} pages ({Unchanged} unchanged, skipped) in {ElapsedMilliseconds} ms.")]
    internal static partial void PagesGenerated(this ILogger logger, int written, int unchanged, long elapsedMilliseconds);

    [LoggerMessage(3, LogLevel.Information, "Generated {Written} pages ({Unchanged} unchanged, skipped, {Reproduced} re-rendered but unchanged) in {ElapsedMilliseconds} ms.")]
    internal static partial void PagesGeneratedWithReproduced(this ILogger logger, int written, int unchanged, int reproduced, long elapsedMilliseconds);

    [LoggerMessage(4, LogLevel.Information, "Removed {Count} file(s) the build did not produce.")]
    internal static partial void OutputsRemoved(this ILogger logger, int count);

    [LoggerMessage(5, LogLevel.Error, "Publishing failed.")]
    internal static partial void PublishFailed(this ILogger logger, Exception exception);

    [LoggerMessage(6, LogLevel.Warning, "Publishing was canceled; output may be incomplete.")]
    internal static partial void PublishCanceled(this ILogger logger);

    [LoggerMessage(10, LogLevel.Debug, "Generated: {Path}")]
    internal static partial void PageGenerated(this ILogger logger, string path);

    [LoggerMessage(11, LogLevel.Debug, "Unchanged: {Path}")]
    internal static partial void PageUnchanged(this ILogger logger, string path);

    [LoggerMessage(12, LogLevel.Debug, "Generated: {Path} ({Width}w)")]
    internal static partial void ImageVariantGenerated(this ILogger logger, string path, int width);

    [LoggerMessage(13, LogLevel.Debug, "Generated: {Path}")]
    internal static partial void ArtifactGenerated(this ILogger logger, string path);
}
