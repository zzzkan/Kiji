using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kiji.Generation;

/// <summary>
/// SHA-256-based fingerprints for incremental build inputs and outputs.
/// </summary>
internal static class BuildFingerprint
{
    /// <summary>Fingerprint reported for inputs that no longer exist.</summary>
    internal const string Missing = "missing";

    internal static string HashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return Missing;
        }
        catch (UnauthorizedAccessException)
        {
            return Missing;
        }
    }

    internal static string HashText(string value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        Span<byte> bytes = count <= 1024 ? stackalloc byte[count] : new byte[count];
        Encoding.UTF8.GetBytes(value, bytes);
        return HashBytes(bytes);
    }

    internal static string HashBytes(ReadOnlySpan<byte> value)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(value, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Compares an output with already verified or freshly rendered bytes.</summary>
    internal static bool FileEquals(string path, ReadOnlySpan<byte> expected)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (RandomAccess.GetLength(handle) != expected.Length) { return false; }
            Span<byte> buffer = stackalloc byte[4096];
            var offset = 0;
            while (offset < expected.Length)
            {
                var read = RandomAccess.Read(handle, buffer[..Math.Min(buffer.Length, expected.Length - offset)], offset);
                if (read == 0 || !buffer[..read].SequenceEqual(expected.Slice(offset, read))) { return false; }
                offset += read;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Digest over a set of (relative path, per-file content hash) pairs, ordered by
    /// path so the result is independent of enumeration order.
    /// </summary>
    internal static string HashFileSet(IEnumerable<(string RelativePath, string ContentHash)> files)
    {
        var builder = new StringBuilder();
        foreach (var (relativePath, contentHash) in files.OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            AppendPart(builder, relativePath.Replace('\\', '/'));
            AppendPart(builder, contentHash);
        }

        return HashText(builder.ToString());
    }

    internal static string? HashParameters(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0)
        {
            return HashText(string.Empty);
        }

        var builder = new StringBuilder();
        foreach (var pair in parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var value = pair.Value;
            if (value is not (null or string or bool or char or sbyte or byte or short or ushort or int or uint or long or ulong or Enum or Guid))
            {
                return null;
            }
            AppendPart(builder, pair.Key);
            AppendPart(builder, value?.GetType().AssemblyQualifiedName);
            AppendPart(builder, value is Enum enumeration
                ? enumeration.ToString("D")
                : Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        return HashText(builder.ToString());
    }

    internal static void AppendPart(StringBuilder builder, string? value)
    {
        builder.Append((value?.Length ?? -1).ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }
}
