using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Pattern-based dynamic function handlers for function names like:
/// - current_unit_inDmgMods_N_param
/// - current_unit_passives_X
/// - current_unit_ability_X
/// - current_buff_X_param
/// </summary>
public sealed class PatternBasedOperations : IScriptOperation
{
    private readonly DbAccessor _db;
    private readonly ScriptSettings _settings;

    public PatternBasedOperations(DbAccessor db, ScriptSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    // This handler doesn't have static names - it handles pattern-matched operations
    public IReadOnlyList<string> SupportedOperations { get; } = Array.Empty<string>();

    public bool TryHandlePattern(
        string funcName,
        string[] args,
        ResolutionContext context,
        ScriptEnvironment env)
    {
        string A(int i) => i < args.Length ? args[i] : "";
        var target = A(0);

        if (funcName.StartsWith("current_unit_inDmgMods_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = funcName.Substring("current_unit_inDmgMods_".Length);
            var parts = suffix.Split('_');
            if (parts.Length == 2 && int.TryParse(parts[0], out var index) &&
                parts[1].Equals("param", StringComparison.OrdinalIgnoreCase))
            {
                if (context.UnitId is null || !_db.TryGetUnit(context.UnitId, out var unit)) return false;

                var path = $"passives.inDmgMods[{index}].param";
                if (!JsonPathReader.TryGet(unit, path, out var el))
                {
                    if (_settings.AssumeZeroForMissingNumericConfig)
                    {
                        env.Set(target, 0.0);
                        return true;
                    }
                    return false;
                }
                SetFromJsonElement(env, target, el);
                return true;
            }
        }

        if (funcName.StartsWith("current_unit_passives_", StringComparison.OrdinalIgnoreCase))
        {
            var fieldName = funcName.Substring("current_unit_passives_".Length);
            if (context.UnitId is null || !_db.TryGetUnit(context.UnitId, out var unit)) return false;

            var path = $"passives.{fieldName}";
            if (!JsonPathReader.TryGet(unit, path, out var el))
            {
                if (_settings.AssumeZeroForMissingNumericConfig)
                {
                    env.Set(target, 0.0);
                    return true;
                }
                return false;
            }
            SetFromJsonElement(env, target, el);
            return true;
        }

        if (funcName.StartsWith("current_unit_ability_", StringComparison.OrdinalIgnoreCase) ||
            funcName.StartsWith("current_unit_passive_", StringComparison.OrdinalIgnoreCase))
        {
            bool isAbility = funcName.StartsWith("current_unit_ability_", StringComparison.OrdinalIgnoreCase);
            var fieldName = isAbility
                ? funcName.Substring("current_unit_ability_".Length)
                : funcName.Substring("current_unit_passive_".Length);

            if (context.UnitId is null || !_db.TryGetUnit(context.UnitId, out var unit)) return false;
            var idx = context.AbilityIndex ?? 0;

            var arrName = isAbility ? "abilities" : "passives";
            if (!unit.TryGetProperty(arrName, out var arr) || arr.ValueKind != JsonValueKind.Array) return false;

            var ability = GetArrayElement(arr, idx);
            if (ability is null) return false;

            if (TryHandleAbilityField(fieldName, ability.Value, env, target))
                return true;

            if (_settings.AssumeZeroForMissingNumericConfig)
            {
                env.Set(target, 0.0);
                return true;
            }
            return false;
        }

        if (funcName.StartsWith("current_buff_", StringComparison.OrdinalIgnoreCase))
        {
            if (context.BuffId is null || !_db.TryGetBuff(context.BuffId, out var buff)) return false;

            var suffix = funcName.Substring("current_buff_".Length);

            if (suffix.EndsWith("_param", StringComparison.OrdinalIgnoreCase))
            {
                var statName = suffix.Substring(0, suffix.Length - "_param".Length);
                var path = $"data.stats.{statName}";
                if (JsonPathReader.TryGet(buff, path, out var el))
                {
                    SetFromJsonElement(env, target, el);
                    return true;
                }

                if (_settings.AssumeZeroForMissingNumericConfig)
                {
                    env.Set(target, 0.0);
                    return true;
                }
                return false;
            }

            if (suffix.Equals("dot_dmg", StringComparison.OrdinalIgnoreCase))
            {
                double dmg = 0.0;
                if (JsonPathReader.TryGet(buff, "actions[0].damageDealer.minStackDmg", out var stackEl))
                {
                    stackEl = UnwrapV(stackEl);
                    dmg = JsonPathReader.AsDouble(stackEl) ?? 0.0;
                }
                else if (JsonPathReader.TryGet(buff, "actions[0].damageDealer.minBaseDmg", out var baseEl))
                {
                    baseEl = UnwrapV(baseEl);
                    dmg = JsonPathReader.AsDouble(baseEl) ?? 0.0;
                }

                if (context.BuffStacks.HasValue)
                    dmg *= context.BuffStacks.Value;

                env.Set(target, dmg);
                return true;
            }

            return false;
        }

        return false;
    }

    private bool TryHandleAbilityField(string fieldName, JsonElement ability, ScriptEnvironment env, string target)
    {
        if (fieldName.Equals("baseBuffDuration", StringComparison.OrdinalIgnoreCase))
        {
            if (JsonPathReader.TryGet(ability, "selfMechanics[0].values[0]", out var el))
            {
                SetFromJsonElement(env, target, el);
                return true;
            }
        }
        else if (fieldName.Equals("buff_duration", StringComparison.OrdinalIgnoreCase))
        {
            if (JsonPathReader.TryGet(ability, "damageDealer.buff.duration", out var el))
            {
                SetFromJsonElement(env, target, el);
                return true;
            }
        }
        else if (fieldName.StartsWith("perStackBuffDuration_", StringComparison.OrdinalIgnoreCase))
        {
            var subField = fieldName.Substring("perStackBuffDuration_".Length);
            if (subField.Equals("unitsBonusDuration", StringComparison.OrdinalIgnoreCase))
            {
                if (JsonPathReader.TryGet(ability, "selfMechanics[0].values[1]", out var el))
                {
                    SetFromJsonElement(env, target, el);
                    return true;
                }
            }
            else if (subField.Equals("unitsCount", StringComparison.OrdinalIgnoreCase))
            {
                if (JsonPathReader.TryGet(ability, "selfMechanics[0].values[2]", out var el))
                {
                    SetFromJsonElement(env, target, el);
                    return true;
                }
            }
        }
        else if (fieldName.StartsWith("trap_", StringComparison.OrdinalIgnoreCase))
        {
            var trapField = fieldName.Substring("trap_".Length);
            if (trapField.Equals("baseBuffDuration", StringComparison.OrdinalIgnoreCase))
            {
                if (JsonPathReader.TryGet(ability, "damageDealer.targetMechanics[0].values[0]", out var el))
                {
                    SetFromJsonElement(env, target, el);
                    return true;
                }
            }
        }

        return false;
    }

    public bool Execute(
        string operationName,
        string[] args,
        ResolutionContext context,
        ScriptEnvironment environment,
        ref string? returnValue)
    {
        // This handler uses TryHandlePattern instead
        return false;
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
