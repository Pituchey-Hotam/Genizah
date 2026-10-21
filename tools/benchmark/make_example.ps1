<#
.SYNOPSIS
Builds the example document: hand-written prose around Bible quotes and liturgy on the first
page, on the second the name in every place Word keeps text outside the body, and on the third
the name beside fields.

.DESCRIPTION
The second page puts the name in a table and a table nested in it, a content control, a text
box, a shape with text, grouped text boxes, the section's header (and a text box in it) and
footer, a footnote and an endnote: all printed, so a scan must censor every one. It also puts
the name in a review comment, which isn't printed and must be left alone. The third page puts
the name beside fields and shape anchors, which censoring must leave whole: at the start of a
link's text, in a link inside a content control, in a table of contents and the heading right
after it, and at the start of a paragraph a text box is anchored to (as the header is too); the
footer has a page number field.

Each case uses havaya, which is always censored, so whether it was censored doesn't depend on
the model. Returns the cases: where the name is, the text that follows it (unique in the
document), and whether a scan should censor it. test_example.ps1 checks them.

.EXAMPLE
powershell -File tools\benchmark\make_example.ps1
#>
param(
    [string]$Path = (Join-Path $PSScriptRoot 'out\example.docx'),
    # A visible Word to build in (Word groups shapes only when visible); by default one is started and quit.
    $Word = $null
)

$ErrorActionPreference = 'Stop'
$name = [string][char]0x05D9 + [char]0x05D4 + [char]0x05D5 + [char]0x05D4

$wdSectionBreakNextPage = 2
$wdContentControlRichText = 0
$wdHeaderFooterPrimary = 1
$wdWrapSquare = 0
$wdWrapTopBottom = 4
$wdRelativeHorizontalPositionColumn = 2
$wdRelativeVerticalPositionParagraph = 2
$wdShapeRight = -999996
$msoTextOrientationHorizontal = 1
$msoShapeRectangle = 1
$wdStyleHeading1 = -2
$wdFieldPage = 33
$wdFormatDocumentDefault = 16
$wdDoNotSaveChanges = 0

$cases = New-Object System.Collections.Generic.List[object]
function Add-Case([string]$where, [string]$before, [string]$after, [bool]$censor = $true) {
    $cases.Add([pscustomobject]@{ Where = $where; After = $after; Censor = $censor })
    $before + $name + $after
}

# Right to left, in the body's font, for any story's text.
function Format-Hebrew($range, [int]$size = 15) {
    $range.ParagraphFormat.ReadingOrder = 0
    $range.Font.NameBi = 'David'
    $range.Font.SizeBi = $size
}

function New-TextBox($shapes, $anchor, [string]$text, [float]$width = 260) {
    $box = $shapes.AddTextbox($msoTextOrientationHorizontal, 0, 0, $width, 34, $anchor)
    $box.TextFrame.TextRange.Text = $text
    Format-Hebrew $box.TextFrame.TextRange 13
    $box
}

# Floats a shape on the lines of its own empty paragraph, by default at the column's right edge.
function Set-BelowAnchor($shape, [float]$left = $wdShapeRight) {
    $shape.WrapFormat.Type = $wdWrapTopBottom
    $shape.RelativeHorizontalPosition = $wdRelativeHorizontalPositionColumn
    $shape.RelativeVerticalPosition = $wdRelativeVerticalPositionParagraph
    $shape.Left = $left
    $shape.Top = 0
}

$quit = $null -eq $Word
if ($quit) {
    $Word = New-Object -ComObject Word.Application
    $Word.Visible = $true
    $Word.DisplayAlerts = 0
}
try {
    $lines = (Get-Content (Join-Path $PSScriptRoot 'example.txt') -Encoding UTF8) | Where-Object { $_ }
    $doc = $Word.Documents.Add()
    $doc.Content.Text = ($lines -join "`r")
    # The first page's quote of Psalms 145:18, a control that the body is scanned at all.
    $cases.Add([pscustomobject]@{ Where = 'body'; After = ' לכל קראיו'; Censor = $true })

    $end = $doc.Content.End - 1
    $doc.Range($end, $end).InsertBreak($wdSectionBreakNextPage)
    $paragraphs = @(
        'מקרי קצה'
        'בעמוד זה השם כתוב במקומות שמחוץ לגוף הטקסט: בטבלה, בפקד תוכן, בתיבות טקסט ובצורות, בכותרת העליונה והתחתונה, ובהערות שוליים וסיום. כל אלה מודפסים, ולכן כולם צריכים להיסרק.'
        'טבלה, ובתוכה טבלה מקוננת:'
        ''
        'פקד תוכן:'
        ''
        'תיבת טקסט:'
        ''
        'צורה ובה טקסט:'
        ''
        'תיבות טקסט מקובצות:'
        ''
        'למשפט זה מוצמדות הערת שוליים והערת סיום.'
        'למשפט זה מוצמדת הערת סקירה. היא אינה מודפסת, ולכן השם שבה נשאר כמות שהוא.'
    )
    $end = $doc.Content.End - 1
    $doc.Range($end, $end).InsertAfter($paragraphs -join "`r")

    $all = $doc.Content
    Format-Hebrew $all
    $all.ParagraphFormat.Alignment = 3             # justified
    $all.ParagraphFormat.SpaceAfter = 10
    $all.ParagraphFormat.LineSpacingRule = 5       # multiple
    $all.ParagraphFormat.LineSpacing = 15
    foreach ($title in @($doc.Paragraphs.Item(1).Range, $doc.Sections.Item(2).Range.Paragraphs.Item(1).Range)) {
        $title.Font.SizeBi = 22
        $title.Font.BoldBi = 1
        $title.ParagraphFormat.Alignment = 1       # centered
        $title.ParagraphFormat.SpaceAfter = 18
    }

    $section = $doc.Sections.Item(2)
    function Paragraph([int]$i) { $section.Range.Paragraphs.Item($i).Range }

    # Comment and notes first: the table and content control below change paragraph numbers.
    $p = Paragraph 14
    $doc.Comments.Add($p, (Add-Case 'comment' 'לבדוק: ' ' הוא האלהים' $false)) | Out-Null
    $p = Paragraph 13
    $at = $doc.Range($p.End - 1, $p.End - 1)
    $note = $doc.Footnotes.Add($at)
    $note.Range.Text = Add-Case 'footnote' 'ראו שמות טו, יח: ' ' ימלך לעלם ועד'
    Format-Hebrew $note.Range 11
    $at = $doc.Range((Paragraph 13).End - 1, (Paragraph 13).End - 1)
    $note = $doc.Endnotes.Add($at)
    $note.Range.Text = Add-Case 'endnote' 'ראו שמות טו, ג: ' ' איש מלחמה'
    Format-Hebrew $note.Range 11

    Set-BelowAnchor (New-TextBox $doc.Shapes (Paragraph 8) (Add-Case 'text box' 'גדול ' ' ומהלל מאד'))
    $shape = $doc.Shapes.AddShape($msoShapeRectangle, 0, 0, 260, 34, (Paragraph 10))
    $shape.Fill.ForeColor.RGB = 0xF2E6DA
    $shape.TextFrame.TextRange.Text = Add-Case 'shape' '' ' מלך גאות לבש'
    Format-Hebrew $shape.TextFrame.TextRange 13
    $shape.TextFrame.TextRange.Font.Color = 0
    Set-BelowAnchor $shape
    # Placed before grouping: a group keeps its parts' positions but ignores its own.
    $a = New-TextBox $doc.Shapes (Paragraph 12) (Add-Case 'grouped text box' '' ' עז לעמו יתן') 200
    Set-BelowAnchor $a 215
    $b = New-TextBox $doc.Shapes (Paragraph 12) (Add-Case 'grouped text box' '' ' יברך את עמו בשלום') 200
    Set-BelowAnchor $b 0
    # Word refuses to group shapes on a page it hasn't laid out for the window yet.
    (Paragraph 12).Select()
    $doc.Shapes.Range([object[]]@($a.Name, $b.Name)).Group().WrapFormat.Type = $wdWrapTopBottom

    $cc = $doc.ContentControls.Add($wdContentControlRichText, (Paragraph 6))
    $cc.Range.Text = Add-Case 'content control' 'זה היום עשה ' ' נגילה ונשמחה בו'

    $table = $doc.Tables.Add((Paragraph 4), 2, 2)
    $table.Borders.Enable = $true
    $table.Cell(1, 1).Range.Text = Add-Case 'table' 'ברוך ' ' יום יום'
    $table.Cell(1, 2).Range.Text = 'תהלים סח, כ'
    $table.Cell(2, 1).Range.Text = 'תהלים כג, א'
    $inner = $table.Cell(2, 2).Range
    $inner.Collapse(1)
    $nested = $doc.Tables.Add($inner, 1, 1)
    $nested.Borders.Enable = $true
    $nested.Cell(1, 1).Range.Text = Add-Case 'nested table' '' ' רעי לא אחסר'

    # Page three: the name beside Word's fields, which censoring must leave whole.
    $first = $doc.Paragraphs.Count + 1
    $end = $doc.Content.End - 1
    $doc.Range($end, $end).InsertAfter("`r" + (@(
        'שדות'
        'כאן השם צמוד לשדות ולעוגנים: בקישור, בקישור שבתוך פקד תוכן, בתוכן העניינים ובכותרת שאחריו, ובראש פסקה שתיבת טקסט מעוגנת אליה. הצנזור משנה רק את האותיות, והשדות והתיבות נשארים שלמים.'
        ''
        'פרק א: ' + (Add-Case 'heading after the table of contents' '' ' מחסי')
        'קישור: ' + (Add-Case 'link' '' ' עוז למו')
        (Add-Case 'start of a paragraph a text box is anchored to' '' ' מעוז חיי, ממי אפחד')
        'פקד תוכן ובו קישור:'
        ''
        'פרק ב: ' + (Add-Case 'table of contents entry' '' ' אורי וישעי')
        'סוף הדוגמה.'
    ) -join "`r"))
    function Field([int]$i) { $doc.Paragraphs.Item($first + $i - 1).Range }
    $title = Field 1
    $title.ParagraphFormat.PageBreakBefore = $true
    $title.Font.SizeBi = 22
    $title.Font.BoldBi = 1
    $title.ParagraphFormat.Alignment = 1
    $title.ParagraphFormat.SpaceAfter = 18
    foreach ($i in 4, 9) {
        $heading = Field $i
        $heading.Style = $wdStyleHeading1
        Format-Hebrew $heading 16
        $heading.Font.BoldBi = 1
    }
    # The name starts the link's text, beside its hidden opening mark and code.
    $p = Field 5
    $doc.Hyperlinks.Add($doc.Range($p.Start + 'קישור: '.Length, $p.End - 1), 'https://example.com/') | Out-Null
    # Word anchors a floating shape before its paragraph's first letter, here the name's.
    $box = New-TextBox $doc.Shapes (Field 6) 'תיבה מעוגנת' 120
    $box.WrapFormat.Type = $wdWrapSquare
    $box.RelativeHorizontalPosition = $wdRelativeHorizontalPositionColumn
    $box.RelativeVerticalPosition = $wdRelativeVerticalPositionParagraph
    $box.Left = 0
    $box.Top = 0
    $cc = $doc.ContentControls.Add($wdContentControlRichText, (Field 8))
    $cc.Range.Text = 'ראו ' + (Add-Case 'link in a content control' '' ' שפטנו')
    $doc.Hyperlinks.Add($doc.Range($cc.Range.Start + 'ראו '.Length, $cc.Range.End), 'https://example.org/') | Out-Null
    # Last, so it lists both headings; the heading after it starts right after its last mark.
    $doc.TablesOfContents.Add((Field 3)) | Out-Null

    # The second section's own header and footer; the first page keeps none.
    $header = $section.Headers.Item($wdHeaderFooterPrimary)
    $header.LinkToPrevious = $false
    $header.Range.Text = Add-Case 'header, where its text box is anchored' '' ' שמרך. לא ינום ולא יישן שומר ישראל.'
    Format-Hebrew $header.Range 11
    # At the header's left, opposite its right-to-left text; anchored before the name.
    $box = New-TextBox $header.Shapes $header.Range (Add-Case 'header text box' '' ' צלך על יד ימינך')
    $box.RelativeHorizontalPosition = $wdRelativeHorizontalPositionColumn
    $box.RelativeVerticalPosition = $wdRelativeVerticalPositionParagraph
    $box.Left = 0
    $box.Top = 0
    $footer = $section.Footers.Item($wdHeaderFooterPrimary)
    $footer.LinkToPrevious = $false
    $footer.Range.Text = 'עמוד  · ' + (Add-Case 'footer, beside a page number' '' ' ישמר צאתך ובואך')
    Format-Hebrew $footer.Range 11
    $at = $footer.Range.Characters.Item('עמוד '.Length + 1)
    $at.Collapse(1)
    $doc.Fields.Add($at, $wdFieldPage) | Out-Null

    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    $doc.SaveAs2([string]$Path, $wdFormatDocumentDefault)
    $doc.Close([ref]$wdDoNotSaveChanges)
}
finally {
    if ($quit) { $Word.Quit([ref]$wdDoNotSaveChanges) }
}
$cases
