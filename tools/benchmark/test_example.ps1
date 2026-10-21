<#
.SYNOPSIS
Scans the example document (make_example.ps1) and checks that the name was censored in every
printed place (body, tables, content control, text boxes and shapes, header, footer, notes)
and left in the review comment, which isn't printed.

.DESCRIPTION
Runs in a visible Word of its own, with the Debug build's hook. Every case is reported; the
script exits 1 if any fails. The scanned document is saved to -OutputDirectory, and -Show
leaves it open in Word with the review pane, to look at.
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

# Every text the document holds, found the way Word's object model offers it, independently of
# how the add-in reads it: each story and its linked continuations, and each shape's text,
# grouped shapes included.
function Get-AllText($doc) {
    $texts = New-Object System.Collections.Generic.List[string]
    foreach ($story in $doc.StoryRanges) {
        $range = $story
        while ($null -ne $range) { $texts.Add($range.Text); $range = $range.NextStoryRange }
    }
    function Add-Shape($shape) {
        if ($shape.Type -eq $msoGroup) { foreach ($item in $shape.GroupItems) { Add-Shape $item } }
        else { try { if ($shape.TextFrame.HasText) { $texts.Add($shape.TextFrame.TextRange.Text) } } catch { } }
    }
    # A header's Shapes holds every header and footer shape in the document.
    foreach ($shape in $doc.Shapes) { Add-Shape $shape }
    foreach ($shape in $doc.Sections.Item(1).Headers.Item(1).Shapes) { Add-Shape $shape }
    $texts -join "`n"
}

$word = $null
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
    $doc = $word.Documents.Open($path)
    $reply = Join-Path $OutputDirectory 'scan.json'
    $hook.StartWindowScan($doc.ActiveWindow.Hwnd, $reply)
    $timings = Reply $reply | ConvertFrom-Json
    $outcome = $timings.outcome
    $scanned = Join-Path $OutputDirectory 'example-scanned.docx'
    $doc.SaveAs2([string]$scanned)

    $text = Get-AllText $doc
    $failed = 0
    $rows = foreach ($case in $cases) {
        $found = if ($text.Contains($name + $case.After)) { 'left' } elseif ($text.Contains($case.After)) { 'censored' } else { 'missing' }
        $expected = if ($case.Censor) { 'censored' } else { 'left' }
        if ($found -ne $expected) { $failed++ }
        [pscustomobject]@{ Result = $(if ($found -eq $expected) { 'PASS' } else { 'FAIL' }); Where = $case.Where; Expected = $expected; Found = $found }
    }
    $rows | Format-Table -AutoSize | Out-String | Write-Host
    # A name mapped to the wrong Word range is skipped, not censored.
    Write-Host "Scan outcome: $outcome, $($timings.candidates) names, $($timings.range_mismatches) mapped to the wrong range. Scanned document: $scanned"
    if ($outcome -ne 'ok') { $failed++ }
    if ($failed -gt 0) { Write-Host "FAIL: $failed of $($cases.Count) checks" } else { Write-Host 'All example checks passed.' }
}
finally {
    if ($null -ne $word -and -not $Show) { $word.Quit([ref]$dontSave) }
}
if ($failed -gt 0) { exit 1 }
