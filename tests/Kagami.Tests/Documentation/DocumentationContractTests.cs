using System.CommandLine;
using System.Text.RegularExpressions;
using Kagami.Backends;

namespace Kagami.Tests.Documentation;

public sealed class DocumentationContractTests
{
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
        var commandLines = AgentFacingDocumentPaths
            .SelectMany(path => ReadDocument(path).Split('\n'))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("kagami ", StringComparison.Ordinal))
            .ToList();
        var commands = string.Join('\n', commandLines);

        Assert.Contains("kagami click --hwnd", commands);
        Assert.Contains("kagami key --keys", commands);
        Assert.Contains("kagami type-text --text", commands);
        Assert.Contains("kagami wait-for element", commands);
        Assert.Contains("kagami get-tree --hwnd", commands);
        Assert.Contains("kagami find --hwnd", commands);

        AssertPhysicalCommandsHaveTarget(commandLines, "kagami click ");
        AssertPhysicalCommandsHaveTarget(commandLines, "kagami key ");
        AssertPhysicalCommandsHaveTarget(
            commandLines.Where(line => line.Contains("--mode keyboard", StringComparison.Ordinal)),
            "kagami type-text ");
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
        var examples = new Dictionary<string, string[]>
        {
            ["safe click"] =
                ["click", "--hwnd", "0x607fc", "--x", "840", "--y", "560", "--expected-state", "guard.json"],
            ["safe key"] =
                ["key", "--keys", "CTRL+L", "--hwnd", "0x607fc", "--expected-state", "guard.json"],
            ["physical typing"] =
                ["type-text", "--text", "hello", "--mode", "keyboard", "--hwnd", "0x607fc"],
            ["preferred wait"] =
                ["wait-for", "element", "--hwnd", "0x607fc", "--locator", "{}"],
            ["compatible wait"] =
                ["wait-for", "--condition", "element", "--hwnd", "0x607fc", "--locator", "{}"],
            ["find"] =
                ["find", "--hwnd", "0x607fc", "--control-type", "Button", "--name", "Login", "--max-results", "20"],
            ["runtime-id subtree"] =
                ["get-tree", "--hwnd", "0x607fc", "--runtime-id", "42.5678", "--depth", "1"],
            ["locator subtree"] =
                ["get-tree", "--hwnd", "0x607fc", "--locator", "{}", "--depth", "1"],
            ["compact interactive tree"] =
                ["get-tree", "--hwnd", "0x607fc", "--interactive-only", "--include-locators", "interactive"]
        };

        foreach (var (name, args) in examples)
        {
            var parseResult = rootCommand.Parse(args);
            Assert.True(
                parseResult.Errors.Count == 0,
                $"Documented {name} example no longer parses: " +
                string.Join("; ", parseResult.Errors.Select(error => error.Message)));
        }
    }

    private static void AssertPhysicalCommandsHaveTarget(
        IEnumerable<string> commandLines,
        string prefix)
    {
        var physicalCommands = commandLines
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(physicalCommands);
        Assert.All(physicalCommands, line => Assert.True(
            line.Contains("--hwnd", StringComparison.Ordinal) ||
            line.Contains("--expected-state", StringComparison.Ordinal),
            $"Physical input example must bind a target HWND directly or through a validated guard: {line}"));
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
}
