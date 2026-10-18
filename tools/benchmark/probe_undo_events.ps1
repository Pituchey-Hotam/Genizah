<#
.SYNOPSIS
Measures actual Word events after COM, ribbon-command and keyboard undo/redo.
.DESCRIPTION
Uses disposable unsaved documents in its own visible Word instance. Outputs results.csv and
raw event transcripts to a temporary directory. Requires this branch's opt-in Debug probe.
This measures detection coverage; it does not implement sidebar synchronization.
#>
param(
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-undo-events-' + [guid]::NewGuid())),
    [ValidateSet('COM', 'RibbonCommand', 'Keyboard')][string[]]$Routes = @('COM', 'RibbonCommand', 'Keyboard'),
    [ValidateSet('equal-length', 'shorter', 'longer', 'formatting', 'grouped-scan')][string[]]$Scenarios = @('equal-length', 'shorter', 'longer', 'formatting', 'grouped-scan')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class UndoProbeWindows {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
'@
$rows = [Collections.Generic.List[object]]::new()
$sequence = 0
function Reply($path) {
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    while (-not [IO.File]::Exists($path)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out: $path" }
        Start-Sleep -Milliseconds 50
    }
    $reply = [IO.File]::ReadAllText($path)
    if ($reply.StartsWith('ERROR:')) { throw $reply }
    $reply
}
function Probe($command) {
    $script:sequence++
    $path = Join-Path $OutputDirectory ("{0:D3}-{1}.txt" -f $script:sequence, $command)
    $hook.ProbeUndoEvents($command, $doc.ActiveWindow.Hwnd, $path)
    Reply $path
}
function Focus-Document($required = $false) {
    $doc.Activate()
    if (-not $required) { return }
    $doc.ActiveWindow.WindowState = 1
    Start-Sleep -Milliseconds 500
    $owner = [UndoProbeWindows]::GetAncestor([IntPtr]$doc.ActiveWindow.Hwnd, 2)
    [UndoProbeWindows]::SetForegroundWindow($owner) | Out-Null
    if ([UndoProbeWindows]::GetForegroundWindow() -ne $owner) {
        $wordProcessId = [uint32]0
        [UndoProbeWindows]::GetWindowThreadProcessId($owner, [ref]$wordProcessId) | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $shell.AppActivate([int]$wordProcessId) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    # Foreground activation can be denied by Windows. Temporarily bring only our window
    # above the desktop, then verify the actual mouse target before clicking its text.
    [UndoProbeWindows]::SetWindowPos($owner, [IntPtr](-1), 0, 0, 0, 0, 0x13) | Out-Null
    try {
        Start-Sleep -Milliseconds 200
        $x = 0; $y = 0; $width = 0; $height = 0
        $doc.ActiveWindow.GetPoint([ref]$x, [ref]$y, [ref]$width, [ref]$height, $doc.Range(0, 1))
        $point = New-Object UndoProbeWindows+Point
        $point.X = $x + [int]($width / 2); $point.Y = $y + [int]($height / 2)
        if ([UndoProbeWindows]::GetAncestor([UndoProbeWindows]::WindowFromPoint($point), 2) -ne $owner) {
            throw 'Disposable Word text is occluded; refusing to click another window.'
        }
        [UndoProbeWindows]::SetCursorPos($point.X, $point.Y) | Out-Null
        [UndoProbeWindows]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        [UndoProbeWindows]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 500
    } finally {
        [UndoProbeWindows]::SetWindowPos($owner, [IntPtr](-2), 0, 0, 0, 0, 0x13) | Out-Null
    }
    if ([UndoProbeWindows]::GetForegroundWindow() -ne $owner) {
        throw 'Cannot focus disposable Word document; refusing to send keyboard input elsewhere.'
    }
}
function Signature {
    # Also detects the pure-formatting case, without changing selection.
    $doc.Content.Text + '|' + $doc.Range(7, 8).Font.Bold
}
function Measure-Operation($route, $scenario, $placement, $operation, $expected) {
    Start-Sleep -Milliseconds 400 # Let preparation notifications drain before arming.
    $armed = Probe 'clear'
    $before = Signature
    if ($operation -ne 'idle') {
        switch ($route) {
            'COM' {
                $success = if ($operation -eq 'undo') { $doc.Undo() } else { $doc.Redo() }
                if (-not $success) { throw "$route $operation refused for $scenario" }
            }
            'RibbonCommand' {
                $command = if ($operation -eq 'undo') { 'Undo' } else { 'Redo' }
                $word.CommandBars.ExecuteMso($command)
            }
            'Keyboard' {
                if ([UndoProbeWindows]::GetForegroundWindow() -ne [UndoProbeWindows]::GetAncestor([IntPtr]$doc.ActiveWindow.Hwnd, 2)) {
                    throw 'Word lost foreground before keyboard input.'
                }
                $keys = if ($operation -eq 'undo') { '^z' } else { '^y' }
                [Windows.Forms.SendKeys]::SendWait($keys)
            }
        }
    }
    Start-Sleep -Milliseconds 700
    $captured = Probe 'read'
    $after = Signature
    if ($after -ne $expected) {
        [pscustomobject]@{ Route=$route; Scenario=$scenario; Placement=$placement; Operation=$operation;
            Before=$before; Expected=$expected; Actual=$after; Armed=$armed; Captured=$captured } |
            ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'failure.json')
        throw "Unexpected document state: $route $scenario $placement $operation. Evidence: $OutputDirectory/failure.json"
    }
    $events = @($captured -split "`n" | Where-Object { $_ -match '^\d+\t' })
    $selectionEvents = @($events | Where-Object { $_ -match '\tWindowSelectionChange$' }).Count
    $otherEvents = @($events | Where-Object { $_ -notmatch '\tWindowSelectionChange$' }).Count
    $sameSelection = ($armed -split "`n")[0] -eq ($captured -split "`n")[0]
    $row = [pscustomobject]@{
        Route = $route; Scenario = $scenario; Placement = $placement; Operation = $operation
        Changed = $before -ne $after; SameSelection = $sameSelection
        SelectionEvents = $selectionEvents; OtherEvents = $otherEvents
        BeforeSelection = ($armed -split "`n")[0]; AfterSelection = ($captured -split "`n")[0]
    }
    $rows.Add($row)
    $rows | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'results.csv')
    Write-Host "$route / $scenario / $placement / $operation : changed=$($row.Changed), sameSelection=$sameSelection, selectionEvents=$selectionEvents, otherEvents=$otherEvents"
}
$word = $null
$doc = $null
$probing = $false
$dontSave = 0
try {
    $word = New-Object -ComObject Word.Application
    $word.DisplayAlerts = 0
    $word.Visible = $true
    $hook = $word.COMAddIns.Item('Genizah').Object
    $inspection = Join-Path $OutputDirectory 'build.txt'
    $hook.InspectWindows($inspection)
    if ((Reply $inspection) -notlike '*Genizah/bin/Debug/Genizah.dll*') { throw 'Expected workspace Debug add-in' }
    Write-Host "Word $($word.Version), build $($word.Build). Evidence: $OutputDirectory"
    foreach ($route in $Routes) {
        foreach ($scenario in $Scenarios) {
            foreach ($placement in @('away', 'at-edit')) {
                $doc = $word.Documents.Add()
                $doc.Content.Text = "prefix ABCD suffix.`rSecond line.`r"
                if ($scenario -eq 'grouped-scan') {
                    $name = [string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4
                    $doc.Content.Text = 'prefix ' + $name + ' suffix ' + $name + " end.`r"
                }
                Focus-Document ($route -eq 'Keyboard')
                $doc.Range(0, 0).Select()
                $doc.UndoClear()
                $original = Signature
                switch ($scenario) {
                    'equal-length' { $doc.Range(7, 11).Text = 'WXYZ' }
                    'shorter' { $doc.Range(7, 11).Text = 'XY' }
                    'longer' { $doc.Range(7, 11).Text = 'UVWXYZ' }
                    'formatting' { $doc.Range(7, 11).Font.Bold = -1 }
                    'grouped-scan' {
                        $path = Join-Path $OutputDirectory ([guid]::NewGuid().ToString() + '.json')
                        $hook.StartWindowScan($doc.ActiveWindow.Hwnd, $path)
                        $scan = (Reply $path) | ConvertFrom-Json
                        if ($scan.outcome -ne 'ok') { throw 'Scan failed' }
                    }
                }
                $edited = Signature
                if ($original -eq $edited) { throw 'Test preparation did not change the document' }
                # The scan can move focus to its pane. Restore document keyboard focus first.
                Focus-Document ($route -eq 'Keyboard')
                if ($placement -eq 'away') { $doc.Range(0, 0).Select() }
                else { $doc.Range(7, 8).Select() }
                Probe 'start' | Out-Null
                $probing = $true
                Measure-Operation $route $scenario $placement 'idle' $edited
                Measure-Operation $route $scenario $placement 'undo' $original
                Measure-Operation $route $scenario $placement 'redo' $edited
                Probe 'stop' | Out-Null
                $probing = $false
                $doc.Close([ref]$dontSave)
                $doc = $null
            }
        }
    }
    $misses = @($rows | Where-Object { $_.Changed -and $_.SelectionEvents -eq 0 })
    Write-Host "Completed $($rows.Count) observations. Changes without selection events: $($misses.Count). Evidence: $OutputDirectory"
} finally {
    if ($probing -and $null -ne $doc) { Probe 'stop' | Out-Null }
    if ($null -ne $word) { $word.Quit([ref]$dontSave) }
}
