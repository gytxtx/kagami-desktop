# Kagami CLI workflow and recovery rules

## Standard flow

```powershell
kagami capabilities
kagami list-windows --title "Cafe Launcher"
kagami observe --hwnd 0x607fc --depth 1 --max-nodes 200
kagami invoke --locator '{...}' --expected-state "C:\...\guard.json"
kagami wait-for element --hwnd 0x607fc --locator '{...}' --timeout 10000
kagami observe --hwnd 0x607fc --depth 1
```

Physical verification:

```powershell
kagami click --x 840 --y 560 --expected-state "C:\...\guard.json"
kagami type-text --text "hello" --mode keyboard --hwnd 0x607fc
kagami key --keys "CTRL+L" --hwnd 0x607fc
```

Capture examples:

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

- stdout: JSON
- stderr: diagnostics
- exit `0`: success
- exit `1`: expected operation failure
- exit `2`: protocol/internal failure
