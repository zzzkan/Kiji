namespace Kiji.Generation;

/// <summary>Inputs and outputs observed by one page. Read after its render completes.</summary>
internal sealed class BuildDependencyRecorder
{
    // Concurrent work within one page shares a lock; separate pages never contend.
    // Pages without images allocate no image collections.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private HashSet<BuildManifestImage>? _imageRequests;
    private HashSet<string>? _images;
    private HashSet<string>? _outputs;

    internal bool Cacheable { get; private set; } = true;
    internal void DisableCache() { lock (_gate) { Cacheable = false; } }

    internal void AddFile(string absolutePath, string digest)
    {
        lock (_gate)
        {
            if (digest == BuildFingerprint.Missing) { Cacheable = false; }
            if (_files.TryGetValue(absolutePath, out var previous) && previous != digest)
            {
                throw new IOException("An input changed during rendering: " + absolutePath);
            }
            _files[absolutePath] = digest;
        }
    }

    internal void AddValue(string key, string? digest)
    {
        lock (_gate)
        {
            if (digest is null) { Cacheable = false; return; }
            _values[key] = BuildFingerprint.HashText(digest);
        }
    }

    internal void AddImageRequest(string source, string output)
    {
        lock (_gate) { (_imageRequests ??= []).Add(new(source, output)); }
    }

    internal void AddImage(string key)
    {
        lock (_gate) { (_images ??= new(StringComparer.Ordinal)).Add(key); }
    }

    internal void AddOutput(string absolutePath)
    {
        lock (_gate) { (_outputs ??= new(StringComparer.OrdinalIgnoreCase)).Add(absolutePath); }
    }

    internal IEnumerable<KeyValuePair<string, string>> FileInputs => _files;
    internal IEnumerable<BuildManifestDependency> Values => _values.OrderBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => new BuildManifestDependency("value", p.Key, p.Value));
    internal IEnumerable<BuildManifestImage> ImageRequests => _imageRequests ?? [];
    internal string[] Images => _images is null ? [] : [.. _images.Order(StringComparer.Ordinal)];
    internal string[] AdditionalOutputs => _outputs is null ? [] : [.. _outputs.Order(StringComparer.OrdinalIgnoreCase)];
}
