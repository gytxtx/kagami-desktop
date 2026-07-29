using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using Kagami.Backends;

namespace Kagami.Tests.Documentation;

public sealed class DocumentationContractTests
{
    private const string CommandContractMarker = "<!-- kagami-command-contract -->";

    private static readonly string[] DocumentPaths =
    {
        "README.md",
        "SKILL.md",
        "references/cli-workflow.md",
        "docs/DESIGN.md"
    };

    private static readonly string[] AgentFacingDocumentPaths =
    {
        "README.md",
        "SKILL.md",
        "references/cli-workflow.md"
    };

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void AgentFacingExamples_UseSafeCurrentCommandSyntax()
    {
        var examples = ReadCommandContractExamples();
        var commands = string.Join('\n', examples.Select(example => example.CommandLine));

        Assert.Contains("kagami click --hwnd", commands);
        Assert.Contains("kagami key --keys", commands);
        Assert.Contains("kagami type-text --text", commands);
        Assert.Contains("kagami wait-for element", commands);
        Assert.Contains("kagami get-tree --hwnd", commands);
        Assert.Contains("kagami find --hwnd", commands);

        var clickExamples = examples.Where(example => example.Args[0] == "click").ToList();
        Assert.NotEmpty(clickExamples);
        Assert.All(clickExamples, example => Assert.True(
            HasOptionValue(example.Args, "--hwnd") ||
            HasOptionValue(example.Args, "--expected-state"),
            $"Click example must bind a target HWND or a non-empty guard: {example.CommandLine}"));

        var keyExamples = examples.Where(example => example.Args[0] == "key").ToList();
        Assert.NotEmpty(keyExamples);
        Assert.All(keyExamples, example => Assert.True(
            HasOptionValue(example.Args, "--hwnd"),
            $"Key example must bind an explicit HWND; guard-only is unsafe: {example.CommandLine}"));

        var physicalTypingExamples = examples
            .Where(example =>
                example.Args[0] == "type-text" &&
                string.Equals(
                    GetOptionValue(example.Args, "--mode"),
                    "keyboard",
                    StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(physicalTypingExamples);
        Assert.All(physicalTypingExamples, example => Assert.True(
            HasOptionValue(example.Args, "--hwnd"),
            $"Physical typing example must bind an explicit HWND; guard-only is unsafe: {example.CommandLine}"));
    }

    [Fact]
    public void MouseOperationDocumentation_CoversEveryPhysicalMouseCommand()
    {
        var readme = ReadDocument("README.md");
        var design = ReadDocument("docs/DESIGN.md");

        Assert.Contains("`kagami move --hwnd <HWND> --x <X> --y <Y>`", readme);
        Assert.Contains("`kagami double-click --hwnd <HWND> --x <X> --y <Y> [--right]`", readme);
        Assert.Contains("`kagami scroll --hwnd <HWND> --x <X> --y <Y> --delta <DELTA>`", readme);
        Assert.Contains("`kagami drag --hwnd <HWND> --from-x <X> --from-y <Y> --to-x <X> --to-y <Y>`", readme);

        Assert.Contains("move --hwnd X --x Y --y Y", design);
        Assert.Contains("double-click --hwnd X --x Y --y Y [--right]", design);
        Assert.Contains("scroll --hwnd X --x Y --y Y --delta N", design);
        Assert.Contains("drag --hwnd X --from-x X --from-y Y --to-x X --to-y Y", design);

        Assert.All(new[] { readme, design }, document =>
        {
            Assert.Contains("目标窗口必须位于前台", document);
            Assert.Contains("命中同一窗口族", document);
            Assert.Contains("正值向上", document);
            Assert.Contains("负值向下", document);
            Assert.Contains("double-click --right", document);
            Assert.Contains("起点和终点", document);
            Assert.Contains("注入任何事件前", document);
            Assert.Contains("移动到起点 → 左键按下 → 移动到终点 → 左键释放", document);
            Assert.Contains("physical_input_generated: true", document);
            Assert.Contains("仅表示输入已注入", document);
            Assert.Contains("不代表业务后置条件完成", document);
        });

        Assert.Contains("必须传 `--hwnd`，或通过已验证的 `--expected-state` guard 推导 HWND", readme);
        Assert.Contains("通过显式 `--hwnd` 或已验证的 `--expected-state` guard 绑定目标", design);
    }

    [Fact]
    public void TreeQueryDocumentation_CoversProgressiveDiscoveryContract()
    {
        var allDocs = ReadAllDocuments();

        Assert.Contains("--runtime-id", allDocs);
        Assert.Contains("--locator", allDocs);
        Assert.Contains("tree_path", allDocs);
        Assert.Contains("--interactive-only", allDocs);
        Assert.Contains("--include-locators all|interactive|none", allDocs);
        Assert.Contains("kagami find", allDocs);
    }

    [Fact]
    public void SafetyDocumentation_DistinguishesInjectionFromBusinessCompletion()
    {
        var allDocs = ReadAllDocuments();

        Assert.Contains("120 秒", allDocs);
        Assert.Contains("fresh observation", allDocs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("physical_input_generated", allDocs);
        Assert.Contains("仅表示输入已注入", allDocs);
        Assert.Contains("不代表业务后置条件完成", allDocs);
        Assert.Contains("目标窗口必须位于前台", allDocs);
        Assert.Contains("is_offscreen", allDocs);
        Assert.Contains("provider 信号", allDocs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("截图", allDocs);

        Assert.DoesNotContain("30s TTL", allDocs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(
                @"(?:guard.{0,80}30\s*秒|30\s*秒.{0,80}guard)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline),
            allDocs);
    }

    [Fact]
    public void WaitForAndMachineProtocol_DocumentPreferredAndCompatibleForms()
    {
        var allDocs = ReadAllDocuments();

        Assert.Contains("kagami wait-for element", allDocs);
        Assert.Contains("kagami wait-for --condition element", allDocs);
        Assert.Contains("位置 condition", allDocs);
        Assert.Contains("兼容", allDocs);
        Assert.Contains("stdout", allDocs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("单个 JSON", allDocs);
        Assert.Contains("解析错误", allDocs);
        Assert.Contains("退出码 2", allDocs);
    }

    [Fact]
    public void DocumentedExamples_AreAcceptedByCurrentParser()
    {
        var rootCommand = Kagami.Program.BuildRootCommand(
            null!,
            null!,
            new CaptureService(Array.Empty<ICaptureBackend>()),
            null!);
        var examples = ReadCommandContractExamples();

        Assert.NotEmpty(examples);
        Assert.All(AgentFacingDocumentPaths, path => Assert.Contains(
            examples,
            example => string.Equals(example.DocumentPath, path, StringComparison.Ordinal)));

        foreach (var example in examples)
        {
            var parseResult = rootCommand.Parse(example.Args);
            Assert.True(
                parseResult.Errors.Count == 0,
                $"Documented example in {example.DocumentPath} no longer parses: " +
                $"{example.CommandLine}. " +
                string.Join("; ", parseResult.Errors.Select(error => error.Message)));
        }
    }

    [Fact]
    public void CaptureGuardAndCommandIndex_AvoidSemanticOverclaims()
    {
        var readme = ReadDocument("README.md");
        var skill = ReadDocument("SKILL.md");
        var design = ReadDocument("docs/DESIGN.md");

        Assert.Contains("`actual_mode: \"window\"`", readme);
        Assert.Contains("`actual_mode: \"visible-desktop-crop\"` 时可能被遮挡", readme);
        Assert.DoesNotContain("| `window` | legacy_window_capture (PrintWindow → DWM Thumbnail) | **无** |", readme);
        Assert.DoesNotContain("捕获单个窗口表面（即使被遮挡）", design);

        Assert.Contains(
            "For supported state-changing commands (`invoke`, `click`, `type-text`, and `key`)",
            skill);
        Assert.DoesNotContain(
            "Pass the newest `--expected-state <guard_path>` to state-changing commands.",
            skill);
        Assert.Contains(
            "状态变更命令 `invoke`、`click`、`type-text`、`key` 支持 `--expected-state`",
            design);
        Assert.DoesNotContain("后续命令 `--expected-state guard-uuid.json` 传入", design);

        Assert.Contains("## 命令索引（语法）", readme);
        Assert.Contains("`kagami invoke --locator <LOCATOR_JSON>`", readme);
        Assert.Contains(
            "`kagami type-text --text <TEXT> --mode keyboard --hwnd <HWND>`",
            readme);
        Assert.Contains(
            "`kagami wait-for element --locator <LOCATOR_JSON>`",
            readme);
    }

    private static IReadOnlyList<CommandContractExample> ReadCommandContractExamples()
    {
        var examples = new List<CommandContractExample>();

        foreach (var path in AgentFacingDocumentPaths)
        {
            var document = ReadDocument(path);
            var matches = Regex.Matches(
                document,
                $@"{Regex.Escape(CommandContractMarker)}\s*```powershell\s*(?<body>.*?)```",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var commands = match.Groups["body"].Value
                    .Split('\n')
                    .Select(line => Regex.Replace(line.Trim(), @"\s+#.*$", ""))
                    .Where(line => line.StartsWith("kagami ", StringComparison.Ordinal));

                foreach (var command in commands)
                {
                    var args = CommandLineStringSplitter.Instance
                        .Split(command["kagami ".Length..])
                        .ToArray();
                    examples.Add(new CommandContractExample(path, command, args));
                }
            }
        }

        return examples;
    }

    private static bool HasOptionValue(IReadOnlyList<string> args, string option) =>
        GetOptionValue(args, option) is not null;

    private static string? GetOptionValue(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.Ordinal))
                return args[index + 1];
        }

        return null;
    }

    private static string ReadAllDocuments() =>
        string.Join('\n', DocumentPaths.Select(ReadDocument));

    private static string ReadDocument(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));

    private static string FindRepositoryRoot()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                    File.Exists(Path.Combine(
                        directory.FullName,
                        "tests",
                        "Kagami.Tests",
                        "Kagami.Tests.csproj")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the kagami-desktop repository root.");
    }

    private sealed record CommandContractExample(
        string DocumentPath,
        string CommandLine,
        string[] Args);
}
