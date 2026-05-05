using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Services;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Scaffolding.Content;

namespace CET46InSpire2.Scripts.Cet46.Models;

/// <summary>
/// 保留 STS1 原模组“考前一天还在玩”的异常彩蛋。
/// </summary>
public sealed class DayBeforeCetPlayGameException() : NullReferenceException("The day before CET is not the time to climb the Spire.");

/// <summary>
/// 开局和终局奖励页后插入的 CET 事件。
/// </summary>
public sealed class CallOfCetEvent : ModEventTemplate
{
    private string EventKey => Id.Entry;

    /// <summary>
    /// 该事件只通过本模组手动插入，不进入普通随机事件候选。
    /// </summary>
    public override bool IsAllowed(IRunState runState)
    {
        return false;
    }

    /// <summary>
    /// 初始页描述会根据离下次 CET 的天数动态替换文本。
    /// </summary>
    public override LocString InitialDescription
    {
        get
        {
            var loc = new LocString("events", _isTomorrow ? $"{EventKey}.pages.INITIAL.description.tomorrow" : $"{EventKey}.pages.INITIAL.description");
            loc.Add("DaysText", LocalizationService.FormatDaysUntilCet(_daysUntilNextCet));
            return loc;
        }
    }

    /// <summary>
    /// 事件开始前计算日期相关状态。
    /// </summary>
    protected override Task BeforeEventStarted(bool fromLoad)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var next = ExamScheduleService.GetNextCet(today);
        _daysUntilNextCet = next.daysUntil;
        _isTomorrow = next.daysUntil == 1;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 为每个已启用词库生成一个遗物选项。
    /// </summary>
    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        var options = new List<EventOption>();

        foreach (var book in ConfigService.GetEnabledBooks(QuizRelicKind.CET))
        {
            options.Add(CreateRelicOption<CetQuizRelicModel>(book));
        }

        foreach (var book in ConfigService.GetEnabledBooks(QuizRelicKind.JLPT))
        {
            options.Add(CreateRelicOption<JlptQuizRelicModel>(book));
        }

        if (_isTomorrow)
        {
            options.Add(new EventOption(this, GoStudyNow, InitialOptionKey("STUDY")));
        }

        options.Add(new EventOption(this, Refuse, InitialOptionKey("REFUSE")));
        return options;
    }

    /// <summary>
    /// 生成带有正确图标和描述的遗物选项预览。
    /// </summary>
    private EventOption CreateRelicOption<T>(LexiconBookId book) where T : QuizRelicModel
    {
        var preview = (T)ModelDb.Relic<T>().ToMutable();
        if (Owner != null)
        {
            preview.Owner = Owner;
        }

        preview.SetSelectedBook(book);
        preview.RefreshFromConfig();

        return EventOption.FromRelic(preview, this, () => TakeRelicAsync<T>(book), InitialOptionKey(book.ToString()));
    }

    /// <summary>
    /// 选择遗物后真正发放给玩家，并锁定词库范围。
    /// </summary>
    private async Task TakeRelicAsync<T>(LexiconBookId book) where T : QuizRelicModel
    {
        if (Owner == null)
        {
            return;
        }

        var relic = await RelicCmd.Obtain<T>(Owner);
        relic.SetSelectedBook(book);
        relic.RefreshFromConfig();
        ShowFollowupPage(PageState.Accept);
    }

    /// <summary>
    /// 拒绝遗物，回到普通流程。
    /// </summary>
    private Task Refuse()
    {
        ShowFollowupPage(PageState.Refuse);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 结束事件并把流程交还给 run manager。
    /// </summary>
    private Task Proceed()
    {
        if (_state == PageState.Accept)
        {
            SetEventFinished(GetPageDescription("ACCEPT"));
            return Task.CompletedTask;
        }

        if (_state == PageState.Refuse)
        {
            SetEventFinished(GetPageDescription("REFUSE"));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 触发彩蛋异常。
    /// </summary>
    private Task GoStudyNow()
    {
        throw new DayBeforeCetPlayGameException();
    }

    /// <summary>
    /// 切到接受/拒绝后的确认页。
    /// </summary>
    private void ShowFollowupPage(PageState state)
    {
        _state = state;
        var pageName = state == PageState.Accept ? "ACCEPT" : "REFUSE";
        var options = new List<EventOption>
        {
            new(this, Proceed, $"{EventKey}.pages.{pageName}.options.EXIT")
        };
        if (_isTomorrow)
        {
            options.Add(new EventOption(this, GoStudyNow, $"{EventKey}.pages.{pageName}.options.STUDY"));
        }

        SetEventState(GetPageDescription(pageName), options);
    }

    /// <summary>
    /// 获取对应页面的本地化描述。
    /// </summary>
    private LocString GetPageDescription(string pageName)
    {
        return new LocString("events", _isTomorrow
            ? $"{EventKey}.pages.{pageName}.description.tomorrow"
            : $"{EventKey}.pages.{pageName}.description");
    }

    private enum PageState
    {
        Initial,
        Accept,
        Refuse,
    }

    private int _daysUntilNextCet = 999;
    private bool _isTomorrow;
    private PageState _state;
}
