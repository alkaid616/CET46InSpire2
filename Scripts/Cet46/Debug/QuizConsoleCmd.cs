using CET46InSpire2.Scripts.Cet46.Data;
using CET46InSpire2.Scripts.Cet46.Models;
using CET46InSpire2.Scripts.Cet46.Services;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Random;

namespace CET46InSpire2.Scripts.Cet46.Debug;

/// <summary>
/// 开发期控制台命令，用于直接打开答题界面或发放测试遗物。
/// </summary>
public sealed class QuizConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "quiz";

    public override string Args => "[give <cet|jlpt>] | [rnd|cet|jlpt] [rnd|cet4|cet6|n1|n2|n3|n4] [id:int]";

    public override string Description => "Open a CET46 debug quiz or grant a CET/JLPT quiz relic for runtime testing.";

    public override bool IsNetworked => false;

    /// <summary>
    /// 解析调试命令并在运行中的单人局里执行。
    /// </summary>
    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase))
        {
            return new CmdResult(true, BuildHelpText());
        }

        if (args.Length > 0 && string.Equals(args[0], "give", StringComparison.OrdinalIgnoreCase))
        {
            return GiveQuizRelic(issuingPlayer, args);
        }

        if (args.Length > 3)
        {
            return BuildError("Too many arguments.");
        }

        if (issuingPlayer == null)
        {
            return new CmdResult(false, "A run is currently not in progress!");
        }

        var rng = Rng.Chaotic;
        if (!TryParseKind(args, rng, out var kind, out var book, out var index, out var error))
        {
            return BuildError(error);
        }

        try
        {
            var prompt = LexiconService.BuildDebugPrompt(kind, book, index, rng);
            if (!QuizRuntimeService.OpenDebugQuiz(prompt))
            {
                return new CmdResult(false, "A CET46 overlay is already open.");
            }

            return new CmdResult(true, $"Opened quiz: {kind} / {(book?.ToString() ?? "rnd")} / {(index?.ToString() ?? "rnd")}");
        }
        catch (Exception exception)
        {
            return BuildError(exception.Message);
        }
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && string.IsNullOrWhiteSpace(args[0])))
        {
            return CompleteArgument(["--help", "give", "rnd", "cet", "jlpt"], Array.Empty<string>(), args.FirstOrDefault() ?? string.Empty, CompletionType.Subcommand);
        }

        if (args.Length == 1)
        {
            return CompleteArgument(["--help", "give", "rnd", "cet", "jlpt"], Array.Empty<string>(), args[0], CompletionType.Subcommand);
        }

        if (args.Length == 2)
        {
            if (string.Equals(args[0], "give", StringComparison.OrdinalIgnoreCase))
            {
                return CompleteArgument(["cet", "jlpt"], [args[0]], args[1], CompletionType.Argument);
            }

            if (string.Equals(args[0], "rnd", StringComparison.OrdinalIgnoreCase))
            {
                return CompleteArgument(["rnd"], [args[0]], args[1], CompletionType.Argument);
            }

            if (!TryParseKindToken(args[0], out var kind))
            {
                return new CompletionResult
                {
                    Type = CompletionType.Argument,
                    ArgumentContext = CmdName,
                };
            }

            var candidates = new List<string> { "rnd" };
            candidates.AddRange(LexiconService.GetDebugBooks(kind).Select(book => book.ToString().ToLowerInvariant()));
            return CompleteArgument(candidates, [args[0]], args[1], CompletionType.Argument);
        }

        if (args.Length == 3)
        {
            if (string.Equals(args[0], "rnd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[1], "rnd", StringComparison.OrdinalIgnoreCase))
            {
                return new CompletionResult
                {
                    Type = CompletionType.Argument,
                    ArgumentContext = CmdName,
                };
            }

            if (!TryParseBookToken(args[1], out var book))
            {
                return new CompletionResult
                {
                    Type = CompletionType.Argument,
                    ArgumentContext = CmdName,
                };
            }

            var entries = LexiconService.GetBook(book);
            var candidates = entries.Count == 0
                ? new List<string>()
                : new List<string> { $"[0,{entries.Max(entry => entry.Index) + 1})" };

            if (int.TryParse(args[2], out var index) && LexiconService.HasEntry(book, index))
            {
                candidates.Add(index.ToString());
            }

            return CompleteArgument(candidates, [args[0], args[1]], args[2], CompletionType.Argument);
        }

        return new CompletionResult
        {
            Type = CompletionType.Argument,
            ArgumentContext = CmdName,
        };
    }

    /// <summary>
    /// 把命令行参数解析成题库种类、词库和词条 id。
    /// </summary>
    private static bool TryParseKind(string[] args, Rng rng, out QuizRelicKind kind, out LexiconBookId? book, out int? index, out string error)
    {
        kind = args.Length == 0 || string.Equals(args[0], "rnd", StringComparison.OrdinalIgnoreCase)
            ? Enum.GetValues<QuizRelicKind>()[rng.NextInt(Enum.GetValues<QuizRelicKind>().Length)]
            : default;
        book = null;
        index = null;
        error = string.Empty;

        if (args.Length > 0 && !string.Equals(args[0], "rnd", StringComparison.OrdinalIgnoreCase) && !TryParseKindToken(args[0], out kind))
        {
            error = $"Could not parse relic kind: {args[0]}.";
            return false;
        }

        if (args.Length > 1)
        {
            if (string.Equals(args[0], "rnd", StringComparison.OrdinalIgnoreCase) && !string.Equals(args[1], "rnd", StringComparison.OrdinalIgnoreCase))
            {
                error = "Book must be rnd when relic kind is rnd.";
                return false;
            }

            if (!string.Equals(args[1], "rnd", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseBookToken(args[1], out var parsedBook))
                {
                    error = $"Could not parse lexicon: {args[1]}.";
                    return false;
                }

                if (!LexiconService.GetDebugBooks(kind).Contains(parsedBook))
                {
                    error = $"Lexicon is not valid: {args[1]} not in {kind}.";
                    return false;
                }

                book = parsedBook;
            }
        }

        if (args.Length > 2)
        {
            if (string.Equals(args[0], "rnd", StringComparison.OrdinalIgnoreCase) ||
                (args.Length > 1 && string.Equals(args[1], "rnd", StringComparison.OrdinalIgnoreCase)))
            {
                error = "Random relic kind or lexicon cannot use a word id.";
                return false;
            }

            if (!int.TryParse(args[2], out var parsedIndex))
            {
                error = $"Word id must be an integer: {args[2]}.";
                return false;
            }

            if (book == null || !LexiconService.HasEntry(book.Value, parsedIndex))
            {
                error = $"Word id out of range: {args[2]}.";
                return false;
            }

            index = parsedIndex;
        }

        return true;
    }

    private static bool TryParseKindToken(string token, out QuizRelicKind kind)
    {
        return TryParseEnum(token, out kind);
    }

    /// <summary>
    /// 解析词库名称。
    /// </summary>
    private static bool TryParseBookToken(string token, out LexiconBookId book)
    {
        return TryParseEnum(token, out book);
    }

    /// <summary>
    /// 直接给当前玩家发放测试用答题遗物。
    /// </summary>
    private static CmdResult GiveQuizRelic(Player? issuingPlayer, string[] args)
    {
        if (issuingPlayer == null)
        {
            return new CmdResult(false, "A run is currently not in progress!");
        }

        if (args.Length != 2)
        {
            return new CmdResult(false, "Usage: quiz give <cet|jlpt>");
        }

        if (string.Equals(args[1], "cet", StringComparison.OrdinalIgnoreCase))
        {
            TaskHelper.RunSafely(RelicCmd.Obtain<CetQuizRelicModel>(issuingPlayer));
            return new CmdResult(true, "Granted CET quiz relic.");
        }

        if (string.Equals(args[1], "jlpt", StringComparison.OrdinalIgnoreCase))
        {
            TaskHelper.RunSafely(RelicCmd.Obtain<JlptQuizRelicModel>(issuingPlayer));
            return new CmdResult(true, "Granted JLPT quiz relic.");
        }

        return new CmdResult(false, "Unknown relic kind. Use: quiz give <cet|jlpt>");
    }

    private CmdResult BuildError(string error)
    {
        return new CmdResult(false, $"{error}\n{BuildHelpText()}");
    }

    /// <summary>
    /// 统一的帮助文本输出。
    /// </summary>
    private string BuildHelpText()
    {
        return "quiz [relic] [lexicon] [id]\n* relic  : cet / jlpt / rnd (default)\n* lexicon: book name / rnd (default)\n* id     : numeric word id in range\nquiz give <cet|jlpt>\n* gives the selected quiz relic for runtime testing";
    }
}
