<# Checks scan undo and sidebar review in disposable Word documents. #>
param([string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-undo-' + [guid]::NewGuid())))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
function Assert($condition, $message) {
    if (-not $condition) { throw "FAIL: $message" }
    Write-Host "PASS: $message"
}
function Reply($path) {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    while (-not [IO.File]::Exists($path)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Timed out: $path" }
        Start-Sleep -Milliseconds 100
    }
    [IO.File]::ReadAllText($path)
}
function Scan($document) {
    $path = Join-Path $OutputDirectory ([guid]::NewGuid().ToString() + '.json')
    $hook.StartWindowScan($document.ActiveWindow.Hwnd, $path)
    $timings = (Reply $path) | ConvertFrom-Json
    Assert ($timings.outcome -eq 'ok') 'Scan completed'
}
$word = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.DisplayAlerts = 0
    $hook = $word.COMAddIns.Item('Genizah').Object
    $path = Join-Path $OutputDirectory 'build.txt'
    $hook.InspectWindows($path)
    Assert ((Reply $path) -like '*Genizah/bin/Debug/Genizah.dll*') 'Workspace Debug add-in loaded'
    $doc = $word.Documents.Add()
    $name = [string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4
    $doc.Content.Text = 'AB ' + (($name + ' ') * 30)
    $doc.Range(0, 1).HighlightColorIndex = 6
    $doc.Range(1, 2).HighlightColorIndex = 7
    $original = $doc.Content.Text
    $doc.UndoClear()
    Scan $doc
    Assert ($doc.Content.Text -ne $original) 'Scan censors names'
    Assert ($doc.Bookmarks.Count -eq 0) 'Scan adds no bookmarks'
    Assert ($doc.Range(0, 1).HighlightColorIndex -eq 6) 'First original highlight preserved'
    Assert ($doc.Range(1, 2).HighlightColorIndex -eq 7) 'Second original highlight preserved'
    $censored = $doc.Content.Text
    $doc.Saved = $true
    foreach ($visible in @($false, $true, $false, $true)) {
        $path = Join-Path $OutputDirectory ([guid]::NewGuid().ToString() + '.txt')
        $hook.SetPaneVisible($doc.ActiveWindow.Hwnd, $visible, $path)
        Assert ((Reply $path) -eq 'ok') 'Pane toggled'
    }
    Assert $doc.Saved 'Pane toggles do not dirty the document'
    Assert ($doc.Content.Text -eq $censored) 'Pane toggles preserve text'
    Assert ($doc.Undo()) 'Scan has an undo step'
    Assert ($doc.Content.Text -eq $original) 'One undo restores every censored name'
    Assert (-not $doc.Undo()) 'No marker or bookmark undo entries remain'
    Scan $doc
    Assert ($doc.Content.Text -eq $censored) 'Rescan after undo succeeds'
    Scan $doc
    Assert ($doc.Undo()) 'Original scan remains undoable after rescanning censored text'
    Assert ($doc.Content.Text -eq $original) 'No-op rescan adds no undo step'
    Assert (-not $doc.Undo()) 'Rescan cleanup adds no undo entries'
}
finally {
    if ($null -ne $word) {
        $dontSave = 0
        $word.Quit([ref]$dontSave)
    }
}
