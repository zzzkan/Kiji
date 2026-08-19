namespace Kiji.Generation;

/// <summary>
/// One file as a directory walk reported it: its path and the stamp that came with it.
/// </summary>
internal readonly record struct ScannedFile(string FullPath, long Length, DateTime LastWriteTimeUtc);
