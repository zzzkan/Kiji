using Xunit;

namespace Kiji.RepositoryTests;

public sealed class CommentBaselineTests
{
    private const string BaselineFile = "src/Kiji.RepositoryTests/comment-baseline.txt";

    [Fact]
    public void NoCommentIsAdded()
    {
        var allowed = ReadBaseline();
        var failures = new List<string>();
        foreach (var path in RepositoryFiles.List("*.cs"))
        {
            foreach (var block in CommentScanner.UnlinkedBlocks(RepositoryFiles.ReadText(path)))
            {
                var entry = $"{block.Id}  {path}";
                if (allowed.TryGetValue(entry, out var remaining) && remaining > 0)
                {
                    allowed[entry] = remaining - 1;
                }
                else
                {
                    failures.Add($"{path}({block.Line}): new comment \"{FirstLine(block.Text)}\". Delete it and make names, types, tests or checks carry the meaning (.agents/skills/no-comments). A comment about an external constraint we cannot change must link its source (https://...).");
                }
            }
        }

        foreach (var (entry, remaining) in allowed.Where(pair => pair.Value > 0))
        {
            failures.Add($"A baseline comment is gone. Remove the line \"{entry}\" from {BaselineFile}{(remaining > 1 ? $" ({remaining} times)" : "")}. Lines are never added to it.");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Dictionary<string, int> ReadBaseline() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "comment-baseline.txt"))
            .Where(line => line.Length > 0)
            .GroupBy(line => line, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0];
        return line.Length > 80 ? line[..80] + "..." : line;
    }
}
