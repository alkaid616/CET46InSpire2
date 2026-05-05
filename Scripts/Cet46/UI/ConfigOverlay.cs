using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Services;
using Godot;

namespace CET46InSpire2.Scripts.Cet46.UI;

/// <summary>
/// 模组配置面板。采用草稿编辑模式，只有点击保存才会写回运行时配置。
/// </summary>
public sealed partial class ConfigOverlay : CanvasLayer
{
    private readonly VBoxContainer _content = new();
    private readonly ColorRect _scrim = new();
    private readonly PanelContainer _panel = new();
    private readonly ScrollContainer _scroll = new();
    private ModConfigData? _draft;

    /// <summary>
    /// 创建覆盖全屏的配置面板骨架。
    /// </summary>
    public override void _Ready()
    {
        Layer = 100;
        Visible = false;

        _scrim.Color = new Color(0, 0, 0, 0.45f);
        _scrim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_scrim);

        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.CustomMinimumSize = new Vector2(760, 640);
        _panel.Position -= _panel.CustomMinimumSize / 2f;
        _panel.AddThemeConstantOverride("margin_left", 20);
        _panel.AddThemeConstantOverride("margin_right", 20);
        _panel.AddThemeConstantOverride("margin_top", 20);
        _panel.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(_panel);

        _content.AddThemeConstantOverride("separation", 10);
        _scroll.CustomMinimumSize = new Vector2(700, 600);
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _panel.AddChild(_scroll);
        _scroll.AddChild(_content);
    }

    /// <summary>
    /// 打开面板时复制当前配置，避免“未保存也修改运行态”。
    /// </summary>
    public void Open()
    {
        _draft = ConfigService.CloneCurrent();
        Build();
        Visible = true;
    }

    /// <summary>
    /// ESC 直接关闭并丢弃未保存草稿。
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible)
        {
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            CloseOverlay();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// 节点销毁时清空内容和运行时引用。
    /// </summary>
    public override void _ExitTree()
    {
        Visible = false;
        ClearContent();
        QuizRuntimeService.DetachConfigOverlay(this);
    }

    /// <summary>
    /// 根据当前草稿重建整张配置面板。
    /// </summary>
    private void Build()
    {
        _draft ??= ConfigService.CloneCurrent();
        ClearContent();

        var title = new Label
        {
            Text = LocalizationService.Get("config.title"),
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        _content.AddChild(title);

        AddToggle(LocalizationService.Get("config.darkMode"), value => _draft.DarkMode = value, _draft.DarkMode);
        AddToggle(LocalizationService.Get("config.pureFont"), value => _draft.PureFont = value, _draft.PureFont);
        AddToggle(LocalizationService.Get("config.fastMode"), value => _draft.FastMode = value, _draft.FastMode);
        AddToggle(LocalizationService.Get("config.autoCheck"), value => _draft.AutoCheckAnswer = value, _draft.AutoCheckAnswer);
        AddToggle(LocalizationService.Get("config.casualMode"), value => _draft.CasualMode = value, _draft.CasualMode);
        AddToggle(LocalizationService.Get("config.ignoreCheck"), value => _draft.IgnoreCheck = value, _draft.IgnoreCheck);
        AddToggle(LocalizationService.Get("config.showLexicon"), value => _draft.ShowLexicon = value, _draft.ShowLexicon);
        AddToggle(LocalizationService.Get("config.loadCET"), value => _draft.LoadCET = value, _draft.LoadCET);
        AddToggle(LocalizationService.Get("config.loadJLPT"), value => _draft.LoadJLPT = value, _draft.LoadJLPT);

        AddSlider(LocalizationService.Get("config.maxAnsNum"), _draft.MaxAnsNum, 1, 3, value => _draft.MaxAnsNum = (int)value);

        AddWeightGroup(QuizRelicKind.CET, [LexiconBookId.CET4, LexiconBookId.CET6]);
        AddWeightGroup(QuizRelicKind.JLPT, [LexiconBookId.N1, LexiconBookId.N2, LexiconBookId.N3, LexiconBookId.N4]);

        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
        };
        _content.AddChild(actions);

        var save = new Button
        {
            Text = LocalizationService.Get("config.save"),
        };
        save.Pressed += () =>
        {
            if (_draft == null)
            {
                return;
            }

            if (!ConfigService.TryValidate(_draft, out var errorKey, out var errorSubjectKey))
            {
                QuizRuntimeService.ShowMessage(string.Format(LocalizationService.Get(errorKey), LocalizationService.Get(errorSubjectKey)));
                return;
            }

            ConfigService.Apply(_draft);
            ConfigService.Save();
            QuizRuntimeService.RefreshLiveRelics();
            CloseOverlay();
        };
        actions.AddChild(save);

        var reset = new Button
        {
            Text = LocalizationService.Get("config.reset"),
        };
        reset.Pressed += () =>
        {
            _draft = ConfigService.CreateDefaultCopy();
            Build();
        };
        actions.AddChild(reset);

        var close = new Button
        {
            Text = LocalizationService.Get("config.close"),
        };
        close.Pressed += CloseOverlay;
        actions.AddChild(close);

        ApplyFontOverrides();
    }

    /// <summary>
    /// 添加布尔配置项。
    /// </summary>
    private void AddToggle(string labelText, Action<bool> setter, bool current)
    {
        var toggle = new CheckBox
        {
            Text = labelText,
            ButtonPressed = current,
        };
        toggle.Toggled += value => setter(value);
        _content.AddChild(toggle);
    }

    /// <summary>
    /// 添加数值滑条配置项。
    /// </summary>
    private void AddSlider(string labelText, double current, double min, double max, Action<double> setter)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = labelText, CustomMinimumSize = new Vector2(180, 0) });
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = 1,
            Value = current,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        var valueLabel = new Label { Text = ((int)current).ToString(), CustomMinimumSize = new Vector2(32, 0) };
        slider.ValueChanged += value =>
        {
            setter(value);
            valueLabel.Text = ((int)value).ToString();
        };
        row.AddChild(slider);
        row.AddChild(valueLabel);
        _content.AddChild(row);
    }

    /// <summary>
    /// 添加单个词库组的权重滑条列表。
    /// </summary>
    private void AddWeightGroup(QuizRelicKind kind, IReadOnlyList<LexiconBookId> books)
    {
        var groupTitle = new Label
        {
            Text = kind == QuizRelicKind.CET
                ? LocalizationService.Get("config.weights.cet")
                : LocalizationService.Get("config.weights.jlpt"),
        };
        groupTitle.AddThemeFontSizeOverride("font_size", 20);
        _content.AddChild(groupTitle);

        foreach (var book in books)
        {
            AddSlider(LocalizationService.BookLabel(book), GetDraftWeight(kind, book), 0, 10, value =>
            {
                SetDraftWeight(kind, book, (int)value);
            });
        }
    }

    /// <summary>
    /// 读取草稿里的权重。
    /// </summary>
    private int GetDraftWeight(QuizRelicKind kind, LexiconBookId book)
    {
        if (_draft == null)
        {
            return 0;
        }

        return _draft.LexiconWeights.TryGetValue($"{kind}:{book}", out var weight) ? Math.Max(0, weight) : 0;
    }

    /// <summary>
    /// 写入草稿里的权重。
    /// </summary>
    private void SetDraftWeight(QuizRelicKind kind, LexiconBookId book, int weight)
    {
        if (_draft == null)
        {
            return;
        }

        _draft.LexiconWeights[$"{kind}:{book}"] = Math.Max(0, weight);
    }

    /// <summary>
    /// 根据纯字体配置给整张面板应用字体覆盖。
    /// </summary>
    private void ApplyFontOverrides()
    {
        ApplyFontRecursive(_content, FontService.GetQuizFont());
    }

    /// <summary>
    /// 清空旧 UI 内容，供重新构建面板使用。
    /// </summary>
    private void ClearContent()
    {
        ApplyFontRecursive(_content, null);
        foreach (var child in _content.GetChildren())
        {
            if (child is Node node)
            {
                _content.RemoveChild(node);
                node.Free();
            }
        }
    }

    /// <summary>
    /// 关闭配置面板并丢弃未保存的草稿。
    /// </summary>
    private void CloseOverlay()
    {
        Visible = false;
        _draft = null;
        ClearContent();
    }

    /// <summary>
    /// 递归给所有控件设置或移除字体覆盖。
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
