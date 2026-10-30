namespace Kiji.Tests.TestSite;

/// <summary>An options type whose default differs from what binding a null value produces.</summary>
public sealed class FeatureOptions
{
    public bool Enabled { get; set; } = true;
}
