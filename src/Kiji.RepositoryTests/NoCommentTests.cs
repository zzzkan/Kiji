using System.Text.RegularExpressions;
using Xunit;

namespace Kiji.RepositoryTests;

public sealed partial class NoCommentTests
{
    private const string Fix = "Delete it and let names, types, tests or checks say it (.agents/skills/no-comments).";

    [Fact]
    public void CSharpHasNoComments()
    {
        var failures = RepositoryFiles.List("*.cs")
            .SelectMany(path => CommentScanner.Blocks(RepositoryFiles.ReadText(path))
                .Select(block => $"{path}({block.Line}): comment \"{FirstLine(block.Text)}\". {Fix}"))
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("*.csproj")]
    [InlineData("*.props")]
    [InlineData("*.targets")]
    [InlineData("*.yml")]
    [InlineData("*.ps1")]
    [InlineData("*.sh")]
    [InlineData("*.py")]
    [InlineData("*.js")]
    [InlineData("*.mjs")]
    [InlineData("*.css")]
    [InlineData("*.razor")]
    [InlineData(".editorconfig")]
    [InlineData(".gitignore")]
    public void OtherFilesHaveNoComments(string pathspec)
    {
        var failures = new List<string>();
        foreach (var path in RepositoryFiles.List(pathspec))
        {
            var lines = RepositoryFiles.ReadText(path).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (IsComment(path, lines[index], index))
                {
                    failures.Add($"{path}({index + 1}): comment \"{lines[index].Trim()}\". {Fix}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool IsComment(string path, string line, int index)
    {
        var text = line.TrimStart();
        return Path.GetExtension(path) switch
        {
            ".csproj" or ".props" or ".targets" => line.Contains("<!--", StringComparison.Ordinal),
            ".razor" => line.Contains("<!--", StringComparison.Ordinal) || line.Contains("@*", StringComparison.Ordinal),
            ".css" => line.Contains("/*", StringComparison.Ordinal),
            ".js" or ".mjs" => text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("/*", StringComparison.Ordinal),
            ".yml" => text.StartsWith('#') || text.StartsWith("//", StringComparison.Ordinal) || TrailingYamlComment().IsMatch(line),
            ".ps1" or ".sh" or ".py" => text.StartsWith('#') && !(index == 0 && text.StartsWith("#!", StringComparison.Ordinal)),
            _ => text.StartsWith('#'),
        };
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0];
        return line.Length > 80 ? line[..80] + "..." : line;
    }

    [GeneratedRegex(@"\s#\s(?!v\d+\.\d+\.\d+$)")]
    private static partial Regex TrailingYamlComment();
}
