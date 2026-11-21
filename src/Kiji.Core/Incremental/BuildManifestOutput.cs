namespace Kiji.Incremental;

internal sealed record BuildManifestOutput(string RelativePath, string Hash)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public OutputStamp? Stamp { get; set; }
}
