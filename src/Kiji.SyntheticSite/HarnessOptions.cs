namespace Kiji.SyntheticSite;

/// <summary>
/// Command line options for the synthetic site build harness.
/// </summary>
public sealed record HarnessOptions
{
    public int Pages { get; init; } = 1000;

    public int Runs { get; init; } = 3;

    public bool Images { get; init; }

    public string? Root { get; init; }

    public string? OutJsonPath { get; init; }

    public bool FullEachRun { get; init; }

    /// <summary>
    /// Reports how long each build stage took, per run. A total that moved is not an
    /// explanation until a phase can be pinned on it.
    /// </summary>
    public bool Phases { get; init; }

    public static HarnessOptions Parse(string[] args)
    {
        var options = new HarnessOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pages":
                    options = options with { Pages = int.Parse(RequireValue(args, ref i)) };
                    break;
                case "--runs":
                    options = options with { Runs = int.Parse(RequireValue(args, ref i)) };
                    break;
                case "--images":
                    options = options with { Images = true };
                    break;
                case "--full":
                    options = options with { FullEachRun = true };
                    break;
                case "--phases":
                    options = options with { Phases = true };
                    break;
                case "--root":
                    options = options with { Root = RequireValue(args, ref i) };
                    break;
                case "--out":
                    options = options with { OutJsonPath = RequireValue(args, ref i) };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'. Usage: [--pages N] [--runs N] [--images] [--full] [--phases] [--root <dir>] [--out <json>]");
            }
        }

        return options;
    }

    private static string RequireValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{args[index]}' requires a value.");
        }

        index++;
        return args[index];
    }
}
