using System;
using System.Diagnostics;

namespace GameData.Shared.Utils;

internal static class DiagnosticsLog
{
    private static bool Enabled = false;

    public static void Trace(string message)
    {
        if (Enabled)
        {
            var output = $"[DIAG] {message}";
            Debug.WriteLine(output);
            Console.WriteLine(output);
        }
    }

    public static void Trace(string message, Exception exception)
    {
        if (Enabled)
        {
            var output = $"[DIAG] {message}: {exception}";
            Debug.WriteLine(output);
            Console.WriteLine(output);
        }
    }
}
