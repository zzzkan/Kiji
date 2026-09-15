namespace Kiji;

/// <summary>
/// An extra input participating in the incremental build fingerprint: either a
/// key/value pair or a file/directory path whose content is hashed. Declared via
/// <see cref="StaticSite.AddBuildInput(string)"/> for inputs Kiji cannot track
/// itself (external data, environment-derived values).
/// </summary>
internal sealed record KijiBuildInput(string Key, string? Value, string? Path);
