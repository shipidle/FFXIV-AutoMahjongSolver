using Mahjong.Core;
using Mahjong.Plugin.Dalamud.Adapters;
using Mahjong.Plugin.Dalamud.Composition;

namespace Mahjong.Plugin.Dalamud.Tests;

public class ConfiguredDomanDoraRuleTests
{
    [Fact]
    public void Doman_display_treats_shown_tile_as_dora()
    {
        var config = new DalamudConfigService(
            _ => { },
            new Configuration { DoraIndicatorDisplayMode = DoraIndicatorDisplayMode.Doman });
        var rule = new ConfiguredDomanDoraRule(config);
        var shown5m = Tile.FromId(4);

        Assert.Equal(shown5m.Id, rule.Next(shown5m).Id);
    }

    [Fact]
    public void Traditional_display_uses_next_tile_as_dora()
    {
        var config = new DalamudConfigService(
            _ => { },
            new Configuration { DoraIndicatorDisplayMode = DoraIndicatorDisplayMode.Traditional });
        var rule = new ConfiguredDomanDoraRule(config);
        var indicator4m = Tile.FromId(3);

        Assert.Equal(4, rule.Next(indicator4m).Id); // 4m indicator -> 5m dora
    }

    [Fact]
    public void Rule_reads_display_setting_live()
    {
        var config = new DalamudConfigService(
            _ => { },
            new Configuration { DoraIndicatorDisplayMode = DoraIndicatorDisplayMode.Doman });
        var rule = new ConfiguredDomanDoraRule(config);
        var shown4m = Tile.FromId(3);

        Assert.Equal(3, rule.Next(shown4m).Id);

        config.Update(c => c with { DoraIndicatorDisplayMode = DoraIndicatorDisplayMode.Traditional });

        Assert.Equal(4, rule.Next(shown4m).Id);
    }
}
