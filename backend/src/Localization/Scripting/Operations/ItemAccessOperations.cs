using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Item/Artifact operations: CurrentItem, CurrentItemSet
/// </summary>
public sealed class ItemAccessOperations : IScriptOperation
{
    private readonly DbAccessor _db;
    private readonly ScriptSettings _settings;

    public ItemAccessOperations(DbAccessor db, ScriptSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[] { "CurrentItem", "CurrentItemSet" };

    public bool Execute(
        string operationName,
        string[] args,
        ResolutionContext context,
        ScriptEnvironment env,
        ref string? returnValue)
    {
        string A(int i) => i < args.Length ? args[i] : "";

        return operationName switch
        {
            "CurrentItem" => ExecuteCurrentItem(A(0), A(1), context, env),
            "CurrentItemSet" => ExecuteCurrentItemSet(A(0), A(1), context, env),
            _ => false
        };
    }

    private bool ExecuteCurrentItem(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.ItemId is null) return false;
        if (!_db.TryGetItem(ctx.ItemId, out var item)) return false;

        if (string.Equals(path, "level", StringComparison.OrdinalIgnoreCase))
        {
            env.Set(target, ctx.ItemLevel ?? 1);
            return true;
        }

        var actualPath = StripConfigPrefix(path);

        if (!JsonPathReader.TryGet(item, actualPath, out var el))
        {
            if (_settings.AssumeZeroForMissingNumericConfig && LooksNumericConfig(actualPath))
            {
                env.Set(target, 0.0);
                return true;
            }
            return false;
        }

        el = UnwrapV(el);

        if (ctx.ItemLevel.HasValue && el.ValueKind == JsonValueKind.Number)
        {
            var baseValue = JsonPathReader.AsDouble(el) ?? 0;
            var upgradePath = path + "PerLevel";
            if (JsonPathReader.TryGet(item, upgradePath, out var upgradeEl))
            {
                upgradeEl = UnwrapV(upgradeEl);
                var increment = JsonPathReader.AsDouble(upgradeEl) ?? 0.0;
                env.Set(target, baseValue + (increment * (ctx.ItemLevel.Value - 1)));
                return true;
            }
        }

        if (el.ValueKind == JsonValueKind.Number)
            env.Set(target, JsonPathReader.AsDouble(el) ?? 0);
        else
            env.Set(target, JsonPathReader.AsString(el) ?? "");
        return true;
    }

    private bool ExecuteCurrentItemSet(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.ItemSetId is null) return false;
        if (!_db.TryGetItemSet(ctx.ItemSetId, out var itemSet)) return false;

        var actualPath = StripConfigPrefix(path);
        if (!JsonPathReader.TryGet(itemSet, actualPath, out var el)) return false;

        el = UnwrapV(el);
        if (el.ValueKind == JsonValueKind.Number)
            env.Set(target, JsonPathReader.AsDouble(el) ?? 0);
        else
            env.Set(target, JsonPathReader.AsString(el) ?? "");
        return true;
    }

    private static string StripConfigPrefix(string path)
    {
        if (path.StartsWith("config.", StringComparison.OrdinalIgnoreCase))
            return path.Substring(7);
        if (path.StartsWith("dataConfig.", StringComparison.OrdinalIgnoreCase))
            return path.Substring(11);
        return path;
    }

    private static JsonElement UnwrapV(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("v", out var vEl))
            return vEl;
        return el;
    }

    private static bool LooksNumericConfig(string path) =>
        path.Contains(".upgrade.", StringComparison.Ordinal) ||
        path.EndsWith(".increment", StringComparison.Ordinal);
}
