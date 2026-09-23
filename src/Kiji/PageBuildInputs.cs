using Kiji.Generation;
using Kiji.Rendering;

namespace Kiji;

/// <summary>Reads declared external values and files while recording a page's dependencies.</summary>
public sealed class PageBuildInputs
{
    private readonly DependencyCatalog _catalog;
    internal PageBuildInputs(DependencyCatalog catalog) { _catalog = catalog; }

    /// <summary>Reads a value registered with StaticSite.AddPageInput.</summary>
    public string Read(string key)
    {
        var value = _catalog.Resolve("external:" + key)
            ?? throw new InvalidOperationException($"Unknown page input '{key}'.");
        PageRenderContext.Current?.Dependencies?.AddValue("external:" + key, value);
        return value;
    }

    /// <summary>Reads a file, recording the exact bytes consumed. Use a root-relative path resolved by the application.</summary>
    public static byte[] ReadFile(string path)
    {
        var absolute = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(absolute);
        PageRenderContext.Current?.Dependencies?.AddFile(absolute, BuildFingerprint.HashBytes(bytes));
        return bytes;
    }

    /// <summary>Disables persistent HTML reuse for this render, for example when using a clock.</summary>
    public static void DisableCache() => PageRenderContext.Current?.Dependencies?.DisableCache();
}
