using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Models;
using CET46InSpire2.Scripts.Cet46.Services;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Patching.Core;
using STS2RitsuLib.Patching.Models;

namespace CET46InSpire2.Scripts.Cet46.Patches;

public sealed class Cet46RitsuPatchSet : IModPatches
{
    public static void AddTo(ModPatcher patcher)
    {
        patcher.RegisterPatch<GameQuitRitsuPatch>();
        patcher.RegisterPatch<LocManagerRitsuPatch>();
        patcher.RegisterPatch<NeowCleanupRitsuPatch>();
        patcher.RegisterPatch<TerminalRewardsProceedRitsuPatch>();
        patcher.RegisterPatch<CardManualPlayRitsuPatch>();
        patcher.RegisterPatch<CardEnqueueManualPlayRitsuPatch>();
        patcher.RegisterPatch<PreparedCompatibilityRitsuPatch>();
        patcher.RegisterPatch<TheBombCompatibilityRitsuPatch>();
        patcher.RegisterPatch<RelicRightClickRitsuPatch>();
    }
}

public sealed class GameQuitRitsuPatch : IPatchMethod
{
    public static string PatchId => "game_quit_cleanup";
    public static string Description => "Dispose CET46 transient UI when the game quits.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NGame), nameof(NGame.Quit), ignoreIfMissing: true)];

    public static void Prefix()
    {
        RuntimeState.DisposeTransientUi();
    }
}

public sealed class LocManagerRitsuPatch : IPatchMethod
{
    public static string PatchId => "language_table_merge";
    public static string Description => "Merge CET46 model localization after language changes.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(LocManager), nameof(LocManager.SetLanguage), ignoreIfMissing: true)];

    public static void Postfix()
    {
        ModLocalizationService.MergeIntoActiveLanguage();
    }
}

public sealed class NeowCleanupRitsuPatch : IPatchMethod
{
    public static string PatchId => "neow_cet_event_injection";
    public static string Description => "Insert the CET46 event after Neow cleanup on floor 0.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(EventModel), nameof(EventModel.EnsureCleanup))];

    public static void Postfix(EventModel __instance)
    {
        if (__instance is not Neow)
        {
            return;
        }

        if (!RunManager.Instance.IsSinglePlayerOrFakeMultiplayer || RuntimeState.NeowEventInjected)
        {
            return;
        }

        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null || state.TotalFloor != 0)
        {
            return;
        }

        if (!HasEnabledBooks())
        {
            return;
        }

        RuntimeState.NeowEventInjected = true;
        TaskHelper.RunSafely(RunManager.Instance.EnterRoomWithoutExitingCurrentRoom(new EventRoom(ModelDb.Event<CallOfCetEvent>()), false));
    }

    private static bool HasEnabledBooks()
    {
        return ConfigService.GetEnabledBooks(QuizRelicKind.CET).Count > 0 ||
               ConfigService.GetEnabledBooks(QuizRelicKind.JLPT).Count > 0;
    }
}

public sealed class TerminalRewardsProceedRitsuPatch : IPatchMethod
{
    public static string PatchId => "terminal_rewards_cet_event_injection";
    public static string Description => "Insert the CET46 event after boss or victory terminal rewards.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(RunManager), nameof(RunManager.ProceedFromTerminalRewardsScreen))];

    public static void Prefix()
    {
        var state = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
        if (ShouldInjectAfterTerminalRewards(state))
        {
            RuntimeState.MarkPendingPostTerminalCetInjection(state!.TotalFloor);
            return;
        }

        RuntimeState.ClearPendingPostTerminalCetInjection();
    }

    public static void Postfix(ref Task __result)
    {
        if (RuntimeState.PendingPostTerminalCetInjection)
        {
            __result = InjectAfterProceedAsync(__result);
        }
    }

    private static async Task InjectAfterProceedAsync(Task originalTask)
    {
        try
        {
            await originalTask;
            if (!RuntimeState.PendingPostTerminalCetInjection)
            {
                return;
            }

            var state = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
            if (state == null ||
                !RunManager.Instance.IsSinglePlayerOrFakeMultiplayer ||
                state.TotalFloor != RuntimeState.PendingPostTerminalCetSourceFloor ||
                !HasEnabledBooks())
            {
                return;
            }

            RuntimeState.ClearPendingPostTerminalCetInjection();
            await RunManager.Instance.EnterRoomWithoutExitingCurrentRoom(new EventRoom(ModelDb.Event<CallOfCetEvent>()), false);
        }
        finally
        {
            RuntimeState.ClearPendingPostTerminalCetInjection();
        }
    }

    private static bool ShouldInjectAfterTerminalRewards(RunState? state)
    {
        if (state == null ||
            !RunManager.Instance.IsSinglePlayerOrFakeMultiplayer ||
            !HasEnabledBooks())
        {
            return false;
        }

        if (!IsBossOrVictoryTerminalRoom(state.CurrentRoom) && !IsBossOrVictoryTerminalRoom(state.BaseRoom))
        {
            return false;
        }

        return state.Map.SecondBossMapPoint == null || state.CurrentMapCoord != state.Map.BossMapPoint.coord;
    }

    private static bool IsBossOrVictoryTerminalRoom(AbstractRoom? room)
    {
        return room != null && (room.RoomType == RoomType.Boss || room.IsVictoryRoom);
    }

    private static bool HasEnabledBooks()
    {
        return ConfigService.GetEnabledBooks(QuizRelicKind.CET).Count > 0 ||
               ConfigService.GetEnabledBooks(QuizRelicKind.JLPT).Count > 0;
    }
}

public sealed class CardManualPlayRitsuPatch : IPatchMethod
{
    public static string PatchId => "manual_card_play_quiz_gate";
    public static string Description => "Open the CET46 quiz before manually playing a card.";
    public static bool IsCritical => true;
    public static ModPatchTarget[] GetTargets() => [new(typeof(CardModel), nameof(CardModel.TryManualPlay))];

    public static bool Prefix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (QuizRuntimeService.IsResumingCardPlay)
        {
            return true;
        }

        if (!QuizRuntimeService.TryInterceptCardPlay(__instance, target))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

public sealed class CardEnqueueManualPlayRitsuPatch : IPatchMethod
{
    public static string PatchId => "enqueue_manual_card_play_quiz_gate";
    public static string Description => "Open the CET46 quiz before the queued manual card play path.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(CardModel), "EnqueueManualPlay", ignoreIfMissing: true)];

    public static bool Prefix(CardModel __instance, Creature? target)
    {
        if (QuizRuntimeService.IsResumingCardPlay)
        {
            return true;
        }

        return !QuizRuntimeService.TryInterceptCardPlay(__instance, target);
    }
}

public sealed class PreparedCompatibilityRitsuPatch : IPatchMethod
{
    public static string PatchId => "prepared_score_compatibility";
    public static string Description => "Apply CET46 score scaling to Prepared's custom play flow.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(Prepared), "OnPlay", ignoreIfMissing: true)];

    public static bool Prefix(Prepared __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        if (!CardCompatibilityService.TryGetContext(__instance, out var context))
        {
            return true;
        }

        __result = CardCompatibilityService.ExecutePreparedAsync(__instance, choiceContext, context.Score);
        return false;
    }
}

public sealed class TheBombCompatibilityRitsuPatch : IPatchMethod
{
    public static string PatchId => "the_bomb_score_compatibility";
    public static string Description => "Apply CET46 score scaling to The Bomb's custom play flow.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(TheBomb), "OnPlay", ignoreIfMissing: true)];

    public static bool Prefix(TheBomb __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        if (!CardCompatibilityService.TryGetContext(__instance, out var context))
        {
            return true;
        }

        __result = CardCompatibilityService.ExecuteTheBombAsync(__instance, choiceContext, context.Score);
        return false;
    }
}

public sealed class RelicRightClickRitsuPatch : IPatchMethod
{
    public static string PatchId => "relic_right_click_correction_quiz";
    public static string Description => "Open CET46 correction quiz from right-clicking a quiz relic in combat.";
    public static bool IsCritical => false;
    public static ModPatchTarget[] GetTargets() => [new(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder._Ready), ignoreIfMissing: true)];

    public static void Postfix(NRelicInventoryHolder __instance)
    {
        __instance.Connect(NClickableControl.SignalName.MouseReleased, Callable.From<InputEvent>(inputEvent =>
        {
            if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Right } mouseButton || mouseButton.IsPressed())
            {
                return;
            }

            if (__instance.Relic?.Model is not QuizRelicModel quizRelic)
            {
                return;
            }

            if (quizRelic.Owner == null || !LocalContext.IsMe(quizRelic.Owner) || quizRelic.Owner.Creature.CombatState == null)
            {
                return;
            }

            QuizRuntimeService.OpenCorrectionQuiz(quizRelic);
        }));
    }
}
