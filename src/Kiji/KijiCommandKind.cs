namespace Kiji;

/// <summary>
/// The command selected from the command line, with its options.
/// </summary>
internal enum KijiCommandKind
{
    Build,
    Dev,
    Preview,
    Clean,
    Unknown,
}
