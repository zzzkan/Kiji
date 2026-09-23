namespace Kiji.Generation;

/// <summary>A fast reuse check for an output exclusively managed by Kiji.</summary>
internal readonly record struct OutputStamp(long Length, DateTime LastWriteTimeUtc)
{
    internal static OutputStamp? Read(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new(file.Length, file.LastWriteTimeUtc) : null;
    }
}
