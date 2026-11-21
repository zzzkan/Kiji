namespace Kiji.Paths;

internal sealed record SiteRuntimeMetadata(
    string? ProjectDirectory = null, string? OutputPath = null, string? JitProfileDirectory = null)
{
    internal static SiteRuntimeMetadata Read(Func<string, object?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return new(
            read("Kiji.ProjectDirectory") as string,
            read("Kiji.OutputPath") as string,
            read("Kiji.JitProfileDirectory") as string);
    }
}
