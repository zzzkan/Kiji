using System.Security.Cryptography;
using Kiji.Generation;
using Microsoft.AspNetCore.WebUtilities;

namespace Kiji;

/// <param name="OutputRelativePath">The artifact's path under the output directory.</param>
/// <param name="WriteAsync">Writes the artifact's bytes.</param>
/// <param name="PreserveUnchangedOutput">Keeps an existing file whose bytes are unchanged.</param>
/// <param name="IndependentOfPages">
/// The writer runs no site code and reads only the page list, so it runs while pages render;
/// its bytes still reach the output directory only after every page has rendered.
/// Writers that run site code, such as custom artifacts and feed factories, run after all
/// pages in registration order.
/// </param>
internal sealed record SiteArtifactRegistration(
    string OutputRelativePath,
    Func<Stream, SiteOutputContext, CancellationToken, Task> WriteAsync,
    bool PreserveUnchangedOutput = false,
    bool IndependentOfPages = false)
{
    /// <param name="path">The artifact's full output path.</param>
    /// <param name="context">The site metadata and pages handed to the writer.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="pagesRendered">
    /// Completes when every page has rendered. A buffered writer runs ahead of it, but the
    /// output directory is touched only afterwards, so a failed build leaves the previous file.
    /// </param>
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
