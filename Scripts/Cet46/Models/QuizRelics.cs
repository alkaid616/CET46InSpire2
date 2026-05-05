using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Services;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace CET46InSpire2.Scripts.Cet46.Models;

/// <summary>
/// CET/JLPT 系列遗物的共享运行时逻辑。
/// </summary>
public abstract class QuizRelicModel : RelicModel
{
    private CorrectionNotebookState? _cachedNotebook;
    private ReviewSchedulerState? _cachedScheduler;
    private CardModel? _compatibilityCard;

    public abstract QuizRelicKind Kind { get; }

    public override RelicRarity Rarity => RelicRarity.Event;

    public override bool ShowCounter => true;

    public override int DisplayAmount => CurrentScore;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new StringVar("BookFocus"),
        new StringVar("StatusSuffix"),
    ];

    protected override string PackedIconOutlinePath => ModPaths.RelicAsset("book_of_cet_outline.png");

    protected override string BigIconPath => CurrentIconPath;

    public override string PackedIconPath => CurrentIconPath;

    public bool IsFunctional => GetActiveStudyBooks().Count > 0;

    [SavedProperty(SerializationCondition.SaveIfNotPropertyDefault)]
    public int SelectedBookValue
    {
        get => _selectedBookValue;
        private set
        {
            AssertMutable();
            _selectedBookValue = value;
        }
    }

    [SavedProperty]
    public int CurrentScore
    {
        get => _currentScore;
        private set
        {
            AssertMutable();
            _currentScore = Math.Max(0, value);
            InvokeDisplayAmountChanged();
            RefreshFromConfig();
        }
    }

    [SavedProperty]
    public int PerfectStreak
    {
        get => _perfectStreak;
        private set
        {
            AssertMutable();
            _perfectStreak = Math.Max(0, value);
            RefreshPerfectPower();
        }
    }

    [SavedProperty]
    public bool RewardGrantedThisCombat
    {
        get => _rewardGrantedThisCombat;
        private set
        {
            AssertMutable();
            _rewardGrantedThisCombat = value;
        }
    }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    private int NotebookTotalCount { get; set; }

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] NotebookItemBooks { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] NotebookItemIndices { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] NotebookItemCounts { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] NotebookRemovedBooks { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] NotebookRemovedIndices { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotPropertyDefault)]
    public string NotebookJson
    {
        get => _notebookJson;
        private set
        {
            AssertMutable();
            _notebookJson = value;
            _cachedNotebook = null;
        }
    }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    private int SchedulerSessionCount { get; set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    private int SchedulerReviewCount { get; set; }

    [SavedProperty(SerializationCondition.SaveIfNotPropertyDefault)]
    private int SchedulerLastEntryBook { get; set; } = -1;

    [SavedProperty(SerializationCondition.SaveIfNotPropertyDefault)]
    private int SchedulerLastEntryIndex { get; set; } = -1;

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerEntryBooks { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerEntryIndices { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerReviews { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerLapses { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerConsecutiveCorrect { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerDueDays { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerLastReviewedDays { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerDifficultyMilli { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotCollectionEmptyOrNull)]
    private int[] SchedulerStabilityMilli { get; set; } = [];

    [SavedProperty(SerializationCondition.SaveIfNotPropertyDefault)]
    public string SchedulerJson
    {
        get => _schedulerJson;
        private set
        {
            AssertMutable();
            _schedulerJson = value;
            _cachedScheduler = null;
        }
    }

    protected string CurrentIconPath => !IsFunctional ? ModPaths.RelicAsset("book_of_missing.png") : ResolvePrimaryIconPath();

    protected abstract string ResolvePrimaryIconPath();

    protected abstract string ResolveFocusLabel();

    /// <summary>
    /// 配置变化后刷新图标、描述和启用状态。
    /// </summary>
    public void RefreshFromConfig()
    {
        RelicIconChanged();
        UpdateDescriptionVars();
        Status = !IsFunctional
            ? RelicStatus.Disabled
            : CurrentScore > 0
                ? RelicStatus.Active
                : RelicStatus.Normal;
    }

    public override Task BeforeCombatStart()
    {
        RewardGrantedThisCombat = false;
        EnsurePerfectPowerExists();
        RefreshPerfectPower();
        RefreshFromConfig();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 回合结束时清空持续分数，保持“本回合内持续倍率”的设计。
    /// </summary>
    public override Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == CombatSide.Player && LocalContext.IsMe(Owner))
        {
            CurrentScore = 0;
            _compatibilityCard = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 战斗结束时重置战斗内状态。
    /// </summary>
    public override Task AfterCombatEnd(CombatRoom room)
    {
        CurrentScore = 0;
        RewardGrantedThisCombat = false;
        _compatibilityCard = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 兼容牌的上下文只对当前那一张牌保留到出牌结束。
    /// </summary>
    public override Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (_compatibilityCard == cardPlay.Card)
        {
            _compatibilityCard = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 当前分数按倍率修正当前触发答题的卡牌伤害。
    /// </summary>
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (!ShouldScaleDamage(props, dealer) || !IsCompatibilityCard(cardSource))
        {
            return 1m;
        }

        // 答题失败时保留原始伤害 (1.0 倍率)。
        return CurrentScore > 0 ? CurrentScore : 1m;
    }

    /// <summary>
    /// 当前分数按倍率修正当前触发答题的卡牌格挡。
    /// </summary>
    public override decimal ModifyBlockMultiplicative(Creature? target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (!ShouldScaleBlock(props, target) || !IsCompatibilityCard(cardSource))
        {
            return 1m;
        }

        // 答题失败时保留原始格挡 (1.0 倍率)。
        return CurrentScore > 0 ? CurrentScore : 1m;
    }

    public override decimal ModifyPowerAmountGiven(PowerModel power, Creature giver, decimal amount, Creature? target, CardModel? cardSource)
    {
        if (!IsCompatibilityCard(cardSource))
        {
            return amount;
        }

        if (CardCompatibilityService.ShouldBypassPowerAmountScaling(power, cardSource))
        {
            return amount;
        }

        // 答题失败时不缩放 Power 数量。
        return CurrentScore > 0 ? amount * CurrentScore : amount;
    }

    public override int ModifyXValue(CardModel card, int originalValue)
    {
        if (!IsCompatibilityCard(card))
        {
            return originalValue;
        }

        // 答题失败时不缩放 X 值。
        return CurrentScore > 0 ? originalValue * CurrentScore : originalValue;
    }

    public LexiconBookId? SelectedBook =>
        ConfigService.SupportsBook(Kind, (LexiconBookId)SelectedBookValue) ? (LexiconBookId)SelectedBookValue : null;

    public string PickCorrectionEntryId()
    {
        return Notebook.PickRandomEntry(Rng.Chaotic);
    }

    public void SetSelectedBook(LexiconBookId? book)
    {
        SelectedBookValue = book.HasValue ? (int)book.Value : -1;
        RefreshFromConfig();
    }

    /// <summary>
    /// 从遗物当前可用的词库范围内挑一题。
    /// </summary>
    public LexiconEntry PickStudyEntry(Rng rng)
    {
        var studyBooks = GetActiveStudyBooks();
        if (studyBooks.Count == 0)
        {
            throw new InvalidOperationException($"No enabled books for {Kind}");
        }

        var book = studyBooks[rng.NextInt(studyBooks.Count)];
        return SchedulerService.PickEntry(Scheduler, book, LexiconService.GetBook(book), rng);
    }

    public bool TryGetCompatibilityScore(CardModel? cardSource, out int score)
    {
        if (!IsCompatibilityCard(cardSource))
        {
            score = 0;
            return false;
        }

        score = CurrentScore;
        return true;
    }

    /// <summary>
    /// 普通答题完成后更新分数、连对、调度器和错题本。
    /// 如果得分为 0 (失败)，则不设置兼容牌，从而不改动卡牌上下文 (X 值、Power 数量等)。
    /// </summary>
    public void ApplyQuizResult(QuizPromptData prompt, QuizResult result, CardModel playedCard)
    {
        if (result.Score > 0)
        {
            _compatibilityCard = playedCard;
            CurrentScore = result.Score+1;
        }
        else
        {
            // 答题失败时不改动卡牌上下文。
            _compatibilityCard = null;
            CurrentScore = 0;
        }

        PerfectStreak = result.Perfect ? PerfectStreak + 1 : 0;
        SchedulerService.RecordResult(Scheduler, prompt.SourceEntryId, result, correctionMode: false);
        SyncScheduler();

        if (result.Score == 0)
        {
            Notebook.Add(prompt.SourceEntryId);
            SyncNotebook();
        }
    }

    /// <summary>
    /// 纠错模式不会影响普通连对，但可以提升当前分数并移除错题。
    /// </summary>
    public void ApplyCorrectionResult(QuizPromptData prompt, QuizResult result)
    {
        SchedulerService.RecordResult(Scheduler, prompt.SourceEntryId, result, correctionMode: true);
        SyncScheduler();
        if (result.Score == 0)
        {
            Notebook.Add(prompt.SourceEntryId);
            SyncNotebook();
        }

        if (result.Score > CurrentScore)
        {
            CurrentScore = result.Score+1;
        }

        if (result.Perfect)
        {
            Notebook.Remove(prompt.SourceEntryId);
            SyncNotebook();
            TryGrantPotionReward(LocalizationService.Get("message.potionReward"));
        }
    }

    /// <summary>
    /// 每场战斗的第一次完美纠错可发放随机药水。
    /// </summary>
    public void TryGrantPotionReward(string successText)
    {
        if (RewardGrantedThisCombat)
        {
            QuizRuntimeService.ShowMessage(LocalizationService.Get("message.rewardUsed"));
            return;
        }

        var potion = MegaCrit.Sts2.Core.Factories.PotionFactory.CreateRandomPotionInCombat(Owner, Rng.Chaotic);
        var result = Owner.AddPotionInternal(potion, -1, false);
        RewardGrantedThisCombat = true;

        QuizRuntimeService.ShowMessage(result.success
            ? successText
            : LocalizationService.Get("message.potionFull"));
    }

    private bool IsCompatibilityCard(CardModel? cardSource)
    {
        return _compatibilityCard != null && ReferenceEquals(_compatibilityCard, cardSource);
    }

    private bool ShouldScaleDamage(ValueProp props, Creature? dealer)
    {
        return props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered) && IsOwnedCreature(dealer);
    }

    private bool ShouldScaleBlock(ValueProp props, Creature? target)
    {
        return props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered) && IsOwnedCreature(target);
    }

    private bool IsOwnedCreature(Creature? creature)
    {
        if (Owner?.Creature == null || creature == null)
        {
            return false;
        }

        return ReferenceEquals(creature, Owner.Creature) || ReferenceEquals(creature.PetOwner, Owner);
    }

    /// <summary>
    /// 如果遗物已锁定具体词库，则只从该词库出题；否则按配置权重抽。
    /// </summary>
    private List<LexiconBookId> GetActiveStudyBooks()
    {
        if (SelectedBook is { } selectedBook)
        {
            return ConfigService.IsBookEnabled(Kind, selectedBook) ? [selectedBook] : [];
        }

        return ConfigService.GetWeightedBooks(Kind);
    }

    protected LexiconBookId ResolveDisplayBook(LexiconBookId fallback)
    {
        if (SelectedBook is { } selectedBook)
        {
            return selectedBook;
        }

        return ConfigService.GetDominantBook(Kind) ?? fallback;
    }

    /// <summary>
    /// 保证连对 power 在战斗内始终存在并与计数同步。
    /// </summary>
    private void EnsurePerfectPowerExists()
    {
        if (Owner?.Creature == null || Owner.Creature.HasPower<PerfectAnsPowerModel>())
        {
            return;
        }

        var power = ModelDb.Power<PerfectAnsPowerModel>().ToMutable();
        power.ApplyInternal(Owner.Creature, 1, true);
        power.SetAmount(PerfectStreak, true);
    }

    private void RefreshPerfectPower()
    {
        if (Owner?.Creature == null)
        {
            return;
        }

        var power = Owner.Creature.GetPower<PerfectAnsPowerModel>();
        if (power != null)
        {
            power.SetAmount(PerfectStreak, true);
            return;
        }

        if (Owner.Creature.CombatState != null)
        {
            EnsurePerfectPowerExists();
        }
    }

    /// <summary>
    /// 懒加载错题本，并在旧 JSON 存档读入后自动迁移到新结构。
    /// </summary>
    private CorrectionNotebookState Notebook
    {
        get
        {
            if (_cachedNotebook != null)
            {
                return _cachedNotebook;
            }

            _cachedNotebook = QuizSaveCodec.DecodeNotebook(
                NotebookTotalCount,
                NotebookItemBooks,
                NotebookItemIndices,
                NotebookItemCounts,
                NotebookRemovedBooks,
                NotebookRemovedIndices,
                NotebookJson,
                out var migratedFromLegacy);
            if (migratedFromLegacy)
            {
                PersistNotebook(_cachedNotebook);
            }

            return _cachedNotebook;
        }
    }

    /// <summary>
    /// 懒加载调度器，并在旧 JSON 存档读入后自动迁移到新结构。
    /// </summary>
    private ReviewSchedulerState Scheduler
    {
        get
        {
            if (_cachedScheduler != null)
            {
                return _cachedScheduler;
            }

            _cachedScheduler = QuizSaveCodec.DecodeScheduler(
                SchedulerSessionCount,
                SchedulerReviewCount,
                SchedulerLastEntryBook,
                SchedulerLastEntryIndex,
                SchedulerEntryBooks,
                SchedulerEntryIndices,
                SchedulerReviews,
                SchedulerLapses,
                SchedulerConsecutiveCorrect,
                SchedulerDueDays,
                SchedulerLastReviewedDays,
                SchedulerDifficultyMilli,
                SchedulerStabilityMilli,
                SchedulerJson,
                out var migratedFromLegacy);
            if (migratedFromLegacy)
            {
                PersistScheduler(_cachedScheduler);
            }

            return _cachedScheduler;
        }
    }

    private void SyncNotebook()
    {
        PersistNotebook(Notebook);
        UpdateDescriptionVars();
    }

    private void SyncScheduler()
    {
        PersistScheduler(Scheduler);
    }

    /// <summary>
    /// 把错题本运行时对象写回结构化 SavedProperty。
    /// </summary>
    private void PersistNotebook(CorrectionNotebookState notebook)
    {
        var encoded = QuizSaveCodec.EncodeNotebook(notebook);
        NotebookTotalCount = encoded.TotalCount;
        NotebookItemBooks = encoded.ItemBooks;
        NotebookItemIndices = encoded.ItemIndices;
        NotebookItemCounts = encoded.ItemCounts;
        NotebookRemovedBooks = encoded.RemovedBooks;
        NotebookRemovedIndices = encoded.RemovedIndices;
        NotebookJson = string.Empty;
        _cachedNotebook = notebook;
    }

    /// <summary>
    /// 把调度器运行时对象写回结构化 SavedProperty。
    /// </summary>
    private void PersistScheduler(ReviewSchedulerState scheduler)
    {
        var encoded = QuizSaveCodec.EncodeScheduler(scheduler);
        SchedulerSessionCount = encoded.SessionCount;
        SchedulerReviewCount = encoded.ReviewCount;
        SchedulerLastEntryBook = encoded.LastEntryBook;
        SchedulerLastEntryIndex = encoded.LastEntryIndex;
        SchedulerEntryBooks = encoded.EntryBooks;
        SchedulerEntryIndices = encoded.EntryIndices;
        SchedulerReviews = encoded.Reviews;
        SchedulerLapses = encoded.Lapses;
        SchedulerConsecutiveCorrect = encoded.ConsecutiveCorrect;
        SchedulerDueDays = encoded.DueDays;
        SchedulerLastReviewedDays = encoded.LastReviewedDays;
        SchedulerDifficultyMilli = encoded.DifficultyMilli;
        SchedulerStabilityMilli = encoded.StabilityMilli;
        SchedulerJson = string.Empty;
        _cachedScheduler = scheduler;
    }

    /// <summary>
    /// 更新描述里的词库标签和禁用提示。
    /// </summary>
    private void UpdateDescriptionVars()
    {
        if (DynamicVars.TryGetValue("BookFocus", out var focusVar) && focusVar is StringVar focus)
        {
            focus.StringValue = IsFunctional
                ? ResolveFocusLabel()
                : LocalizationService.DisabledBookLabel();
        }

        if (DynamicVars.TryGetValue("StatusSuffix", out var statusVar) && statusVar is StringVar status)
        {
            status.StringValue = IsFunctional
                ? string.Empty
                : LocalizationService.DisabledRelicStatusSuffix();
        }
    }

    private int _currentScore;
    private int _perfectStreak;
    private bool _rewardGrantedThisCombat;
    private int _selectedBookValue = -1;
    private string _notebookJson = string.Empty;
    private string _schedulerJson = string.Empty;
}

/// <summary>
/// CET 系答题遗物。
/// </summary>
public sealed class CetQuizRelicModel : QuizRelicModel
{
    public override QuizRelicKind Kind => QuizRelicKind.CET;

    protected override string ResolvePrimaryIconPath()
    {
        return ResolveDisplayBook(LexiconBookId.CET4) switch
        {
            LexiconBookId.CET6 => ModPaths.RelicAsset("book_of_cet6.png"),
            _ => ModPaths.RelicAsset("book_of_cet4.png"),
        };
    }

    protected override string ResolveFocusLabel()
    {
        return LocalizationService.BookLabel(ResolveDisplayBook(LexiconBookId.CET4));
    }
}

/// <summary>
/// JLPT 系答题遗物。
/// </summary>
public sealed class JlptQuizRelicModel : QuizRelicModel
{
    public override QuizRelicKind Kind => QuizRelicKind.JLPT;

    protected override string ResolvePrimaryIconPath()
    {
        return ResolveDisplayBook(LexiconBookId.N1) switch
        {
            LexiconBookId.N2 => ModPaths.RelicAsset("book_of_n2.png"),
            LexiconBookId.N3 => ModPaths.RelicAsset("book_of_n3.png"),
            LexiconBookId.N4 => ModPaths.RelicAsset("book_of_n4.png"),
            _ => ModPaths.RelicAsset("book_of_n1.png"),
        };
    }

    protected override string ResolveFocusLabel()
    {
        return LocalizationService.BookLabel(ResolveDisplayBook(LexiconBookId.N1));
    }
}

/// <summary>
/// 用于显示连对计数，并在 5/10 连对时按原规则给敌方上 debuff。
/// </summary>
public sealed class PerfectAnsPowerModel : PowerModel
{
    [SavedProperty]
    public bool OddTurn
    {
        get => _oddTurn;
        set
        {
            AssertMutable();
            _oddTurn = value;
        }
    }

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override LocString Description
    {
        get
        {
            var text = new LocString("powers", $"{Id.Entry}.description");
            text.Add("Amount", Amount);
            return text;
        }
    }

    /// <summary>
    /// 连对达到阈值后，隔回合施加 Weak/Vulnerable。
    /// </summary>
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        OddTurn = !OddTurn;
        var enemies = player.Creature.CombatState?.HittableEnemies ?? [];
        if (Amount >= 10 && !OddTurn)
        {
            foreach (var enemy in enemies)
            {
                await PowerCmd.Apply<VulnerablePower>(choiceContext, enemy, 1, player.Creature, null, true);
            }
        }
        else if (Amount >= 5 && OddTurn)
        {
            foreach (var enemy in enemies)
            {
                await PowerCmd.Apply<WeakPower>(choiceContext, enemy, 1, player.Creature, null, true);
            }
        }
    }

    private bool _oddTurn = true;
}
