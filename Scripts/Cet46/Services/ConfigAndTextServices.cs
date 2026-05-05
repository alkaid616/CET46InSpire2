using System.Text.Json;
using CET46InSpire2.Scripts.Cet46.Bootstrap;
using CET46InSpire2.Scripts.Cet46.Data;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using STS2RitsuLib;
using STS2RitsuLib.Data;
using STS2RitsuLib.Utils.Persistence;

namespace CET46InSpire2.Scripts.Cet46.Services;

public static class ModPaths
{
    // 所有模组资源路径都从这里统一拼接，避免散落硬编码。
    public static string ModAssetRoot => $"res://{Cet46Bootstrap.PckName}";
    public static string Vocabulary(LexiconBookId book) => $"{ModAssetRoot}/assets/vocabulary/{book}.json";
    public static string RelicAsset(string fileName) => $"{ModAssetRoot}/assets/relics/{fileName}";
    public static string FontAsset => $"{ModAssetRoot}/assets/font/VictorMono-Medium.ttf";
    public static string LocalizationTable(string language, string table) => $"{ModAssetRoot}/localization/{language}/{table}.json";
    public static string LegacyConfigPath => Path.Combine(ProjectSettings.GlobalizePath("user://"), Cet46Bootstrap.PckName, "cet46_config.json");
}

/// <summary>
/// 保存跨场景但不需要写入 run save 的瞬时运行态。
/// </summary>
public static class RuntimeState
{
    private static bool _cleanupHookInstalled;

    public static bool NeowEventInjected { get; set; }
    public static bool PendingPostTerminalCetInjection { get; private set; }
    public static int PendingPostTerminalCetSourceFloor { get; private set; } = -1;

    /// <summary>
    /// 新开一局时重置所有瞬时状态。
    /// </summary>
    public static void ResetForRun()
    {
        DisposeTransientUi();
        NeowEventInjected = false;
        ClearPendingPostTerminalCetInjection();
        QuizRuntimeService.ResetRuntimeState();
    }

    /// <summary>
    /// 安装一次性的 Godot 生命周期清理钩子，确保退出时释放临时 UI/字体。
    /// </summary>
    public static void InstallLifetimeHooks()
    {
        if (_cleanupHookInstalled)
        {
            return;
        }

        if (Engine.GetMainLoop() is not SceneTree sceneTree || sceneTree.Root == null)
        {
            return;
        }

        sceneTree.Root.TreeExiting += HandleRootTreeExiting;
        _cleanupHookInstalled = true;
    }

    /// <summary>
    /// 根节点退出时兜底清理资源，减少悬挂 UI 和字体对象。
    /// </summary>
    private static void HandleRootTreeExiting()
    {
        DisposeTransientUi();
        _cleanupHookInstalled = false;
    }

    /// <summary>
    /// 释放不会跨局保留的 UI 和运行时缓存。
    /// </summary>
    public static void DisposeTransientUi()
    {
        ClearPendingPostTerminalCetInjection();
        QuizRuntimeService.DisposeRuntimeUi();
        FontService.Release();
    }

    /// <summary>
    /// 记录 terminal rewards 返回后需要补一次 CET 事件。
    /// </summary>
    public static void MarkPendingPostTerminalCetInjection(int sourceFloor)
    {
        PendingPostTerminalCetInjection = true;
        PendingPostTerminalCetSourceFloor = sourceFloor;
    }

    /// <summary>
    /// 清除挂起的奖励页后置注入状态。
    /// </summary>
    public static void ClearPendingPostTerminalCetInjection()
    {
        PendingPostTerminalCetInjection = false;
        PendingPostTerminalCetSourceFloor = -1;
    }
}

/// <summary>
/// 模组内置短文本和运行时格式化帮助。
/// </summary>
public static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> Zh = new Dictionary<string, string>
    {
        ["config.button"] = "配置",
        ["config.title"] = "CET46 配置",
        ["config.save"] = "保存",
        ["config.close"] = "关闭",
        ["config.reset"] = "恢复默认",
        ["config.darkMode"] = "深色模式",
        ["config.pureFont"] = "纯字体",
        ["config.fastMode"] = "快速模式",
        ["config.autoCheck"] = "自动检查答案",
        ["config.casualMode"] = "休闲模式",
        ["config.ignoreCheck"] = "自动返回",
        ["config.showLexicon"] = "显示词库",
        ["config.loadCET"] = "启用 CET",
        ["config.loadJLPT"] = "启用 JLPT",
        ["config.maxAnsNum"] = "最多正确项",
        ["config.weights.cet"] = "CET 权重",
        ["config.weights.jlpt"] = "JLPT 权重",
        ["config.error.zeroWeights"] = "{0}不能全为 0。",
        ["quiz.title"] = "单词测验",
        ["quiz.check"] = "检查答案",
        ["quiz.return"] = "返回",
        ["quiz.score"] = "得分",
        ["quiz.book"] = "词库",
        ["quiz.correcting"] = "纠错模式",
        ["message.emptyNotebook"] = "没有错题，直接发放奖励药水。",
        ["message.potionReward"] = "获得了一瓶随机药水。",
        ["message.potionFull"] = "没有空药水栏位，但奖励判定已消耗。",
        ["message.rewardUsed"] = "本场战斗的纠错奖励已经发放。",
        ["book.CET4"] = "CET4",
        ["book.CET6"] = "CET6",
        ["book.N1"] = "N1",
        ["book.N2"] = "N2",
        ["book.N3"] = "N3",
        ["book.N4"] = "N4",
        ["relic.focus.disabled"] = "未启用",
        ["relic.status.disabled"] = " NL 当前没有启用任何词库，答题效果暂停。",
        ["event.days.today"] = "今天",
        ["event.days.single"] = "仅剩一天",
        ["event.days.multiple"] = "还有 {0} 天",
    };

    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["config.button"] = "Config",
        ["config.title"] = "CET46 Settings",
        ["config.save"] = "Save",
        ["config.close"] = "Close",
        ["config.reset"] = "Reset Defaults",
        ["config.darkMode"] = "Dark Mode",
        ["config.pureFont"] = "Pure Font",
        ["config.fastMode"] = "Fast Mode",
        ["config.autoCheck"] = "Auto Check Answers",
        ["config.casualMode"] = "Casual Mode",
        ["config.ignoreCheck"] = "Auto Return",
        ["config.showLexicon"] = "Show Book",
        ["config.loadCET"] = "Enable CET",
        ["config.loadJLPT"] = "Enable JLPT",
        ["config.maxAnsNum"] = "Max Correct Answers",
        ["config.weights.cet"] = "CET Weights",
        ["config.weights.jlpt"] = "JLPT Weights",
        ["config.error.zeroWeights"] = "{0} cannot all be zero.",
        ["quiz.title"] = "Word Quiz",
        ["quiz.check"] = "Check",
        ["quiz.return"] = "Return",
        ["quiz.score"] = "Score",
        ["quiz.book"] = "Book",
        ["quiz.correcting"] = "Correction",
        ["message.emptyNotebook"] = "No mistakes recorded. Awarding a random potion.",
        ["message.potionReward"] = "A random potion was awarded.",
        ["message.potionFull"] = "No potion slot was open, but the reward has been consumed.",
        ["message.rewardUsed"] = "The correction reward for this combat was already claimed.",
        ["book.CET4"] = "CET4",
        ["book.CET6"] = "CET6",
        ["book.N1"] = "N1",
        ["book.N2"] = "N2",
        ["book.N3"] = "N3",
        ["book.N4"] = "N4",
        ["relic.focus.disabled"] = "Disabled",
        ["relic.status.disabled"] = " NL No books are enabled, so this relic is inactive.",
        ["event.days.today"] = "today",
        ["event.days.single"] = "one day left",
        ["event.days.multiple"] = "{0} days left",
    };

    public static bool IsChinese
    {
        get
        {
            var language = LocManager.Instance?.Language;
            return string.Equals(language, "zhs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(language, "zht", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 读取模组内置文案。找不到时回退成 key，方便调试缺失文本。
    /// </summary>
    public static string Get(string key)
    {
        var dict = IsChinese ? Zh : En;
        return dict.TryGetValue(key, out var value) ? value : key;
    }

    public static string BookLabel(LexiconBookId book) => Get($"book.{book}");

    public static string DisabledBookLabel() => Get("relic.focus.disabled");

    public static string DisabledRelicStatusSuffix() => Get("relic.status.disabled");

    /// <summary>
    /// 把日期差值格式化成事件描述文本。
    /// </summary>
    public static string FormatDaysUntilCet(int daysUntil)
    {
        if (daysUntil <= 0)
        {
            return Get("event.days.today");
        }

        if (daysUntil == 1)
        {
            return Get("event.days.single");
        }

        return string.Format(Get("event.days.multiple"), daysUntil);
    }
}

/// <summary>
/// 纯字体模式下的字体资源缓存。
/// </summary>
public static class FontService
{
    private static FontFile? _font;

    /// <summary>
    /// 根据配置决定是否返回模组专用字体。
    /// </summary>
    public static Font? GetQuizFont()
    {
        if (!ConfigService.Current.PureFont)
        {
            return null;
        }

        _font ??= ResourceLoader.Load<FontFile>(ModPaths.FontAsset, null, ResourceLoader.CacheMode.Ignore);
        return _font;
    }

    /// <summary>
    /// 释放字体资源，避免退出时残留 Godot 资源。
    /// </summary>
    public static void Release()
    {
        _font?.Dispose();
        _font = null;
    }
}

/// <summary>
/// 把模组自己的文本表合并到 STS2 当前语言表中。
/// </summary>
public static class ModLocalizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private static readonly string[] Tables = ["events", "relics", "powers"];

    /// <summary>
    /// 根据当前语言选择 zhs/eng 资源并合并到活动词典。
    /// </summary>
    public static void MergeIntoActiveLanguage()
    {
        var manager = LocManager.Instance;
        if (manager == null)
        {
            return;
        }

        var language = string.Equals(manager.Language, "zhs", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(manager.Language, "zht", StringComparison.OrdinalIgnoreCase)
            ? "zhs"
            : "eng";
        foreach (var tableName in Tables)
        {
            var path = ModPaths.LocalizationTable(language, tableName);
            if (!Godot.FileAccess.FileExists(path))
            {
                continue;
            }

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            var merged = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText(), JsonOptions);
            if (merged == null || merged.Count == 0)
            {
                continue;
            }

            manager.GetTable(tableName).MergeWith(merged);
        }
    }
}

/// <summary>
/// 模组配置的读取、克隆、校验和落盘入口。
/// </summary>
public static class ConfigService
{
    public const string DataKey = "config";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
    };

    private static bool _storeRegistered;

    public static ModConfigData Current { get; private set; } = CreateDefault();

    private static ModDataStore Store => RitsuLibFramework.GetDataStore(Cet46Bootstrap.ModId);

    /// <summary>
    /// Registers the RitsuLib-backed config slot. The legacy user:// file is only used
    /// as the default source when the new managed store has no data yet.
    /// </summary>
    public static void RegisterDataStore()
    {
        if (_storeRegistered)
        {
            return;
        }

        using (RitsuLibFramework.BeginModDataRegistration(Cet46Bootstrap.ModId))
        {
            Store.Register(
                key: DataKey,
                fileName: "cet46_config.json",
                scope: SaveScope.Global,
                defaultFactory: CreateDefaultFromLegacyConfig,
                autoCreateIfMissing: true);
        }

        _storeRegistered = true;
    }

    /// <summary>
    /// 从磁盘读取配置；没有配置文件时自动创建默认值。
    /// </summary>
    public static void Load()
    {
        if (_storeRegistered)
        {
            Current = Store.Get<ModConfigData>(DataKey);
            EnsureDefaults(Current);
            Store.Save(DataKey);
            return;
        }

        try
        {
            var path = ModPaths.LegacyConfigPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(path))
            {
                Current = CreateDefault();
                Save();
                return;
            }

            Current = JsonSerializer.Deserialize<ModConfigData>(File.ReadAllText(path), JsonOptions) ?? CreateDefault();
            EnsureDefaults(Current);
        }
        catch
        {
            Current = CreateDefault();
        }
    }

    /// <summary>
    /// 将当前配置写回用户目录。
    /// </summary>
    public static void Save()
    {
        if (_storeRegistered)
        {
            EnsureDefaults(Current);
            Store.Save(DataKey);
            return;
        }

        var path = ModPaths.LegacyConfigPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(Current, JsonOptions));
    }

    /// <summary>
    /// 复制当前配置，供配置面板以“草稿模式”编辑。
    /// </summary>
    public static ModConfigData CloneCurrent()
    {
        return Clone(Current);
    }

    /// <summary>
    /// 生成一份默认配置副本，用于恢复默认值。
    /// </summary>
    public static ModConfigData CreateDefaultCopy()
    {
        return CreateDefault();
    }

    /// <summary>
    /// 先替换运行时配置，再由调用方决定是否保存到磁盘。
    /// </summary>
    public static void Apply(ModConfigData config)
    {
        if (_storeRegistered)
        {
            var next = Clone(config);
            Store.Modify<ModConfigData>(DataKey, data => CopyInto(data, next));
            Current = Store.Get<ModConfigData>(DataKey);
            EnsureDefaults(Current);
            return;
        }

        Current = Clone(config);
        EnsureDefaults(Current);
    }

    public static bool TryUpdate(Action<ModConfigData> update)
    {
        var draft = CloneCurrent();
        update(draft);
        if (!TryValidate(draft, out _, out _))
        {
            return false;
        }

        Apply(draft);
        Save();
        QuizRuntimeService.RefreshLiveRelics();
        return true;
    }

    public static void ResetToDefaults()
    {
        Apply(CreateDefault());
        Save();
        QuizRuntimeService.RefreshLiveRelics();
    }

    /// <summary>
    /// 只有启用中的词库组才要求至少存在一个非零权重。
    /// </summary>
    public static bool TryValidate(ModConfigData config, out string errorKey, out string errorSubjectKey)
    {
        EnsureDefaults(config);
        if (config.LoadCET && !HasNonZeroWeight(config, QuizRelicKind.CET))
        {
            errorKey = "config.error.zeroWeights";
            errorSubjectKey = "config.weights.cet";
            return false;
        }

        if (config.LoadJLPT && !HasNonZeroWeight(config, QuizRelicKind.JLPT))
        {
            errorKey = "config.error.zeroWeights";
            errorSubjectKey = "config.weights.jlpt";
            return false;
        }

        errorKey = string.Empty;
        errorSubjectKey = string.Empty;
        return true;
    }

    public static int GetWeight(QuizRelicKind kind, LexiconBookId book)
    {
        EnsureDefaults(Current);
        return Current.LexiconWeights.TryGetValue(Key(kind, book), out var weight) ? Math.Max(0, weight) : 0;
    }

    public static bool SupportsBook(QuizRelicKind kind, LexiconBookId book)
    {
        return GetAllBooks(kind).Contains(book);
    }

    public static bool IsKindEnabled(QuizRelicKind kind)
    {
        EnsureDefaults(Current);
        return kind switch
        {
            QuizRelicKind.CET => Current.LoadCET,
            QuizRelicKind.JLPT => Current.LoadJLPT,
            _ => false,
        };
    }

    public static bool IsBookEnabled(QuizRelicKind kind, LexiconBookId book)
    {
        return SupportsBook(kind, book) && IsKindEnabled(kind) && GetWeight(kind, book) > 0;
    }

    public static void SetWeight(QuizRelicKind kind, LexiconBookId book, int weight)
    {
        Current.LexiconWeights[Key(kind, book)] = Math.Max(0, weight);
    }

    public static List<LexiconBookId> GetEnabledBooks(QuizRelicKind kind)
    {
        EnsureDefaults(Current);
        if (!IsKindEnabled(kind))
        {
            return [];
        }

        return GetAllBooks(kind).Where(book => IsBookEnabled(kind, book)).ToList();
    }

    public static List<LexiconBookId> GetWeightedBooks(QuizRelicKind kind)
    {
        var result = new List<LexiconBookId>();
        foreach (var book in GetEnabledBooks(kind))
        {
            var weight = GetWeight(kind, book);
            for (var i = 0; i < Math.Max(1, weight); i++)
            {
                result.Add(book);
            }
        }

        return result;
    }

    public static bool HasNonZeroWeight(QuizRelicKind kind)
    {
        return HasNonZeroWeight(Current, kind);
    }

    public static LexiconBookId? GetDominantBook(QuizRelicKind kind)
    {
        return GetEnabledBooks(kind)
            .OrderByDescending(book => GetWeight(kind, book))
            .ThenBy(book => book.ToString(), StringComparer.Ordinal)
            .Cast<LexiconBookId?>()
            .FirstOrDefault();
    }

    private static string Key(QuizRelicKind kind, LexiconBookId book) => $"{kind}:{book}";

    private static bool HasNonZeroWeight(ModConfigData config, QuizRelicKind kind)
    {
        EnsureDefaults(config);
        return GetAllBooks(kind).Any(book =>
            config.LexiconWeights.TryGetValue(Key(kind, book), out var weight) && Math.Max(0, weight) > 0);
    }

    public static IReadOnlyList<LexiconBookId> GetAllBooks(QuizRelicKind kind)
    {
        return kind == QuizRelicKind.CET
            ? [LexiconBookId.CET4, LexiconBookId.CET6]
            : [LexiconBookId.N1, LexiconBookId.N2, LexiconBookId.N3, LexiconBookId.N4];
    }

    private static ModConfigData CreateDefault()
    {
        var config = new ModConfigData();
        EnsureDefaults(config);
        return config;
    }

    private static ModConfigData CreateDefaultFromLegacyConfig()
    {
        try
        {
            var path = ModPaths.LegacyConfigPath;
            if (!File.Exists(path))
            {
                return CreateDefault();
            }

            var config = JsonSerializer.Deserialize<ModConfigData>(File.ReadAllText(path), JsonOptions) ?? CreateDefault();
            EnsureDefaults(config);
            return config;
        }
        catch
        {
            return CreateDefault();
        }
    }

    private static ModConfigData Clone(ModConfigData source)
    {
        EnsureDefaults(source);
        return new ModConfigData
        {
            SchemaVersion = source.SchemaVersion,
            DarkMode = source.DarkMode,
            PureFont = source.PureFont,
            FastMode = source.FastMode,
            AutoCheckAnswer = source.AutoCheckAnswer,
            CasualMode = source.CasualMode,
            IgnoreCheck = source.IgnoreCheck,
            ShowLexicon = source.ShowLexicon,
            MaxAnsNum = source.MaxAnsNum,
            LoadCET = source.LoadCET,
            LoadJLPT = source.LoadJLPT,
            ExamScheduleVersion = source.ExamScheduleVersion,
            LexiconWeights = new Dictionary<string, int>(source.LexiconWeights, StringComparer.Ordinal),
        };
    }

    private static void CopyInto(ModConfigData target, ModConfigData source)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.DarkMode = source.DarkMode;
        target.PureFont = source.PureFont;
        target.FastMode = source.FastMode;
        target.AutoCheckAnswer = source.AutoCheckAnswer;
        target.CasualMode = source.CasualMode;
        target.IgnoreCheck = source.IgnoreCheck;
        target.ShowLexicon = source.ShowLexicon;
        target.MaxAnsNum = source.MaxAnsNum;
        target.LoadCET = source.LoadCET;
        target.LoadJLPT = source.LoadJLPT;
        target.ExamScheduleVersion = source.ExamScheduleVersion;
        target.LexiconWeights = new Dictionary<string, int>(source.LexiconWeights, StringComparer.Ordinal);
        EnsureDefaults(target);
    }

    private static void EnsureDefaults(ModConfigData config)
    {
        config.SchemaVersion = Math.Max(1, config.SchemaVersion);
        config.MaxAnsNum = Math.Clamp(config.MaxAnsNum, 1, 3);

        foreach (var pair in new (QuizRelicKind kind, LexiconBookId book)[]
        {
            (QuizRelicKind.CET, LexiconBookId.CET4),
            (QuizRelicKind.CET, LexiconBookId.CET6),
            (QuizRelicKind.JLPT, LexiconBookId.N1),
            (QuizRelicKind.JLPT, LexiconBookId.N2),
            (QuizRelicKind.JLPT, LexiconBookId.N3),
            (QuizRelicKind.JLPT, LexiconBookId.N4),
        })
        {
            if (!config.LexiconWeights.ContainsKey(Key(pair.kind, pair.book)))
            {
                config.LexiconWeights[Key(pair.kind, pair.book)] = 1;
            }
        }
    }
}

public static class ExamScheduleService
{
    private static readonly DateOnly[] CetDates =
    [
        new(2026, 6, 13),
        new(2026, 12, 12),
        new(2027, 6, 12),
        new(2027, 12, 11),
    ];

    public static (DateOnly? date, int daysUntil) GetNextCet(DateOnly today)
    {
        foreach (var date in CetDates)
        {
            if (date >= today)
            {
                return (date, date.DayNumber - today.DayNumber);
            }
        }

        return (null, 999);
    }
}
