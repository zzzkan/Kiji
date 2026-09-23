namespace Kiji.Generation;

/// <summary>An image blob and its page-local destination.</summary>
internal sealed record BuildManifestOutput(string RelativePath, string Hash)
{
    // Recipe records are portable and have no output-specific stamp.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public OutputStamp? Stamp { get; set; }
}
