using Mahjong.Rules.Scoring;

namespace Mahjong.Rules.Rulesets;

/// <summary>
/// FFXIV Doman: uses standard riichi yaku/scoring with a one-yaku minimum.
/// </summary>
public sealed class DomanRuleSet : IRuleSet
{
    private readonly RiichiRuleSet riichi = new();
    private readonly IDoraRule doraRule;

    public DomanRuleSet(IDoraRule? doraRule = null)
    {
        this.doraRule = doraRule ?? new StandardDoraRule();
    }

    public string Name => "Doman";

    public IReadOnlyList<IYakuRule> YakuRules => riichi.YakuRules;
    public IScoringRule ScoringRule => riichi.ScoringRule;
    public IDoraRule DoraRule => doraRule;
    public IFuRule FuRule => riichi.FuRule;

    public bool AllowsRedDora => true;
    public bool AllowsKuitan => true;
    public int MinHan => 1;
    public int KazoeThreshold => ScoringConstants.KazoeYakumanHan;
    public int MaxYakuman => 2;
}
