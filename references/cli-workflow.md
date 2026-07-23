# Kagami CLI workflow and recovery rules

## Standard flow

<!-- kagami-command-contract -->
```powershell
kagami capabilities
kagami list-windows --title "Cafe Launcher"
kagami observe --hwnd 0x607fc --depth 1 --max-nodes 200 --interactive-only --include-locators interactive
kagami find --hwnd 0x607fc --control-type Button --name "Login" --max-results 20
kagami get-tree --hwnd 0x607fc --locator '{...}' --depth 1 --include-locators interactive
kagami invoke --locator '{...}' --expected-state "C:\...\guard.json"
kagami wait-for element --hwnd 0x607fc --locator '{...}' --timeout 10000
kagami observe --hwnd 0x607fc --depth 1
```

Physical verification:

<!-- kagami-command-contract -->
```powershell
kagami click --hwnd 0x607fc --x 840 --y 560 --expected-state "C:\...\guard.json"
kagami type-text --text "hello" --mode keyboard --hwnd 0x607fc --expected-state "C:\...\guard.json"
kagami key --keys "CTRL+L" --hwnd 0x607fc --expected-state "C:\...\guard.json"
```

`click` can omit `--hwnd` only when a validated `--expected-state` guard supplies the target. `key` and physical keyboard typing always require `--hwnd`, even when a guard is also supplied. Before `SendInput`, Kagami verifies that the target window family is foreground; click additionally verifies that the point hits the target family. A response with `physical_input_generated: true` only means input was injected, not that the business postcondition completed. Follow it with `wait-for` and a fresh observation.

Capture examples:

<!-- kagami-command-contract -->
```powershell
kagami screenshot --hwnd 0x607fc --mode window
kagami screenshot --hwnd 0x607fc --mode auto --allow-semantic-fallback
kagami screenshot --display 0 --mode visible-desktop
```

## Locator rules

Prefer locators returned by `observe` or `get-tree`.

```json
{
  "window": { "hwnd": "0x607fc" },
  "path": [
    {
      "control_type": "Button",
      "automation_id": "BtnLogin",
      "name": "Login",
      "class_name": "Button",
      "ordinal": 0
    }
  ]
}
```

Priority:

1. `automation_id`
2. control type + exact name + class
3. ordinal among siblings matching the preceding conditions

Refresh the tree on `LOCATOR_NOT_FOUND`. Refine the locator from returned candidates on `LOCATOR_AMBIGUOUS`.

## Progressive tree queries

Prefer a cheap `find` before expanding a large tree:

<!-- kagami-command-contract -->
```powershell
kagami find --hwnd 0x607fc --automation-id "BtnLogin" --max-results 20 --view control
kagami get-tree --hwnd 0x607fc --runtime-id "42.5678" --depth 1
kagami get-tree --hwnd 0x607fc --locator '{...}' --depth 1
kagami get-tree --hwnd 0x607fc --depth 2 --interactive-only --include-locators none
```

`get-tree` accepts at most one of `--path`, `--runtime-id`, and `--locator`. Returned `tree_path` values identify nodes relative to the selected UIA view; Runtime ID and tree path are discovery handles, while locator is the re-resolvable action handle. Use `--interactive-only` to retain interactive nodes and their ancestors. `--include-locators all|interactive|none` chooses all locators, interactive-node locators, or no locators.

UIA `is_offscreen` is a provider signal, not proof that pixels are visually visible. Confirm visual visibility with a current screenshot, particularly when `observe` returns `uia_visibility_ambiguous`.

## Guards and waits

Observation guards have a 120 秒 maximum TTL, but age alone cannot prove the UI is unchanged. For state-changing commands that expose `--expected-state` (`invoke`, `click`, `type-text`, and `key`), take a fresh observation immediately before acting, pass its guard, and refresh on `STALE_OBSERVATION`. `activate` does not accept a guard; observational `wait-for` does.

The positional condition is preferred:

<!-- kagami-command-contract -->
```powershell
kagami wait-for element --hwnd 0x607fc --locator '{...}' --timeout 10000
```

The option form remains compatible for existing callers:

<!-- kagami-command-contract -->
```powershell
kagami wait-for --condition element --hwnd 0x607fc --locator '{...}' --timeout 10000
```

## Structured errors

Branch on `error.code`, not message text.

| Error | Recovery |
|---|---|
| `WINDOW_NOT_FOUND` | Re-run `list-windows`; refine title/process filters. |
| `STALE_OBSERVATION` | Re-run `observe`; use the new guard. |
| `LOCATOR_NOT_FOUND` | Refresh `observe` or `get-tree`; rebuild locator. |
| `LOCATOR_AMBIGUOUS` | Add AutomationId, exact name, class, or ordinal. |
| `FOREGROUND_ACTIVATION_DENIED` | Do not assume keyboard input reached the target. |
| `INPUT_INJECTION_FAILED` | Check elevation, integrity, focus, session, and coordinates. |
| `CAPTURE_FAILED` | Check minimized state, desktop/session access, and backend availability. |
| `OPERATION_TIMEOUT` | Retry only when `retryable` is true or after refreshing state. |

## Protocol

- stdout: a single JSON document (单个 JSON) for success and failure
- stderr: diagnostics
- exit `0`: success
- exit `1`: expected operation failure
- exit `2`: protocol/internal failure; a command-line parse error is also JSON and uses 退出码 2
- `--help` (`-h`, `/h`, `-?`, `/?`) and `--version`: human-readable text, exit `0`
