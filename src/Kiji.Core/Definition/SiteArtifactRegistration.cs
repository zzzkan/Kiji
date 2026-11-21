using System.Security.Cryptography;
using Kiji.Incremental;
using Microsoft.AspNetCore.WebUtilities;

namespace Kiji.Definition;

internal sealed record SiteArtifactRegistration(
    string OutputRelativePath,
    Func<Stream, SiteOutputContext, CancellationToken, Task> WriteAsync,
    bool PreserveUnchangedOutput = false,
    bool IndependentOfPages = false)
{
    internal async Task WriteFileAsync(string path, SiteOutputContext context, CancellationToken cancellationToken,
        Task? pagesRendered = null)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!PreserveUnchangedOutput)
        {
            if (pagesRendered is not null) { await pagesRendered; }
            Directory.CreateDirectory(directory);
            await using var output = OpenOutput(path);
            await WriteAsync(output, context, cancellationToken);
            return;
        }

        await using var buffer = new FileBufferingWriteStream(1024 * 1024, null, () => Directory.CreateDirectory(directory).FullName);
        using var hash = SHA256.Create();
        await using (var hashing = new CryptoStream(buffer, hash, CryptoStreamMode.Write, leaveOpen: true))
        {
            await WriteAsync(hashing, context, cancellationToken);
            await hashing.FlushFinalBlockAsync(cancellationToken);
        }
        if (pagesRendered is not null) { await pagesRendered; }
        if (BuildFingerprint.HashFile(path) == Convert.ToHexStringLower(hash.Hash!)) { return; }

        Directory.CreateDirectory(directory);
        await using var changedOutput = OpenOutput(path);
        await buffer.DrainBufferAsync(changedOutput, cancellationToken);
    }

    private static FileStream OpenOutput(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
}
