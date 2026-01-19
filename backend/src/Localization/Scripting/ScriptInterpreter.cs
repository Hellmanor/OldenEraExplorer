using System.Globalization;
using Localization.DbAccess;
using Localization.Resolution;
using Localization.Scripting.Operations;

namespace Localization.Scripting;

public sealed class ScriptInterpreter
{
    private readonly ScriptRegistry _registry;
    private readonly OperationRegistry _operations;
    private readonly ScriptSettings _settings;
    private readonly PatternBasedOperations _patternOps;

    public ScriptInterpreter(
        ScriptRegistry registry,
        DbAccessor db,
        ScriptSettings? settings = null)
    {
        _registry = registry;
        _settings = settings ?? new ScriptSettings();

        var ops = new List<IScriptOperation>
        {
            new ArithmeticOperations(),
            new UnitAccessOperations(db, _settings),
            new BuffAccessOperations(db, _settings),
            new ItemAccessOperations(db, _settings),
            new MagicAccessOperations(db),
            new SkillAccessOperations(db),
            new HeroAccessOperations(db, _settings),
            new DbOperations(db),
            new MiscAccessOperations(db),
            new ControlFlowOperations(this)
        };

        _operations = new OperationRegistry(ops);
        _patternOps = new PatternBasedOperations(db, _settings);
    }

    public ScriptSettings Settings => _settings;

    public bool TryEvaluate(string funcName, ResolutionContext ctx, out string? value)
    {
        value = null;

        if (!_registry.TryGet(funcName, out var fn))
            return false;

        var env = new ScriptEnvironment();
        string? returnValue = null;

        foreach (var statement in fn.Body)
        {
            if (!ExecuteStatement(statement, ctx, env, ref returnValue))
                return false;
        }

        var hasReturn = env.TryGet("return", out var retVal);
        var raw = returnValue ?? (hasReturn ? retVal.ToString() : null);

        if (raw == null)
            return false;

        if (!string.IsNullOrWhiteSpace(fn.DeclaredType) &&
            double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal))
        {
            raw = FormatByType(fn.DeclaredType, numVal);
        }

        value = raw;
        return true;
    }

    private bool ExecuteStatement(
        ScriptRegistry.Statement statement,
        ResolutionContext ctx,
        ScriptEnvironment env,
        ref string? returnValue)
    {
        if (_operations.TryGetOperation(statement.Op, out var operation))
        {
            return operation.Execute(statement.Op, statement.Args, ctx, env, ref returnValue);
        }

        if (_patternOps.TryHandlePattern(statement.Op, statement.Args, ctx, env))
        {
            return true;
        }

        return false;
    }

    private static string FormatByType(string declaredType, double num)
    {
        return declaredType switch
        {
            "modPercentNumeric" => Math.Round(Math.Abs(num) * 100).ToString("0", CultureInfo.InvariantCulture),
            "modFloatPercentF1Numeric" => (Math.Abs(num) * 100).ToString("0.#", CultureInfo.InvariantCulture),
            "modInt" => Math.Round(Math.Abs(num)).ToString("0", CultureInfo.InvariantCulture),
            "int" => Math.Round(num).ToString("0", CultureInfo.InvariantCulture),
            "float" => num.ToString(CultureInfo.InvariantCulture),
            _ => num.ToString(CultureInfo.InvariantCulture)
        };
    }
}
