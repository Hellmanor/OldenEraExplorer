using System.Text;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Control flow operations: Call, Invoke, Return, Text, Concat, Print
/// </summary>
public sealed class ControlFlowOperations : IScriptOperation
{
    private readonly ScriptInterpreter _interpreter;

    public ControlFlowOperations(ScriptInterpreter interpreter)
    {
        _interpreter = interpreter;
    }

    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "Call", "Invoke", "Return", "Text", "Concat", "Print"
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
            "Call" => ExecuteCall(A(0), A(1), context, env),
            "Invoke" => ExecuteInvoke(A(0), A(1), context, env),
            "Return" => ExecuteReturn(A(0), env, ref returnValue),
            "Text" => ExecuteText(args, env, ref returnValue),
            "Concat" => ExecuteConcat(args, env),
            "Print" => ExecutePrint(A(0), env),
            _ => false
        };
    }

    private bool ExecuteCall(string target, string fnTok, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (!env.TryResolveValue(fnTok, out var fnVal)) return false;
        var fnName = fnVal.ToString();
        if (string.IsNullOrWhiteSpace(fnName)) return false;

        if (!_interpreter.TryEvaluate(fnName, ctx, out var outStr)) return false;

        if (!string.IsNullOrEmpty(outStr) && double.TryParse(outStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var outNum))
            env.Set(target, outNum);
        else
            env.Set(target, outStr ?? string.Empty);
        return true;
    }

    private bool ExecuteInvoke(string target, string fnTok, ResolutionContext ctx, ScriptEnvironment env)
    {
        if (!env.TryResolveValue(fnTok, out var fnVal)) return false;
        var fnName = fnVal.ToString();
        if (string.IsNullOrWhiteSpace(fnName)) return false;

        if (!_interpreter.TryEvaluate(fnName, ctx, out var outStr)) return false;

        if (!string.IsNullOrEmpty(outStr) && double.TryParse(outStr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var outNum))
            env.Set(target, outNum);
        else
            env.Set(target, outStr ?? string.Empty);
        return true;
    }

    private bool ExecuteReturn(string valueTok, ScriptEnvironment env, ref string? returnValue)
    {
        if (string.IsNullOrEmpty(valueTok)) return false;
        if (!env.TryResolveValue(valueTok, out var v)) return false;
        returnValue = v.ToString();
        env.Set("return", v.ToString());
        return true;
    }

    private bool ExecuteText(string[] args, ScriptEnvironment env, ref string? returnValue)
    {
        string A(int i) => i < args.Length ? args[i] : "";

        if (args.Length >= 2 && string.Equals(A(0), "return", StringComparison.OrdinalIgnoreCase))
        {
            if (!env.TryResolveValue(A(1), out var v)) return false;
            returnValue = v.ToString();
            env.Set("return", v.ToString());
            return true;
        }

        if (args.Length >= 2)
        {
            if (!env.TryResolveValue(A(1), out var v)) return false;
            env.Set(A(0), v.ToString());
            return true;
        }

        return false;
    }

    private bool ExecuteConcat(string[] args, ScriptEnvironment env)
    {
        if (args.Length < 1) return false;
        string A(int i) => i < args.Length ? args[i] : "";

        var target = A(0);
        var sb = new StringBuilder();
        for (int i = 1; i < args.Length; i++)
        {
            if (!env.TryResolveValue(A(i), out var vv)) return false;
            sb.Append(vv.ToString());
        }
        env.Set(target, sb.ToString());
        return true;
    }

    private bool ExecutePrint(string valueTok, ScriptEnvironment env)
    {
        return true;
    }
}
