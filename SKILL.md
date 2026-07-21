---
name: kagami-desktop
description: Use when an AI agent needs to observe, inspect, automate, or verify behavior in native Windows desktop applications through the Kagami Desktop CLI, including screenshots, UI Automation trees, semantic control actions, physical input, and post-action validation.
---

# Kagami Desktop

## Core principle

Treat Kagami as an external Windows observation-and-action tool. Parse stdout as JSON, keep capture and interaction semantics explicit, and verify every state-changing action with a fresh observation.

## Workflow

1. Run `kagami capabilities`; do not assume a capture backend or API is available.
2. Find the target with `kagami list-windows`, retaining `hwnd`, PID, process name, title, and rectangle.
3. Run `kagami observe --hwnd <HWND> --depth 1 --max-nodes 200`.
4. If `stable` is false, observe again.
5. Prefer a returned, re-resolvable `locator`. Never use Runtime ID as a persistent locator.
6. Choose interaction semantics deliberately:
   - `invoke` / `type-text --mode value`: UIA provider behavior.
   - `click` / `key` / `type-text --mode keyboard`: physical reachability.
7. Pass `--expected-state <guard_path>` to state-changing commands. On `STALE_OBSERVATION`, observe again.
8. Use `wait-for` conditions instead of fixed sleeps.
9. Observe again after the action. Claim success only after the expected visual and/or UIA state change is confirmed.

Read [CLI workflow and recovery rules](references/cli-workflow.md) when forming commands or handling failures.

## Capture rules

- Inspect `capture_backend`, `capture_method`, `actual_mode`, `fallback_used`, and `occlusion_possible`.
- `visible-desktop` may include occluding windows.
- Cross-semantic fallback requires explicit `--allow-semantic-fallback`.
- Current releases may use `legacy_window_capture`; do not call it Windows Graphics Capture unless `capabilities` reports WGC.
- Never describe a desktop crop as an occlusion-free window surface.

## Safety

- Confirm the target and foreground state before physical input.
- Never reuse coordinates after the window moves, resizes, or the guard expires.
- Clipboard fallback is opt-in through `--allow-clipboard`.
- Semantic success does not prove a user can physically click or type.
- Keep automation limited to the user-authorized application and action.

## Completion check

Before reporting success, verify:

- command exit code is `0` and JSON has `success: true`;
- the target still belongs to the expected process;
- no unacknowledged fallback changed semantics;
- the post-action observation is stable;
- the expected visual or UIA state change occurred, with limitations stated when only one signal is available.
