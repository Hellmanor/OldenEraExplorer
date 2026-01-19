using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Database lookup operations: DbAbility, DbObstacle, DbTrap
/// </summary>
public sealed class DbOperations : IScriptOperation
{
    private readonly DbAccessor _db;

    public DbOperations(DbAccessor db)
    {
        _db = db;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "DbAbility", "DbObstacle", "DbTrap"
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
            "DbAbility" => ExecuteDbAbility(A(0), A(1), A(2), A(3), env),
            "DbObstacle" => ExecuteDbObstacle(A(0), A(1), A(2), env),
            "DbTrap" => ExecuteDbTrap(A(0), A(1), A(2), env),
            _ => false
        };
    }

    private bool ExecuteDbAbility(string target, string abilityIdTok, string levelTok, string path, ScriptEnvironment env)
    {
        if (!env.TryResolveValue(abilityIdTok, out var abilityIdVal)) return false;
        var abilityId = abilityIdVal.ToString();

        if (!env.TryResolveValue(levelTok, out var levelVal)) return false;
        int level = (int)levelVal.AsDouble();

        if (!_db.TryGetAbility(abilityId, out var ability)) return false;

        if (!ability.TryGetProperty("levels", out var levelsArray) ||
            levelsArray.ValueKind != JsonValueKind.Array)
            return false;

        if (level < 0 || level >= levelsArray.GetArrayLength())
            return false;

        var levelElement = GetArrayElement(levelsArray, level);
        if (levelElement is null) return false;

        if (!JsonPathReader.TryGet(levelElement.Value, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteDbObstacle(string target, string obstacleIdTok, string path, ScriptEnvironment env)
    {
        if (!env.TryResolveValue(obstacleIdTok, out var obstacleIdVal)) return false;
        var obstacleId = obstacleIdVal.ToString();

        if (!_db.TryGetObstacle(obstacleId, out var obstacle)) return false;
        if (!JsonPathReader.TryGet(obstacle, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteDbTrap(string target, string trapSidTok, string path, ScriptEnvironment env)
    {
        if (!env.TryGet(trapSidTok, out var trapSidVal)) return false;
        var trapId = trapSidVal.StringValue ?? trapSidVal.ToString();
        if (string.IsNullOrEmpty(trapId)) return false;

        if (!_db.TryGetTrap(trapId, out var trap)) return false;
        if (!JsonPathReader.TryGet(trap, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private static JsonElement? GetArrayElement(JsonElement array, int index)
    {
        int i = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (i == index) return item;
            i++;
        }
        return null;
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
