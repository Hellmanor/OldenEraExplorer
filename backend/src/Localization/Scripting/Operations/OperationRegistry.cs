namespace Localization.Scripting.Operations;

/// <summary>
/// Registry of all available script operations.
/// Maps operation names to their handlers.
/// </summary>
public sealed class OperationRegistry
{
    private readonly Dictionary<string, IScriptOperation> _operations = new(StringComparer.OrdinalIgnoreCase);

    public OperationRegistry(IEnumerable<IScriptOperation> operations)
    {
        foreach (var op in operations)
        {
            foreach (var name in op.SupportedOperations)
            {
                _operations[name] = op;
            }
        }
    }

    public bool TryGetOperation(string operationName, out IScriptOperation operation)
        => _operations.TryGetValue(operationName, out operation!);
}
