using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Miscellaneous operations: CurrentFractionLawConfig, BuildingsCount, CurrentSentry, EventBankCurrentVariant,
/// context references (Ability, Hero, Unit, Player, Side), Campage, EgorInt
/// </summary>
public sealed class MiscAccessOperations : IScriptOperation
{
    private readonly DbAccessor _db;

    public MiscAccessOperations(DbAccessor db)
    {
        _db = db;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "CurrentFractionLawConfig", "BuildingsCount", "CurrentSentry", "EventBankCurrentVariant",
        "Ability", "Hero", "Unit", "Player", "Side", "Campage", "EgorInt"
    };

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
            "CurrentFractionLawConfig" => ExecuteCurrentFractionLawConfig(A(0), A(1), context, env),
            "BuildingsCount" => ExecuteBuildingsCount(A(0), env),
            "CurrentSentry" or "EventBankCurrentVariant" => ExecuteMapObjectAccess(A(0), A(1), context, env),
            "Ability" or "Hero" or "Unit" or "Player" or "Side" => ExecuteContextRef(A(0), operationName, env),
            "Campage" => ExecuteCampage(A(0), env),
            "EgorInt" => ExecuteEgorInt(A(0), A(1), env),
            _ => false
        };
    }

    private bool ExecuteCurrentFractionLawConfig(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.LawId is null) return false;
        if (!_db.TryGetLaw(ctx.LawId, out var law)) return false;

        var actualPath = StripConfigPrefix(path);

        if (!actualPath.StartsWith("parametersPerLevel", StringComparison.OrdinalIgnoreCase))
        {
            var levelIndex = (ctx.LawLevel ?? 1) - 1;
            actualPath = $"parametersPerLevel[{levelIndex}].{actualPath}";
        }

        if (!JsonPathReader.TryGet(law, actualPath, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteBuildingsCount(string target, ScriptEnvironment env)
    {
        env.Set(target, 0.0);
        return true;
    }

    private bool ExecuteMapObjectAccess(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.MapObjectId is null) return false;
        if (!_db.TryGetMapObject(ctx.MapObjectId, out var mapObj)) return false;
        if (!JsonPathReader.TryGet(mapObj, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteContextRef(string target, string opName, ScriptEnvironment env)
    {
        env.Set(target, opName.ToLowerInvariant());
        return true;
    }

    private bool ExecuteCampage(string target, ScriptEnvironment env)
    {
        env.Set(target, 0.0);
        return true;
    }

    private bool ExecuteEgorInt(string target, string valueTok, ScriptEnvironment env)
    {
        if (!env.TryResolveValue(valueTok, out var v)) return false;
        env.Set(target, Math.Floor(v.AsDouble()));
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

    private static void SetFromJsonElement(ScriptEnvironment env, string target, JsonElement el)
    {
        el = UnwrapV(el);
        if (el.ValueKind == JsonValueKind.Number)
            env.Set(target, JsonPathReader.AsDouble(el) ?? 0);
        else
            env.Set(target, JsonPathReader.AsString(el) ?? "");
    }

    private static JsonElement UnwrapV(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("v", out var vEl))
            return vEl;
        return el;
    }
}
