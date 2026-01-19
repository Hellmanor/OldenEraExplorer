namespace Localization.Scripting;

/// <summary>
/// Fallback behavior when game data is missing or runtime-only.
/// Allows graceful degradation instead of resolution failure.
/// </summary>
public sealed class ScriptSettings
{
    public bool AssumeBaselineStacksWhenMissing { get; set; } = false;
    public bool AssumeZeroForMissingNumericConfig { get; set; } = false;
    public bool AssumeHeroLevelWhenMissing { get; set; } = true;
    public double HeroLevelBaseline { get; set; } = 1.0;
}
