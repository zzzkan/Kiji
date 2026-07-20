namespace Kiji.Markdown;

/// <summary>
/// Mutable slot for the per-document <see cref="ResponsiveImageContext"/> of a pooled
/// Markdig renderer. The image writer is attached to a renderer once (attaching per
/// render would accumulate try-writers); each render swaps the current context in
/// through this holder instead.
/// </summary>
internal sealed class ResponsiveImageContextHolder
{
    internal ResponsiveImageContext? Current { get; set; }
}
