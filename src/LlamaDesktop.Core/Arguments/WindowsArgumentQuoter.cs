using System.Text;

namespace LlamaDesktop.Core.Arguments;

public static class WindowsArgumentQuoter
{
    public static string Quote(IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(QuoteOne(args[i]));
        }
        return sb.ToString();
    }

    private static string QuoteOne(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (arg.All(c => !char.IsWhiteSpace(c) && c != '"')) return arg;
        var sb = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }
            sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
