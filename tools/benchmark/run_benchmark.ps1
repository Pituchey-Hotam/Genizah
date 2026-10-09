<#
.SYNOPSIS
Times the add-in's scan, phase by phase, on OSHB documents of increasing size.

.DESCRIPTION
Builds Word documents from the OSHB corpus (-Corpus, one paragraph per verse),
opens each in the running Word (starting one if none is), and starts a scan through the Debug
build's automation hook (Scanning/BenchmarkHook.cs). The add-in writes the scan's timings to a
file when it ends; this script waits for that file, never calling into Word mid-scan, since
the call would block while the scan holds Word's UI thread.

Layouts:
  plain  - verses only: every text offset is a Word position.
  mixed  - adds a table at the top and a DATE field every 40 verses, so text offsets and
           Word positions drift apart, as they do in real documents.

Documents are cached in out/docs; each run's timings are appended to out/results.jsonl.
The scan uses the results pane of the Word it runs in, clearing whatever it showed.

.EXAMPLE
powershell -File tools\benchmark\run_benchmark.ps1 -Corpus <oshb corpus> -Label baseline
powershell -File tools\benchmark\run_benchmark.ps1 -Corpus <oshb corpus> -Sizes 400000,1600000 -Layouts plain
#>
param(
    # Target document sizes, in characters of text. Documents stop at the first verse past it.
    [int[]]$Sizes = @(25000, 100000, 400000),
    [ValidateSet('plain', 'mixed')]
    [string[]]$Layouts = @('plain', 'mixed'),
    # Recorded with each result, to tell runs of different code apart.
    [string]$Label = '',
    # Per scan. A scan that runs out stops the benchmark, and leaves Word busy until it ends.
    [int]$TimeoutMinutes = 10,
    # A folder of .jsonl files, one chapter per LF-terminated line, its text in "body" with
    # each verse ending in ':'.
    [Parameter(Mandatory = $true)]
    [string]$Corpus
)

$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'out'
$docs = Join-Path $out 'docs'
New-Item -ItemType Directory -Force $docs | Out-Null

$FieldMarker = [string][char]0xA4
$wdFieldDate = 31
$wdFormatDocumentDefault = 16
$wdDoNotSaveChanges = 0
$wdReadingOrderRtl = 0

function Get-Verses {
    $verses = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ChildItem (Join-Path $Corpus '*.jsonl') | Sort-Object Name) {
        # LF only: the corpus has characters other splitters treat as line breaks.
        foreach ($line in ([IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8) -split "`n")) {
            if ($line.Trim().Length -eq 0) { continue }
            $body = ($line | ConvertFrom-Json).body
            foreach ($v in ($body -split '(?<=:)\s+')) {
                if ($v.Length -gt 0) { $verses.Add($v) }
            }
        }
    }
    return $verses
}

function Get-Word {
    try { return [Runtime.InteropServices.Marshal]::GetActiveObject('Word.Application') } catch { }
    Write-Host 'Starting Word...'
    Start-Process winword -ArgumentList '/q'
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        try { return [Runtime.InteropServices.Marshal]::GetActiveObject('Word.Application') } catch { }
    }
    throw 'Word did not start'
}

function New-BenchmarkDocument($word, $verses, [int]$size, [string]$layout, [string]$path) {
    $paragraphs = New-Object System.Collections.Generic.List[string]
    $chars = 0
    foreach ($v in $verses) {
        if ($chars -ge $size) { break }
        if ($layout -eq 'mixed' -and $paragraphs.Count % 40 -eq 39) { $v += ' ' + $FieldMarker }
        $paragraphs.Add($v)
        $chars += $v.Length + 1
    }
    $doc = $word.Documents.Add()
    try {
        $doc.Content.Text = [string]::Join("`r", $paragraphs)
        $doc.Content.ParagraphFormat.ReadingOrder = $wdReadingOrderRtl
        if ($layout -eq 'mixed') {
            # Each marker becomes a DATE field: its code is in the document but not in Content.Text.
            $range = $doc.Content
            $find = $range.Find
            $find.ClearFormatting()
            $find.Text = $FieldMarker
            $find.Forward = $true
            $find.Wrap = 0
            while ($find.Execute()) {
                $field = $doc.Fields.Add($range, $wdFieldDate)
                $range = $doc.Range($field.Result.End, $doc.Content.End)
                $find = $range.Find
                $find.Text = $FieldMarker
                $find.Forward = $true
                $find.Wrap = 0
            }
            # A table of verses at the top: each cell ends in a two-unit mark.
            $table = $doc.Tables.Add($doc.Range(0, 0), 3, 3)
            for ($i = 0; $i -lt 9; $i++) {
                $table.Cell([math]::Floor($i / 3) + 1, $i % 3 + 1).Range.Text = $verses[$i]
            }
        }
        $doc.SaveAs2($path, $wdFormatDocumentDefault)
    }
    finally {
        $doc.Close($wdDoNotSaveChanges)
    }
}

$word = Get-Word
# A Word just started answers before it has loaded its add-ins.
$deadline = (Get-Date).AddSeconds(60)
do {
    $addin = $null
    try { $addin = $word.COMAddIns.Item('Genizah') } catch { }
    if ($null -ne $addin -and $addin.Connect -and $null -ne $addin.Object) { break }
    Start-Sleep -Seconds 1
} while ((Get-Date) -lt $deadline)
if ($null -eq $addin -or -not $addin.Connect -or $null -eq $addin.Object) {
    throw "The Genizah add-in isn't loaded in Word, or isn't a Debug build (the hook is Debug-only)."
}
$hook = $addin.Object

$verses = $null
$results = @()
:sizes foreach ($size in $Sizes) {
    foreach ($layout in $Layouts) {
        $path = Join-Path $docs "oshb-$size-$layout.docx"
        if (-not (Test-Path $path)) {
            if ($null -eq $verses) { $verses = Get-Verses }
            Write-Host "Building $path"
            New-BenchmarkDocument $word $verses $size $layout $path
        }

        $doc = $word.Documents.Open($path, $false, $true)
        $doc.Activate()
        # How far Word's positions run past the text: 0 means every offset is a position.
        $drift = ($doc.Content.End - $doc.Content.Start) - $doc.Content.Text.Replace("`r`a", "`r").Length
        $timingsPath = Join-Path $out 'scan.json'
        Write-Host "Scanning $size characters ($layout, drift $drift)..."
        $hook.StartScan($timingsPath)

        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        while (-not (Test-Path $timingsPath) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        if (-not (Test-Path $timingsPath)) {
            Write-Warning "No result after $TimeoutMinutes minutes. Word is still scanning; stopping here."
            break sizes
        }
        $t = Get-Content $timingsPath -Raw | ConvertFrom-Json
        $doc.Close($wdDoNotSaveChanges)

        $record = [ordered]@{
            time = (Get-Date).ToString('s'); label = $Label; size = $size; layout = $layout; drift = $drift
            text_length = $t.text_length; candidates = $t.candidates; range_mismatches = $t.range_mismatches; outcome = $t.outcome
            total_ms = $t.total_ms; phases_ms = $t.phases_ms
        }
        Add-Content -Encoding UTF8 (Join-Path $out 'results.jsonl') (ConvertTo-Json $record -Compress)

        $row = [ordered]@{ layout = $layout; chars = $t.text_length; names = $t.candidates; wrong = $t.range_mismatches; drift = $drift; total_s = [math]::Round($t.total_ms / 1000, 1) }
        foreach ($p in $t.phases_ms.PSObject.Properties) { $row[$p.Name + '_s'] = [math]::Round($p.Value / 1000, 2) }
        $results += [pscustomobject]$row
        if ($t.outcome -ne 'ok') { Write-Warning "Scan outcome: $($t.outcome)" }
        if ($t.range_mismatches -gt 0) { Write-Warning "$($t.range_mismatches) names mapped to the wrong Word range" }
    }
}

# Every phase: Format-Table shows only the first ten properties unless told otherwise.
$results | Format-Table -Property * -AutoSize | Out-String -Width 400 | Write-Host
