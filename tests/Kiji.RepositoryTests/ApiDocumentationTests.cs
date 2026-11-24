using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Kiji.RepositoryTests;

public sealed class ApiDocumentationTests
{
    [Fact]
    public void EveryPublicTypeIsDocumentedOnItsNamespacePage()
    {
        var configuration = typeof(ApiDocumentationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "KijiConfiguration").Value!;
        var assemblies = RepositoryFiles.List("src/*/*.csproj")
            .Select(project => Path.GetFileNameWithoutExtension(project))
            .Where(name => name != "Kiji.Runtime")
            .Select(name => Path.Combine(RepositoryFiles.Root, "src", name, "bin", configuration, "net10.0", $"{name}.dll"))
            .ToList();

        var publicTypes = assemblies.SelectMany(PublicTypes).Distinct().Order().ToList();
        Assert.Contains(("Kiji", "StaticSite"), publicTypes);
        Assert.Contains(("Kiji.Images", "IImageProcessor"), publicTypes);

        var failures = new List<string>();
        foreach (var (ns, type) in publicTypes)
        {
            var page = $"docs/content/api-{(ns == "Kiji" ? "kiji" : ns["Kiji.".Length..].Replace('.', '-').ToLowerInvariant())}/index.md";
            var path = Path.Combine(RepositoryFiles.Root, page);
            if (!File.Exists(path))
            {
                failures.Add($"{ns}.{type}: {page} does not exist. Each public namespace has an API page.");
            }
            else if (!File.ReadAllText(path).Contains($"`{type}", StringComparison.Ordinal))
            {
                failures.Add($"{ns}.{type} is not mentioned on {page}. Document the author-facing API change there.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IEnumerable<(string Namespace, string Type)> PublicTypes(string assembly)
    {
        Assert.True(File.Exists(assembly), $"{assembly} was not built.");
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var types = new List<(string, string)>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var definition = metadata.GetTypeDefinition(handle);
            if ((definition.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
            {
                continue;
            }

            var name = metadata.GetString(definition.Name);
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            types.Add((metadata.GetString(definition.Namespace), tick < 0 ? name : name[..tick]));
        }

        return types;
    }
}
