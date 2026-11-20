using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kiji.RepositoryTests;

internal static class CommentScanner
{
    private static readonly CSharpParseOptions s_options = new(LanguageVersion.Preview);

    public static IEnumerable<CommentBlock> UnlinkedBlocks(string source) =>
        Blocks(source).Where(block => !block.LinksASource);

    private static List<CommentBlock> Blocks(string source)
    {
        var blocks = new List<(int Line, List<string> Lines)>();
        var lastSingleLineCommentLine = int.MinValue;
        foreach (var trivia in CSharpSyntaxTree.ParseText(source, s_options).GetRoot().DescendantTrivia(descendIntoTrivia: true))
        {
            var isSingleLine = trivia.IsKind(SyntaxKind.SingleLineCommentTrivia);
            if (!isSingleLine && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                continue;
            }

            var line = trivia.GetLocation().GetLineSpan().StartLinePosition.Line;
            var text = string.Join('\n', trivia.ToString().Split('\n').Select(part => part.Trim()));
            if (isSingleLine && line == lastSingleLineCommentLine + 1)
            {
                blocks[^1].Lines.Add(text);
            }
            else
            {
                blocks.Add((line + 1, [text]));
            }

            lastSingleLineCommentLine = isSingleLine ? line : int.MinValue;
        }

        return [.. blocks.Select(block => new CommentBlock(block.Line, string.Join('\n', block.Lines)))];
    }
}
