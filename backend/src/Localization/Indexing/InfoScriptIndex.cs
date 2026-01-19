using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Localization.Indexing;

/// <summary>
/// Extracts constant Text(return,"...") literals from game script files.
/// Scripts with complex logic aren't indexed here - they're executed by ScriptInterpreter.
/// </summary>
public sealed partial class InfoScriptIndex
{
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public int Count => _map.Count;

    [GeneratedRegex(@"string\s+(?<name>[A-Za-z0-9_]+)\s*\{(?<body>.*?)\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"Text\(\s*return\s*,\s*(['""])(?<val>.*?)\1\s*\)\s*;?", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TextReturnRegex();

    public InfoScriptIndex(string streamingAssetsRoot)
    {
        if (string.IsNullOrWhiteSpace(streamingAssetsRoot)) return;

        var coreZipPath = Path.Combine(streamingAssetsRoot, "Core.zip");
        if (!File.Exists(coreZipPath)) return;

        try
        {
            using var zip = ZipFile.OpenRead(coreZipPath);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith("DB/info/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.EndsWith(".script", StringComparison.OrdinalIgnoreCase)) continue;

                using var s = entry.Open();
                using var sr = new StreamReader(s);
                var content = sr.ReadToEnd();

                foreach (Match fn in FunctionRegex().Matches(content))
                {
                    var funcName = fn.Groups["name"].Value;
                    var body = fn.Groups["body"].Value;

                    var m = TextReturnRegex().Match(body);
                    if (m.Success && !_map.ContainsKey(funcName))
                    {
                        _map[funcName] = m.Groups["val"].Value;
                    }
                }
            }
        }
        catch
        {
        }
    }

    public string? TryGetConstant(string funcName)
        => string.IsNullOrWhiteSpace(funcName) ? null
           : (_map.TryGetValue(funcName, out var v) ? v : null);
}
