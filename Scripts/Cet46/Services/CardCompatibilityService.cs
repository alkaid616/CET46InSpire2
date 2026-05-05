using CET46InSpire2.Scripts.Cet46.Models;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CET46InSpire2.Scripts.Cet46.Services;

/// <summary>
/// 为特定卡牌保留的兼容层上下文。
/// </summary>
public readonly record struct CardCompatibilityContext(QuizRelicModel Relic, CardModel Card, int Score);

/// <summary>
/// 迁移 STS1 原模组里对少数特殊牌的单独修正规则。
/// </summary>
public static class CardCompatibilityService
{
    /// <summary>
    /// 仅为需要特殊处理的牌提取当前答题上下文。
    /// </summary>
    public static bool TryGetContext(CardModel? cardSource, out CardCompatibilityContext context)
    {
        context = default;
        if (cardSource is not Prepared and not TheBomb)
        {
            return false;
        }

        var owner = cardSource.Owner;
        if (owner == null)
        {
            return false;
        }

        foreach (var relic in owner.Relics.OfType<QuizRelicModel>())
        {
            if (!relic.TryGetCompatibilityScore(cardSource, out var score))
            {
                continue;
            }

            context = new CardCompatibilityContext(relic, cardSource, score);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The Bomb 的 stack 数表示倒计时，不应该被答题分数放大。
    /// </summary>
    public static bool ShouldBypassPowerAmountScaling(PowerModel power, CardModel? cardSource)
    {
        return power is TheBombPower && TryGetContext(cardSource, out var context) && context.Card is TheBomb;
    }

    /// <summary>
    /// Prepared 的抽弃牌数量按当前分数重算，并在 0 分时保留原始数值。
    /// </summary>
    public static async Task ExecutePreparedAsync(Prepared prepared, PlayerChoiceContext choiceContext, int score)
    {
        var owner = prepared.Owner;
        if (owner == null)
        {
            return;
        }

        var multiplier = Math.Max(1, score);
        var cardCount = Math.Max(0, prepared.DynamicVars.Cards.IntValue * multiplier);
        if (cardCount <= 0)
        {
            return;
        }

        await CardPileCmd.Draw(choiceContext, cardCount, owner);
        var selected = await CardSelectCmd.FromHandForDiscard(
            choiceContext,
            owner,
            new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, cardCount),
            null,
            prepared);
        await CardCmd.Discard(choiceContext, selected ?? []);
    }

    /// <summary>
    /// The Bomb 通过重复挂 instanced power 来复刻“多倍炸弹”的老逻辑。
    /// 答题失败时保留原始伤害。
    /// </summary>
    public static async Task ExecuteTheBombAsync(TheBomb theBomb, PlayerChoiceContext choiceContext, int score)
    {
        var owner = theBomb.Owner?.Creature;
        if (owner == null)
        {
            return;
        }

        var turns = theBomb.DynamicVars["Turns"].BaseValue;
        var damage = theBomb.DynamicVars["BombDamage"].BaseValue;
        var copies = Math.Max(1, score);
        for (var i = 0; i < copies; i++)
        {
            var power = await PowerCmd.Apply<TheBombPower>(choiceContext, owner, turns, owner, theBomb, false);
            power?.SetDamage(damage);
        }
    }
}
