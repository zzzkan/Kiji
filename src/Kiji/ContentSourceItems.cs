namespace Kiji;

/// <summary>
/// What a content loader produces: the items, and optionally where each one came from.
/// </summary>
/// <param name="Items">The loaded items, in source order.</param>
/// <param name="Provenance">
/// The source file of each item, positionally aligned with <paramref name="Items"/>.
/// Loaders that project items into a model the incremental build cannot inspect supply
/// this explicitly; <see langword="null"/> falls back to deriving it from
/// <see cref="IContentSourceFile"/>.
/// </param>
internal readonly record struct ContentSourceItems<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<string?>? Provenance)
    where T : class;
