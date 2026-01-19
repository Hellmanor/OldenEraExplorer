using Localization.Resolution;

namespace Localization.Scripting.Operations;

/// <summary>Handles one or more related script commands (e.g., "Add", "Sub", "Mul").</summary>
public interface IScriptOperation
{
    IReadOnlyList<string> SupportedOperations { get; }

    bool Execute(
        string operationName,
        string[] args,
        ResolutionContext context,
        ScriptEnvironment environment,
        ref string? returnValue);
}
