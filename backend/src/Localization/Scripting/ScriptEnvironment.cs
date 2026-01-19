using System.Globalization;

namespace Localization.Scripting;

public sealed class ScriptEnvironment
{
    private readonly Dictionary<string, ScriptValue> _variables = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, double value) => _variables[name] = new ScriptValue(value);
    public void Set(string name, string value) => _variables[name] = new ScriptValue(value);

    public bool TryGet(string name, out ScriptValue value) => _variables.TryGetValue(name, out value);

    public bool TryGetNumeric(string name, out double value)
    {
        if (_variables.TryGetValue(name, out var sv) && sv.IsNumeric)
        {
            value = sv.NumericValue!.Value;
            return true;
        }
        value = 0;
        return false;
    }

    public bool TryResolveValue(string token, out ScriptValue value)
    {
        if (_variables.TryGetValue(token, out value))
            return true;

        var unquoted = token;
        if (unquoted.Length >= 2 && unquoted[0] == '"' && unquoted[^1] == '"')
            unquoted = unquoted[1..^1];

        if (double.TryParse(unquoted, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
        {
            value = new ScriptValue(num);
            return true;
        }

        value = new ScriptValue(unquoted);
        return true;
    }
}

public readonly struct ScriptValue
{
    public double? NumericValue { get; }
    public string? StringValue { get; }
    public bool IsNumeric => NumericValue.HasValue;

    public ScriptValue(double value) { NumericValue = value; StringValue = null; }
    public ScriptValue(string value) { StringValue = value; NumericValue = null; }

    public override string ToString() =>
        IsNumeric && NumericValue.HasValue
            ? NumericValue.Value.ToString(CultureInfo.InvariantCulture)
            : StringValue ?? "";

    public double AsDouble() => NumericValue ?? (double.TryParse(StringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0);
}
