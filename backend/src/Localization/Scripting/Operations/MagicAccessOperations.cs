using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Magic/Spell operations: CurrentMagicBattle, CurrentMagicBattleRoot, CurrentMagicWorld, CurrentMagicLevel, SpellpowerForCurrentMagic
/// </summary>
public sealed class MagicAccessOperations : IScriptOperation
{
    private readonly DbAccessor _db;

    public MagicAccessOperations(DbAccessor db)
    {
        _db = db;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "CurrentMagicBattle", "CurrentMagicBattleRoot", "CurrentMagicWorld", "CurrentMagicLevel", "SpellpowerForCurrentMagic"
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
            "CurrentMagicBattle" => ExecuteCurrentMagicBattle(A(0), A(1), context, env),
            "CurrentMagicBattleRoot" => ExecuteCurrentMagicBattleRoot(A(0), A(1), context, env),
            "CurrentMagicWorld" => ExecuteCurrentMagicWorld(A(0), A(1), context, env),
            "CurrentMagicLevel" => ExecuteCurrentMagicLevel(A(0), context, env),
            "SpellpowerForCurrentMagic" => ExecuteSpellpowerForCurrentMagic(A(0), context, env),
            _ => false
        };
    }

    private bool ExecuteCurrentMagicBattle(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.MagicId is null) return false;
        if (!_db.TryGetMagic(ctx.MagicId, out var magic)) return false;

        if (ctx.MagicLevel.HasValue &&
            magic.TryGetProperty("levels", out var levelsArray) &&
            levelsArray.ValueKind == JsonValueKind.Array)
        {
            int levelIndex = ctx.MagicLevel.Value - 1;
            if (levelIndex >= 0 && levelIndex < levelsArray.GetArrayLength())
            {
                var levelElement = GetArrayElement(levelsArray, levelIndex);
                if (levelElement.HasValue && JsonPathReader.TryGet(levelElement.Value, path, out var el))
                {
                    SetFromJsonElement(env, target, el);
                    return true;
                }
            }
        }

        JsonElement searchRoot = magic;
        bool hasBattleMagic = magic.TryGetProperty("battleMagic", out var battleMagic);
        if (hasBattleMagic)
        {
            if (battleMagic.TryGetProperty("magicDealers", out var magicDealers) &&
                magicDealers.ValueKind == JsonValueKind.Array &&
                magicDealers.GetArrayLength() > 0)
            {
                int dealerIndex = (ctx.MagicLevel ?? 1) - 1;
                if (dealerIndex >= 0 && dealerIndex < magicDealers.GetArrayLength())
                    searchRoot = magicDealers[dealerIndex];
                else
                    searchRoot = magicDealers[magicDealers.GetArrayLength() - 1];
            }
            else
            {
                searchRoot = battleMagic;
            }
        }

        if (!JsonPathReader.TryGet(searchRoot, path, out var element))
        {
            if (hasBattleMagic && JsonPathReader.TryGet(magic, path, out element))
            {
            }
            else
            {
                if (path.Contains("durationPerStack") || path.Contains("PerStack"))
                {
                    env.Set(target, 0.0);
                    return true;
                }
                return false;
            }
        }

        SetFromJsonElement(env, target, element);
        return true;
    }

    private bool ExecuteCurrentMagicBattleRoot(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.MagicId is null) return false;
        if (!_db.TryGetMagic(ctx.MagicId, out var magic)) return false;

        if (!JsonPathReader.TryGet(magic, path, out var element))
            return false;

        SetFromJsonElement(env, target, element);
        return true;
    }

    private bool ExecuteCurrentMagicWorld(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.MagicId is null) return false;
        if (!_db.TryGetMagic(ctx.MagicId, out var magic)) return false;

        if (!magic.TryGetProperty("worldMagic", out var worldMagic))
            return false;

        int level = (ctx.MagicLevel ?? 1) - 1;
        if (level < 0) level = 0;

        if (worldMagic.TryGetProperty("magicSettings", out var magicSettings) &&
            magicSettings.ValueKind == JsonValueKind.Array)
        {
            int settingsIndex = level;
            if (worldMagic.TryGetProperty("settingPerLevels", out var settingPerLevels) &&
                settingPerLevels.ValueKind == JsonValueKind.Array &&
                settingPerLevels.GetArrayLength() > level)
            {
                settingsIndex = settingPerLevels[level].GetInt32();
            }

            if (magicSettings.GetArrayLength() > settingsIndex)
            {
                var levelSettings = magicSettings[settingsIndex];
                if (JsonPathReader.TryGet(levelSettings, path, out var element))
                {
                    SetFromJsonElement(env, target, element);
                    return true;
                }
            }
        }

        if (JsonPathReader.TryGet(worldMagic, path, out var rootElement))
        {
            SetFromJsonElement(env, target, rootElement);
            return true;
        }

        return false;
    }

    private bool ExecuteCurrentMagicLevel(string target, ResolutionContext ctx, ScriptEnvironment env)
    {
        env.Set(target, ctx.MagicLevel ?? 1);
        return true;
    }

    private bool ExecuteSpellpowerForCurrentMagic(string target, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.BuffSpellPower is null) return false;
        env.Set(target, ctx.BuffSpellPower.Value);
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
