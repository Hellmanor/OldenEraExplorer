using AssetExtractor.Models;

namespace AssetExtractor.CLI;

/// <summary>
/// Configuration parsed from command-line arguments.
/// Immutable record holding all CLI settings for the extraction process.
/// </summary>
record CliConfig(
    /// <summary>
    /// The primary command to execute (e.g., "extract-glb", "list-units").
    /// </summary>
    string Command,

    /// <summary>
    /// Manually specified game installation path (via --game-path flag).
    /// Null if not provided - will be auto-detected.
    /// </summary>
    string? GamePath,

    /// <summary>
    /// Output directory for extracted assets (via --output-path flag).
    /// Null means use default (./output).
    /// </summary>
    string? OutputPath,

    /// <summary>
    /// Enable verbose (DEBUG level) logging (via --verbose flag).
    /// </summary>
    bool IsVerbose,

    /// <summary>
    /// Force re-extraction even if version is cached (via --force or -f flag).
    /// </summary>
    bool Force,

    /// <summary>
    /// Output progress as JSON lines instead of visual progress bar (via --json-progress flag).
    /// Useful for automation and subprocess integration.
    /// </summary>
    bool JsonProgress,

    /// <summary>
    /// Remaining command-specific arguments after flag parsing.
    /// Example: For "extract-glb esquire", this would be ["extract-glb", "esquire"].
    /// </summary>
    List<string> Arguments
)
{
    /// <summary>
    /// Parse command-line arguments into a CliConfig.
    /// </summary>
    /// <param name="args">Raw command-line arguments from Main(string[] args)</param>
    /// <returns>Parsed configuration, or null if help was requested</returns>
    public static CliConfig? Parse(string[] args)
    {
        // Handle help request
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            return null; // Signal caller to print help and exit
        }

        string? gamePath = null;
        string? outputPath = null;
        bool isVerbose = false;
        bool force = false;
        bool jsonProgress = false;
        var filteredArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game-path" when i + 1 < args.Length:
                    gamePath = args[i + 1];
                    i++; // Skip the path value
                    break;

                case "--output-path" when i + 1 < args.Length:
                    outputPath = args[i + 1];
                    i++; // Skip the path value
                    break;

                case "--force":
                case "-f":
                    force = true;
                    break;

                case "--json-progress":
                    jsonProgress = true;
                    break;

                case "--verbose":
                    isVerbose = true;
                    break;

                case "--versioned":
                case "-v":
                    // Deprecated flag - show warning in Main
                    // Don't add to filtered args
                    break;

                default:
                    filteredArgs.Add(args[i]);
                    break;
            }
        }

        string command = args[0].ToLower();

        return new CliConfig(
            Command: command,
            GamePath: gamePath,
            OutputPath: outputPath,
            IsVerbose: isVerbose,
            Force: force,
            JsonProgress: jsonProgress,
            Arguments: filteredArgs
        );
    }

    /// <summary>
    /// Check if the deprecated --versioned flag was used.
    /// </summary>
    public static bool HasDeprecatedVersionedFlag(string[] args)
    {
        return args.Any(arg => arg == "--versioned" || arg == "-v");
    }
}
