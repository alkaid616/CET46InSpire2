using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Services;
using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace CET46InSpire2.Scripts.Cet46.UI;

/// <summary>
/// 战斗中的答题覆盖层，同时支持普通答题和纠错答题。
/// </summary>
public sealed partial class QuizOverlay : CanvasLayer
{
    private readonly Dictionary<string, CheckBox> _choiceButtons = new(StringComparer.Ordinal);
    private readonly VBoxContainer _content = new();
    private readonly Label _bookLabel = new();
    private readonly Label _titleLabel = new();
    private readonly Label _scoreLabel = new();
    private readonly VBoxContainer _choices = new();
    private readonly Button _checkButton = new();
    private readonly Button _returnButton = new();
    private readonly ColorRect _scrim = new();
    private readonly PanelContainer _panel = new();
    private Action<QuizResult>? _callback;
    private QuizPromptData? _prompt;
    private int _sessionId;

    /// <summary>
    /// 构建答题窗口基础布局。
    /// </summary>
    public override void _Ready()
    {
        Layer = 100;
        Visible = false;

        _scrim.Color = new Color(0, 0, 0, 0.55f);
        _scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_scrim);

        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.CustomMinimumSize = new Vector2(760, 560);
        _panel.Position -= _panel.CustomMinimumSize / 2f;
        AddChild(_panel);

        _content.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(_content);
        _panel.AddThemeConstantOverride("margin_left", 24);
        _panel.AddThemeConstantOverride("margin_right", 24);
        _panel.AddThemeConstantOverride("margin_top", 24);
        _panel.AddThemeConstantOverride("margin_bottom", 24);

        _titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _titleLabel.AddThemeFontSizeOverride("font_size", 28);

        _bookLabel.AddThemeColorOverride("font_color", new Color("#ecd37d"));
        _scoreLabel.AddThemeColorOverride("font_color", new Color("#9de18b"));

        _content.AddChild(_bookLabel);
        _content.AddChild(_titleLabel);
        _content.AddChild(_scoreLabel);
        _content.AddChild(_choices);

        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
        };
        _content.AddChild(actions);

        _checkButton.Text = LocalizationService.Get("quiz.check");
        _checkButton.Pressed += OnCheckPressed;
        actions.AddChild(_checkButton);

        _returnButton.Text = LocalizationService.Get("quiz.return");
        _returnButton.Visible = false;
        _returnButton.Pressed += CloseWithCurrentSelection;
        actions.AddChild(_returnButton);
    }

    /// <summary>
    /// 用新的题面数据刷新界面并显示出来。
    /// </summary>
    public void Open(QuizPromptData prompt, Action<QuizResult> callback)
    {
        _sessionId += 1;
        _prompt = prompt;
        _callback = callback;
        _scoreLabel.Text = string.Empty;
        _returnButton.Visible = false;
        _checkButton.Visible = true;
        _choiceButtons.Clear();

        _bookLabel.Text = ConfigService.Current.ShowLexicon
            ? prompt.IsCorrectionMode
                ? LocalizationService.Get("quiz.correcting")
                : $"{LocalizationService.Get("quiz.book")}: {LocalizationService.BookLabel(prompt.Book)}"
            : string.Empty;
        _titleLabel.Text = prompt.PromptText;

        _panel.Modulate = ConfigService.Current.DarkMode ? new Color("#20252b") : new Color("#f0e7d0");
        _titleLabel.AddThemeColorOverride("font_color", Colors.White);
        ApplyFontOverrides();

        ClearChoices();

        foreach (var option in prompt.Options)
        {
            var check = new CheckBox
            {
                Text = option,
                FocusMode = Control.FocusModeEnum.All,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            check.Toggled += _ => TryAutoCheck();
            _choices.AddChild(check);
            _choiceButtons[option] = check;
        }

        Visible = true;
        GrabFirstFocusable();
    }

    /// <summary>
    /// 键盘和手柄输入：确认判题，取消返回。
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible)
        {
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            if (_returnButton.Visible)
            {
                CloseWithCurrentSelection();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (@event.IsActionPressed("ui_accept"))
        {
            if (_returnButton.Visible)
            {
                CloseWithCurrentSelection();
            }
            else
            {
                OnCheckPressed();
            }

            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// 节点退出时通知运行时层解绑。
    /// </summary>
    public override void _ExitTree()
    {
        ResetState();
        QuizRuntimeService.DetachQuizOverlay(this);
    }

    /// <summary>
    /// 打开时默认把焦点放到第一个选项上。
    /// </summary>
    private void GrabFirstFocusable()
    {
        if (_choices.GetChildCount() > 0 && _choices.GetChild(0) is Control control)
        {
            control.GrabFocus();
        }
    }

    /// <summary>
    /// 判题并进入结果展示态。
    /// </summary>
    private void OnCheckPressed()
    {
        if (_prompt == null)
        {
            return;
        }

        var result = BuildResult();
        var correct = _prompt.CorrectOptions.ToHashSet(StringComparer.Ordinal);
        foreach (var pair in _choiceButtons)
        {
            var isSelected = result.SelectedOptions.Contains(pair.Key);
            var isCorrect = correct.Contains(pair.Key);
            pair.Value.Modulate = isSelected && isCorrect
                ? new Color("#8ce17a")
                : isSelected && !isCorrect
                    ? new Color("#e57f73")
                    : !isSelected && isCorrect
                        ? new Color("#ddc95f")
                        : Colors.White;
            pair.Value.Disabled = true;
        }

        _scoreLabel.Text = $"{LocalizationService.Get("quiz.score")}: {result.Score}";
        _checkButton.Visible = false;
        _returnButton.Visible = !ConfigService.Current.IgnoreCheck;

        // if (ConfigService.Current.IgnoreCheck)
        // {
        //     CloseWithCurrentSelection();
        //     return;
        // }

        if (ConfigService.Current.FastMode)
        {
            var sessionId = _sessionId;
            TaskHelper.RunSafely(CloseAfterFastModeDelayAsync(result, sessionId));
        }
    }

    /// <summary>
    /// 自动检查开启时，选满与正确项数量相同的答案后立即判题。
    /// </summary>
    private void TryAutoCheck()
    {
        if (!ConfigService.Current.AutoCheckAnswer || _prompt == null || !_checkButton.Visible)
        {
            return;
        }

        var selectedCount = _choiceButtons.Values.Count(button => button.ButtonPressed);
        if (selectedCount != _prompt.CorrectOptions.Count)
        {
            return;
        }

        OnCheckPressed();
    }

    /// <summary>
    /// 从当前勾选状态生成一个结果并关闭。
    /// </summary>
    private void CloseWithCurrentSelection()
    {
        Close(BuildResult());
    }

    /// <summary>
    /// 根据正确项和勾选项计算得分与完美状态。
    /// </summary>
    private QuizResult BuildResult()
    {
        var prompt = _prompt ?? throw new InvalidOperationException("Prompt not initialized.");
        var selected = _choiceButtons.Where(pair => pair.Value.ButtonPressed).Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var correct = prompt.CorrectOptions.ToHashSet(StringComparer.Ordinal);
        var score = correct.Count(item => selected.Contains(item)) - selected.Count(item => !correct.Contains(item));
        score = Math.Max(ConfigService.Current.CasualMode ? 1 : 0, score);
        return new QuizResult
        {
            SelectedOptions = selected,
            Score = score,
            Perfect = selected.SetEquals(correct),
        };
    }

    /// <summary>
    /// 关闭答题界面并把结果回传给调用方。
    /// </summary>
    private void Close(QuizResult result)
    {
        _sessionId += 1;
        Visible = false;
        var callback = _callback;
        _callback = null;
        _prompt = null;
        ResetState();
        callback?.Invoke(result);
    }

    /// <summary>
    /// 纯字体模式下递归应用字体覆盖。
    /// </summary>
    private void ApplyFontOverrides()
    {
        ApplyFontRecursive(_content, FontService.GetQuizFont());
    }

    /// <summary>
    /// 快速模式下在短延时后自动退出结果页。
    /// </summary>
    private async Task CloseAfterFastModeDelayAsync(QuizResult result, int sessionId)
    {
        var tree = GetTree();
        if (tree == null)
        {
            return;
        }

        await ToSignal(tree.CreateTimer(0.18), SceneTreeTimer.SignalName.Timeout);
        if (!GodotObject.IsInstanceValid(this) || !Visible || sessionId != _sessionId || _prompt == null ||
            _checkButton.Visible)
        {
            return;
        }

        Close(result);
    }

    /// <summary>
    /// 清空旧选择和旧控件状态。
    /// </summary>
    private void ResetState()
    {
        Visible = false;
        _callback = null;
        _prompt = null;
        _choiceButtons.Clear();
        ClearChoices();
        ApplyFontRecursive(_content, null);
        _titleLabel.RemoveThemeColorOverride("font_color");
        _checkButton.Visible = true;
        _returnButton.Visible = false;
        _scoreLabel.Text = string.Empty;
    }

    /// <summary>
    /// 移除所有旧选项控件。
    /// </summary>
    private void ClearChoices()
    {
        foreach (var child in _choices.GetChildren())
        {
            if (child is Node node)
            {
                _choices.RemoveChild(node);
                node.Free();
            }
        }
    }

    /// <summary>
    /// 递归设置或移除字体覆盖。
    /// </summary>
    private static void ApplyFontRecursive(Node node, Font? font)
    {
        if (node is Control control)
        {
            if (font == null)
            {
                control.RemoveThemeFontOverride("font");
            }
            else
            {
                control.AddThemeFontOverride("font", font);
            }
        }

        foreach (var child in node.GetChildren())
        {
            ApplyFontRecursive(child, font);
        }
    }
}