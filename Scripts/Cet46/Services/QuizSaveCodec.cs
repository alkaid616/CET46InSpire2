using System.Text.Json;
using CET46InSpire2.Scripts.Cet46.Data;

namespace CET46InSpire2.Scripts.Cet46.Services;

/// <summary>
/// 错题本结构化存档的并行数组表示。
/// </summary>
public readonly record struct EncodedNotebookState(
    int TotalCount,
    int[] ItemBooks,
    int[] ItemIndices,
    int[] ItemCounts,
    int[] RemovedBooks,
    int[] RemovedIndices);

/// <summary>
/// 调度器结构化存档的并行数组表示。
/// </summary>
public readonly record struct EncodedSchedulerState(
    int SessionCount,
    int ReviewCount,
    int LastEntryBook,
    int LastEntryIndex,
    int[] EntryBooks,
    int[] EntryIndices,
    int[] Reviews,
    int[] Lapses,
    int[] ConsecutiveCorrect,
    int[] DueDays,
    int[] LastReviewedDays,
    int[] DifficultyMilli,
    int[] StabilityMilli);

/// <summary>
/// 在运行时对象和 SavedProperty 并行数组之间做双向转换。
/// </summary>
public static class QuizSaveCodec
{
    private const int FixedPointScale = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    /// <summary>
    /// 把错题本状态编码成适合 SavedProperty 的整型数组。
    /// </summary>
    public static EncodedNotebookState EncodeNotebook(CorrectionNotebookState notebook)
    {
        var items = notebook.Items
            .Select(pair => TryParseEntryId(pair.Key, out var book, out var index)
                ? new NotebookItem((int)book, index, Math.Max(0, pair.Value))
                : default(NotebookItem?))
            .Where(item => item is { Count: > 0 })
            .Select(item => item!.Value)
            .OrderBy(item => item.Book)
            .ThenBy(item => item.Index)
            .ToList();

        var removed = notebook.RemovedItems
            .Select(entryId => TryParseEntryId(entryId, out var book, out var index)
                ? new NotebookKey((int)book, index)
                : default(NotebookKey?))
            .Where(key => key.HasValue)
            .Select(key => key!.Value)
            .Distinct()
            .OrderBy(key => key.Book)
            .ThenBy(key => key.Index)
            .ToList();

        return new EncodedNotebookState(
            items.Sum(item => item.Count),
            items.Select(item => item.Book).ToArray(),
            items.Select(item => item.Index).ToArray(),
            items.Select(item => item.Count).ToArray(),
            removed.Select(item => item.Book).ToArray(),
            removed.Select(item => item.Index).ToArray());
    }

    /// <summary>
    /// 从结构化字段恢复错题本；没有新格式时兼容读取旧 JSON。
    /// </summary>
    public static CorrectionNotebookState DecodeNotebook(
        int totalCount,
        int[]? itemBooks,
        int[]? itemIndices,
        int[]? itemCounts,
        int[]? removedBooks,
        int[]? removedIndices,
        string? legacyJson,
        out bool migratedFromLegacy)
    {
        migratedFromLegacy = false;
        if (!HasStructuredNotebookData(totalCount, itemBooks, itemIndices, itemCounts, removedBooks, removedIndices))
        {
            if (!string.IsNullOrWhiteSpace(legacyJson))
            {
                migratedFromLegacy = true;
                return JsonSerializer.Deserialize<CorrectionNotebookState>(legacyJson, JsonOptions) ?? new CorrectionNotebookState();
            }

            return new CorrectionNotebookState();
        }

        var notebook = new CorrectionNotebookState();
        var itemLength = MinLength(itemBooks, itemIndices, itemCounts);
        for (var i = 0; i < itemLength; i++)
        {
            if (!TryCreateEntryId(itemBooks![i], itemIndices![i], out var entryId))
            {
                continue;
            }

            var count = Math.Max(0, itemCounts![i]);
            if (count <= 0)
            {
                continue;
            }

            notebook.Items[entryId] = count;
            notebook.TotalCount += count;
        }

        var removedLength = MinLength(removedBooks, removedIndices);
        for (var i = 0; i < removedLength; i++)
        {
            if (TryCreateEntryId(removedBooks![i], removedIndices![i], out var entryId))
            {
                notebook.RemovedItems.Add(entryId);
            }
        }

        if (notebook.TotalCount <= 0 && totalCount > 0)
        {
            notebook.TotalCount = totalCount;
        }

        return notebook;
    }

    /// <summary>
    /// 把调度器状态编码成结构化字段。
    /// </summary>
    public static EncodedSchedulerState EncodeScheduler(ReviewSchedulerState scheduler)
    {
        var entries = scheduler.ProgressByEntryId
            .Select(pair => TryParseEntryId(pair.Key, out var book, out var index)
                ? new SchedulerEntry((int)book, index, pair.Value)
                : default(SchedulerEntry?))
            .Where(entry => entry.HasValue)
            .Select(entry => entry!.Value)
            .OrderBy(entry => entry.Book)
            .ThenBy(entry => entry.Index)
            .ToList();

        var hasLastEntry = TryParseEntryId(scheduler.LastEntryId, out var lastBook, out var lastIndex);
        return new EncodedSchedulerState(
            Math.Max(0, scheduler.SessionCount),
            Math.Max(0, scheduler.ReviewCount),
            hasLastEntry ? (int)lastBook : -1,
            hasLastEntry ? lastIndex : -1,
            entries.Select(entry => entry.Book).ToArray(),
            entries.Select(entry => entry.Index).ToArray(),
            entries.Select(entry => Math.Max(0, entry.Progress.Reviews)).ToArray(),
            entries.Select(entry => Math.Max(0, entry.Progress.Lapses)).ToArray(),
            entries.Select(entry => Math.Max(0, entry.Progress.ConsecutiveCorrect)).ToArray(),
            entries.Select(entry => ClampDay(entry.Progress.DueDayNumber)).ToArray(),
            entries.Select(entry => ClampDay(entry.Progress.LastReviewedDayNumber)).ToArray(),
            entries.Select(entry => ToFixed(entry.Progress.Difficulty)).ToArray(),
            entries.Select(entry => ToFixed(entry.Progress.Stability)).ToArray());
    }

    /// <summary>
    /// 从结构化字段恢复调度器；必要时执行旧 JSON 迁移。
    /// </summary>
    public static ReviewSchedulerState DecodeScheduler(
        int sessionCount,
        int reviewCount,
        int lastEntryBook,
        int lastEntryIndex,
        int[]? entryBooks,
        int[]? entryIndices,
        int[]? reviews,
        int[]? lapses,
        int[]? consecutiveCorrect,
        int[]? dueDays,
        int[]? lastReviewedDays,
        int[]? difficultyMilli,
        int[]? stabilityMilli,
        string? legacyJson,
        out bool migratedFromLegacy)
    {
        migratedFromLegacy = false;
        if (!HasStructuredSchedulerData(sessionCount, reviewCount, lastEntryBook, lastEntryIndex, entryBooks, entryIndices, reviews, lapses, consecutiveCorrect, dueDays, lastReviewedDays, difficultyMilli, stabilityMilli))
        {
            if (!string.IsNullOrWhiteSpace(legacyJson))
            {
                migratedFromLegacy = true;
                return SchedulerService.Deserialize(legacyJson);
            }

            return new ReviewSchedulerState();
        }

        var scheduler = new ReviewSchedulerState
        {
            SessionCount = Math.Max(0, sessionCount),
            ReviewCount = Math.Max(0, reviewCount),
            LastEntryId = TryCreateEntryId(lastEntryBook, lastEntryIndex, out var lastEntryId) ? lastEntryId : string.Empty,
        };

        var length = MinLength(entryBooks, entryIndices, reviews, lapses, consecutiveCorrect, dueDays, lastReviewedDays, difficultyMilli, stabilityMilli);
        for (var i = 0; i < length; i++)
        {
            if (!TryCreateEntryId(entryBooks![i], entryIndices![i], out var entryId))
            {
                continue;
            }

            scheduler.ProgressByEntryId[entryId] = new ReviewProgressState
            {
                Reviews = Math.Max(0, reviews![i]),
                Lapses = Math.Max(0, lapses![i]),
                ConsecutiveCorrect = Math.Max(0, consecutiveCorrect![i]),
                DueDayNumber = dueDays![i],
                LastReviewedDayNumber = lastReviewedDays![i],
                Difficulty = FromFixed(difficultyMilli![i]),
                Stability = FromFixed(stabilityMilli![i]),
            };
        }

        return scheduler;
    }

    /// <summary>
    /// 把稳定的 entry id 解析回 book/index。
    /// </summary>
    public static bool TryParseEntryId(string? entryId, out LexiconBookId book, out int index)
    {
        book = default;
        index = -1;
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        var split = entryId.Split(':', 2, StringSplitOptions.TrimEntries);
        return split.Length == 2 &&
               Enum.TryParse(split[0], true, out book) &&
               Enum.IsDefined(book) &&
               int.TryParse(split[1], out index) &&
               index >= 0;
    }

    /// <summary>
    /// 把 book/index 拼回稳定的 entry id。
    /// </summary>
    private static bool TryCreateEntryId(int bookValue, int index, out string entryId)
    {
        entryId = string.Empty;
        if (!Enum.IsDefined(typeof(LexiconBookId), bookValue) || index < 0)
        {
            return false;
        }

        entryId = $"{(LexiconBookId)bookValue}:{index}";
        return true;
    }

    /// <summary>
    /// 判断错题本是否已有新结构存档。
    /// </summary>
    private static bool HasStructuredNotebookData(
        int totalCount,
        int[]? itemBooks,
        int[]? itemIndices,
        int[]? itemCounts,
        int[]? removedBooks,
        int[]? removedIndices)
    {
        return totalCount > 0 ||
               (itemBooks?.Length ?? 0) > 0 ||
               (itemIndices?.Length ?? 0) > 0 ||
               (itemCounts?.Length ?? 0) > 0 ||
               (removedBooks?.Length ?? 0) > 0 ||
               (removedIndices?.Length ?? 0) > 0;
    }

    /// <summary>
    /// 判断调度器是否已有新结构存档。
    /// </summary>
    private static bool HasStructuredSchedulerData(
        int sessionCount,
        int reviewCount,
        int lastEntryBook,
        int lastEntryIndex,
        int[]? entryBooks,
        int[]? entryIndices,
        int[]? reviews,
        int[]? lapses,
        int[]? consecutiveCorrect,
        int[]? dueDays,
        int[]? lastReviewedDays,
        int[]? difficultyMilli,
        int[]? stabilityMilli)
    {
        return sessionCount > 0 ||
               reviewCount > 0 ||
               lastEntryBook >= 0 ||
               lastEntryIndex >= 0 ||
               (entryBooks?.Length ?? 0) > 0 ||
               (entryIndices?.Length ?? 0) > 0 ||
               (reviews?.Length ?? 0) > 0 ||
               (lapses?.Length ?? 0) > 0 ||
               (consecutiveCorrect?.Length ?? 0) > 0 ||
               (dueDays?.Length ?? 0) > 0 ||
               (lastReviewedDays?.Length ?? 0) > 0 ||
               (difficultyMilli?.Length ?? 0) > 0 ||
               (stabilityMilli?.Length ?? 0) > 0;
    }

    /// <summary>
    /// 对平行数组取最短长度，自动裁掉坏尾项。
    /// </summary>
    private static int MinLength(params int[]?[] arrays)
    {
        return arrays.Where(array => array != null).Select(array => array!.Length).DefaultIfEmpty(0).Min();
    }

    /// <summary>
    /// 双精度值转为定点整数，避免 SavedProperty 里继续塞 JSON。
    /// </summary>
    private static int ToFixed(double value)
    {
        return (int)Math.Round(value * FixedPointScale, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 定点整数还原为双精度值。
    /// </summary>
    private static double FromFixed(int value)
    {
        return value / (double)FixedPointScale;
    }

    /// <summary>
    /// 保护 long day number 写入 int SavedProperty 时不溢出。
    /// </summary>
    private static int ClampDay(long dayNumber)
    {
        return dayNumber < int.MinValue ? int.MinValue : dayNumber > int.MaxValue ? int.MaxValue : (int)dayNumber;
    }

    private readonly record struct NotebookItem(int Book, int Index, int Count);

    private readonly record struct NotebookKey(int Book, int Index);

    private readonly record struct SchedulerEntry(int Book, int Index, ReviewProgressState Progress);
}
