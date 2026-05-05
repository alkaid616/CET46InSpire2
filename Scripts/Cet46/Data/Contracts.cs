using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace CET46InSpire2.Scripts.Cet46.Data;

/// <summary>
/// 遗物所属的题库大类。
/// </summary>
public enum QuizRelicKind
{
    CET,
    JLPT,
}

/// <summary>
/// 具体词库标识。事件内选书和调度器存档都基于这个枚举。
/// </summary>
public enum LexiconBookId
{
    CET4,
    CET6,
    N1,
    N2,
    N3,
    N4,
}

/// <summary>
/// 单个词库的配置权重。当前主要保留给配置模型和后续扩展。
/// </summary>
public sealed class WeightedLexiconConfig
{
    public required LexiconBookId Book { get; init; }
    public required int Weight { get; set; }
}

/// <summary>
/// 模组配置持久化对象。
/// </summary>
public sealed class ModConfigData
{
    public int SchemaVersion { get; set; } = 1;
    public bool DarkMode { get; set; }
    public bool PureFont { get; set; } = true;
    public bool FastMode { get; set; }
    public bool AutoCheckAnswer { get; set; } = true;
    public bool CasualMode { get; set; }
    public bool IgnoreCheck { get; set; }
    public bool ShowLexicon { get; set; } = true;
    public int MaxAnsNum { get; set; } = 3;
    public bool LoadCET { get; set; } = true;
    public bool LoadJLPT { get; set; } = true;
    public int ExamScheduleVersion { get; set; } = 1;
    public Dictionary<string, int> LexiconWeights { get; set; } = new();
}

/// <summary>
/// 标准化后的词条结构，统一承载 CET 和 JLPT 词库数据。
/// </summary>
public sealed class LexiconEntry
{
    public required string EntryId { get; init; }
    public required LexiconBookId Book { get; init; }
    public required int Index { get; init; }
    public required string Prompt { get; init; }
    public required List<string> Meanings { get; init; }
    public string Kana { get; init; } = string.Empty;
    public string Kanji { get; init; } = string.Empty;
    public List<string> ConfusedFurigana { get; init; } = new();

    [JsonIgnore]
    public bool HasKanji => !string.IsNullOrWhiteSpace(Kanji);
}

/// <summary>
/// 一次答题会话的只读输入数据。
/// </summary>
public sealed class QuizPromptData
{
    public required string PromptId { get; init; }
    public required string SourceEntryId { get; init; }
    public required LexiconBookId Book { get; init; }
    public required string PromptText { get; init; }
    public required List<string> CorrectOptions { get; init; }
    public required List<string> Options { get; init; }
    public required QuizRelicKind Kind { get; init; }
    public required bool IsCorrectionMode { get; init; }
}

/// <summary>
/// 答题界面返回的作答结果。
/// </summary>
public sealed class QuizResult
{
    public required HashSet<string> SelectedOptions { get; init; }
    public required int Score { get; init; }
    public required bool Perfect { get; init; }
}

/// <summary>
/// 拦截打牌后暂存的上下文，用于答题结束后恢复原始出牌。
/// </summary>
public sealed class PendingCardPlayContext
{
    public required Player Owner { get; init; }
    public required CardModel Card { get; init; }
    public Creature? Target { get; init; }
}

/// <summary>
/// 调度器里单个词条的复习进度。
/// </summary>
public sealed class ReviewProgressState
{
    public int Reviews { get; set; }
    public int Lapses { get; set; }
    public int ConsecutiveCorrect { get; set; }
    public double Stability { get; set; } = 1;
    public double Difficulty { get; set; } = 5;
    public long DueDayNumber { get; set; }
    public long LastReviewedDayNumber { get; set; }
}

/// <summary>
/// 本局运行中的简化调度器状态。
/// </summary>
public sealed class ReviewSchedulerState
{
    public int SessionCount { get; set; }
    public int ReviewCount { get; set; }
    public string LastEntryId { get; set; } = string.Empty;
    public Dictionary<string, ReviewProgressState> ProgressByEntryId { get; set; } = new();
}

/// <summary>
/// 本局临时错题本。只在当前 run 内保存，不跨 run 共享。
/// </summary>
public sealed class CorrectionNotebookState
{
    public int TotalCount { get; set; }
    public Dictionary<string, int> Items { get; set; } = new();
    public HashSet<string> RemovedItems { get; set; } = new();

    public bool IsEmpty => TotalCount <= 0 || Items.Count == 0;

    /// <summary>
    /// 记录一道错题。如果同题重复答错，则累计次数。
    /// </summary>
    public void Add(string entryId)
    {
        if (Items.TryGetValue(entryId, out var count))
        {
            Items[entryId] = count + 1;
        }
        else
        {
            Items[entryId] = 1;
        }

        TotalCount += 1;
    }

    /// <summary>
    /// 在纠错成功时移除一次记录。
    /// </summary>
    public bool Remove(string entryId)
    {
        if (!Items.TryGetValue(entryId, out var count) || count <= 0)
        {
            return false;
        }

        TotalCount = Math.Max(0, TotalCount - 1);
        if (count == 1)
        {
            Items.Remove(entryId);
            RemovedItems.Add(entryId);
        }
        else
        {
            Items[entryId] = count - 1;
        }

        return true;
    }

    /// <summary>
    /// 按错题出现次数加权随机抽一题用于纠错。
    /// </summary>
    public string PickRandomEntry(Rng rng)
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var target = rng.NextInt(Math.Max(1, TotalCount));
        var cursor = 0;
        foreach (var pair in Items)
        {
            cursor += pair.Value;
            if (cursor > target)
            {
                return pair.Key;
            }
        }

        return Items.Keys.FirstOrDefault() ?? string.Empty;
    }
}
