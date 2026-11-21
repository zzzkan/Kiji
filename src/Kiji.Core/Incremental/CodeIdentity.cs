using System.Reflection;

namespace Kiji.Incremental;

internal static class CodeIdentity
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, Lazy<List<string>>> CodeDependencies = [];

    internal static IReadOnlyList<string> Collect(IEnumerable<Assembly> roots)
    {
        return [.. roots.Distinct().SelectMany(root => CodeDependencies.GetValue(root,
                static root => new Lazy<List<string>>(() => CollectAssemblyDependencies(root))).Value)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static List<string> CollectAssemblyDependencies(Assembly root)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var fingerprints = new List<string>();
        var queue = new Queue<Assembly>([root]);

        while (queue.TryDequeue(out var assembly))
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (!visited.Add(name))
            {
                continue;
            }

            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
            {
                fingerprints.Add($"{name}:unavailable");
                continue;
            }
            var mvid = assembly.ManifestModule.ModuleVersionId;
            fingerprints.Add(mvid == Guid.Empty ? $"{name}:unavailable" : $"{name}:mvid:{mvid:N}");

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (visited.Contains(reference.Name ?? string.Empty))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    visited.Add(reference.Name ?? string.Empty);
                    fingerprints.Add($"{reference.Name}:unavailable");
                }
            }
        }

        fingerprints.Sort(StringComparer.Ordinal);
        return fingerprints;
    }
}
