using Mahjong.Rules;
using Mahjong.Rules.Scoring;

namespace Mahjong.Plugin.Dalamud.Adapters;

/// <summary>
/// Interprets the tile read from FFXIV's on-screen dora display.
/// Doman display shows the bonus tile itself; Traditional shows the preceding indicator.
/// </summary>
public sealed class ConfiguredDomanDoraRule : IDoraRule
{
    private readonly IConfigService<Configuration> config;
    private readonly StandardDoraRule traditional = new();

    public ConfiguredDomanDoraRule(IConfigService<Configuration> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;
    }

    public Tile Next(Tile indicator)
        => config.Current.DoraIndicatorDisplayMode == DoraIndicatorDisplayMode.Doman
            ? indicator
            : traditional.Next(indicator);
}
