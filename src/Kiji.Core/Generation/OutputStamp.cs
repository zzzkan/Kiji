namespace Kiji.Generation;

internal readonly record struct OutputStamp(long Length, DateTime LastWriteTimeUtc)
{
    internal static OutputStamp? Read(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new(file.Length, file.LastWriteTimeUtc) : null;
    }
}
