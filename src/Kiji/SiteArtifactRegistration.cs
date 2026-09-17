namespace Kiji;

internal sealed record SiteArtifactRegistration(
    string OutputRelativePath,
    Func<Stream, SiteOutputContext, CancellationToken, Task> WriteAsync);
