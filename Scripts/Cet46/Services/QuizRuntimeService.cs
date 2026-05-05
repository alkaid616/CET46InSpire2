using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Models;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using ConfigOverlay = CET46InSpire2.Scripts.Cet46.UI.ConfigOverlay;
using QuizOverlay = CET46InSpire2.Scripts.Cet46.UI.QuizOverlay;

namespace CET46InSpire2.Scripts.Cet46.Services;

/// <summary>
/// 负责答题 UI、配置 UI、提示框和“恢复原始出牌”的运行时协调。
/// </summary>
public static class QuizRuntimeService
{
    private static QuizOverlay? _quizOverlay;
    private static ConfigOverlay? _configOverlay;
    private static AcceptDialog? _messageDialog;

    public static bool IsResumingCardPlay { get; private set; }
    public static bool HasOpenOverlay { get; private set; }
    public static PendingCardPlayContext? PendingCardPlay { get; private set; }

    /// <summary>
    /// 在开新局、读档或退出时清掉当前局的瞬时 UI 状态。
    /// </summary>
    public static void ResetRuntimeState()
    {
        IsResumingCardPlay = false;
        HasOpenOverlay = false;
        PendingCardPlay = null;
    }

    /// <summary>
    /// 在玩家手动打牌时决定是否先弹出测验界面。
    /// </summary>
    public static bool TryInterceptCardPlay(CardModel card, Creature? target)
    {
        if (HasOpenOverlay || !RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
        {
            return false;
        }

        var owner = card.Owner;
        if (owner == null || !LocalContext.IsMe(owner) || card.Type is CardType.Curse or CardType.Status)
        {
            return false;
        }

        var relic = owner.Relics.OfType<QuizRelicModel>().FirstOrDefault(model => model.IsFunctional);
        if (relic == null)
        {
            return false;
        }

        PendingCardPlay = new PendingCardPlayContext
        {
            Owner = owner,
            Card = card,
            Target = target,
        };

        var prompt = LexiconService.BuildPrompt(relic, GetRunRng());
        EnsureQuizOverlay().Open(prompt, result =>
        {
            try
            {
                relic.ApplyQuizResult(prompt, result, card);
                ResumePendingCardPlay();
            }
            finally
            {
                HasOpenOverlay = false;
            }
        });

        HasOpenOverlay = true;
        return true;
    }

    /// <summary>
    /// 从错题本中抽题并打开纠错界面。
    /// </summary>
    public static void OpenCorrectionQuiz(QuizRelicModel relic)
    {
        if (HasOpenOverlay || !RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
        {
            return;
        }

        var entryId = relic.PickCorrectionEntryId();
        if (string.IsNullOrEmpty(entryId))
        {
            relic.TryGrantPotionReward(LocalizationService.Get("message.emptyNotebook"));
            return;
        }

        var prompt = LexiconService.BuildPrompt(relic, GetRunRng(), entryId, correctionMode: true);
        EnsureQuizOverlay().Open(prompt, result =>
        {
            try
            {
                relic.ApplyCorrectionResult(prompt, result);
            }
            finally
            {
                HasOpenOverlay = false;
            }
        });

        HasOpenOverlay = true;
    }

    /// <summary>
    /// 打开配置面板。
    /// </summary>
    public static void OpenConfig()
    {
        EnsureConfigOverlay().Open();
    }

    /// <summary>
    /// 开发命令使用的简化开题入口。
    /// </summary>
    public static bool OpenDebugQuiz(QuizPromptData prompt)
    {
        if (HasOpenOverlay)
        {
            return false;
        }

        HasOpenOverlay = true;
        EnsureQuizOverlay().Open(prompt, _ =>
        {
            HasOpenOverlay = false;
        });
        return true;
    }

    /// <summary>
    /// 统一弹出短消息提示。
    /// </summary>
    public static void ShowMessage(string text)
    {
        var dialog = EnsureMessageDialog();
        dialog.DialogText = text;
        if (dialog.GetParent() == null)
        {
            GetRoot().AddChild(dialog);
        }
        dialog.PopupCentered(new Vector2I(520, 220));
    }

    /// <summary>
    /// 销毁模组动态创建的所有 UI 节点。
    /// </summary>
    public static void DisposeRuntimeUi()
    {
        ResetRuntimeState();
        FreeNode(ref _messageDialog);
        FreeNode(ref _quizOverlay);
        FreeNode(ref _configOverlay);
    }

    /// <summary>
    /// UI 自己销毁时同步清空缓存引用。
    /// </summary>
    public static void DetachQuizOverlay(QuizOverlay overlay)
    {
        if (_quizOverlay != null && GodotObject.IsInstanceValid(_quizOverlay) && ReferenceEquals(_quizOverlay, overlay))
        {
            _quizOverlay = null;
        }
    }

    /// <summary>
    /// 配置面板销毁时同步清空缓存引用。
    /// </summary>
    public static void DetachConfigOverlay(ConfigOverlay overlay)
    {
        if (_configOverlay != null && GodotObject.IsInstanceValid(_configOverlay) && ReferenceEquals(_configOverlay, overlay))
        {
            _configOverlay = null;
        }
    }

    /// <summary>
    /// 配置保存后刷新本地玩家现有遗物的描述、图标和可用性。
    /// </summary>
    public static void RefreshLiveRelics()
    {
        var player = GetLocalPlayer();
        if (player == null)
        {
            return;
        }

        foreach (var relic in player.Relics.OfType<QuizRelicModel>())
        {
            relic.RefreshFromConfig();
        }

        foreach (var node in EnumerateTree(GetRoot()))
        {
            if (node is NRelicInventoryHolder holder && holder.Relic?.Model is QuizRelicModel)
            {
                holder.Relic.Call("Reload");
            }
        }
    }

    /// <summary>
    /// 测验完成后继续执行之前被拦截的那次手动出牌。
    /// </summary>
    private static void ResumePendingCardPlay()
    {
        var pendingCardPlay = PendingCardPlay;
        if (pendingCardPlay == null)
        {
            return;
        }

        try
        {
            IsResumingCardPlay = true;
            pendingCardPlay.Card.TryManualPlay(pendingCardPlay.Target);
        }
        finally
        {
            IsResumingCardPlay = false;
            PendingCardPlay = null;
        }
    }

    /// <summary>
    /// 懒加载答题面板。
    /// </summary>
    private static QuizOverlay EnsureQuizOverlay()
    {
        if (_quizOverlay != null && GodotObject.IsInstanceValid(_quizOverlay))
        {
            return _quizOverlay;
        }

        _quizOverlay = new QuizOverlay();
        GetRoot().AddChild(_quizOverlay);
        return _quizOverlay;
    }

    /// <summary>
    /// 懒加载配置面板。
    /// </summary>
    private static ConfigOverlay EnsureConfigOverlay()
    {
        if (_configOverlay != null && GodotObject.IsInstanceValid(_configOverlay))
        {
            return _configOverlay;
        }

        _configOverlay = new ConfigOverlay();
        GetRoot().AddChild(_configOverlay);
        return _configOverlay;
    }

    /// <summary>
    /// 懒加载通用消息框。
    /// </summary>
    private static AcceptDialog EnsureMessageDialog()
    {
        if (_messageDialog != null && GodotObject.IsInstanceValid(_messageDialog))
        {
            return _messageDialog;
        }

        _messageDialog = new AcceptDialog
        {
            Title = "CET46",
            Exclusive = true,
            Unresizable = true,
        };
        _messageDialog.Canceled += HideMessageDialog;
        _messageDialog.Confirmed += HideMessageDialog;
        return _messageDialog;
    }

    /// <summary>
    /// 取得 Godot 根节点，供动态 UI 挂载使用。
    /// </summary>
    private static Node GetRoot()
    {
        return ((SceneTree)Engine.GetMainLoop()).Root;
    }

    /// <summary>
    /// 单人局优先使用 run RNG；无 run 时回退到混沌 RNG。
    /// </summary>
    private static Rng GetRunRng()
    {
        var state = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
        return state?.Rng.UpFront ?? Rng.Chaotic;
    }

    /// <summary>
    /// 获取本地玩家；假多人场景下也保证能拿到“我”的玩家对象。
    /// </summary>
    private static Player? GetLocalPlayer()
    {
        var state = RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
        return state == null ? null : LocalContext.GetMe(state.Players) ?? state.Players.FirstOrDefault();
    }

    /// <summary>
    /// 深度遍历节点树，用于刷新已有遗物节点。
    /// </summary>
    private static IEnumerable<Node> EnumerateTree(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren())
        {
            if (child is Node childNode)
            {
                foreach (var descendant in EnumerateTree(childNode))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// 隐藏消息框但不销毁，以便复用节点。
    /// </summary>
    private static void HideMessageDialog()
    {
        if (_messageDialog != null && GodotObject.IsInstanceValid(_messageDialog))
        {
            _messageDialog.Hide();
        }
    }

    /// <summary>
    /// 安全释放动态创建的节点。
    /// </summary>
    private static void FreeNode<T>(ref T? node) where T : Node
    {
        if (node == null || !GodotObject.IsInstanceValid(node))
        {
            node = null;
            return;
        }

        node.QueueFreeSafely();
        node = null;
    }
}
