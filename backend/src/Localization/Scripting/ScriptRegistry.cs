using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Localization.Scripting;

public sealed partial class ScriptRegistry
{
    public sealed class Function
    {
        public string Name { get; init; } = "";
        public string DeclaredType { get; init; } = "";
        public List<Statement> Body { get; } = new();
    }

    public sealed class Statement
    {
        public string Op { get; init; } = "";
        public string[] Args { get; init; } = Array.Empty<string>();
    }

    private readonly Dictionary<string, Function> _funcs = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?ms)^\s*(?<decl>[A-Za-z_]\w*)\s+(?<name>[A-Za-z_]\w*)\s*\{(?<body>.*?)\}", RegexOptions.Compiled)]
    private static partial Regex FuncRx();

    [GeneratedRegex(@"([A-Za-z_]\w*)\s*\((.*?)\)\s*", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex CallRx();

    public ScriptRegistry(string streamingAssetsRoot)
    {
        var zipPath = Path.Combine(streamingAssetsRoot ?? throw new ArgumentNullException(nameof(streamingAssetsRoot)), "Core.zip");
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith("DB/info/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.FullName.EndsWith(".script", StringComparison.OrdinalIgnoreCase)) continue;
            using var s = new StreamReader(e.Open());
            var text = s.ReadToEnd();
            ParseFile(text);
        }
    }

    private void ParseFile(string text)
    {
        foreach (Match m in FuncRx().Matches(text))
        {
            var decl = m.Groups["decl"].Value.Trim();
            var name = m.Groups["name"].Value.Trim();
            var body = m.Groups["body"].Value;

            var fun = new Function { Name = name, DeclaredType = decl };

            foreach (Match c in CallRx().Matches(body))
            {
                var op = c.Groups[1].Value.Trim();
                var args = SplitArgs(c.Groups[2].Value);
                fun.Body.Add(new Statement { Op = op, Args = args });
            }
            _funcs[name] = fun;
        }
    }

    private static string[] SplitArgs(string inner)
    {
        var list = new List<string>();
        int i = 0; int n = inner.Length; bool inStr = false; char strQ = '"';
        var cur = new StringBuilder();
        while (i < n)
        {
            var ch = inner[i];
            if (inStr)
            {
                if (ch == '\\' && i + 1 < n) { cur.Append(ch); cur.Append(inner[i + 1]); i += 2; continue; }
                if (ch == strQ) { inStr = false; cur.Append(ch); i++; continue; }
                cur.Append(ch); i++; continue;
            }
            else
            {
                if (ch == '"' || ch == '\'') { inStr = true; strQ = ch; cur.Append(ch); i++; continue; }
                if (ch == ',') { list.Add(cur.ToString().Trim()); cur.Clear(); i++; continue; }
                cur.Append(ch); i++; continue;
            }
        }
        var last = cur.ToString().Trim();
        if (last.Length > 0) list.Add(last);

        for (int k = 0; k < list.Count; k++)
        {
            var a = list[k].Trim();
            if ((a.StartsWith("\"") && a.EndsWith("\"")) || (a.StartsWith("'") && a.EndsWith("'")))
                a = a.Substring(1, a.Length - 2);
            list[k] = a.Trim();
        }
        return list.ToArray();
    }

    public bool TryGet(string name, out Function fn)
    {
        if (_funcs.TryGetValue(name, out var foundFn) && foundFn is not null)
        {
            fn = foundFn;
            return true;
        }
        fn = new Function();
        return false;
    }
}
