<#
.SYNOPSIS
Checks window-local panes in an isolated Word COM instance using the registered Debug build.
.DESCRIPTION
Creates only disposable, unsaved documents. Does not click the ribbon or exercise print dialogs.
Requires the Debug COM hook from this branch. Closes its own Word instance in finally.
#>
param([string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-window-tests-' + [guid]::NewGuid())))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$script:sequence = 0
$script:passes = [Collections.Generic.List[string]]::new()
function Next-Path {
    $script:sequence++
    Join-Path $OutputDirectory "$script:sequence.txt"
}
function Wait-Reply($path) {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not [IO.File]::Exists($path)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out waiting for $path" }
        Start-Sleep -Milliseconds 100
    }
    [IO.File]::ReadAllText($path)
}
function Assert($condition, $message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:passes.Add($message)
    Write-Host "PASS: $message"
}
function Snapshot {
    $path = Next-Path
    $hook.InspectWindows($path)
    $lines = (Wait-Reply $path) -split "`n"
    Assert ($lines[0] -like '*Genizah/bin/Debug/Genizah.dll') 'Registered add-in loads the workspace Debug build'
    $states = @{}
    foreach ($line in $lines | Select-Object -Skip 1) {
        if (-not $line) { continue }
        $parts = $line -split '\|'
        $states[[int]$parts[0]] = [pscustomobject]@{
            Owner = [int]$parts[1]; Visible = $parts[2] -eq 'True'; Count = [int]$parts[3]
            Pending = [int]$parts[4]; Reviewed = $parts[5] -eq 'True'; Scanning = $parts[6] -eq 'True'
        }
    }
    return ,$states
}
function Start-Scan($hwnd) {
    $path = Next-Path
    $hook.StartWindowScan($hwnd, $path)
    return $path
}
function Finish-Scan($path) {
    $timings = (Wait-Reply $path) | ConvertFrom-Json
    Assert ($timings.outcome -eq 'ok') "Scan completed successfully ($path)"
    Assert ($timings.range_mismatches -eq 0) 'Scan mapped all candidate ranges correctly'
}
function Set-Visible($hwnd, $visible) {
    $path = Next-Path
    $hook.SetPaneVisible($hwnd, $visible, $path)
    Assert ((Wait-Reply $path) -eq 'ok') 'Pane visibility request completed'
}
$word = $null
$dontSave = 0
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $hook = $word.COMAddIns.Item('Genizah').Object
    if ($null -eq $hook) { throw 'The registered add-in must be a connected Debug build.' }
    $a = $word.Documents.Add()
    $a.Content.Text = 'A ' + [string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4
    $ha = $a.ActiveWindow.Hwnd
    $b = $word.Documents.Add()
    $bText = 'B ' + (([string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4 + ' ') * 2)
    $b.Content.Text = $bText
    $hb = $b.ActiveWindow.Hwnd
    Assert ((Snapshot).Count -eq 0) 'Opening documents does not create a shared startup pane'
    Finish-Scan (Start-Scan $hb)
    $states = Snapshot
    Assert ($states.Count -eq 1 -and $states[$hb].Owner -eq $hb -and $states[$hb].Visible) 'Scanning B creates a visible pane attached only to B'
    Assert ($states[$hb].Count -eq 2) 'B retains its two results'
    Finish-Scan (Start-Scan $ha)
    $states = Snapshot
    Assert ($states.Count -eq 2 -and $states[$ha].Owner -eq $ha -and $states[$ha].Count -eq 1) 'A has its own pane and one result'
    Assert ($states[$hb].Count -eq 2 -and $states[$hb].Reviewed) 'Scanning A preserves B results and review snapshot'
    Set-Visible $ha $false
    $states = Snapshot
    Assert (-not $states[$ha].Visible -and $states[$hb].Visible) 'Hiding A leaves B visible'
    $a.Content.InsertAfter(' edited')
    $states = Snapshot
    Assert (-not $states[$ha].Reviewed -and $states[$hb].Reviewed) 'Editing A invalidates only its review snapshot'
    Finish-Scan (Start-Scan $ha)
    $states = Snapshot
    Assert ($states[$ha].Visible -and $states[$hb].Count -eq 2 -and $states[$hb].Reviewed) 'Rescanning A reopens only its own pane and preserves B'

    $a.Content.Text = 'A ' + (([string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4 + ' ') * 100)
    $b.Content.Text = $bText
    $scanA = Start-Scan $ha
    $scanB = Start-Scan $hb
    $states = Snapshot
    Assert ($states[$ha].Scanning -and $states[$hb].Scanning) 'Two windows retain separate outstanding scan requests'
    Finish-Scan $scanA
    Finish-Scan $scanB
    $states = Snapshot
    Assert ($states[$ha].Count -eq 100 -and $states[$hb].Count -eq 2) 'Queued scans publish results to their own windows'

    $extra = $b.ActiveWindow.NewWindow()
    $he = $extra.Hwnd
    Finish-Scan (Start-Scan $he)
    Set-Visible $hb $false
    $states = Snapshot
    Assert ($states[$he].Owner -eq $he -and $states[$he].Visible -and -not $states[$hb].Visible) 'Two views of one document have independently owned panes and visibility'
    $extra.Close([ref]$dontSave)
    Assert (-not (Snapshot).ContainsKey($he)) 'Closing an extra window releases its pane state'

    $a.Content.Text = 'A ' + (([string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4 + ' ') * 100)
    $closingScan = Start-Scan $ha
    Start-Sleep -Milliseconds 500
    Assert ((Snapshot)[$ha].Scanning) 'Document has an outstanding scan before closing'
    $a.Close([ref]$dontSave)
    $closedTimings = (Wait-Reply $closingScan) | ConvertFrom-Json
    Assert ($closedTimings.text_length -gt 0 -and $closedTimings.outcome -like 'failed:*') 'Closing during scoring ends the scan through its error handler'
    $states = Snapshot
    Assert (-not $states.ContainsKey($ha) -and $states.ContainsKey($hb)) 'Closing a scanning document releases only its own pane state'
    $b.Content.Text = $bText
    Finish-Scan (Start-Scan $hb)
    Assert ((Snapshot)[$hb].Count -eq 2) 'Remaining document can scan after another closes during a scan'
    $b.Close([ref]$dontSave)
    Assert ((Snapshot).Count -eq 0) 'Closing all test documents releases all pane state'
    $script:passes | Set-Content -LiteralPath (Join-Path $OutputDirectory 'passed.txt')
    Write-Host "All assertions passed. Evidence: $OutputDirectory"
} finally {
    if ($null -ne $word) { $word.Quit([ref]$dontSave) }
}
