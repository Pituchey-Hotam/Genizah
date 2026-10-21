<#
.SYNOPSIS
Scans the example document (make_example.ps1) and checks that the name was censored in every
printed place (body, tables, content control, text boxes and shapes, header, footer, notes,
beside fields) and left in the review comment, which isn't printed.

.DESCRIPTION
Runs in a visible Word of its own, with the Debug build's hook. The document is scanned twice,
from the same file: with field codes hidden, and shown (Alt+F9), which mustn't change what the
scan reads. Each pass also checks that every field and link is still there. Every case is
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

# What censoring must leave as it was: how many fields each story has, and where each link goes.
function Get-Fields($doc) {
    $counts = foreach ($range in Get-Stories $doc) { "$($range.StoryType):$($range.Fields.Count)" }
    $links = foreach ($link in $doc.Hyperlinks) { $link.Address }
    "fields $($counts -join ' '); links $($links -join ' ')"
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
        [pscustomobject]@{ Result = $(if ($intact) { 'PASS' } else { 'FAIL' }); Codes = $pass.Codes; Where = 'fields and links'; Expected = 'intact'; Found = $(if ($intact) { 'intact' } else { 'changed' }) }
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
