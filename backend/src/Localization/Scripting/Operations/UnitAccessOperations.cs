using System.Globalization;
using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Unit-related operations: CurrentUnitConfig, CurrentUnitStats, CurrentUnitData, CurrentAbility
/// </summary>
public sealed class UnitAccessOperations : IScriptOperation
{
    private readonly DbAccessor _db;
    private readonly ScriptSettings _settings;

    public UnitAccessOperations(DbAccessor db, ScriptSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "CurrentUnitConfig", "CurrentUnitStats", "CurrentUnitData", "CurrentAbility"
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
            "CurrentUnitConfig" => ExecuteCurrentUnitConfig(A(0), A(1), context, env),
            "CurrentUnitStats" => ExecuteCurrentUnitStats(A(0), A(1), context, env),
            "CurrentUnitData" => ExecuteCurrentUnitData(A(0), A(1), context, env),
            "CurrentAbility" => ExecuteCurrentAbility(A(0), A(1), context, env),
            _ => false
        };
    }

    private bool ExecuteCurrentUnitConfig(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.UnitId is null) return false;
        if (!_db.TryGetUnit(ctx.UnitId, out var unit)) return false;
        if (!JsonPathReader.TryGet(unit, path, out var el))
        {
            if (_settings.AssumeZeroForMissingNumericConfig && LooksNumericConfig(path))
            {
                env.Set(target, 0.0);
                return true;
            }
            return false;
        }

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteCurrentUnitStats(string target, string statName, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.UnitId is null) return false;
        if (!_db.TryGetUnit(ctx.UnitId, out var unit)) return false;

        string[] candidates = { $"stats.{statName}", statName };
        foreach (var path in candidates)
        {
            if (JsonPathReader.TryGet(unit, path, out var el))
            {
                SetFromJsonElement(env, target, el);
                return true;
            }
        }

        if (_settings.AssumeZeroForMissingNumericConfig)
        {
            env.Set(target, 0.0);
            return true;
        }
        return false;
    }

    private bool ExecuteCurrentUnitData(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.UnitId is null) return false;
        if (!_db.TryGetUnit(ctx.UnitId, out var unit)) return false;
        if (!JsonPathReader.TryGet(unit, path, out var el))
        {
            // Baseline only for the two known stack fields
            if (_settings.AssumeBaselineStacksWhenMissing &&
                (path == "fullStacks" || path == "startBattleFullStacks"))
            {
                env.Set(target, 0.0);
                return true;
            }
            return false;
        }

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteCurrentAbility(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.UnitId is null) return false;
        if (!_db.TryGetUnit(ctx.UnitId, out var unit)) return false;

        JsonElement ability;
        if (path.StartsWith("defaultAttacks", StringComparison.Ordinal))
        {
            if (!JsonPathReader.TryGet(unit, path, out var el))
            {
                if (_settings.AssumeZeroForMissingNumericConfig && LooksNumericConfig(path))
                {
                    env.Set(target, 0.0);
                    return true;
                }
                return false;
            }
            ability = UnwrapV(el);
        }
        else
        {
            var arrName = (ctx.IsActiveAbility ?? true) || path.StartsWith("selfMechanics", StringComparison.Ordinal)
                ? "abilities"
                : "passives";

            if (!unit.TryGetProperty(arrName, out var arr)) return false;

            var idx = ctx.AbilityIndex ?? 0;
            JsonElement? selected = null;
            int i = 0;
            foreach (var item in arr.EnumerateArray())
            {
                if (i == idx) { selected = item; break; }
                i++;
            }

            if (selected is null) return false;

            if (!JsonPathReader.TryGet(selected.Value, path, out var abilityEl))
            {
                if (_settings.AssumeZeroForMissingNumericConfig && LooksNumericConfig(path))
                {
                    double fallback = path.EndsWith(".statDmgMult", StringComparison.Ordinal) ? 1.0 : 0.0;
                    env.Set(target, fallback);
                    return true;
                }
                return false;
            }

            ability = UnwrapV(abilityEl);
        }

        if (ability.ValueKind == JsonValueKind.Number)
            env.Set(target, JsonPathReader.AsDouble(ability) ?? 0);
        else
            env.Set(target, JsonPathReader.AsString(ability) ?? "");
        return true;
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
               path.Contains("upgrade.increment", StringComparison.Ordinal) ||
               path.EndsWith(".increment", StringComparison.Ordinal) ||
               (path.Contains("bonuses[", StringComparison.Ordinal) &&
                (path.Contains(".parameters[", StringComparison.Ordinal) ||
                 path.Contains(".upgrade", StringComparison.Ordinal))) ||
               path.Contains("durationPerStack", StringComparison.Ordinal) ||
               path.EndsWith(".duration", StringComparison.Ordinal) ||
               path.Contains(".values[", StringComparison.Ordinal) ||
               path.EndsWith(".minBaseDmg", StringComparison.Ordinal) ||
               path.EndsWith(".maxBaseDmg", StringComparison.Ordinal) ||
               path.EndsWith(".minStackDmg", StringComparison.Ordinal) ||
               path.EndsWith(".maxStackDmg", StringComparison.Ordinal) ||
               path.EndsWith(".statDmgMult", StringComparison.Ordinal) ||
               path.EndsWith(".damageMultiplerPerHeroLevel", StringComparison.Ordinal);
    }
}
