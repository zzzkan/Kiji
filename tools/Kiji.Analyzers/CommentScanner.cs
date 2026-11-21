using System.Text.RegularExpressions;

namespace Kiji.Analyzers;

internal static class CommentScanner
{
    private static readonly Regex s_heredoc = new(@"<<-?\s*(['""]?)([A-Za-z_][A-Za-z0-9_]*)\1", RegexOptions.CultureInvariant);
    private static readonly Regex s_versionPin = new(@"^#\s+v\d+\.\d+\.\d+\s*$", RegexOptions.CultureInvariant);

    internal static ISet<int> CommentLines(string fileName, string[] lines)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".js" or ".mjs" => JavaScript(lines),
            ".sh" => Shell(lines),
            ".py" => Python(lines),
            ".ps1" => PowerShell(lines),
            ".yml" or ".yaml" => Matching(lines, IsYamlComment),
            ".csproj" or ".props" or ".targets" => Matching(lines, line => line.Contains("<!--")),
            ".razor" => Matching(lines, line => line.Contains("<!--") || line.Contains("@*")),
            ".css" => Matching(lines, line => line.Contains("/*")),
            _ when fileName is ".editorconfig" or ".gitignore" => Matching(lines, line => line.TrimStart().StartsWith("#", StringComparison.Ordinal)),
            _ => [],
        };
    }

    private static bool IsYamlComment(string line)
    {
        var hash = HashOutsideQuotes(line);
        return hash >= 0 && (line.Substring(0, hash).Trim().Length == 0 || !s_versionPin.IsMatch(line.Substring(hash)));
    }

    private static HashSet<int> Matching(string[] lines, Func<string, bool> isComment) =>
        [.. Enumerable.Range(0, lines.Length).Where(index => isComment(lines[index]))];

    private static HashSet<int> JavaScript(string[] lines)
    {
        var found = new HashSet<int>();
        var block = false;
        char? quote = null;
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number];
            var regexAfterKeyword = false;
            var previous = '\0';
            if (quote is '\'' or '"') { quote = null; }
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                var next = index + 1 < line.Length ? line[index + 1] : '\0';
                if (block)
                {
                    found.Add(number);
                    if (current == '*' && next == '/') { block = false; index++; }
                    continue;
                }
                if (quote is { } open)
                {
                    if (current == '\\') { index++; }
                    else if (current == open) { quote = null; previous = current; }
                    continue;
                }
                if (char.IsWhiteSpace(current)) { continue; }
                if (char.IsLetterOrDigit(current) || current is '_' or '$')
                {
                    var start = index;
                    while (index + 1 < line.Length && (char.IsLetterOrDigit(line[index + 1]) || line[index + 1] is '_' or '$')) { index++; }
                    regexAfterKeyword = previous != '.' && index - start + 1 == "return".Length
                        && string.CompareOrdinal(line, start, "return", 0, "return".Length) == 0;
                    previous = line[index];
                    continue;
                }
                if (current is '\'' or '"' or '`') { regexAfterKeyword = false; quote = current; continue; }
                if (current == '/' && next == '/') { found.Add(number); break; }
                if (current == '/' && next == '*') { found.Add(number); block = true; index++; continue; }
                if (current == '/' && (regexAfterKeyword || StartsRegex(previous))) { index = SkipRegex(line, index); }
                regexAfterKeyword = false;
                previous = current;
            }
        }
        return found;
    }

    private static bool StartsRegex(char previous) => previous is '\0' or '(' or ',' or '=' or ':' or '[' or '!' or '&' or '|' or '?' or '{' or '}' or ';';

    private static int SkipRegex(string line, int start)
    {
        var inClass = false;
        for (var index = start + 1; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '\\') { index++; }
            else if (current == '[') { inClass = true; }
            else if (current == ']') { inClass = false; }
            else if (current == '/' && !inClass) { return index; }
        }
        return line.Length;
    }

    private static HashSet<int> Shell(string[] lines)
    {
        var found = new HashSet<int>();
        string? heredoc = null;
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number];
            if (heredoc is not null)
            {
                if (line.Trim() == heredoc) { heredoc = null; }
                continue;
            }
            if (number == 0 && line.StartsWith("#!", StringComparison.Ordinal)) { continue; }
            if (HashOutsideQuotes(line) >= 0)
            {
                found.Add(number);
            }
            var match = s_heredoc.Match(line);
            if (match.Success) { heredoc = match.Groups[2].Value; }
        }
        return found;
    }

    private static int HashOutsideQuotes(string line)
    {
        char? quote = null;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (quote is { } open)
            {
                if (current == '\\' && open == '"') { index++; }
                else if (current == open) { quote = null; }
                continue;
            }
            if (current == '\\') { index++; continue; }
            if (current is '\'' or '"') { quote = current; continue; }
            if (current == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1]) || line[index - 1] is ';' or '|' or '&' or '(' or ')'))
            {
                return index;
            }
        }
        return -1;
    }

    private static HashSet<int> Python(string[] lines)
    {
        var found = new HashSet<int>();
        string? triple = null;
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number];
            if (number == 0 && line.StartsWith("#!", StringComparison.Ordinal)) { continue; }
            char? quote = null;
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                if (triple is not null)
                {
                    if (current == '\\') { index++; }
                    else if (string.CompareOrdinal(line, index, triple, 0, 3) == 0) { triple = null; index += 2; }
                    continue;
                }
                if (quote is { } open)
                {
                    if (current == '\\') { index++; }
                    else if (current == open) { quote = null; }
                    continue;
                }
                if (current is '\'' or '"')
                {
                    if (index + 2 < line.Length && line[index + 1] == current && line[index + 2] == current) { triple = new string(current, 3); index += 2; }
                    else { quote = current; }
                    continue;
                }
                if (current == '#') { found.Add(number); break; }
            }
        }
        return found;
    }

    private static HashSet<int> PowerShell(string[] lines)
    {
        var found = new HashSet<int>();
        var block = false;
        string? hereString = null;
        for (var number = 0; number < lines.Length; number++)
        {
            var line = lines[number];
            if (hereString is not null)
            {
                if (line.StartsWith(hereString, StringComparison.Ordinal)) { hereString = null; }
                continue;
            }
            char? quote = null;
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                var next = index + 1 < line.Length ? line[index + 1] : '\0';
                if (block)
                {
                    found.Add(number);
                    if (current == '#' && next == '>') { block = false; index++; }
                    continue;
                }
                if (quote is { } open)
                {
                    if (current == '`' && open == '"') { index++; }
                    else if (current == open) { quote = null; }
                    continue;
                }
                if (current == '@' && next is '\'' or '"' && index + 2 == line.Length) { hereString = next + "@"; break; }
                if (current == '`') { index++; continue; }
                if (current is '\'' or '"') { quote = current; continue; }
                if (current == '<' && next == '#') { found.Add(number); block = true; index++; continue; }
                if (current == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1]) || line[index - 1] is ';' or '|' or '(' or ')' or '{' or '}'))
                {
                    found.Add(number);
                    break;
                }
            }
        }
        return found;
    }
}
