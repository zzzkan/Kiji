namespace Kiji;

/// <summary>
/// A parsed Kiji command line. Only the members relevant to <see cref="Kind"/> are meaningful.
/// </summary>
internal sealed record KijiCommand(
    KijiCommandKind Kind,
    int Port = KijiCommandLine.DefaultPort,
    string? RawCommand = null,
    bool Verbose = false,
    bool Force = false);
