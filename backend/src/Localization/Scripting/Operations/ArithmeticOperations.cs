using System.Globalization;
using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>
/// Arithmetic operations: Add, Sub, Mul, Div, Min, Max, Avg, Floor, Round, Ceil, Plus, Multiply
/// </summary>
public sealed class ArithmeticOperations : IScriptOperation
{
    public IReadOnlyList<string> SupportedOperations { get; } = new[]
    {
        "Add", "Sub", "Mul", "Div", "Min", "Max", "Avg", "Floor", "Round", "Ceil", "Plus", "Multiply"
    };

    public bool Execute(
        string operationName,
        string[] args,
        ResolutionContext context,
        ScriptEnvironment env,
        ref string? returnValue)
    {
        string A(int i) => i < args.Length ? args[i] : "";

        switch (operationName)
        {
            case "Add":
            case "Plus":
                return BinaryNumeric(env, A(0), A(1), A(2), (x, y) => x + y);

            case "Sub":
                return BinaryNumeric(env, A(0), A(1), A(2), (x, y) => x - y);

            case "Mul":
            case "Multiply":
                return BinaryNumeric(env, A(0), A(1), A(2), (x, y) => x * y);

            case "Div":
                return DivideOperation(env, A(0), A(1), A(2));

            case "Min":
                return BinaryNumeric(env, A(0), A(1), A(2), Math.Min);

            case "Max":
                return BinaryNumeric(env, A(0), A(1), A(2), Math.Max);

            case "Avg":
                return BinaryNumeric(env, A(0), A(1), A(2), (x, y) => (x + y) / 2.0);

            case "Floor":
                return UnaryNumeric(env, A(0), A(1), Math.Floor);

            case "Round":
                return UnaryNumeric(env, A(0), A(1), Math.Round);

            case "Ceil":
                return UnaryNumeric(env, A(0), A(1), Math.Ceiling);

            default:
                return false;
        }
    }

    private static bool BinaryNumeric(
        ScriptEnvironment env,
        string target,
        string op1,
        string op2,
        Func<double, double, double> operation)
    {
        if (!env.TryResolveValue(op1, out var v1) || !env.TryResolveValue(op2, out var v2))
            return false;

        var n1 = v1.AsDouble();
        var n2 = v2.AsDouble();
        env.Set(target, operation(n1, n2));
        return true;
    }

    private static bool UnaryNumeric(
        ScriptEnvironment env,
        string target,
        string operand,
        Func<double, double> operation)
    {
        if (!env.TryResolveValue(operand, out var v))
            return false;

        env.Set(target, operation(v.AsDouble()));
        return true;
    }

    private static bool DivideOperation(ScriptEnvironment env, string target, string op1, string op2)
    {
        if (!env.TryResolveValue(op1, out var v1) || !env.TryResolveValue(op2, out var v2))
            return false;

        var n1 = v1.AsDouble();
        var n2 = v2.AsDouble();

        if (n2 == 0.0)
        {
            env.Set(target, 0.0);
        }
        else
        {
            env.Set(target, n1 / n2);
        }
        return true;
    }
}
