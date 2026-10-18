<# End-to-end sidebar reconciliation in disposable Word documents using the Debug hook. #>
param([string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-reconciliation-' + [guid]::NewGuid())))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$dontSave=0
$name=[string][char]0x05D9+[char]0x05D4+[char]0x05D5+[char]0x05D4
function Assert($condition,$message) {
    if(-not $condition){throw "FAIL: $message"}
    Write-Host "PASS: $message"
}
function New-ReplyPath { Join-Path $OutputDirectory ([guid]::NewGuid().ToString()+'.txt') }
function Reply($path) {
    $deadline=[DateTime]::UtcNow.AddMinutes(2)
    while(-not [IO.File]::Exists($path)) {
        if([DateTime]::UtcNow -gt $deadline){throw "Timed out: $path"}
        Start-Sleep -Milliseconds 50
    }
    $reply=[IO.File]::ReadAllText($path)
    if($reply.StartsWith('ERROR:')){throw $reply}
    $reply
}
function Rows($hwnd) {
    Start-Sleep -Milliseconds 250 # Let the deferred event handler run; inspection does not reconcile.
    $path=New-ReplyPath
    $hook.InspectResultRanges($hwnd,$path)
    $reply=Reply $path
    return ,@($reply -split "`n" | Where-Object {$_} | ForEach-Object {
        $p=$_ -split "`t"
        [pscustomobject]@{
            Start=[int]$p[1]; End=[int]$p[2]
            Text=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p[3]))
            Original=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p[4]))
            Censored=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($p[5]))
            State=$p[6]; Stale=$p[7] -eq 'True'
        }
    })
}
function Expect($hwnd,$state,$stale,$message) {
    $row=(Rows $hwnd)[0]
    Assert ($row.State -eq $state -and $row.Stale -eq $stale) "$message (state=$($row.State), stale=$($row.Stale))"
}
function Expect-Consistent($hwnd,$uncensored,$message) {
    $row=(Rows $hwnd)[0]
    if($row.Text -eq $row.Original){$valid=-not $row.Stale -and $row.State -eq $uncensored}
    elseif($row.Text -eq $row.Censored){$valid=-not $row.Stale -and $row.State -eq 'Censored'}
    else{$valid=$row.Stale}
    Assert $valid "$message (state=$($row.State), stale=$($row.Stale))"
}
function Review($hwnd,$index,$censor,$expected=$true) {
    $path=New-ReplyPath
    $hook.ReviewResult($hwnd,$index,$censor,$path)
    Assert ((Reply $path) -eq $expected.ToString()) 'Sidebar action returned expected result'
}
function Seed($hwnd,$leave=$false) {
    $path=New-ReplyPath
    $hook.SeedReview($hwnd,2,6,$leave,$path)
    Assert ((Reply $path) -eq 'ok') 'Debug review fixture initialized without editing the document'
}
function Scan($hwnd) {
    $path=New-ReplyPath
    $hook.StartWindowScan($hwnd,$path)
    Assert (((Reply $path)|ConvertFrom-Json).outcome -eq 'ok') 'Real scan completed'
}
function Is-Reviewed($hwnd) {
    $path=New-ReplyPath
    $hook.InspectWindows($path)
    $line=@((Reply $path) -split "`n" | Where-Object {$_ -like "$hwnd|*"})[0]
    ($line -split '\|')[5] -eq 'True'
}
$word=$null
try {
    $word=New-Object -ComObject Word.Application
    $word.DisplayAlerts=0
    $hook=$word.COMAddIns.Item('Genizah').Object
    $doc=$word.Documents.Add()
    $doc.Content.Text='p '+$name+' tail'
    $hwnd=$doc.ActiveWindow.Hwnd
    $doc.UndoClear()
    Scan $hwnd
    Expect $hwnd 'Censored' $false 'Scan is censored'
    Assert ($doc.Undo()) 'Initial one-name scan can be undone'
    Expect $hwnd 'Pending' $false 'Undo restores Pending automatically'
    Assert ($doc.Redo()) 'Scan can be redone'
    Expect $hwnd 'Censored' $false 'Redo restores Censored automatically'
    Review $hwnd 0 $false
    Expect $hwnd 'Left' $false 'Restoration records the explicit Leave decision'
    Review $hwnd 0 $true
    Assert ($doc.Undo()) 'Individual censor can be undone'
    Expect $hwnd 'Left' $false 'Explicit Leave survives undo of censor'
    Assert ($doc.Redo()) 'Individual censor can be redone'
    Expect $hwnd 'Censored' $false 'Individual redo updates automatically'
    Review $hwnd 0 $false
    Assert ($doc.Undo()) 'Restoration can be undone'
    Expect $hwnd 'Censored' $false 'Undo restoration updates automatically'
    Assert ($doc.Redo()) 'Restoration can be redone'
    Expect $hwnd 'Left' $false 'Redo restoration returns to Left'
    $row=(Rows $hwnd)[0]
    $doc.Range($row.Start,($row.Start+1)).Text='Z'
    # Range.Text automation is silent; a real user edit also changes the document selection.
    $end=$doc.Content.End-1
    $doc.Range($end,$end).Select()
    Expect $hwnd 'Left' $true 'Partial manual edit disables only this row'
    $edited=$doc.Content.Text
    Review $hwnd 0 $true $false
    Assert ($doc.Content.Text -eq $edited) 'Stale action makes no document edit'
    Assert ($doc.Undo()) 'Manual partial edit is still the next undo action'
    Expect $hwnd 'Left' $false 'Undo partial edit recovers the row'
    $doc.Range(0,0).Text='intro '
    $end=$doc.Content.End-1
    $doc.Range($end,$end).Select()
    Expect $hwnd 'Left' $false 'Prefix edit preserves tracked occurrence'
    Assert (-not (Is-Reviewed $hwnd)) 'Unrelated edits invalidate print freshness'
    Review $hwnd 0 $true
    Assert (-not (Is-Reviewed $hwnd)) 'Later sidebar action does not renew invalidated print freshness'
    $doc.Close([ref]$dontSave)

    # Two panes tracking the same document, with separate remembered Leave decisions.
    $doc=$word.Documents.Add()
    $doc.Content.Text='p '+$name+' tail'
    $first=$doc.ActiveWindow.Hwnd
    Seed $first
    $otherWindow=$doc.Windows.Add()
    $second=$otherWindow.Hwnd
    Seed $second $true
    $doc.UndoClear()
    Review $first 0 $true
    Expect $first 'Censored' $false 'First pane reflects its censor'
    Expect-Consistent $second 'Left' 'Other pane reconciles or disables its changed range'
    $word.CommandBars.ExecuteMso('Undo')
    Expect $first 'Pending' $false 'Ribbon undo restores first pane Pending'
    Expect-Consistent $second 'Left' 'Other pane remains consistent after ribbon undo'
    $word.CommandBars.ExecuteMso('Redo')
    Expect $first 'Censored' $false 'Ribbon redo refreshes first pane'
    Expect-Consistent $second 'Left' 'Ribbon redo refreshes the other pane'
    $doc.Undo()|Out-Null
    $doc.Redo()|Out-Null
    Expect $first 'Censored' $false 'Rapid undo/redo ends in correct state'
    $before=$doc.Content.Text
    $doc.Saved=$true
    $doc.Range(0,0).Select()
    Rows $first | Out-Null
    Assert ($doc.Content.Text -eq $before -and $doc.Saved) 'Reconciliation does not edit or dirty the document'
    Assert ($doc.Undo()) 'Reconciliation adds no undo entry'
    Assert (-not $doc.Undo()) 'Only the original censor occupied undo history'
    $doc.Close([ref]$dontSave)

    $doc=$word.Documents.Add()
    $doc.Content.Text=($name+' gap ')*3
    $hwnd=$doc.ActiveWindow.Hwnd
    $doc.UndoClear()
    Scan $hwnd
    Assert ($doc.Undo()) 'Batch scan can be undone'
    $batch=Rows $hwnd
    Assert (@($batch|Where-Object{$_.Stale}).Count -eq 3) 'Batch undo disables broken rows without relocating them'
    $before=$doc.Content.Text
    Review $hwnd 0 $true $false
    Assert ($doc.Content.Text -eq $before) 'Broken batch range cannot overwrite document text'
    $doc.Redo()|Out-Null
    Assert (@((Rows $hwnd)|Where-Object{$_.Stale}).Count -eq 3) 'Redo does not pretend broken batch ranges recovered'
    $doc.Undo()|Out-Null
    Scan $hwnd
    Assert (@((Rows $hwnd)|Where-Object{$_.Stale}).Count -eq 0) 'Rescanning replaces broken ranges'
    $doc.Undo()|Out-Null
    $doc.Redo()|Out-Null
    Assert (@((Rows $hwnd)|Where-Object{$_.Stale}).Count -eq 3) 'Rapid batch undo/redo still disables broken ranges'
    $doc.Close([ref]$dontSave)
    Write-Host "All reconciliation assertions passed. Evidence: $OutputDirectory"
} finally {
    if($null -ne $word){$word.Quit([ref]$dontSave)}
}
