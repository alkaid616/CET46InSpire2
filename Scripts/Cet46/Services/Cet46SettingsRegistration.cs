using CET46InSpire2.Scripts.Cet46.Bootstrap;
using CET46InSpire2.Scripts.Cet46.Data;
using STS2RitsuLib;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;

namespace CET46InSpire2.Scripts.Cet46.Services;

public static class Cet46SettingsRegistration
{
    public static void Register()
    {
        RitsuLibFramework.RegisterModSettings(Cet46Bootstrap.ModId, page => page
            .WithModDisplayName(Text("config.title"))
            .WithTitle(Text("config.title"))
            .WithDescription(Text("config.title"))
            .AddSection("general", section => section
                .WithTitle(Text("config.title"))
                .AddToggle("dark_mode", Text("config.darkMode"), Bind("dark_mode", c => c.DarkMode, (c, value) => c.DarkMode = value), Empty(), () => true)
                .AddToggle("pure_font", Text("config.pureFont"), Bind("pure_font", c => c.PureFont, (c, value) => c.PureFont = value), Empty(), () => true)
                .AddToggle("fast_mode", Text("config.fastMode"), Bind("fast_mode", c => c.FastMode, (c, value) => c.FastMode = value), Empty(), () => true)
                .AddToggle("auto_check", Text("config.autoCheck"), Bind("auto_check", c => c.AutoCheckAnswer, (c, value) => c.AutoCheckAnswer = value), Empty(), () => true)
                .AddToggle("casual_mode", Text("config.casualMode"), Bind("casual_mode", c => c.CasualMode, (c, value) => c.CasualMode = value), Empty(), () => true)
                .AddToggle("ignore_check", Text("config.ignoreCheck"), Bind("ignore_check", c => c.IgnoreCheck, (c, value) => c.IgnoreCheck = value), Empty(), () => true)
                .AddToggle("show_lexicon", Text("config.showLexicon"), Bind("show_lexicon", c => c.ShowLexicon, (c, value) => c.ShowLexicon = value), Empty(), () => true)
                .AddIntSlider("max_answer_count", Text("config.maxAnsNum"), Bind("max_answer_count", c => c.MaxAnsNum, (c, value) => c.MaxAnsNum = value), 1, 3, 1, value => value.ToString(), Empty())
                .AddButton("reset_defaults", Text("config.reset"), Text("config.reset"), ConfigService.ResetToDefaults, ModSettingsButtonTone.Danger, Empty()))
            .AddSection("books", section => section
                .WithTitle(Text("quiz.book"))
                .AddToggle("load_cet", Text("config.loadCET"), Bind("load_cet", c => c.LoadCET, (c, value) => c.LoadCET = value), Empty(), () => true)
                .AddToggle("load_jlpt", Text("config.loadJLPT"), Bind("load_jlpt", c => c.LoadJLPT, (c, value) => c.LoadJLPT = value), Empty(), () => true)
                .AddIntSlider("weight_cet4", Text("book.CET4"), BindWeight(QuizRelicKind.CET, LexiconBookId.CET4), 0, 10, 1, value => value.ToString(), Empty())
                .AddIntSlider("weight_cet6", Text("book.CET6"), BindWeight(QuizRelicKind.CET, LexiconBookId.CET6), 0, 10, 1, value => value.ToString(), Empty())
                .AddIntSlider("weight_n1", Text("book.N1"), BindWeight(QuizRelicKind.JLPT, LexiconBookId.N1), 0, 10, 1, value => value.ToString(), Empty())
                .AddIntSlider("weight_n2", Text("book.N2"), BindWeight(QuizRelicKind.JLPT, LexiconBookId.N2), 0, 10, 1, value => value.ToString(), Empty())
                .AddIntSlider("weight_n3", Text("book.N3"), BindWeight(QuizRelicKind.JLPT, LexiconBookId.N3), 0, 10, 1, value => value.ToString(), Empty())
                .AddIntSlider("weight_n4", Text("book.N4"), BindWeight(QuizRelicKind.JLPT, LexiconBookId.N4), 0, 10, 1, value => value.ToString(), Empty())));
    }

    private static IModSettingsValueBinding<int> BindWeight(QuizRelicKind kind, LexiconBookId book)
    {
        return ModSettingsBindings.Callback(
            Cet46Bootstrap.ModId,
            $"weight_{kind}_{book}",
            () => ConfigService.GetWeight(kind, book),
            value => ConfigService.TryUpdate(config => config.LexiconWeights[$"{kind}:{book}"] = Math.Max(0, value)),
            () => { },
            SaveScope.Global);
    }

    private static IModSettingsValueBinding<T> Bind<T>(string key, Func<ModConfigData, T> getter, Action<ModConfigData, T> setter)
    {
        return ModSettingsBindings.Callback(
            Cet46Bootstrap.ModId,
            key,
            () => getter(ConfigService.Current),
            value => ConfigService.TryUpdate(config => setter(config, value)),
            () => { },
            SaveScope.Global);
    }

    private static ModSettingsText Text(string key)
    {
        return ModSettingsText.Dynamic(() => LocalizationService.Get(key));
    }

    private static ModSettingsText Empty()
    {
        return ModSettingsText.Literal(string.Empty);
    }
}
