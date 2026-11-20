using System.Globalization;

namespace Kiji.Hosting;

internal sealed record PublishJitProfileStamp(string CodeIdentity, int RenderedPages, bool Complete)
{
    private const string Version = "1";

    internal static PublishJitProfileStamp? Read(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Split(' ') is [Version, var identity, var pages, var extent]
                && int.TryParse(pages, NumberStyles.None, CultureInfo.InvariantCulture, out var renderedPages)
                && extent is "complete" or "rendering"
                    ? new PublishJitProfileStamp(identity, renderedPages, extent == "complete")
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Version} {CodeIdentity} {RenderedPages} {(Complete ? "complete" : "rendering")}");
}
