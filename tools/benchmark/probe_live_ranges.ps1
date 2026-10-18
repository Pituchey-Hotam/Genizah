<#
.SYNOPSIS
Measures whether live Word ranges still identify their names after undo/redo.
.DESCRIPTION
Uses disposable unsaved documents, tests synthetic replacements using SearchResult.Replace's
exact range-writing sequence, then inspects real scan results through the Debug COM hook.
Records mismatches instead of repairing ranges or searching for text.
#>
param(
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-live-ranges-' + [guid]::NewGuid())),
    [switch]$SidebarOnly
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$dontSave = 0
$prefix = 'pre '
$gap = ' gap '
$suffix = " tail`r"
$originals = @('ABCD', 'EFGH', 'ABCD') # Repeated spellings, distinct occurrences.
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
function Fresh-Document($tokens) {
    $script:doc = $word.Documents.Add()
    # Word retains its mandatory final paragraph mark when assigning Content.Text.
    $doc.Content.Text = ($prefix + ($tokens -join $gap) + $suffix).TrimEnd([char]13)
    $script:tracked = @()
    $position = $prefix.Length
    foreach ($token in $tokens) {
        $script:tracked += $doc.Range($position, ($position + $token.Length)).Duplicate
        $position += $token.Length + $gap.Length
    }
    $doc.Range(0, 0).Select()
    $doc.UndoClear()
}
function Replace-Tracked($index, $text) {
    $range = $tracked[$index]
    $start = $range.Start
    $range.Text = $text
    $range.SetRange($start, ($start + $text.Length))
}
function Measure-Ranges($route, $scenario, $step, $expectedTokens, $expectedPrefix = $prefix, $snapshots = $null, $expectedGaps = @($gap, $gap), $expectedSuffix = $suffix) {
    $expectedDocument = $expectedPrefix + $expectedTokens[0] + $expectedGaps[0] + $expectedTokens[1] + $expectedGaps[1] + $expectedTokens[2] + $expectedSuffix
    if ($doc.Content.Text -ne $expectedDocument) {
        throw "Document state incorrect: $route $scenario $step. Expected=[$($expectedDocument.Replace("`r", '<CR>'))] Actual=[$($doc.Content.Text.Replace("`r", '<CR>'))] tokens=$($expectedTokens.Count) gaps=$($expectedGaps.Count)"
    }
    $position = $expectedPrefix.Length
    for ($index = 0; $index -lt $expectedTokens.Count; $index++) {
        if ($null -eq $snapshots) {
            $actualStart = $tracked[$index].Start; $actualEnd = $tracked[$index].End; $actualText = [string]$tracked[$index].Text
        } else {
            $actualStart = $snapshots[$index].Start; $actualEnd = $snapshots[$index].End; $actualText = $snapshots[$index].Text
        }
        $expectedEnd = $position + $expectedTokens[$index].Length
        $row = [pscustomobject]@{
            Route=$route; Scenario=$scenario; Step=$step; Index=$index
            ExpectedStart=$position; ActualStart=$actualStart; ExpectedEnd=$expectedEnd; ActualEnd=$actualEnd
            ExpectedText=$expectedTokens[$index]; ActualText=$actualText
            Exact=($actualStart -eq $position -and $actualEnd -eq $expectedEnd -and $actualText -eq $expectedTokens[$index])
            KnownSpelling=($actualText -eq $originals[$index] -or $actualText -eq $script:replacements[$index])
        }
        $rows.Add($row)
        $position = $expectedEnd
        if ($index -lt $expectedGaps.Count) { $position += $expectedGaps[$index].Length }
    }
    $rows | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory 'results.csv')
    $bad = @($rows | Where-Object { $_.Route -eq $route -and $_.Scenario -eq $scenario -and $_.Step -eq $step -and -not $_.Exact })
    Write-Host "$route / $scenario / $step : $($expectedTokens.Count - $bad.Count)/$($expectedTokens.Count) ranges exact"
}
function Invoke-ProbeHistory($route, $operation) {
    if ($route -eq 'COM') {
        $ok = if ($operation -eq 'undo') { $doc.Undo() } else { $doc.Redo() }
        if (-not $ok) { throw "$operation refused" }
    } else {
        $word.CommandBars.ExecuteMso($(if ($operation -eq 'undo') { 'Undo' } else { 'Redo' }))
    }
}
function Scan-Snapshots {
    $path = Join-Path $OutputDirectory ([guid]::NewGuid().ToString() + '.tsv')
    $hook.InspectResultRanges($doc.ActiveWindow.Hwnd, $path)
    $reply = Reply $path
    $snapshots = @($reply -split "`n" | Where-Object { $_ } | ForEach-Object {
        $p = $_ -split "`t"
        [pscustomobject]@{
            Start=[int]$p[1]; End=[int]$p[2]
            Text=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p[3]))
            Original=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p[4]))
            Censored=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p[5]))
        }
    })
    return ,$snapshots
}
$word = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.DisplayAlerts = 0
    $hook = $word.COMAddIns.Item('Genizah').Object
    $path = Join-Path $OutputDirectory 'build.txt'
    $hook.InspectWindows($path)
    if ((Reply $path) -notlike '*Genizah/bin/Debug/Genizah.dll*') { throw 'Expected workspace Debug add-in' }
    Write-Host "Word $($word.Version), build $($word.Build). Evidence: $OutputDirectory"
    foreach ($route in @('COM', 'RibbonCommand')) {
        $scenarios = @('grouped-single-equal', 'grouped-single-shorter', 'grouped-single-longer', 'sidebar-restore')
        if (-not $SidebarOnly) {
            $scenarios += @('equal-length', 'shorter', 'longer', 'grouped-mixed', 'prefix-insert', 'gap-insert', 'suffix-insert', 'whole-name-delete', 'partial-name-edit')
        }
        foreach ($scenario in $scenarios) {
            Fresh-Document $originals
            $baseline = @($originals)
            if ($scenario -eq 'sidebar-restore') {
                $word.UndoRecord.StartCustomRecord('Range probe censor first')
                try { Replace-Tracked 0 'X' } finally { $word.UndoRecord.EndCustomRecord() }
                $doc.UndoClear()
                $baseline[0]='X'
            }
            $script:replacements = @($originals)
            Measure-Ranges $route $scenario 'initial' $baseline
            $expected = @($originals)
            $changedPrefix = $prefix
            $changedGaps = @($gap,$gap)
            $changedSuffix = $suffix
            switch ($scenario) {
                'equal-length' { $expected[0]='WXYZ'; Replace-Tracked 0 $expected[0] }
                'shorter' { $expected[0]='X'; Replace-Tracked 0 $expected[0] }
                'longer' { $expected[0]='UVWXYZAB'; Replace-Tracked 0 $expected[0] }
                { $_ -in @('grouped-single-equal','grouped-single-shorter','grouped-single-longer','sidebar-restore') } {
                    $expected[0] = switch ($scenario) {
                        'grouped-single-equal' { 'WXYZ' }
                        'grouped-single-shorter' { 'X' }
                        'grouped-single-longer' { 'UVWXYZAB' }
                        'sidebar-restore' { $originals[0] }
                    }
                    $word.UndoRecord.StartCustomRecord('Range probe single review decision')
                    try { Replace-Tracked 0 $expected[0] } finally { $word.UndoRecord.EndCustomRecord() }
                }
                'grouped-mixed' {
                    $expected=@('X', 'UVWXYZAB', 'WXYZ')
                    $word.UndoRecord.StartCustomRecord('Range probe grouped replacements')
                    try { for ($i=0; $i -lt 3; $i++) { Replace-Tracked $i $expected[$i] } }
                    finally { $word.UndoRecord.EndCustomRecord() }
                }
                'prefix-insert' { $doc.Range(1,1).Text='INSERT'; $changedPrefix='pINSERTre ' }
                'gap-insert' { $doc.Range(10,10).Text='INSERT'; $changedGaps[0]=' gINSERTap ' }
                'suffix-insert' { $end=$doc.Content.End-2; $doc.Range($end,$end).Text='INSERT'; $changedSuffix=" taiINSERTl`r" }
                'whole-name-delete' { $doc.Range(4,8).Text=''; $expected[0]='' }
                'partial-name-edit' { $doc.Range(5,6).Text='x'; $expected[0]='AxCD' }
            }
            $script:replacements = @($expected)
            Measure-Ranges $route $scenario 'changed' $expected $changedPrefix -expectedGaps $changedGaps -expectedSuffix $changedSuffix
            for ($cycle=1; $cycle -le 2; $cycle++) {
                Invoke-ProbeHistory $route 'undo'
                Measure-Ranges $route $scenario "undo-$cycle" $baseline
                Invoke-ProbeHistory $route 'redo'
                Measure-Ranges $route $scenario "redo-$cycle" $expected $changedPrefix -expectedGaps $changedGaps -expectedSuffix $changedSuffix
            }
            $doc.Close([ref]$dontSave)
        }
        if ($SidebarOnly) { continue }
        $name=[string][char]0x05D9+[char]0x05D4+[char]0x05D5+[char]0x05D4
        $originals=@($name,$name,$name)
        Fresh-Document $originals
        $path=Join-Path $OutputDirectory ([guid]::NewGuid().ToString()+'.json')
        $hook.StartWindowScan($doc.ActiveWindow.Hwnd,$path)
        if (((Reply $path)|ConvertFrom-Json).outcome -ne 'ok') { throw 'Scan failed' }
        $snapshots=Scan-Snapshots
        if ($snapshots.Count -ne 3) { throw 'Expected three scan results' }
        $script:replacements=@($snapshots|ForEach-Object{$_.Censored})
        Measure-Ranges $route 'real-scan' 'censored' $replacements $prefix $snapshots
        for($cycle=1;$cycle -le 2;$cycle++) {
            Invoke-ProbeHistory $route 'undo'; Measure-Ranges $route 'real-scan' "undo-$cycle" $originals $prefix (Scan-Snapshots)
            Invoke-ProbeHistory $route 'redo'; Measure-Ranges $route 'real-scan' "redo-$cycle" $replacements $prefix (Scan-Snapshots)
        }
        $doc.Close([ref]$dontSave)
        $originals=@('ABCD','EFGH','ABCD')
    }
    $bad=@($rows|Where-Object{-not $_.Exact})
    Write-Host "Completed $($rows.Count) range observations; mismatches=$($bad.Count). Evidence: $OutputDirectory"
} finally {
    if ($null -ne $word) { $word.Quit([ref]$dontSave) }
}
