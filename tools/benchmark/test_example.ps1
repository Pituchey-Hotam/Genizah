<#
.SYNOPSIS
Scans the example document (make_example.ps1) and checks that the name was censored in every
printed place (body, tables, content control, text boxes and shapes, header, footer, notes,
beside fields) and left in the review comment, which isn't printed.

.DESCRIPTION
Runs in a visible Word of its own, with the Debug build's hook. The document is scanned twice,
from the same file: with field codes hidden, and shown (Alt+F9), which mustn't change what the
scan reads. Each pass also checks that every field, link and shape is still there. The first then
selects every result from the pane, and leaves and censors again each censored one; the
second undoes the scan in one step and redoes it, all checked in every story. Every case is
reported; the script exits 1 if any fails. The scanned documents are saved to
-OutputDirectory, and -Show leaves the last one open in Word with the review pane, to look at.
#>
param(
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-example-' + [guid]::NewGuid())),
    [switch]$Show
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$name = [string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4
$msoGroup = 6
$dontSave = 0

# Calls the hook with a fresh reply file, and returns the reply.
function Ask([scriptblock]$call) {
    $path = Join-Path $OutputDirectory ([guid]::NewGuid().ToString() + '.txt')
    & $call $path
    Reply $path
}

function Decode($base64) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($base64)) }

function Reply($path) {
    $deadline = [DateTime]::UtcNow.AddMinutes(5)
    while (-not [IO.File]::Exists($path)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out: $path" }
        Start-Sleep -Milliseconds 100
    }
    $reply = [IO.File]::ReadAllText($path)
    if ($reply.StartsWith('ERROR:')) { throw $reply }
    $reply
}

# Each story and its linked continuations.
function Get-Stories($doc) {
    foreach ($story in $doc.StoryRanges) {
        $range = $story
        while ($null -ne $range) { $range; $range = $range.NextStoryRange }
    }
}

# Every text the document holds, found the way Word's object model offers it, independently of
# how the add-in reads it: each story, and each shape's text, grouped shapes included.
function Get-AllText($doc) {
    $texts = New-Object System.Collections.Generic.List[string]
    foreach ($range in Get-Stories $doc) { $texts.Add($range.Text) }
    function Add-Shape($shape) {
        if ($shape.Type -eq $msoGroup) { foreach ($item in $shape.GroupItems) { Add-Shape $item } }
        else { try { if ($shape.TextFrame.HasText) { $texts.Add($shape.TextFrame.TextRange.Text) } } catch { } }
    }
    # A header's Shapes holds every header and footer shape in the document.
    foreach ($shape in $doc.Shapes) { Add-Shape $shape }
    foreach ($shape in $doc.Sections.Item(1).Headers.Item(1).Shapes) { Add-Shape $shape }
    $texts -join "`n"
}

# What censoring must leave as it was: how many fields each story has, where each link goes,
# and the shapes in the body and in headers and footers.
function Get-Fields($doc) {
    $counts = foreach ($range in Get-Stories $doc) { "$($range.StoryType):$($range.Fields.Count)" }
    $links = foreach ($link in $doc.Hyperlinks) { $link.Address }
    "fields $($counts -join ' '); links $($links -join ' '); shapes $($doc.Shapes.Count), $($doc.Sections.Item(1).Headers.Item(1).Shapes.Count)"
}

$word = $null
$failed = 0
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $true
    $word.DisplayAlerts = 0
    $addin = $word.COMAddIns.Item('Genizah')
    if (-not $addin.Connect -or $null -eq $addin.Object) {
        throw "The Genizah add-in isn't loaded in Word, or isn't a Debug build (the hook is Debug-only)."
    }
    $hook = $addin.Object

    $path = Join-Path $OutputDirectory 'example.docx'
    $cases = & (Join-Path $PSScriptRoot 'make_example.ps1') -Path $path -Word $word
    $passes = @(@{ Codes = 'hidden'; Show = $false }, @{ Codes = 'shown'; Show = $true })
    $rows = foreach ($pass in $passes) {
        $doc = $word.Documents.Open($path)
        $fields = Get-Fields $doc
        $original = Get-AllText $doc
        $view = $doc.ActiveWindow.View
        $view.ShowFieldCodes = $pass.Show
        $reply = Join-Path $OutputDirectory "scan-codes-$($pass.Codes).json"
        # An earlier run's reply would be read as this scan's before it ends.
        Remove-Item $reply -ErrorAction SilentlyContinue
        $hook.StartWindowScan($doc.ActiveWindow.Hwnd, $reply)
        $timings = Reply $reply | ConvertFrom-Json
        # Range.Text follows the view; read results, not codes.
        $view.ShowFieldCodes = $false
        $scanned = Join-Path $OutputDirectory "example-scanned-codes-$($pass.Codes).docx"
        $doc.SaveAs2([string]$scanned)

        $text = Get-AllText $doc
        foreach ($case in $cases) {
            $found = if ($text.Contains($name + $case.After)) { 'left' } elseif ($text.Contains($case.After)) { 'censored' } else { 'missing' }
            $expected = if ($case.Censor) { 'censored' } else { 'left' }
            if ($found -ne $expected) { $failed++ }
            [pscustomobject]@{ Result = $(if ($found -eq $expected) { 'PASS' } else { 'FAIL' }); Codes = $pass.Codes; Where = $case.Where; Expected = $expected; Found = $found }
        }
        $after = Get-Fields $doc
        $intact = $after -eq $fields
        if (-not $intact) { $failed++; Write-Host "Fields before: $fields`nFields after:  $after" }
        [pscustomobject]@{ Result = $(if ($intact) { 'PASS' } else { 'FAIL' }); Codes = $pass.Codes; Where = 'fields, links and shapes'; Expected = 'intact'; Found = $(if ($intact) { 'intact' } else { 'changed' }) }

        $hwnd = $doc.ActiveWindow.Hwnd
        if ($pass.Codes -eq 'hidden') {
            # The pane, in every story: selecting a row selects its name, and leaving a censored
            # name and censoring it again writes each spelling back.
            $results = @((Ask { param($p) $hook.InspectResultRanges($hwnd, $p) }) -split "`n" | ForEach-Object { , ($_ -split "`t") })
            $wrong = @(for ($i = 0; $i -lt $results.Count; $i++) {
                $selected = (Ask { param($p) $hook.SelectResult($hwnd, $i, $p) }) -split "`t"
                if ($selected[0] -ne 'True') { "$i ($($selected[1..6] -join ' '))" }
            })
            if ($wrong.Count -gt 0) { $failed++ }
            [pscustomobject]@{ Result = $(if ($wrong.Count -eq 0) { 'PASS' } else { 'FAIL' }); Codes = $pass.Codes; Where = 'selecting each result from the pane'; Expected = "$($results.Count) selected"; Found = $(if ($wrong.Count -eq 0) { "$($results.Count) selected" } else { "wrong: $($wrong -join ', ')" }) }
            $censored = @($results | Where-Object { $_[6] -eq 'Censored' } | ForEach-Object { [int]$_[0] })
            $wrong = @()
            foreach ($censor in $false, $true) {
                foreach ($i in $censored) { Ask { param($p) $hook.ReviewResult($hwnd, $i, $censor, $p) } | Out-Null }
                $now = @((Ask { param($p) $hook.InspectResultRanges($hwnd, $p) }) -split "`n" | ForEach-Object { , ($_ -split "`t") })
                $column = if ($censor) { 5 } else { 4 }
                $wrong += @($censored | Where-Object { (Decode $now[$_][3]) -ne (Decode $now[$_][$column]) } | ForEach-Object { "$_ $(if ($censor) { 'censor' } else { 'leave' })" })
            }
            if ($wrong.Count -gt 0) { $failed++ }
            [pscustomobject]@{ Result = $(if ($wrong.Count -eq 0) { 'PASS' } else { 'FAIL' }); Codes = $pass.Codes; Where = 'leaving and censoring again from the pane'; Expected = "$($censored.Count) of each"; Found = $(if ($wrong.Count -eq 0) { "$($censored.Count) of each" } else { "wrong: $($wrong -join ', ')" }) }
        }
        else {
            # One undo takes back the whole scan, in every story; redo puts it back.
            $scannedText = Get-AllText $doc
            $undone = $doc.Undo() -and (Get-AllText $doc) -eq $original -and -not $doc.Undo()
            $redone = $doc.Redo() -and (Get-AllText $doc) -eq $scannedText
            foreach ($check in @(@{ Where = 'one undo restores every story'; Ok = $undone }, @{ Where = 'redo censors every story again'; Ok = $redone })) {
                if (-not $check.Ok) { $failed++ }
                [pscustomobject]@{ Result = $(if ($check.Ok) { 'PASS' } else { 'FAIL' }); Codes = $pass.Codes; Where = $check.Where; Expected = 'yes'; Found = $(if ($check.Ok) { 'yes' } else { 'no' }) }
            }
        }
        # A name mapped to the wrong Word range is skipped, not censored.
        Write-Host "Codes $($pass.Codes): scan outcome $($timings.outcome), $($timings.candidates) names, $($timings.range_mismatches) mapped to the wrong range. Scanned document: $scanned"
        if ($timings.outcome -ne 'ok') { $failed++ }
        if (-not $Show -or $pass -ne $passes[-1]) { $doc.Close([ref]$dontSave) }
    }
    $rows | Format-Table -AutoSize | Out-String | Write-Host
    if ($failed -gt 0) { Write-Host "FAIL: $failed of $($rows.Count) checks" } else { Write-Host 'All example checks passed.' }
}
finally {
    if ($null -ne $word -and -not $Show) { $word.Quit([ref]$dontSave) }
}
if ($failed -gt 0) { exit 1 }
