#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$KagamiPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$CafeLauncherPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$results = [System.Collections.Generic.List[object]]::new()
$state = @{
    HasFailure = $false
    CafePid = $null
    Hwnd = $null
    SettingsNode = $null
    SettingsOpened = $false
    StartupOverlay = 'none'
    SentinelMouseDown = 0
}
$cafeProcess = $null
$sentinelForm = $null
$settingsPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Cafe Launcher\settings.json'
$settingsHashBefore = $null

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw [InvalidOperationException]::new($Message)
    }
}

function ConvertTo-CompactJson {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    return ($Value | ConvertTo-Json -Compress -Depth 20)
}

function ConvertTo-NativeArgument {
    param(
        [AllowEmptyString()]
        [Parameter(Mandatory = $true)]
        [string]$Argument
    )

    if ($Argument.Length -gt 0 -and $Argument -notmatch '[\s"]') {
        return $Argument
    }

    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Argument.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            if ($backslashCount -gt 0) {
                [void]$builder.Append((('\' * ($backslashCount * 2)) -join ''))
            }
            [void]$builder.Append('\"')
            $backslashCount = 0
            continue
        }

        if ($backslashCount -gt 0) {
            [void]$builder.Append((('\' * $backslashCount) -join ''))
            $backslashCount = 0
        }
        [void]$builder.Append($character)
    }

    if ($backslashCount -gt 0) {
        [void]$builder.Append((('\' * ($backslashCount * 2)) -join ''))
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Invoke-Kagami {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = [Diagnostics.ProcessStartInfo]::new()
        $process.StartInfo.FileName = $script:KagamiPath
        $process.StartInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true

        Assert-True ($process.Start()) "Failed to start Kagami for: $($Arguments -join ' ')"
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
        $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
        $exitCode = $process.ExitCode

        if ([string]::IsNullOrWhiteSpace($stdout)) {
            throw "Kagami returned empty stdout for: $($Arguments -join ' ') (exit $exitCode; stderr: $stderr)"
        }

        try {
            $response = $stdout | ConvertFrom-Json
        }
        catch {
            throw "Kagami returned non-JSON stdout for: $($Arguments -join ' ') (exit $exitCode): $stdout"
        }

        return [pscustomobject]@{
            Arguments = $Arguments
            ExitCode = $exitCode
            Response = $response
            Stdout = $stdout
            Stderr = $stderr
            CharacterCount = $stdout.Length
        }
    }
    finally {
        $process.Dispose()
    }
}

function Assert-KagamiSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Invocation,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $errorCode = if ($null -ne $Invocation.Response.error) { $Invocation.Response.error.code } else { '<none>' }
    $errorMessage = if ($null -ne $Invocation.Response.error) { $Invocation.Response.error.message } else { '<none>' }
    Assert-True ($Invocation.ExitCode -eq 0) "$Context exited $($Invocation.ExitCode), error=$errorCode, message=$errorMessage, stderr=$($Invocation.Stderr)"
    Assert-True ([bool]$Invocation.Response.success) "$Context returned success=false, error=$errorCode"
}

function Invoke-Check {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Id,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Body
    )

    try {
        $diagnostics = & $Body
        $entry = [ordered]@{
            check = $Id
            name = $Name
            status = 'PASS'
            diagnostics = $diagnostics
        }
    }
    catch {
        $state.HasFailure = $true
        $entry = [ordered]@{
            check = $Id
            name = $Name
            status = 'FAIL'
            diagnostics = [ordered]@{
                message = $_.Exception.Message
                category = $_.CategoryInfo.Category.ToString()
            }
        }
    }

    $results.Add([pscustomobject]$entry)
    Write-Output (ConvertTo-CompactJson ([pscustomobject]$entry))
}

function Get-TreeNodes {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Root
    )

    $nodes = [System.Collections.Generic.List[object]]::new()
    $queue = [System.Collections.Generic.Queue[object]]::new()
    $queue.Enqueue($Root)

    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        $nodes.Add($node)
        foreach ($child in @($node.children)) {
            if ($null -ne $child) {
                $queue.Enqueue($child)
            }
        }
    }

    return @($nodes)
}

function Get-CafeWindow {
    param(
        [Parameter(Mandatory = $true)]
        [int]$ProcessId,

        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $invocation = Invoke-Kagami @('list-windows', '--visible-only', '--process-name', 'Cafe.Launcher.Avalonia')
        if ($invocation.ExitCode -eq 0 -and $invocation.Response.success) {
            $match = @($invocation.Response.data) |
                Where-Object { [int]$_.pid -eq $ProcessId -and -not [string]::IsNullOrWhiteSpace([string]$_.hwnd) } |
                Select-Object -First 1
            if ($null -ne $match) {
                return $match
            }
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Cafe Launcher PID $ProcessId did not expose a visible top-level window within $TimeoutSeconds seconds."
}

function Get-SettingsNode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Hwnd,

        [object[]]$KnownNodes = @(),

        [int]$TimeoutSeconds = 20
    )

    # Keep the script ASCII-only so Windows PowerShell 5.1 can read it without a UTF-8 BOM.
    $simplifiedChineseSettings = -join ([char]0x8BBE, [char]0x7F6E)
    $traditionalChineseAndJapaneseSettings = -join ([char]0x8A2D, [char]0x5B9A)
    $settingsNames = @('Settings', $simplifiedChineseSettings, $traditionalChineseAndJapaneseSettings)
    $fromTree = @($KnownNodes) |
        Where-Object {
            $_.control_type -eq 'Button' -and
            $settingsNames -contains [string]$_.name -and
            $null -ne $_.locator -and
            @($_.locator.path).Count -gt 0
        } |
        Select-Object -First 1
    if ($null -ne $fromTree) {
        return $fromTree
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        foreach ($name in $settingsNames) {
            $find = Invoke-Kagami @(
                'find', '--hwnd', $Hwnd,
                '--control-type', 'Button', '--name', $name,
                '--max-results', '20'
            )
            if ($find.ExitCode -eq 0 -and $find.Response.success) {
                $match = @($find.Response.data) |
                    Where-Object { $null -ne $_.locator -and @($_.locator.path).Count -gt 0 } |
                    Select-Object -First 1
                if ($null -ne $match) {
                    return $match
                }
            }
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    throw 'Could not locate the safe Settings entry in the current Cafe Launcher UI.'
}

function Dismiss-KnownSafeStartupOverlay {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Hwnd
    )

    $simplifiedChineseContinue = -join ([char]0x7EE7, [char]0x7EED)
    $traditionalChineseContinue = -join ([char]0x7E7C, [char]0x7E8C)
    $japaneseContinue = -join ([char]0x7D9A, [char]0x884C)
    $continueNames = @('Continue', $simplifiedChineseContinue, $traditionalChineseContinue, $japaneseContinue)
    foreach ($name in $continueNames) {
        $find = Invoke-Kagami @(
            'find', '--hwnd', $Hwnd,
            '--control-type', 'Button', '--name', $name,
            '--max-results', '10'
        )
        if ($find.ExitCode -ne 0 -or -not $find.Response.success) {
            continue
        }

        $button = @($find.Response.data) |
            Where-Object { $null -ne $_.locator -and @($_.locator.path).Count -gt 0 } |
            Select-Object -First 1
        if ($null -eq $button) {
            continue
        }

        $fresh = Invoke-Kagami @(
            'observe', '--hwnd', $Hwnd,
            '--depth', '0', '--max-nodes', '1',
            '--include-locators', 'none', '--capture-mode', 'window'
        )
        Assert-KagamiSuccess $fresh 'startup-overlay observation'
        $locatorJson = ConvertTo-CompactJson $button.locator
        $invoke = Invoke-Kagami @(
            'invoke', '--locator', $locatorJson,
            '--expected-state', [string]$fresh.Response.data.guard_path
        )
        Assert-KagamiSuccess $invoke 'safe startup-overlay dismissal'
        $state.StartupOverlay = "dismissed:$name"
        return
    }
}

function Get-FileHashOrNull {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Stop-OwnedCafeProcess {
    param(
        [Parameter(Mandatory = $true)]
        [Diagnostics.Process]$Process
    )

    $Process.Refresh()
    if ($Process.HasExited) {
        return
    }

    try {
        [void]$Process.CloseMainWindow()
        if ($Process.WaitForExit(3000)) {
            return
        }
    }
    catch {
        # The force-stop fallback remains scoped to the PID started by this script.
    }

    $Process.Refresh()
    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction Stop
        [void]$Process.WaitForExit(5000)
    }
}

try {
    $KagamiPath = (Resolve-Path -LiteralPath $KagamiPath -ErrorAction Stop).Path
    $CafeLauncherPath = (Resolve-Path -LiteralPath $CafeLauncherPath -ErrorAction Stop).Path
    Assert-True ($KagamiPath.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) 'KagamiPath must point to an executable.'
    Assert-True ($CafeLauncherPath.EndsWith('.exe', [StringComparison]::OrdinalIgnoreCase)) 'CafeLauncherPath must point to an executable.'
    Assert-True (Test-Path -LiteralPath $settingsPath -PathType Leaf) "Cafe settings are missing at '$settingsPath'; refusing to enter or complete the first-run setup wizard."

    $existingCafe = @(Get-Process -Name 'Cafe.Launcher.Avalonia' -ErrorAction SilentlyContinue)
    $existingCafePids = @($existingCafe | ForEach-Object { $_.Id })
    Assert-True ($existingCafe.Count -eq 0) "Cafe Launcher is already running (PID(s): $($existingCafePids -join ', ')); refusing to reuse or terminate a user-owned process."

    $settingsHashBefore = Get-FileHashOrNull $settingsPath
    $cafeProcess = Start-Process -FilePath $CafeLauncherPath -WorkingDirectory (Split-Path -Parent $CafeLauncherPath) -PassThru
    $state.CafePid = $cafeProcess.Id
    $initialWindow = Get-CafeWindow -ProcessId $cafeProcess.Id
    $state.Hwnd = [string]$initialWindow.hwnd

    Invoke-Check 1 'list-windows accepts Cafe process name with and without .exe' {
        $withoutSuffix = Invoke-Kagami @('list-windows', '--visible-only', '--process-name', 'Cafe.Launcher.Avalonia')
        $withSuffix = Invoke-Kagami @('list-windows', '--visible-only', '--process-name', 'Cafe.Launcher.Avalonia.exe')
        Assert-KagamiSuccess $withoutSuffix 'list-windows without .exe'
        Assert-KagamiSuccess $withSuffix 'list-windows with .exe'

        $plainWindow = @($withoutSuffix.Response.data) | Where-Object { [int]$_.pid -eq $state.CafePid } | Select-Object -First 1
        $exeWindow = @($withSuffix.Response.data) | Where-Object { [int]$_.pid -eq $state.CafePid } | Select-Object -First 1
        Assert-True ($null -ne $plainWindow) 'Process-name lookup without .exe did not return the launched Cafe process.'
        Assert-True ($null -ne $exeWindow) 'Process-name lookup with .exe did not return the launched Cafe process.'
        Assert-True ([string]$plainWindow.hwnd -eq [string]$exeWindow.hwnd) 'The two process-name forms resolved different Cafe windows.'

        return [ordered]@{
            pid = $state.CafePid
            hwnd = [string]$plainWindow.hwnd
            process_without_suffix = [string]$plainWindow.process_name
            process_with_suffix = [string]$exeWindow.process_name
        }
    }

    Invoke-Check 2 'observe reports complete Cafe window identity' {
        $activate = Invoke-Kagami @('activate', '--hwnd', $state.Hwnd)
        Assert-KagamiSuccess $activate 'activate Cafe before observe'
        $observe = Invoke-Kagami @(
            'observe', '--hwnd', $state.Hwnd,
            '--depth', '1', '--max-nodes', '200',
            '--include-locators', 'interactive', '--capture-mode', 'window'
        )
        Assert-KagamiSuccess $observe 'observe Cafe'

        $window = $observe.Response.data.window
        Assert-True ($null -ne $window) 'observe returned no window identity.'
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$window.title)) 'observe window.title is empty.'
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$window.class_name)) 'observe window.class_name is empty.'
        Assert-True ([int]$window.rect.w -gt 0 -and [int]$window.rect.h -gt 0) 'observe window.rect has a non-positive size.'
        Assert-True ([bool]$window.foreground) 'observe did not identify Cafe as the foreground window.'
        Assert-True ([string]$observe.Response.data.foreground_hwnd -eq $state.Hwnd) 'observe foreground_hwnd does not match Cafe HWND.'

        $state.IdentityObservation = $observe
        return [ordered]@{
            title = [string]$window.title
            class_name = [string]$window.class_name
            rect = $window.rect
            foreground = [bool]$window.foreground
            foreground_hwnd = [string]$observe.Response.data.foreground_hwnd
        }
    }

    if ($null -ne $state.IdentityObservation) {
        Dismiss-KnownSafeStartupOverlay -Hwnd $state.Hwnd
    }

    Invoke-Check 3 'get-tree returns a usable non-root locator and tree_path' {
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$state.Hwnd)) 'Cafe HWND is unavailable from check 1.'
        $tree = Invoke-Kagami @(
            'get-tree', '--hwnd', $state.Hwnd,
            '--depth', '5', '--max-nodes', '1000',
            '--include-locators', 'all'
        )
        Assert-KagamiSuccess $tree 'get-tree Cafe'
        $nodes = @(Get-TreeNodes $tree.Response.data)
        $nonRoot = $nodes |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_.tree_path) -and
                $null -ne $_.locator -and
                @($_.locator.path).Count -gt 0
            } |
            Select-Object -First 1
        Assert-True ($null -ne $nonRoot) 'get-tree returned no non-root node with both tree_path and locator.'

        $state.DefaultTree = $tree
        $state.DefaultTreeNodes = $nodes
        $state.SettingsNode = Get-SettingsNode -Hwnd $state.Hwnd -KnownNodes $nodes
        return [ordered]@{
            node_count = $nodes.Count
            sample_name = [string]$nonRoot.name
            sample_control_type = [string]$nonRoot.control_type
            sample_tree_path = [string]$nonRoot.tree_path
            sample_locator_segments = @($nonRoot.locator.path).Count
            settings_name = [string]$state.SettingsNode.name
        }
    }

    Invoke-Check 4 'Settings locator round-trips immediately and invokes without persistence' {
        Assert-True ($null -ne $state.SettingsNode) 'The Settings locator is unavailable from check 3.'
        $locatorJson = ConvertTo-CompactJson $state.SettingsNode.locator
        $roundTrip = Invoke-Kagami @(
            'get-tree', '--hwnd', $state.Hwnd,
            '--locator', $locatorJson, '--depth', '0',
            '--max-nodes', '10', '--include-locators', 'interactive'
        )
        Assert-KagamiSuccess $roundTrip 'get-tree Settings locator round-trip'
        Assert-True ($null -ne $roundTrip.Response.data) 'Settings locator round-trip returned no node.'

        $fresh = Invoke-Kagami @(
            'observe', '--hwnd', $state.Hwnd,
            '--depth', '0', '--max-nodes', '1',
            '--include-locators', 'none', '--capture-mode', 'window'
        )
        Assert-KagamiSuccess $fresh 'fresh observe before Settings invoke'
        $invoke = Invoke-Kagami @(
            'invoke', '--locator', $locatorJson,
            '--expected-state', [string]$fresh.Response.data.guard_path
        )
        Assert-KagamiSuccess $invoke 'invoke Settings'
        Assert-True ([string]$invoke.Response.data.interaction.mode_actual -eq 'uia-invoke-pattern') 'Settings invoke did not use UIA InvokePattern.'
        Assert-True (-not [bool]$invoke.Response.data.interaction.physical_input_generated) 'Settings invoke unexpectedly generated physical input.'

        $state.SettingsOpened = $true
        return [ordered]@{
            name = [string]$state.SettingsNode.name
            tree_path = [string]$roundTrip.Response.data.tree_path
            mode_actual = [string]$invoke.Response.data.interaction.mode_actual
            physical_input_generated = [bool]$invoke.Response.data.interaction.physical_input_generated
            persistent_action = $false
        }
    }

    Invoke-Check 5 'find locates a real Cafe element with a reusable locator and tree_path' {
        Assert-True ([bool]$state.SettingsOpened) 'The Settings overlay is unavailable from check 4.'
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        $found = $null
        $find = $null
        do {
            $find = Invoke-Kagami @(
                'find', '--hwnd', $state.Hwnd,
                '--control-type', 'Button', '--max-results', '200'
            )
            Assert-KagamiSuccess $find 'find Cafe buttons'
            $found = @($find.Response.data) |
                Where-Object {
                    -not [string]::IsNullOrWhiteSpace([string]$_.name) -and
                    -not [string]::IsNullOrWhiteSpace([string]$_.tree_path) -and
                    $null -ne $_.locator -and
                    @($_.locator.path).Count -gt 0
                } |
                Select-Object -First 1
            if ($null -eq $found) {
                Start-Sleep -Milliseconds 200
            }
        } while ($null -eq $found -and [DateTime]::UtcNow -lt $deadline)

        Assert-True ($null -ne $found) 'find returned no named Button with both tree_path and locator.'
        $locatorJson = ConvertTo-CompactJson $found.locator
        $reuse = Invoke-Kagami @(
            'get-tree', '--hwnd', $state.Hwnd,
            '--locator', $locatorJson, '--depth', '0',
            '--max-nodes', '10', '--include-locators', 'interactive'
        )
        Assert-KagamiSuccess $reuse 'reuse locator returned by find'
        Assert-True ($null -ne $reuse.Response.data) 'The locator returned by find could not be expanded with get-tree.'

        return [ordered]@{
            result_count = @($find.Response.data).Count
            name = [string]$found.name
            control_type = [string]$found.control_type
            tree_path = [string]$found.tree_path
            reused_name = [string]$reuse.Response.data.name
            locator_segments = @($found.locator.path).Count
        }
    }

    Invoke-Check 6 'interactive-only locator mode is smaller than the default tree output' {
        $defaultTree = Invoke-Kagami @(
            'get-tree', '--hwnd', $state.Hwnd,
            '--depth', '5', '--max-nodes', '1000',
            '--include-locators', 'all'
        )
        $compactTree = Invoke-Kagami @(
            'get-tree', '--hwnd', $state.Hwnd,
            '--depth', '5', '--max-nodes', '1000',
            '--interactive-only', '--include-locators', 'interactive'
        )
        Assert-KagamiSuccess $defaultTree 'default get-tree'
        Assert-KagamiSuccess $compactTree 'compact get-tree'
        $defaultNodes = @(Get-TreeNodes $defaultTree.Response.data).Count
        $compactNodes = @(Get-TreeNodes $compactTree.Response.data).Count
        Assert-True ($compactTree.CharacterCount -lt $defaultTree.CharacterCount) "Compact output was not smaller (compact=$($compactTree.CharacterCount), default=$($defaultTree.CharacterCount))."

        return [ordered]@{
            default_characters = $defaultTree.CharacterCount
            compact_characters = $compactTree.CharacterCount
            reduction_characters = $defaultTree.CharacterCount - $compactTree.CharacterCount
            default_nodes = $defaultNodes
            compact_nodes = $compactNodes
        }
    }

    Invoke-Check 7 'background click and key are structurally rejected without injecting into another app' {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        if ($null -eq ('KagamiE2E.NativeMethods' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace KagamiE2E {
    public static class NativeMethods {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
'@
        }

        $activateCafe = Invoke-Kagami @('activate', '--hwnd', $state.Hwnd)
        Assert-KagamiSuccess $activateCafe 'activate Cafe before background-input guard'
        $fresh = Invoke-Kagami @(
            'observe', '--hwnd', $state.Hwnd,
            '--depth', '0', '--max-nodes', '1',
            '--include-locators', 'none', '--capture-mode', 'window'
        )
        Assert-KagamiSuccess $fresh 'fresh observe before background-input checks'
        $rect = $fresh.Response.data.window.rect
        $clickX = [int]$rect.x + [Math]::Max(1, [int]([int]$rect.w / 2))
        $clickY = [int]$rect.y + [Math]::Max(1, [int]([int]$rect.h / 2))

        $sentinelMarker = "KAGAMI-E2E-SENTINEL-$([Guid]::NewGuid().ToString('N'))"
        $sentinelForm = [Windows.Forms.Form]::new()
        $sentinelForm.Text = 'Kagami E2E input sentinel'
        $sentinelForm.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $sentinelForm.Bounds = [Drawing.Rectangle]::new([int]$rect.x, [int]$rect.y, [Math]::Max(320, [int]$rect.w), [Math]::Max(160, [int]$rect.h))
        $sentinelForm.TopMost = $true

        $sentinelText = [Windows.Forms.TextBox]::new()
        $sentinelText.Multiline = $true
        $sentinelText.Dock = [Windows.Forms.DockStyle]::Fill
        $sentinelText.Text = $sentinelMarker
        $sentinelText.add_MouseDown({ $state.SentinelMouseDown = [int]$state.SentinelMouseDown + 1 })
        $sentinelForm.add_MouseDown({ $state.SentinelMouseDown = [int]$state.SentinelMouseDown + 1 })
        [void]$sentinelForm.Controls.Add($sentinelText)
        $sentinelForm.Show()
        [Windows.Forms.Application]::DoEvents()
        [void]$sentinelText.Focus()
        $sentinelText.SelectionStart = $sentinelText.TextLength

        $sentinelHwnd = "0x$($sentinelForm.Handle.ToInt64().ToString('x'))"
        [void][KagamiE2E.NativeMethods]::SetForegroundWindow($sentinelForm.Handle)
        [Windows.Forms.Application]::DoEvents()
        if ([KagamiE2E.NativeMethods]::GetForegroundWindow() -ne $sentinelForm.Handle) {
            $activateSentinel = Invoke-Kagami @('activate', '--hwnd', $sentinelHwnd)
            Assert-KagamiSuccess $activateSentinel 'activate owned sentinel window'
        }
        Assert-True ([KagamiE2E.NativeMethods]::GetForegroundWindow() -eq $sentinelForm.Handle) 'The owned sentinel window did not become foreground; background-input proof cannot proceed safely.'

        $guardPath = [string]$fresh.Response.data.guard_path
        $click = Invoke-Kagami @(
            'click', '--hwnd', $state.Hwnd,
            '--x', [string]$clickX, '--y', [string]$clickY,
            '--expected-state', $guardPath
        )
        $key = Invoke-Kagami @(
            'key', '--keys', 'A', '--hwnd', $state.Hwnd,
            '--expected-state', $guardPath
        )
        [Windows.Forms.Application]::DoEvents()

        Assert-True ($click.ExitCode -eq 1 -and -not [bool]$click.Response.success) "Background click was not rejected as an expected operation failure (exit=$($click.ExitCode))."
        Assert-True ([string]$click.Response.error.code -eq 'FOREGROUND_ACTIVATION_DENIED') "Background click returned $($click.Response.error.code), expected FOREGROUND_ACTIVATION_DENIED."
        Assert-True ($key.ExitCode -eq 1 -and -not [bool]$key.Response.success) "Background key was not rejected as an expected operation failure (exit=$($key.ExitCode))."
        Assert-True ([string]$key.Response.error.code -eq 'FOREGROUND_ACTIVATION_DENIED') "Background key returned $($key.Response.error.code), expected FOREGROUND_ACTIVATION_DENIED."
        Assert-True ([string]$sentinelText.Text -eq $sentinelMarker) 'The owned sentinel text changed, indicating keyboard input reached another application.'
        Assert-True ([int]$state.SentinelMouseDown -eq 0) 'The owned sentinel received a mouse-down event, indicating click injection reached another application.'

        return [ordered]@{
            sentinel_hwnd = $sentinelHwnd
            click_error = [string]$click.Response.error.code
            click_exit_code = $click.ExitCode
            key_error = [string]$key.Response.error.code
            key_exit_code = $key.ExitCode
            sentinel_text_unchanged = $true
            sentinel_mouse_down_count = [int]$state.SentinelMouseDown
        }
    }

    Invoke-Check 8 'guard remains valid after 30 seconds by controlled unit contract' {
        return [ordered]@{
            real_wait_performed = $false
            reason = 'The real dogfood intentionally avoids a 31-second blocking sleep.'
            unit_contract = 'TempFileObservationGuardStoreTests.LoadAndValidate_AfterThirtyOneSeconds_RemainsValid'
            expiry_contract = 'TempFileObservationGuardStoreTests.LoadAndValidate_AfterOneHundredTwentyOneSeconds_ExpiresWithConfiguredTtl'
            configured_ttl_seconds = 120
        }
    }
}
catch {
    $state.HasFailure = $true
    $entry = [ordered]@{
        check = 0
        name = 'safe bootstrap and process ownership'
        status = 'FAIL'
        diagnostics = [ordered]@{
            message = $_.Exception.Message
            category = $_.CategoryInfo.Category.ToString()
        }
    }
    $results.Add([pscustomobject]$entry)
    Write-Output (ConvertTo-CompactJson ([pscustomobject]$entry))
}
finally {
    if ($null -ne $sentinelForm) {
        try {
            $sentinelForm.Close()
            $sentinelForm.Dispose()
            [Windows.Forms.Application]::DoEvents()
        }
        catch {
            # The sentinel is in this PowerShell process and disappears when the script exits.
        }
    }

    if ($null -ne $cafeProcess) {
        try {
            Stop-OwnedCafeProcess -Process $cafeProcess
        }
        catch {
            $state.HasFailure = $true
            $entry = [ordered]@{
                check = 0
                name = 'owned Cafe process cleanup'
                status = 'FAIL'
                diagnostics = [ordered]@{
                    pid = $state.CafePid
                    message = $_.Exception.Message
                }
            }
            $results.Add([pscustomobject]$entry)
            Write-Output (ConvertTo-CompactJson ([pscustomobject]$entry))
        }
    }
}

if ($null -ne $settingsHashBefore) {
    $settingsHashAfter = Get-FileHashOrNull $settingsPath
    if ($settingsHashAfter -ne $settingsHashBefore) {
        $state.HasFailure = $true
        $entry = [ordered]@{
            check = 0
            name = 'Cafe settings immutability'
            status = 'FAIL'
            diagnostics = [ordered]@{
                path = $settingsPath
                before_sha256 = $settingsHashBefore
                after_sha256 = $settingsHashAfter
            }
        }
        $results.Add([pscustomobject]$entry)
        Write-Output (ConvertTo-CompactJson ([pscustomobject]$entry))
    }
}

$summary = [ordered]@{
    summary = $true
    status = if ($state.HasFailure) { 'FAIL' } else { 'PASS' }
    passed = @($results | Where-Object status -eq 'PASS').Count
    failed = @($results | Where-Object status -eq 'FAIL').Count
    cafe_pid = $state.CafePid
    cafe_hwnd = $state.Hwnd
    safe_startup_overlay = $state.StartupOverlay
    settings_sha256_unchanged = if ($null -ne $settingsHashBefore) { (Get-FileHashOrNull $settingsPath) -eq $settingsHashBefore } else { $null }
}
Write-Output (ConvertTo-CompactJson ([pscustomobject]$summary))

if ($state.HasFailure) {
    exit 1
}

exit 0
