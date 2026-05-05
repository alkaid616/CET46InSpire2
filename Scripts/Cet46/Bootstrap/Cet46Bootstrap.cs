using System.Reflection;
using CET46InSpire2.Scripts.Cet46.Models;
using CET46InSpire2.Scripts.Cet46.Patches;
using CET46InSpire2.Scripts.Cet46.Services;
using MegaCrit.Sts2.Core.Logging;
using STS2RitsuLib;
using STS2RitsuLib.Content;
using STS2RitsuLib.Patching.Core;

namespace CET46InSpire2.Scripts.Cet46.Bootstrap;

/// <summary>
/// 集中处理模组启动阶段需要加载的一次性资源和服务。
/// </summary>
public static class Cet46Bootstrap
{
    public const string ModId = "CET46InSpire2";
    public const string HarmonyId = ModId;
    public const string PckName = "CET46InSpire2";
    public const string DisplayName = "CET46 In Spire 2";

    public static Logger Logger { get; private set; } = null!;

    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// 在程序集初始化后运行一次，加载配置、词库、本地化并建立生命周期清理钩子。
    /// </summary>
    public static void Initialize(Assembly assembly)
    {
        if (IsInitialized)
        {
            return;
        }

        Logger = RitsuLibFramework.CreateLogger(ModId);
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);

        RegisterContent();
        RegisterPatches();
        RegisterLifecycle();

        ConfigService.RegisterDataStore();
        ConfigService.Load();
        LexiconService.LoadAll();
        ModLocalizationService.MergeIntoActiveLanguage();
        Cet46SettingsRegistration.Register();
        RuntimeState.ResetForRun();

        IsInitialized = true;
        Log.Info("CET46 bootstrap completed", 2);
    }

    private static void RegisterContent()
    {
        RitsuLibFramework.CreateContentPack(ModId)
            .Relic<Cet46EventRelicPool, CetQuizRelicModel>(ModelPublicEntryOptions.FromFullPublicEntry("CET_QUIZ_RELIC_MODEL"))
            .Relic<Cet46EventRelicPool, JlptQuizRelicModel>(ModelPublicEntryOptions.FromFullPublicEntry("JLPT_QUIZ_RELIC_MODEL"))
            .Power<PerfectAnsPowerModel>()
            .SharedEvent<CallOfCetEvent>()
            .Apply();
    }

    private static void RegisterPatches()
    {
        var patcher = RitsuLibFramework.CreatePatcher(ModId, "cet46-core", "CET46 core patches");
        patcher.RegisterPatches<Cet46RitsuPatchSet>();

        if (!patcher.PatchAll())
        {
            throw new InvalidOperationException($"{DisplayName} failed to apply required patches.");
        }
    }

    private static void RegisterLifecycle()
    {
        RitsuLibFramework.SubscribeLifecycle<GameTreeEnteredEvent>(_ => RuntimeState.InstallLifetimeHooks());
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(_ => RuntimeState.ResetForRun());
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(_ => RuntimeState.ResetForRun());
        RitsuLibFramework.SubscribeLifecycle<RunEndedEvent>(_ => RuntimeState.DisposeTransientUi());
    }
}
