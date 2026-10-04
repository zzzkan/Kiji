using System.Security.Cryptography;
using Kiji.Generation;
using Microsoft.AspNetCore.WebUtilities;

namespace Kiji;

internal sealed record SiteArtifactRegistration(
    string OutputRelativePath,
    Func<Stream, SiteOutputContext, CancellationToken, Task> WriteAsync,
    bool PreserveUnchangedOutput = false)
{
    internal async Task WriteFileAsync(string path, SiteOutputContext context, CancellationToken cancellationToken)
    {
        if (!PreserveUnchangedOutput)
        {
            await using var output = OpenOutput(path);
            await WriteAsync(output, context, cancellationToken);
            return;
        }

        // Built-in feeds always regenerate. Buffering avoids rewriting identical
        // bytes and retriggering SDK compression. Larger feeds spill to disk.
        await using var buffer = new FileBufferingWriteStream(1024 * 1024, null, () => Path.GetDirectoryName(path)!);
        using var hash = SHA256.Create();
        await using (var hashing = new CryptoStream(buffer, hash, CryptoStreamMode.Write, leaveOpen: true))
        {
            await WriteAsync(hashing, context, cancellationToken);
            await hashing.FlushFinalBlockAsync(cancellationToken);
        }
        if (BuildFingerprint.HashFile(path) == Convert.ToHexStringLower(hash.Hash!)) { return; }

        await using var changedOutput = OpenOutput(path);
        await buffer.DrainBufferAsync(changedOutput, cancellationToken);
    }

    private static FileStream OpenOutput(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
}
