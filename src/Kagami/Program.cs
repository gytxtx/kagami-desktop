using System.CommandLine;
using Kagami.Backends;
using Kagami.Commands;

namespace Kagami;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Utilities.TempFileManager.CleanupExpired();

        var guardStore = new TempFileObservationGuardStore();
        using var automation = new UiaAutomationBackend(guardStore);
        using var input = new Win32InputBackend(automation, guardStore);
        var capture = new CaptureService();

        var rootCommand = new RootCommand("Kagami — AI Agent Windows Desktop Observation and Action Protocol");

        rootCommand.AddCommand(CreateCapabilitiesCommand(new CapabilitiesCommand(capture)));
        rootCommand.AddCommand(CreateListWindowsCommand(new ListWindowsCommand(automation)));
        rootCommand.AddCommand(CreateObserveCommand(new ObserveCommand(automation, capture, guardStore)));
        rootCommand.AddCommand(CreateGetTreeCommand(new GetTreeCommand(automation)));
        rootCommand.AddCommand(CreateScreenshotCommand(new ScreenshotCommand(capture)));

        var interactionCmds = new InteractionCommands(input, automation, guardStore);
        rootCommand.AddCommand(CreateActivateCommand(interactionCmds));
        rootCommand.AddCommand(CreateInvokeCommand(interactionCmds));
        rootCommand.AddCommand(CreateClickCommand(interactionCmds));
        rootCommand.AddCommand(CreateTypeTextCommand(interactionCmds));
        rootCommand.AddCommand(CreateKeyCommand(interactionCmds));
        rootCommand.AddCommand(CreateWaitForCommand(new WaitForCommand(automation, capture)));

        return await rootCommand.InvokeAsync(args);
    }

    // ── capabilities ──
    static Command CreateCapabilitiesCommand(CapabilitiesCommand cmd)
    {
        var c = new Command("capabilities", "Query environment capabilities");
        c.SetHandler(() => Task.FromResult(cmd.Run()));
        return c;
    }

    // ── list-windows ──
    static Command CreateListWindowsCommand(ListWindowsCommand cmd)
    {
        var visibleOpt = new Option<bool>("--visible-only", () => false);
        var procOpt = new Option<string?>("--process-name");
        var titleOpt = new Option<string?>("--title");
        var c = new Command("list-windows") { visibleOpt, procOpt, titleOpt };
        c.SetHandler((v, p, t) => cmd.RunAsync(v, p, t), visibleOpt, procOpt, titleOpt);
        return c;
    }

    // ── observe ──
    static Command CreateObserveCommand(ObserveCommand cmd)
    {
        var hwndOpt = new Option<string>("--hwnd") { IsRequired = true };
        var depthOpt = new Option<int>("--depth", () => 1);
        var maxNodesOpt = new Option<int>("--max-nodes", () => 200);
        var viewOpt = new Option<string>("--view", () => "control");
        var modeOpt = new Option<string>("--capture-mode", () => "auto");
        var fallbackOpt = new Option<bool>("--allow-semantic-fallback");
        var outputOpt = new Option<string?>("--output");
        var c = new Command("observe") { hwndOpt, depthOpt, maxNodesOpt, viewOpt, modeOpt, fallbackOpt, outputOpt };
        c.SetHandler(
            (h, d, n, v, m, f, o) => cmd.RunAsync(h, d, n, v, m, f, o),
            hwndOpt, depthOpt, maxNodesOpt, viewOpt, modeOpt, fallbackOpt, outputOpt);
        return c;
    }

    // ── get-tree ──
    static Command CreateGetTreeCommand(GetTreeCommand cmd)
    {
        var hwndOpt = new Option<string>("--hwnd") { IsRequired = true };
        var depthOpt = new Option<int>("--depth", () => 1);
        var maxNodesOpt = new Option<int>("--max-nodes", () => 200);
        var viewOpt = new Option<string>("--view", () => "control");
        var pathOpt = new Option<string?>("--path");
        var c = new Command("get-tree") { hwndOpt, depthOpt, maxNodesOpt, viewOpt, pathOpt };
        c.SetHandler(
            (h, d, n, v, p) => cmd.RunAsync(h, d, n, v, p, null),
            hwndOpt, depthOpt, maxNodesOpt, viewOpt, pathOpt);
        return c;
    }

    // ── screenshot ──
    static Command CreateScreenshotCommand(ScreenshotCommand cmd)
    {
        var hwndOpt = new Option<string?>("--hwnd");
        var xOpt = new Option<int?>("--x");
        var yOpt = new Option<int?>("--y");
        var wOpt = new Option<int?>("--w");
        var hOpt = new Option<int?>("--h");
        var displayOpt = new Option<int?>("--display");
        var modeOpt = new Option<string>("--mode", () => "auto");
        var fallbackOpt = new Option<bool>("--allow-semantic-fallback");
        var outputOpt = new Option<string?>("--output");
        var c = new Command("screenshot") { hwndOpt, xOpt, yOpt, wOpt, hOpt, displayOpt, modeOpt, fallbackOpt, outputOpt };

        // Only 8 params — within limit
        c.SetHandler(
            ctx =>
            {
                var hwnd = ctx.ParseResult.GetValueForOption(hwndOpt);
                var x = ctx.ParseResult.GetValueForOption(xOpt);
                var y = ctx.ParseResult.GetValueForOption(yOpt);
                var w = ctx.ParseResult.GetValueForOption(wOpt);
                var h = ctx.ParseResult.GetValueForOption(hOpt);
                var display = ctx.ParseResult.GetValueForOption(displayOpt);
                var mode = ctx.ParseResult.GetValueForOption(modeOpt) ?? "auto";
                var fallback = ctx.ParseResult.GetValueForOption(fallbackOpt);
                var output = ctx.ParseResult.GetValueForOption(outputOpt);
                return cmd.RunAsync(hwnd, x, y, w, h, display, mode, fallback, output);
            });

        return c;
    }

    // ── activate ──
    static Command CreateActivateCommand(InteractionCommands cmds)
    {
        var hwndOpt = new Option<string>("--hwnd") { IsRequired = true };
        var c = new Command("activate") { hwndOpt };
        c.SetHandler(h => cmds.ActivateAsync(h), hwndOpt);
        return c;
    }

    // ── invoke ──
    static Command CreateInvokeCommand(InteractionCommands cmds)
    {
        var locOpt = new Option<string>("--locator") { IsRequired = true };
        var guardOpt = new Option<string?>("--expected-state");
        var c = new Command("invoke") { locOpt, guardOpt };
        c.SetHandler((l, g) => cmds.InvokeAsync(l, g), locOpt, guardOpt);
        return c;
    }

    // ── click ──
    static Command CreateClickCommand(InteractionCommands cmds)
    {
        var xOpt = new Option<int>("--x") { IsRequired = true };
        var yOpt = new Option<int>("--y") { IsRequired = true };
        var rightOpt = new Option<bool>("--right");
        var hwndOpt = new Option<string?>("--hwnd");
        var guardOpt = new Option<string?>("--expected-state");
        var c = new Command("click") { xOpt, yOpt, rightOpt, hwndOpt, guardOpt };
        c.SetHandler(
            (x, y, r, h, g) => cmds.ClickAsync(x, y, r, h, g),
            xOpt, yOpt, rightOpt, hwndOpt, guardOpt);
        return c;
    }

    // ── type-text ──
    static Command CreateTypeTextCommand(InteractionCommands cmds)
    {
        var textOpt = new Option<string>("--text") { IsRequired = true };
        var modeOpt = new Option<string>("--mode", () => "auto");
        var clipOpt = new Option<bool>("--allow-clipboard");
        var locOpt = new Option<string?>("--locator");
        var hwndOpt = new Option<string?>("--hwnd");
        var guardOpt = new Option<string?>("--expected-state");
        var c = new Command("type-text") { textOpt, modeOpt, clipOpt, locOpt, hwndOpt, guardOpt };
        c.SetHandler(
            (t, m, cl, l, h, g) => cmds.TypeTextAsync(t, m, cl, l, h, g),
            textOpt, modeOpt, clipOpt, locOpt, hwndOpt, guardOpt);
        return c;
    }

    // ── key ──
    static Command CreateKeyCommand(InteractionCommands cmds)
    {
        var keysOpt = new Option<string>("--keys") { IsRequired = true };
        var hwndOpt = new Option<string?>("--hwnd");
        var guardOpt = new Option<string?>("--expected-state");
        var c = new Command("key") { keysOpt, hwndOpt, guardOpt };
        c.SetHandler((k, h, g) => cmds.KeyAsync(k, h, g), keysOpt, hwndOpt, guardOpt);
        return c;
    }

    // ── wait-for ──
    static Command CreateWaitForCommand(WaitForCommand cmd)
    {
        var condOpt = new Option<string>("--condition") { IsRequired = true };
        var hwndOpt = new Option<string?>("--hwnd");
        var procOpt = new Option<string?>("--process");
        var titleOpt = new Option<string?>("--title");
        var locOpt = new Option<string?>("--locator");
        var propOpt = new Option<string?>("--property");
        var eqOpt = new Option<string?>("--equals");
        var toOpt = new Option<int>("--timeout", () => 10000);
        var piOpt = new Option<int>("--poll-interval", () => 200);
        var consOpt = new Option<int>("--consecutive", () => 5);
        var thrOpt = new Option<double>("--threshold", () => 0.95);
        var regOpt = new Option<string?>("--region");
        var guardOpt = new Option<string?>("--expected-state");
        var c = new Command("wait-for") { condOpt, hwndOpt, procOpt, titleOpt, locOpt, propOpt, eqOpt,
            toOpt, piOpt, consOpt, thrOpt, regOpt, guardOpt };

        c.SetHandler(
            ctx =>
            {
                var cond = ctx.ParseResult.GetValueForOption(condOpt) ?? "";
                var hwnd = ctx.ParseResult.GetValueForOption(hwndOpt);
                var proc = ctx.ParseResult.GetValueForOption(procOpt);
                var title = ctx.ParseResult.GetValueForOption(titleOpt);
                var loc = ctx.ParseResult.GetValueForOption(locOpt);
                var prop = ctx.ParseResult.GetValueForOption(propOpt);
                var eq = ctx.ParseResult.GetValueForOption(eqOpt);
                var to = ctx.ParseResult.GetValueForOption(toOpt);
                var pi = ctx.ParseResult.GetValueForOption(piOpt);
                var cons = ctx.ParseResult.GetValueForOption(consOpt);
                var thr = ctx.ParseResult.GetValueForOption(thrOpt);
                var reg = ctx.ParseResult.GetValueForOption(regOpt);
                var guard = ctx.ParseResult.GetValueForOption(guardOpt);
                return cmd.RunAsync(cond, hwnd, proc, title, loc, prop, eq, to, pi, cons, thr, reg, guard);
            });

        return c;
    }
}
