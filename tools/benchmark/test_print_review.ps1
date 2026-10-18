<# Checks explicit review-pane scans and quick print decisions; never submits print jobs. #>
param([string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('genizah-print-review-' + [guid]::NewGuid())))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PrintDialogWindows {
    private delegate bool EnumProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    public static IntPtr FindNext(IntPtr after, string title) { return FindWindowEx(IntPtr.Zero, after, null, title); }
    public static string Describe(uint process) {
        var names = new System.Collections.Generic.List<string>();
        EnumWindows((hwnd, parameter) => {
            uint owner; GetWindowThreadProcessId(hwnd, out owner);
            if (owner == process) { var title=new System.Text.StringBuilder(512); GetWindowText(hwnd,title,512); names.Add(hwnd+": "+title); }
            return true;
        }, IntPtr.Zero);
        return string.Join("\n",names);
    }
}
'@
function Assert($condition,$message){if(-not $condition){throw "FAIL: $message"};Write-Host "PASS: $message"}
function New-ReplyPath { Join-Path $OutputDirectory ([guid]::NewGuid().ToString()+'.txt') }
function Reply($path){
    $deadline=[DateTime]::UtcNow.AddMinutes(2)
    while(-not [IO.File]::Exists($path)){
        if([DateTime]::UtcNow -gt $deadline){throw "Timed out: $path"}
        Start-Sleep -Milliseconds 50
    }
    while($true){
        try{$reply=[IO.File]::ReadAllText($path);break}
        catch [IO.IOException]{if([DateTime]::UtcNow -gt $deadline){throw};Start-Sleep -Milliseconds 50}
    }
    if($reply.StartsWith('ERROR:')){throw $reply}
    $reply
}
function Windows {
    $path=New-ReplyPath; $hook.InspectWindows($path); $reply=Reply $path
    if(-not $script:verifiedBuild){
        Assert ($reply -like '*Genizah/bin/Debug/Genizah.dll*') 'Workspace Debug add-in loaded'
        $script:verifiedBuild=$true
    }
    $states=@{}
    foreach($line in ($reply -split "`n"|Select-Object -Skip 1)){
        if(-not $line){continue};$p=$line -split '\|'
        $states[[int]$p[0]]=[pscustomobject]@{Visible=$p[2] -eq 'True';Count=[int]$p[3];Pending=[int]$p[4];Reviewed=$p[5] -eq 'True';Scanning=$p[6] -eq 'True'}
    }
    return ,$states
}
function Open-Review($hwnd){$path=New-ReplyPath;$hook.OpenReview($hwnd,$path);Assert ((Reply $path) -eq 'ok') 'Review pane opened'}
function View($hwnd){$path=New-ReplyPath;$hook.InspectReviewUi($hwnd,$path);Reply $path}
function Print-Probe($hwnd,$choice){$path=New-ReplyPath;$hook.ProbeBeforePrint($hwnd,$choice,$path);(Reply $path)|ConvertFrom-Json}
function Wait-Scan($hwnd){
    $deadline=[DateTime]::UtcNow.AddMinutes(2)
    do{
        $state=(Windows)[$hwnd]
        if($null -ne $state -and -not $state.Scanning){return $state}
        if([DateTime]::UtcNow -gt $deadline){throw 'Scan did not finish'}
        Start-Sleep -Milliseconds 100
    }while($true)
}
function Seed($hwnd){$path=New-ReplyPath;$hook.SeedReview($hwnd,2,6,$false,$path);Assert ((Reply $path) -eq 'ok') 'Pending review fixture seeded'}
function Real-PrintDialog($hwnd,$buttonText){
    $word.Visible=$true
    $wordProcessId=[uint32]0
    [PrintDialogWindows]::GetWindowThreadProcessId([IntPtr]$hwnd,[ref]$wordProcessId)|Out-Null
    $path=New-ReplyPath
    $hook.ProbeBeforePrint($hwnd,-1,$path)
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    $dialog=$null
    while($null -eq $dialog -and [DateTime]::UtcNow -lt $deadline){
        if([IO.File]::Exists($path)){Reply $path|Out-Null;throw 'Print handler returned without a dialog'}
        $handle=[IntPtr]::Zero
        do{
            $handle=[PrintDialogWindows]::FindNext($handle,'גניזה — הדפסה')
            if($handle -eq [IntPtr]::Zero){break}
            $dialogProcessId=[uint32]0
            [PrintDialogWindows]::GetWindowThreadProcessId($handle,[ref]$dialogProcessId)|Out-Null
            if($dialogProcessId -eq $wordProcessId){
                $dialog=[Windows.Automation.AutomationElement]::FromHandle($handle)
                break
            }
        }while($true)
        if($null -eq $dialog){Start-Sleep -Milliseconds 100}
    }
    if($null -eq $dialog){Write-Host ([PrintDialogWindows]::Describe($wordProcessId))}
    Assert ($null -ne $dialog) 'Real print-choice dialog appeared'
    if($buttonText -eq 'close'){
        $dialog.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    }else{
        $condition=New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty),$buttonText
        $button=$dialog.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
        Assert ($null -ne $button) "Explicit dialog button exists: $buttonText"
        $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    (Reply $path)|ConvertFrom-Json
}
$word=$null;$dontSave=0
try{
    $word=New-Object -ComObject Word.Application
    $word.DisplayAlerts=0
    $hook=$word.COMAddIns.Item('Genizah').Object
    $doc=$word.Documents.Add()
    $doc.Content.Text=('הילד הלך אל החנות וקנה לחם וחלב. '*4000)
    $hwnd=$doc.ActiveWindow.Hwnd
    $before=$doc.Content.Text
    $doc.UndoClear();$doc.Saved=$true
    Assert ((Windows).Count -eq 0) 'No startup review pane or automatic scan'
    $probe=Print-Probe $hwnd 0
    Assert ($probe.status -eq 'Unreviewed' -and -not $probe.cancelled) 'Unreviewed document can print without checking'
    Assert ($probe.elapsed_ms -lt 1000) "Print decision is quick on $($before.Length) characters ($($probe.elapsed_ms) ms)"
    Assert ((Windows).Count -eq 0) 'Printing without checking creates no pane or scan'
    $probe=Print-Probe $hwnd 2
    Assert $probe.cancelled 'Cancel cancels only this print attempt'
    Assert ($doc.Content.Text -eq $before -and $doc.Saved -and -not $doc.Undo()) 'Print choices leave document and undo history untouched'
    $actual=Real-PrintDialog $hwnd 'הדפס ללא בדיקה'
    Assert (-not $actual.cancelled -and $actual.status -eq 'Unreviewed') 'Real Print without checking button permits printing'
    $actual=Real-PrintDialog $hwnd 'close'
    Assert $actual.cancelled 'Closing the print-choice dialog cancels printing'
    $doc.Close([ref]$dontSave)

    $doc=$word.Documents.Add();$doc.Content.Text='Ordinary candidate-free text.';$hwnd=$doc.ActiveWindow.Hwnd
    Open-Review $hwnd
    $state=Wait-Scan $hwnd
    Assert ($state.Reviewed -and $state.Count -eq 0) 'First review click automatically completes a clean scan'
    Assert ((View $hwnd) -like '*סרוק שוב*') 'Completed empty scan offers Scan again'
    $view=View $hwnd
    $path=New-ReplyPath;$hook.SetPaneVisible($hwnd,$false,$path);Reply $path|Out-Null
    Open-Review $hwnd
    Assert (-not (Windows)[$hwnd].Scanning -and (View $hwnd) -eq $view) 'Reopening a completed zero-result scan preserves the review'
    $path=New-ReplyPath;$hook.RequestPaneScan($hwnd,$path);Assert ((Reply $path) -eq 'ok') 'Footer Scan again button invoked'
    $state=Wait-Scan $hwnd
    Assert ($state.Reviewed -and $state.Count -eq 0) 'Footer action completes a fresh scan'
    $probe=Print-Probe $hwnd 2
    Assert ($probe.status -eq 'Current' -and -not $probe.cancelled) 'Current clean review prints without any prompt'
    $doc.Close([ref]$dontSave)

    $name=[string][char]0x05D9+[char]0x05D4+[char]0x05D5+[char]0x05D4
    $doc=$word.Documents.Add();$doc.Content.Text='p '+$name+' tail';$hwnd=$doc.ActiveWindow.Hwnd
    Seed $hwnd
    $probe=Print-Probe $hwnd 0
    Assert ($probe.status -eq 'Pending' -and -not $probe.cancelled) 'Pending review supports Print anyway'
    $view=View $hwnd
    $path=New-ReplyPath;$hook.SetPaneVisible($hwnd,$false,$path);Reply $path|Out-Null
    $probe=Print-Probe $hwnd 1
    Assert ($probe.cancelled -and $probe.status -eq 'Pending') 'Review choice cancels print rather than starting over'
    Open-Review $hwnd
    Assert ((View $hwnd) -eq $view) 'Reopening preserves existing review contents and expansion state'
    Assert ((Windows)[$hwnd].Pending -eq 1) 'Reopening preserves existing decisions'
    $path=New-ReplyPath;$hook.ReviewResult($hwnd,0,$false,$path);Assert ((Reply $path) -eq 'True') 'Leave action works'
    $probe=Print-Probe $hwnd 2
    Assert ($probe.status -eq 'Current' -and -not $probe.cancelled) 'Resolved Leave is trusted for printing'
    $doc.Range(0,0).Text='new '
    $probe=Print-Probe $hwnd 0
    Assert ($probe.status -eq 'Unreviewed' -and -not $probe.cancelled) 'Print snapshot check detects edits even before reconciliation'
    $doc.Close([ref]$dontSave)

    $doc=$word.Documents.Add();$doc.Content.Text=$name+' '+$name;$hwnd=$doc.ActiveWindow.Hwnd
    $probe=Print-Probe $hwnd 1
    Assert ($probe.status -eq 'Unreviewed' -and $probe.cancelled) 'Check first cancels print immediately'
    $state=Wait-Scan $hwnd
    Assert ($state.Visible -and $state.Reviewed -and $state.Count -eq 2 -and $doc.Content.Text -notlike "*$name*") 'Check first explicitly starts a full scan in the pane'
    $view=View $hwnd
    $path=New-ReplyPath;$hook.SetPaneVisible($hwnd,$false,$path);Reply $path|Out-Null
    Open-Review $hwnd
    Assert ((View $hwnd) -eq $view) 'Reopening completed results does not rescan or reset the view'
    $doc.Close([ref]$dontSave)

    $doc=$word.Documents.Add();$doc.Content.Text=($name+' gap ')*160;$hwnd=$doc.ActiveWindow.Hwnd
    Open-Review $hwnd
    Assert ((Windows)[$hwnd].Scanning) 'Explicit longer scan is running'
    Open-Review $hwnd
    Assert ((View $hwnd) -like 'progress|*') 'Reopening an ongoing scan shows its progress'
    $probe=Print-Probe $hwnd 0
    Assert ($probe.status -eq 'Scanning' -and -not $probe.cancelled) 'Running scan does not force the user to wait before printing unchecked'
    $probe=Print-Probe $hwnd 1
    Assert ($probe.status -eq 'Scanning' -and $probe.cancelled) 'Returning to an ongoing scan cancels only the print attempt'
    $state=Wait-Scan $hwnd
    Assert ($state.Count -eq 160 -and $state.Reviewed) 'Reopening and print choices do not restart or duplicate the ongoing scan'
    $doc.Close([ref]$dontSave)
    Write-Host "All print/review assertions passed. Evidence: $OutputDirectory"
}finally{if($null -ne $word){$word.Quit([ref]$dontSave)}}
