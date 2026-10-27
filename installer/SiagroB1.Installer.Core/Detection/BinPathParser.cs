using System.Text.RegularExpressions;

namespace SiagroB1.Installer.Core.Detection;

/// <summary>Lê o binPath gravado pelos scripts antigos e pelo assistente: "exe" --environment X --urls http://*:porta</summary>
public static partial class BinPathParser
{
    public static (string ExePath, string Environment, int Port)? Parse(string binPath)
    {
        var text = binPath.Trim();
        string exe;
        string rest;
        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            if (close < 0)
            {
                return null;
            }
            exe = text[1..close];
            rest = text[(close + 1)..];
        }
        else
        {
            var space = text.IndexOf(' ');
            exe = space < 0 ? text : text[..space];
            rest = space < 0 ? "" : text[space..];
        }

        var port = UrlsPort().Match(rest);
        if (!port.Success)
        {
            return null;
        }

        var env = Environment().Match(rest);
        return (exe, env.Success ? env.Groups[1].Value : "Production", int.Parse(port.Groups[1].Value));
    }

    [GeneratedRegex(@"--urls[ =]+""?https?://[^:\s""]+:(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlsPort();

    [GeneratedRegex(@"--environment[ =]+""?([^\s""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Environment();
}
