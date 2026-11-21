namespace Kiji.StaticAssets;

internal sealed record StaticAsset(string Source, string Target)
{
    internal string? ExpectedHash { get; init; }
}
