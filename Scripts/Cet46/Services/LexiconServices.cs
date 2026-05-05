using System.Text.Json;
using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Models;
using MegaCrit.Sts2.Core.Random;

namespace CET46InSpire2.Scripts.Cet46.Services;

/// <summary>
/// 简化版本局调度器，负责在已启用词库内挑题和记录结果。
/// </summary>
public static class SchedulerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    /// <summary>
    /// 结合新题优先、到期优先和遗忘惩罚，从当前词库中抽一道题。
    /// </summary>
    public static LexiconEntry PickEntry(ReviewSchedulerState scheduler, LexiconBookId book, IReadOnlyList<LexiconEntry> entries, Rng rng)
    {
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"No lexicon entries available for {book}.");
        }

        scheduler.SessionCount += 1;
        var today = DateOnly.FromDateTime(DateTime.Now).DayNumber;
        var candidates = entries
            .Select(entry =>
            {
                var progress = GetProgress(scheduler, entry.EntryId);
                var dueBoost = progress.Reviews == 0 ? 18d : Math.Max(0, today - progress.DueDayNumber) * 4d;
                var lapseBoost = progress.Lapses * 3d;
                var noveltyBoost = progress.Reviews == 0 ? 6d : 0d;
                var stabilityPenalty = progress.Stability * 0.35d;
                var repeatPenalty = string.Equals(scheduler.LastEntryId, entry.EntryId, StringComparison.Ordinal) ? 6d : 0d;
                var priority = dueBoost + lapseBoost + noveltyBoost - stabilityPenalty - repeatPenalty;
                return (entry, priority);
            })
            .OrderByDescending(pair => pair.priority)
            .Take(16)
            .ToList();

        var totalWeight = candidates.Sum(pair => Math.Max(1d, pair.priority + 1d));
        var roll = rng.NextFloat() * (float)totalWeight;
        foreach (var candidate in candidates)
        {
            roll -= (float)Math.Max(1d, candidate.priority + 1d);
            if (roll <= 0f)
            {
                scheduler.LastEntryId = candidate.entry.EntryId;
                return candidate.entry;
            }
        }

        scheduler.LastEntryId = candidates[0].entry.EntryId;
        return candidates[0].entry;
    }

    /// <summary>
    /// 根据测验结果更新同题的复习进度。
    /// </summary>
    public static void RecordResult(ReviewSchedulerState scheduler, string entryId, QuizResult result, bool correctionMode)
    {
        var progress = GetProgress(scheduler, entryId);
        scheduler.ReviewCount += 1;
        scheduler.LastEntryId = entryId;
        progress.Reviews += 1;
        progress.LastReviewedDayNumber = DateOnly.FromDateTime(DateTime.Now).DayNumber;

        if (result.Perfect)
        {
            progress.ConsecutiveCorrect += 1;
            progress.Difficulty = Math.Max(1.3d, progress.Difficulty - (correctionMode ? 0.2d : 0.35d));
            progress.Stability = Math.Min(60d, progress.Stability * 1.8d + progress.ConsecutiveCorrect + Math.Max(0, result.Score - 1) * 0.5d);
            progress.DueDayNumber = progress.LastReviewedDayNumber + Math.Max(1L, (long)Math.Round(progress.Stability));
        }
        else
        {
            progress.ConsecutiveCorrect = 0;
            progress.Lapses += 1;
            progress.Difficulty = Math.Min(10d, progress.Difficulty + (result.Score == 0 ? 1.1d : 0.6d));
            progress.Stability = Math.Max(0.75d, progress.Stability * 0.55d);
            progress.DueDayNumber = progress.LastReviewedDayNumber + (correctionMode || result.Score > 0 ? 1 : 0);
        }
    }

    /// <summary>
    /// 旧版 JSON 调度器存档保留的兼容序列化接口。
    /// </summary>
    public static string Serialize(ReviewSchedulerState scheduler)
    {
        return JsonSerializer.Serialize(scheduler, JsonOptions);
    }

    public static ReviewSchedulerState Deserialize(string json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? new ReviewSchedulerState()
            : JsonSerializer.Deserialize<ReviewSchedulerState>(json, JsonOptions) ?? new ReviewSchedulerState();
    }

    /// <summary>
    /// 获取或创建指定词条的进度节点。
    /// </summary>
    private static ReviewProgressState GetProgress(ReviewSchedulerState scheduler, string entryId)
    {
        if (!scheduler.ProgressByEntryId.TryGetValue(entryId, out var progress))
        {
            progress = new ReviewProgressState();
            scheduler.ProgressByEntryId[entryId] = progress;
        }

        return progress;
    }
}

/// <summary>
/// 词库加载、查词和组装题目选项的统一入口。
/// </summary>
public static class LexiconService
{
    private sealed class RawLexiconEntry
    {
        public string[]? TEXT { get; set; }
        public Dictionary<string, string>? TEXT_DICT { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private static readonly Dictionary<LexiconBookId, List<LexiconEntry>> Entries = new();
    private static readonly Dictionary<string, LexiconEntry> EntriesById = new(StringComparer.Ordinal);

    /// <summary>
    /// 启动时读取全部词库 JSON。
    /// </summary>
    public static void LoadAll()
    {
        Entries.Clear();
        EntriesById.Clear();

        foreach (var book in Enum.GetValues<LexiconBookId>())
        {
            var loaded = LoadBook(book);
            Entries[book] = loaded;
            foreach (var entry in loaded)
            {
                EntriesById[entry.EntryId] = entry;
            }
        }
    }

    public static IReadOnlyList<LexiconEntry> GetBook(LexiconBookId book) => Entries.TryGetValue(book, out var entries) ? entries : [];

    /// <summary>
    /// 通过稳定的 entry id 取回词条。
    /// </summary>
    public static LexiconEntry GetEntry(string entryId)
    {
        if (!EntriesById.TryGetValue(entryId, out var entry))
        {
            throw new KeyNotFoundException($"Missing lexicon entry: {entryId}");
        }

        return entry;
    }

    /// <summary>
    /// 生成正式答题或纠错答题的题面数据。
    /// </summary>
    public static QuizPromptData BuildPrompt(QuizRelicModel relic, Rng rng, string? forcedEntryId = null, bool correctionMode = false)
    {
        LexiconEntry entry;
        if (!string.IsNullOrEmpty(forcedEntryId))
        {
            entry = GetEntry(forcedEntryId);
        }
        else
        {
            entry = relic.PickStudyEntry(rng);
        }

        return relic.Kind == QuizRelicKind.CET ? BuildCetPrompt(entry, rng, correctionMode) : BuildJlptPrompt(entry, rng, correctionMode);
    }

    /// <summary>
    /// 开发命令专用：允许指定词库和词条编号直接开题。
    /// </summary>
    public static QuizPromptData BuildDebugPrompt(QuizRelicKind kind, LexiconBookId? book, int? index, Rng rng)
    {
        var selectedBook = book ?? GetDebugBooks(kind)[rng.NextInt(GetDebugBooks(kind).Count)];
        if (!GetDebugBooks(kind).Contains(selectedBook))
        {
            throw new InvalidOperationException($"Book {selectedBook} is not valid for {kind}.");
        }

        var entry = index.HasValue ? GetEntryByIndex(selectedBook, index.Value) : PickRandomEntry(selectedBook, rng);
        return kind == QuizRelicKind.CET ? BuildCetPrompt(entry, rng, false) : BuildJlptPrompt(entry, rng, false);
    }

    public static IReadOnlyList<LexiconBookId> GetDebugBooks(QuizRelicKind kind)
    {
        return kind == QuizRelicKind.CET
            ? [LexiconBookId.CET4, LexiconBookId.CET6]
            : [LexiconBookId.N1, LexiconBookId.N2, LexiconBookId.N3, LexiconBookId.N4];
    }

    /// <summary>
    /// 用于控制台校验指定词条编号是否存在。
    /// </summary>
    public static bool HasEntry(LexiconBookId book, int index)
    {
        return GetBook(book).Any(entry => entry.Index == index);
    }

    /// <summary>
    /// 从 Godot 资源路径读取单本词库。
    /// </summary>
    private static List<LexiconEntry> LoadBook(LexiconBookId book)
    {
        if (!Godot.FileAccess.FileExists(ModPaths.Vocabulary(book)))
        {
            return [];
        }

        using var file = Godot.FileAccess.Open(ModPaths.Vocabulary(book), Godot.FileAccess.ModeFlags.Read);
        var raw = JsonSerializer.Deserialize<Dictionary<string, RawLexiconEntry>>(file.GetAsText(), JsonOptions) ?? new();
        return raw
            .Where(pair => !pair.Key.EndsWith("_info", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => ParseIndex(pair.Key))
            .Select(pair => BuildEntry(book, ParseIndex(pair.Key), pair.Value))
            .ToList();
    }

    /// <summary>
    /// 把原始 JSON 词条统一转换为运行时结构。
    /// </summary>
    private static LexiconEntry BuildEntry(LexiconBookId book, int index, RawLexiconEntry raw)
    {
        var text = raw.TEXT ?? [];
        var entryId = $"{book}:{index}";
        if (book == LexiconBookId.CET4 || book == LexiconBookId.CET6)
        {
            return new LexiconEntry
            {
                EntryId = entryId,
                Book = book,
                Index = index,
                Prompt = text.ElementAtOrDefault(0) ?? string.Empty,
                Meanings = text.Skip(1).Where(value => !string.IsNullOrWhiteSpace(value)).ToList(),
            };
        }

        var confused = raw.TEXT_DICT != null && raw.TEXT_DICT.TryGetValue("ConfusedFurigana", out var value)
            ? value.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList()
            : [];

        return new LexiconEntry
        {
            EntryId = entryId,
            Book = book,
            Index = index,
            Prompt = text.ElementAtOrDefault(0) ?? string.Empty,
            Meanings = text.Skip(1).Take(1).Where(item => !string.IsNullOrWhiteSpace(item)).ToList(),
            Kana = text.ElementAtOrDefault(2) ?? string.Empty,
            Kanji = text.ElementAtOrDefault(3) ?? string.Empty,
            ConfusedFurigana = confused,
        };
    }

    /// <summary>
    /// CET 题型支持多正确项，干扰项从同词库抽取。
    /// </summary>
    private static QuizPromptData BuildCetPrompt(LexiconEntry entry, Rng rng, bool correctionMode)
    {
        var correctOptions = entry.Meanings
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(1, ConfigService.Current.MaxAnsNum))
            .ToList();

        var options = new HashSet<string>(correctOptions, StringComparer.Ordinal);
        var maxOptions = Math.Min(9, Math.Max(correctOptions.Count * 3, correctOptions.Count + 2));
        var pool = GetBook(entry.Book).Where(item => item.EntryId != entry.EntryId).ToList();
        while (options.Count < maxOptions && pool.Count > 0)
        {
            var distractorEntry = pool[rng.NextInt(pool.Count)];
            pool.Remove(distractorEntry);
            foreach (var meaning in distractorEntry.Meanings.Where(text => !string.IsNullOrWhiteSpace(text)))
            {
                options.Add(meaning);
                if (options.Count >= maxOptions)
                {
                    break;
                }
            }
        }

        return new QuizPromptData
        {
            PromptId = entry.EntryId,
            SourceEntryId = entry.EntryId,
            Book = entry.Book,
            PromptText = entry.Prompt,
            CorrectOptions = correctOptions,
            Options = Shuffle(options.ToList(), rng),
            Kind = QuizRelicKind.CET,
            IsCorrectionMode = correctionMode,
        };
    }

    /// <summary>
    /// JLPT 题型同时兼顾释义和假名干扰项。
    /// </summary>
    private static QuizPromptData BuildJlptPrompt(LexiconEntry entry, Rng rng, bool correctionMode)
    {
        var correctOptions = new HashSet<string>(StringComparer.Ordinal);
        var allOptions = new HashSet<string>(StringComparer.Ordinal);

        if (entry.Meanings.Count > 0)
        {
            correctOptions.Add(entry.Meanings[0]);
            allOptions.Add(entry.Meanings[0]);
        }

        var meaningPool = GetBook(entry.Book).Where(item => item.EntryId != entry.EntryId).ToList();
        while (allOptions.Count < 4 && meaningPool.Count > 0)
        {
            var other = meaningPool[rng.NextInt(meaningPool.Count)];
            meaningPool.Remove(other);
            if (other.Meanings.Count > 0)
            {
                allOptions.Add(other.Meanings[0]);
            }
        }

        if (entry.HasKanji && !string.IsNullOrWhiteSpace(entry.Kana))
        {
            correctOptions.Add(entry.Kana);
            allOptions.Add(entry.Kana);
            foreach (var confused in entry.ConfusedFurigana.Where(text => !string.IsNullOrWhiteSpace(text)).Take(4))
            {
                allOptions.Add(confused);
            }
        }

        return new QuizPromptData
        {
            PromptId = entry.EntryId,
            SourceEntryId = entry.EntryId,
            Book = entry.Book,
            PromptText = entry.Prompt,
            CorrectOptions = correctOptions.ToList(),
            Options = Shuffle(allOptions.ToList(), rng),
            Kind = QuizRelicKind.JLPT,
            IsCorrectionMode = correctionMode,
        };
    }

    /// <summary>
    /// 从指定词库内随机抽取一题。
    /// </summary>
    private static LexiconEntry PickRandomEntry(LexiconBookId book, Rng rng)
    {
        var entries = GetBook(book);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException($"No lexicon entries available for {book}.");
        }

        return entries[rng.NextInt(entries.Count)];
    }

    /// <summary>
    /// 根据编号查找单本词库中的词条。
    /// </summary>
    private static LexiconEntry GetEntryByIndex(LexiconBookId book, int index)
    {
        var entry = GetBook(book).FirstOrDefault(item => item.Index == index);
        if (entry == null)
        {
            throw new KeyNotFoundException($"Missing lexicon entry for {book}:{index}");
        }

        return entry;
    }

    /// <summary>
    /// 从原始 JSON key 中拆出词条编号。
    /// </summary>
    private static int ParseIndex(string key)
    {
        var split = key.Split('_');
        return int.TryParse(split[^1], out var index) ? index : 0;
    }

    /// <summary>
    /// 原地洗牌，用于随机化选项顺序。
    /// </summary>
    private static List<string> Shuffle(List<string> list, Rng rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var swap = rng.NextInt(i + 1);
            (list[i], list[swap]) = (list[swap], list[i]);
        }

        return list;
    }
}
