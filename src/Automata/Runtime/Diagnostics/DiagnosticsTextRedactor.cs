using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OrbAutomata;

/// <summary>One deterministic privacy pass for every textual member of a diagnostics bundle.</summary>
internal sealed class DiagnosticsTextRedactor
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly Regex AbsolutePath = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|/|\\\\)[^ \t\r\n""'<>|]+",
        RegexOptions.CultureInvariant);
    private readonly string[] _sensitivePaths;
    private readonly Regex[] _usernames;

    internal DiagnosticsTextRedactor(
        IEnumerable<string>? sensitivePaths = null,
        IEnumerable<string>? usernames = null)
    {
        _sensitivePaths = (sensitivePaths ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .ToArray();
        _usernames = (usernames ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new Regex(
                @"(?<![A-Za-z0-9])" + Regex.Escape(value) + @"(?![A-Za-z0-9])",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            .ToArray();
    }

    internal byte[] Redact(byte[] utf8) => Utf8.GetBytes(Redact(Utf8.GetString(utf8 ?? Array.Empty<byte>())));

    internal string Redact(string text)
    {
        var result = text ?? string.Empty;
        for (var index = 0; index < _sensitivePaths.Length; index++)
            result = ReplaceOrdinalIgnoreCase(result, _sensitivePaths[index], "<user-path>");
        result = AbsolutePath.Replace(result, "<absolute-path>");
        for (var index = 0; index < _usernames.Length; index++)
            result = _usernames[index].Replace(result, "<user>");
        return result;
    }

    private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string replacement)
    {
        var offset = 0;
        var builder = new StringBuilder(source.Length);
        while (true)
        {
            var index = source.IndexOf(oldValue, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                builder.Append(source, offset, source.Length - offset);
                return builder.ToString();
            }
            builder.Append(source, offset, index - offset);
            builder.Append(replacement);
            offset = index + oldValue.Length;
        }
    }
}
