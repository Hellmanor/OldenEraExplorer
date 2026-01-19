using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Buff-related operations: CurrentBuff, CurrentBuffSP, CurrentBuffStacks, CurrentBuffSumMinDmg, CurrentBuffSumMaxDmg, DbBuff, DbSideBuff
/// </summary>
public sealed class BuffAccessOperations : IScriptOperation
{
    private readonly DbAccessor _db;
    private readonly ScriptSettings _settings;

    public BuffAccessOperations(DbAccessor db, ScriptSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "CurrentBuff", "CurrentBuffSP", "CurrentBuffStacks", "CurrentBuffSumMinDmg", "CurrentBuffSumMaxDmg", "DbBuff", "DbSideBuff"
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
            "CurrentBuff" => ExecuteCurrentBuff(A(0), A(1), context, env),
            "CurrentBuffSP" => ExecuteCurrentBuffSP(A(0), context, env),
            "CurrentBuffStacks" => ExecuteCurrentBuffStacks(A(0), context, env),
            "CurrentBuffSumMinDmg" => ExecuteCurrentBuffSumDmg(A(0), context, env, isMax: false),
            "CurrentBuffSumMaxDmg" => ExecuteCurrentBuffSumDmg(A(0), context, env, isMax: true),
            "DbBuff" => ExecuteDbBuff(A(0), A(1), A(2), env),
            "DbSideBuff" => ExecuteDbSideBuff(A(0), A(1), A(2), env),
            _ => false
        };
    }

    private bool ExecuteCurrentBuff(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.BuffId is null) return false;
        if (!_db.TryGetBuff(ctx.BuffId, out var buff)) return false;

        if (string.Equals(path, "charges", StringComparison.OrdinalIgnoreCase))
        {
            env.Set(target, ctx.BuffStacks ?? 1);
            return true;
        }

        var actualPath = StripConfigPrefix(path);

        if (!JsonPathReader.TryGet(buff, actualPath, out var el))
        {
            if (actualPath.Contains("data.stats.hpPerc", StringComparison.Ordinal))
            {
                var aliasPath = actualPath.Replace("data.stats.hpPerc", "data.stats.hp");
                if (JsonPathReader.TryGet(buff, aliasPath, out el))
                {
                }
                else if (_settings.AssumeZeroForMissingNumericConfig && LooksNumericConfig(actualPath))
                {
                    env.Set(target, 0.0);
                    return true;
                }
                else return false;
            }
            else if (_settings.AssumeZeroForMissingNumericConfig && LooksNumericConfig(actualPath))
            {
                env.Set(target, 0.0);
                return true;
            }
            else return false;
        }

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteCurrentBuffSP(string target, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.BuffSpellPower is null) return false;
        env.Set(target, ctx.BuffSpellPower.Value);
        return true;
    }

    private bool ExecuteCurrentBuffStacks(string target, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.BuffStacks is null) return false;
        env.Set(target, ctx.BuffStacks.Value);
        return true;
    }

    private bool ExecuteCurrentBuffSumDmg(string target, ResolutionContext ctx, ScriptEnvironment env, bool isMax)
    {
        if (ctx.BuffId is null || ctx.BuffStacks is null) return false;
        if (!_db.TryGetBuff(ctx.BuffId, out var buff)) return false;

        var stackDmgPath = isMax ? "actions[0].damageDealer.maxStackDmg" : "actions[0].damageDealer.minStackDmg";
        double dmgPerStack = 0.0;

        if (JsonPathReader.TryGet(buff, stackDmgPath, out var dmgEl))
        {
            dmgEl = UnwrapV(dmgEl);
            dmgPerStack = JsonPathReader.AsDouble(dmgEl) ?? 0.0;
        }
        else
        {
            var baseDmgPath = isMax ? "actions[0].damageDealer.maxBaseDmg" : "actions[0].damageDealer.minBaseDmg";
            if (JsonPathReader.TryGet(buff, baseDmgPath, out var baseDmgEl))
            {
                baseDmgEl = UnwrapV(baseDmgEl);
                dmgPerStack = JsonPathReader.AsDouble(baseDmgEl) ?? 0.0;
            }
            else return false;
        }

        env.Set(target, dmgPerStack * ctx.BuffStacks.Value);
        return true;
    }

    private bool ExecuteDbBuff(string target, string sidOrIdTok, string path, ScriptEnvironment env)
    {
        if (!env.TryResolveValue(sidOrIdTok, out var sidVal)) return false;
        var sid = sidVal.ToString();
        if (!_db.TryGetBuff(sid, out var buff)) return false;
        if (!JsonPathReader.TryGet(buff, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteDbSideBuff(string target, string sideBuffIdTok, string path, ScriptEnvironment env)
    {
        if (!env.TryResolveValue(sideBuffIdTok, out var sideBuffIdVal)) return false;
        var sideBuffId = sideBuffIdVal.ToString();
        if (!_db.TryGetSideBuff(sideBuffId, out var sideBuff)) return false;
        if (!JsonPathReader.TryGet(sideBuff, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
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

    private static bool LooksNumericConfig(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.Contains(".upgrade.", StringComparison.Ordinal) ||
               path.Contains("durationPerStack", StringComparison.Ordinal) ||
               path.EndsWith(".duration", StringComparison.Ordinal) ||
               path.Contains(".values[", StringComparison.Ordinal);
    }
}
