param(
    [string]$OutputDir = "test/corpus/features"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-Utf8Bytes {
    param([string]$Text)
    return [System.Text.Encoding]::UTF8.GetBytes($Text)
}

function Add-ZipEntry {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$Name,
        [byte[]]$Bytes
    )

    $entry = $Archive.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function New-SectionXml {
    param([string]$BodyXml)

    return "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?>" +
        "<hs:sec xmlns:hp=`"http://www.hancom.co.kr/hwpml/2011/paragraph`" " +
        "xmlns:hs=`"http://www.hancom.co.kr/hwpml/2011/section`" " +
        "xmlns:hh=`"http://www.hancom.co.kr/hwpml/2011/head`">" +
        $BodyXml +
        "</hs:sec>"
}

function New-ParagraphXml {
    param([string]$InnerXml)

    return "<hp:p id=`"0`"><hp:run>" + $InnerXml + "</hp:run></hp:p>"
}

function New-BaseEntries {
    param([string]$SectionXml)

    return [ordered]@{
        "mimetype" = New-Utf8Bytes "application/hwp+zip"
        "version.xml" = New-Utf8Bytes "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><hv:HCFVersion xmlns:hv=`"http://www.hancom.co.kr/hwpml/2011/version`" tagetApplication=`"WORDPROCESSOR`" major=`"5`" minor=`"1`" micro=`"1`" buildNumber=`"0`" os=`"1`" xmlVersion=`"1.5`" application=`"OpenHWP SDK fixture`" appVersion=`"fixture`"/>"
        "Contents/content.hpf" = New-Utf8Bytes "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><opf:package xmlns:opf=`"http://www.idpf.org/2007/opf/`" version=`"1.0`"><opf:manifest><opf:item id=`"header`" href=`"Contents/header.xml`" media-type=`"application/xml`"/><opf:item id=`"section0`" href=`"Contents/section0.xml`" media-type=`"application/xml`"/></opf:manifest><opf:spine><opf:itemref idref=`"header`"/><opf:itemref idref=`"section0`"/></opf:spine></opf:package>"
        "Contents/header.xml" = New-Utf8Bytes "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><hh:head xmlns:hh=`"http://www.hancom.co.kr/hwpml/2011/head`" version=`"1.5`" secCnt=`"1`"/>"
        "Contents/section0.xml" = New-Utf8Bytes $SectionXml
        "META-INF/container.xml" = New-Utf8Bytes "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><ocf:container xmlns:ocf=`"urn:oasis:names:tc:opendocument:xmlns:container`"><ocf:rootfiles><ocf:rootfile full-path=`"Contents/content.hpf`" media-type=`"application/hwpml-package+xml`"/></ocf:rootfiles></ocf:container>"
        "META-INF/manifest.xml" = New-Utf8Bytes "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><odf:manifest xmlns:odf=`"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0`"/>"
        "Preview/PrvText.txt" = New-Utf8Bytes "OpenHWP SDK feature fixture"
    }
}

function Write-HwpxFixture {
    param(
        [string]$FileName,
        [string]$SectionXml,
        [hashtable]$ExtraEntries = @{}
    )

    $outputPath = Join-Path $OutputDir $FileName
    $parent = Split-Path -Parent $outputPath
    if (!(Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath
    }

    $archive = [System.IO.Compression.ZipFile]::Open($outputPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $entries = New-BaseEntries $SectionXml
        foreach ($key in $ExtraEntries.Keys) {
            $entries[$key] = $ExtraEntries[$key]
        }
        foreach ($key in $entries.Keys) {
            Add-ZipEntry $archive $key $entries[$key]
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host $outputPath
}

$headerFooterBody = New-ParagraphXml (
    "<hp:header><hp:subList><hp:p><hp:run><hp:t>Header fixture</hp:t></hp:run></hp:p></hp:subList></hp:header>" +
    "<hp:footer><hp:subList><hp:p><hp:run><hp:t>Footer fixture</hp:t></hp:run></hp:p></hp:subList></hp:footer>" +
    "<hp:header idRef=`"header-ref`" applyPageType=`"BOTH`"/><hp:footer idRef=`"footer-ref`" applyPageType=`"EVEN`"/>"
)
Write-HwpxFixture "header-footer.hwpx" (New-SectionXml $headerFooterBody)

$noteBody = New-ParagraphXml (
    "<hp:footNote><hp:subList><hp:p><hp:run><hp:t>Footnote fixture</hp:t></hp:run></hp:p></hp:subList></hp:footNote>" +
    "<hp:endNote><hp:subList><hp:p><hp:run><hp:t>Endnote fixture</hp:t></hp:run></hp:p></hp:subList></hp:endNote>" +
    "<hp:memo><hp:subList><hp:p><hp:run><hp:t>Memo fixture</hp:t></hp:run></hp:p></hp:subList></hp:memo>" +
    "<hp:comment><hp:p><hp:run><hp:t>Comment fixture</hp:t></hp:run></hp:p></hp:comment>"
)
Write-HwpxFixture "footnote-endnote.hwpx" (New-SectionXml $noteBody)

$fieldBody = New-ParagraphXml (
    "<hp:fieldBegin name=`"field-a`"/><hp:fieldEnd/>" +
    "<hp:press name=`"press-a`"><hp:t>Press field fixture</hp:t></hp:press>" +
    "<hp:checkBox name=`"check-a`"/><hp:radioBtn name=`"radio-a`"/>" +
    "<hp:comboBox name=`"combo-a`"/><hp:editField name=`"edit-a`"/>"
)
Write-HwpxFixture "press-field-form.hwpx" (New-SectionXml $fieldBody)

$referenceBody = New-ParagraphXml (
    "<hp:bookmark name=`"bookmark-a`"/><hp:caption><hp:p><hp:run><hp:t>Caption fixture</hp:t></hp:run></hp:p></hp:caption>" +
    "<hp:crossRef target=`"bookmark-a`"/><hp:tocMark/><hp:indexmark/><hp:hyperlink target=`"https://example.invalid`"/>"
)
Write-HwpxFixture "caption-crossref-toc.hwpx" (New-SectionXml $referenceBody)

$equationBody = New-ParagraphXml "<hp:equation id=`"eq-a`"><hp:t>sqrt { x }</hp:t></hp:equation>"
Write-HwpxFixture "equation.hwpx" (New-SectionXml $equationBody)

$shapeBody = New-ParagraphXml (
    "<hp:line/><hp:rect/><hp:ellipse/><hp:arc/><hp:polygon/><hp:curve/>" +
    "<hp:container><hp:rect/></hp:container><hp:group><hp:line/></hp:group>" +
    "<hp:textBox><hp:p><hp:run><hp:t>Text box fixture</hp:t></hp:run></hp:p></hp:textBox><hp:textart/><hp:shapeObject/>"
)
Write-HwpxFixture "shape-textbox.hwpx" (New-SectionXml $shapeBody)

$objectBody = New-ParagraphXml "<hp:chart id=`"chart-a`"/><hp:oleObject id=`"ole-a`"/><hp:video id=`"video-a`"/><hp:sound id=`"sound-a`"/>"
$objectExtra = @{
    "BinData/oleObject1.bin" = New-Utf8Bytes "ole-fixture"
    "BinData/video1.mp4" = New-Utf8Bytes "video-fixture"
    "BinData/sound1.mp3" = New-Utf8Bytes "sound-fixture"
}
Write-HwpxFixture "chart-ole.hwpx" (New-SectionXml $objectBody) $objectExtra

$tableBody = "<hp:p id=`"0`"><hp:run><hp:tbl><hp:tr><hp:tc><hp:cellSpan rowSpan=`"2`" colSpan=`"1`"/><hp:p><hp:run><hp:t>Outer cell</hp:t></hp:run></hp:p><hp:tbl><hp:tr><hp:tc><hp:p><hp:run><hp:t>Nested cell</hp:t></hp:run></hp:p></hp:tc></hp:tr></hp:tbl></hp:tc></hp:tr></hp:tbl></hp:run></hp:p>"
Write-HwpxFixture "table-authoring.hwpx" (New-SectionXml $tableBody)
