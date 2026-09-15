using System.Globalization;
using System.Buffers;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;

namespace Kiji.Generation;

/// <summary>
/// XxHash128-based fingerprints for incremental build inputs and outputs.
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
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                var hash = new XxHash128();
                int read;
                while ((read = stream.Read(buffer)) != 0)
                {
                    hash.Append(buffer.AsSpan(0, read));
                }
                return Convert.ToHexStringLower(hash.GetCurrentHash());
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
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
        return Convert.ToHexStringLower(XxHash128.Hash(MemoryMarshal.AsBytes(value.AsSpan())));
    }

    internal static string HashBytes(ReadOnlySpan<byte> value)
    {
        return Convert.ToHexStringLower(XxHash128.Hash(value));
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

    internal static string HashParameters(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters.Count == 0)
        {
            return HashText(string.Empty);
        }

        var builder = new StringBuilder();
        foreach (var pair in parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            AppendPart(builder, pair.Key);
            AppendPart(builder, pair.Value?.GetType().FullName);
            AppendPart(builder, pair.Value is null ? null : Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
        }

        return HashText(builder.ToString());
    }

    internal static void AppendPart(StringBuilder builder, string? value)
    {
        builder.Append((value?.Length ?? -1).ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }
}
