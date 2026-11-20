using System.Security.Cryptography;
using System.Text;

namespace Kiji.RepositoryTests;

internal sealed record CommentBlock(int Line, string Text)
{
    public string Id { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text)))[..16];

    public bool LinksASource =>
        Text.Contains("https://", StringComparison.Ordinal) || Text.Contains("http://", StringComparison.Ordinal);
}
