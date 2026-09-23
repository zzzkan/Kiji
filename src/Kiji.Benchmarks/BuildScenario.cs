namespace Kiji.Benchmarks;

/// <summary>
/// The build scenarios a site author waits on.
/// </summary>
public enum BuildScenario
{
    /// <summary>
    /// Every page re-rendered, the output directory already populated — a CI rebuild
    /// on a warm checkout, and what <c>--force</c> costs locally.
    /// </summary>
    Full,

    /// <summary>
    /// Nothing changed since the last build: the cost of proving that.
    /// </summary>
    NoChange,

    /// <summary>Only the portable cache survives; all outputs are restored.</summary>
    CacheOnly,

    /// <summary>
    /// One post edited — the number a site author feels while writing.
    /// </summary>
    OneEdited,

    /// <summary>
    /// The site's own code changed, so every page must render again even though the
    /// manifest is intact — editing a layout component, and the most common reason a
    /// whole site re-renders during development.
    /// </summary>
    CodeChanged,
}
