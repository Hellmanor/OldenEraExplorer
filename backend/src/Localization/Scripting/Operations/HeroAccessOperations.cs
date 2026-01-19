using System.Text.Json;
using Localization.DbAccess;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Hero operations: CurrentHero, CurrentHeroSpecializationConfig, CurrentHeroAbility
/// </summary>
public sealed class HeroAccessOperations : IScriptOperation
{
    private readonly DbAccessor _db;
    private readonly ScriptSettings _settings;

    public HeroAccessOperations(DbAccessor db, ScriptSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "CurrentHero", "CurrentHeroSpecializationConfig", "CurrentHeroAbility"
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
            "CurrentHero" => ExecuteCurrentHero(A(0), A(1), env),
            "CurrentHeroSpecializationConfig" => ExecuteCurrentHeroSpecializationConfig(A(0), A(1), context, env),
            "CurrentHeroAbility" => ExecuteCurrentHeroAbility(A(0), A(1), context, env),
            _ => false
        };
    }

    private bool ExecuteCurrentHero(string target, string path, ScriptEnvironment env)
    {
        if (string.Equals(path, "level", StringComparison.OrdinalIgnoreCase))
        {
            if (_settings.AssumeHeroLevelWhenMissing)
            {
                env.Set(target, _settings.HeroLevelBaseline);
                return true;
            }
            return false;
        }

        if (string.Equals(path, "heroStat.viewRadius", StringComparison.OrdinalIgnoreCase))
        {
            env.Set(target, 1.0);
            return true;
        }

        return false;
    }

    private bool ExecuteCurrentHeroSpecializationConfig(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.HeroSpecializationId is null) return false;
        if (!_db.TryGetHeroSpecialization(ctx.HeroSpecializationId, out var spec)) return false;
        if (!JsonPathReader.TryGet(spec, path, out var el)) return false;

        SetFromJsonElement(env, target, el);
        return true;
    }

    private bool ExecuteCurrentHeroAbility(string target, string path, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (ctx.HeroAbilityId is null) return false;
        if (!_db.TryGetHeroAbility(ctx.HeroAbilityId, out var heroAbility)) return false;

        if (!heroAbility.TryGetProperty("levels", out var levelsArray) ||
            levelsArray.ValueKind != JsonValueKind.Array ||
            levelsArray.GetArrayLength() == 0)
            return false;

        var firstLevel = levelsArray[0];

        var actualPath = path;
        if (path.Contains(".data.", StringComparison.Ordinal))
            actualPath = path.Replace(".data.", ".");

        if (!JsonPathReader.TryGet(firstLevel, actualPath, out var el))
        {
            if (_settings.AssumeZeroForMissingNumericConfig && LooksNumericConfig(actualPath))
            {
                env.Set(target, 0.0);
                return true;
            }
            return false;
        }

        SetFromJsonElement(env, target, el);
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
        return path.EndsWith(".minBaseDmg", StringComparison.Ordinal) ||
               path.EndsWith(".maxBaseDmg", StringComparison.Ordinal) ||
               path.Contains(".values[", StringComparison.Ordinal);
    }
}
