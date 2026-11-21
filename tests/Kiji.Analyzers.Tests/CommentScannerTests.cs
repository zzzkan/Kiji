using Xunit;

namespace Kiji.Analyzers.Tests;

public sealed class CommentScannerTests
{
    [Theory]
    [InlineData("a.js", "const a = 1; // trailing")]
    [InlineData("a.js", "call(); /* trailing */")]
    [InlineData("a.mjs", "export const b = 2; // trailing")]
    [InlineData("a.sh", "echo done # trailing")]
    [InlineData("a.sh", "run;# trailing")]
    [InlineData("a.py", "value = 1  # trailing")]
    [InlineData("a.py", "call('x')# trailing")]
    [InlineData("a.ps1", "Write-Host 'done' # trailing")]
    [InlineData("a.ps1", "$a = 1 <# trailing #>")]
    [InlineData("a.yml", "run: dotnet test # trailing")]
    public void TrailingComment_IsReported(string file, string line)
    {
        Assert.Equal([0], CommentScanner.CommentLines(file, [line]));
    }

    [Theory]
    [InlineData("a.js", "const url = 'https://example.com/';")]
    [InlineData("a.js", "const url = \"https://example.com/\";")]
    [InlineData("a.js", "const url = `https://${host}/path`;")]
    [InlineData("a.js", "const slashes = /\\/\\//g;")]
    [InlineData("a.js", "const ratio = width / height / 2;")]
    [InlineData("a.sh", "echo \"#not a comment\"")]
    [InlineData("a.sh", "echo '# not a comment'")]
    [InlineData("a.sh", "count=${#items[@]}; echo $#")]
    [InlineData("a.sh", "url=http://host/#anchor")]
    [InlineData("a.py", "color = '#fff'")]
    [InlineData("a.py", "text = \"a # b\"")]
    [InlineData("a.ps1", "$color = '#fff'; $text = \"a # b\"")]
    [InlineData("a.ps1", "$value = $items[0]#not a comment")]
    [InlineData("a.yml", "- uses: actions/checkout@abc123 # v7.0.1")]
    [InlineData("a.yml", "value: 'a # b'")]
    public void CommentMarkerInsideCode_IsNotReported(string file, string line)
    {
        Assert.Empty(CommentScanner.CommentLines(file, [line]));
    }

    [Fact]
    public void WholeLineVersionComment_IsReported()
    {
        Assert.Equal([0], CommentScanner.CommentLines("a.yml", ["# v1.2.3"]));
    }

    [Fact]
    public void Shebang_IsNotAComment()
    {
        Assert.Empty(CommentScanner.CommentLines("a.sh", ["#!/usr/bin/env bash", "echo ok"]));
        Assert.Empty(CommentScanner.CommentLines("a.py", ["#!/usr/bin/env python3", "print('ok')"]));
    }

    [Fact]
    public void PythonTripleQuotedString_HidesHashUntilItCloses()
    {
        string[] lines = ["text = \"\"\"", "# inside the string", "\"\"\"  # after it"];

        Assert.Equal([2], CommentScanner.CommentLines("a.py", lines));
    }

    [Fact]
    public void ShellHeredoc_HidesHashUntilItsDelimiter()
    {
        string[] lines = ["cat > file <<'EOF'", "# data, not a comment", "EOF", "echo done # comment"];

        Assert.Equal([3], CommentScanner.CommentLines("a.sh", lines));
    }

    [Fact]
    public void JavaScriptBlockComment_CoversEveryLine()
    {
        string[] lines = ["a();", "/* first", "second */ b();", "c();"];

        Assert.Equal([1, 2], CommentScanner.CommentLines("a.js", lines).Order());
    }

    [Fact]
    public void PowerShellHereString_HidesHashUntilItCloses()
    {
        string[] lines = ["$text = @'", "# data", "'@", "Write-Host $text # comment"];

        Assert.Equal([3], CommentScanner.CommentLines("a.ps1", lines));
    }
}
