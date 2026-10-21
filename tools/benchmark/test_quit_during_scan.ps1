<#
.SYNOPSIS
Checks that quitting Word mid-scan ends Word cleanly and leaves the add-in enabled.
.DESCRIPTION
Quits one Word instance while a scan is scoring and another just after starting a scan (while
the model loads, unless it already has), then starts a third to check the add-in still loads.
Each must exit, with no crash of WINWORD.EXE in the Application log (Word's exit code is 0
even when it crashes), so run it while no other Word test is running. A Resiliency\DisabledItems entry or a
changed LoadBehavior this run causes for the add-in is undone before the script ends.
Uses the registered Debug build's COM hook and disposable, unsaved documents.
#>
param(
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-quit-' + [guid]::NewGuid())),
    [int]$Names = 1000
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -Namespace GenizahTest -Name Native -MemberDefinition @'
[DllImport("user32.dll")]
public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
'@
$script:sequence = 0
$disabledItems = 'HKCU:\Software\Microsoft\Office\16.0\Word\Resiliency\DisabledItems'
$addInKey = 'HKCU:\Software\Microsoft\Office\Word\Addins\Genizah'
$name = [string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4
# The pane's stages: loading the model, then stage 1 of 3, scoring.
$loading = [string][char]0x05D8 + [char]0x05D5 + [char]0x05E2 + [char]0x05DF
$scoring = [string][char]0x05E9 + [char]0x05DC + [char]0x05D1 + ' 1 '
$dontSave = 0
function Next-Path {
    $script:sequence++
    $path = Join-Path $OutputDirectory "$script:sequence.txt"
    # A stale reply would be read as this request's.
    Remove-Item -LiteralPath $path, "$path.tmp" -ErrorAction SilentlyContinue
    $path
}
function Reply($path) {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not [IO.File]::Exists($path)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out waiting for $path" }
        Start-Sleep -Milliseconds 100
    }
    [IO.File]::ReadAllText($path)
}
function Assert($condition, $message) {
    if (-not $condition) { throw "FAIL: $message" }
    Write-Host "PASS: $message"
}
function Disabled-Genizah {
    if (-not (Test-Path $disabledItems)) { return @() }
    $key = Get-Item $disabledItems
    @($key.GetValueNames() | Where-Object {
        $value = $key.GetValue($_)
        $value -is [byte[]] -and [Text.Encoding]::Unicode.GetString($value) -match 'genizah'
    })
}
function Load-Behavior { (Get-ItemProperty -Path $addInKey -Name LoadBehavior -ErrorAction SilentlyContinue).LoadBehavior }
function Start-Word {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $doc = $word.Documents.Add()
    $hwnd = $doc.ActiveWindow.Hwnd
    $processId = [uint32]0
    [void][GenizahTest.Native]::GetWindowThreadProcessId([IntPtr]$hwnd, [ref]$processId)
    $process = Get-Process -Id $processId
    # Opened now, so the exit code can still be read once Word has gone.
    $null = $process.Handle
    $script:started.Add($process)
    $hook = $word.COMAddIns.Item('Genizah').Object
    if ($null -eq $hook) { throw 'FAIL: the add-in did not load (is it disabled, or not a connected Debug build?)' }
    [pscustomobject]@{ Word = $word; Doc = $doc; Hwnd = $hwnd; Hook = $hook; Process = $process }
}
function Check-Build($session) {
    $path = Next-Path
    $session.Hook.InspectWindows($path)
    Assert (((Reply $path) -split "`n")[0] -like '*Genizah/bin/Debug/Genizah.dll') 'Registered add-in loads the workspace Debug build'
}
function Start-Scan($session) {
    $path = Next-Path
    $session.Hook.StartWindowScan($session.Hwnd, $path)
    $path
}
# The pane's progress stage, or null once the scan is over.
function Stage($session) {
    $path = Next-Path
    $session.Hook.InspectReviewUi($session.Hwnd, $path)
    $parts = (Reply $path) -split '\|'
    if ($parts[0] -ne 'progress') { return $null }
    [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parts[-1]))
}
# Crashes of any Word since the given time: .NET's event has no process id.
function Crashes($since) {
    Start-Sleep -Milliseconds 1000
    @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $since } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error' -and $_.Message -match 'WINWORD\.EXE' })
}
function Quit-Word($session, $when) {
    $since = Get-Date
    try { $session.Word.Quit([ref]$dontSave) }
    catch [Runtime.InteropServices.COMException] { Write-Host "Quit returned: $($_.Exception.Message)" }
    foreach ($com in @($session.Hook, $session.Doc, $session.Word)) {
        try { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) } catch { }
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    if (-not $session.Process.WaitForExit(120000)) { throw "FAIL: Word did not exit after quitting $when" }
    $crashes = Crashes $since
    foreach ($entry in $crashes) { Write-Host "$($entry.TimeCreated) $($entry.ProviderName) $($entry.Id): $($entry.Message)" }
    Assert ($crashes.Count -eq 0) "Word (process $($session.Process.Id)) exits without crashing when quit $when"
}

$script:started = [Collections.Generic.List[Diagnostics.Process]]::new()
$startTime = Get-Date
$disabledBefore = Disabled-Genizah
if ($disabledBefore.Count -gt 0) { throw 'The add-in is already in Resiliency\DisabledItems: re-enable it before testing.' }
$loadBehaviorBefore = Load-Behavior
try {
    # Quit while scoring: the scan's await on Task.Run posts back after Word has unloaded the add-in.
    $session = Start-Word
    Check-Build $session
    $session.Doc.Content.Text = 'A ' + $name
    $warmup = Start-Scan $session
    Assert (((Reply $warmup) | ConvertFrom-Json).outcome -eq 'ok') 'A first scan loads the model'
    $session.Doc.Content.Text = 'A ' + (($name + ' ') * $Names)
    $scan = Start-Scan $session
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do { $stage = Stage $session } until ($stage -like "$scoring*" -or [DateTime]::UtcNow -gt $deadline)
    Start-Sleep -Milliseconds 1000
    $stage = Stage $session
    Assert ($stage -like "$scoring*" -and -not [IO.File]::Exists($scan)) "Scan of $Names names is still scoring before quitting"
    Quit-Word $session 'during scoring'

    # Quit right after starting the first scan: in a fresh Word the model is usually still loading.
    $session = Start-Word
    $session.Doc.Content.Text = 'A ' + (($name + ' ') * $Names)
    $scan = Start-Scan $session
    $stage = Stage $session
    Assert ($null -ne $stage -and -not [IO.File]::Exists($scan)) 'Scan is under way before quitting'
    $when = if ($stage -like "$loading*") { 'while the model loads' } else { 'early in scoring (the model had already loaded)' }
    Quit-Word $session $when

    # Word disables an add-in at the start after one that crashed it.
    $session = Start-Word
    Check-Build $session
    Quit-Word $session 'with no scan'

    Assert ((Crashes $startTime).Count -eq 0) 'No crash of WINWORD.EXE was logged during the test'
    Assert ((Disabled-Genizah).Count -eq 0) 'Word has not disabled the add-in'
    Assert ((Load-Behavior) -eq $loadBehaviorBefore) 'The add-in keeps its LoadBehavior'
    Write-Host "All assertions passed. Evidence: $OutputDirectory"
} finally {
    foreach ($process in $script:started) {
        if (-not $process.HasExited) { Write-Host "Killing Word ($($process.Id))"; $process.Kill() }
    }
    # Undo what a failing run did to the add-in, so Word keeps loading it.
    foreach ($value in Disabled-Genizah) {
        Write-Host "Removing the add-in's DisabledItems entry $value"
        Remove-ItemProperty -Path $disabledItems -Name $value
    }
    if ($null -ne $loadBehaviorBefore -and (Load-Behavior) -ne $loadBehaviorBefore) {
        Write-Host "Restoring LoadBehavior $loadBehaviorBefore"
        Set-ItemProperty -Path $addInKey -Name LoadBehavior -Value $loadBehaviorBefore -Type DWord
    }
}
