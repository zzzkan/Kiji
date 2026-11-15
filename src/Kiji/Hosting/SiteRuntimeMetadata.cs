namespace Kiji.Hosting;

/// <summary>MSBuild's runtimeconfig contract, read once at the process boundary.</summary>
/// <param name="ProjectDirectory">The site project directory.</param>
/// <param name="OutputPath">The publish destination, absent while serving.</param>
/// <param name="JitProfileDirectory">The machine-local publish JIT profile directory.</param>
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
