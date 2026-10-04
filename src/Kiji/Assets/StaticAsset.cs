namespace Kiji.Assets;

internal sealed record StaticAsset(string Source, string Target)
{
    internal string? ExpectedHash { get; init; }
}
