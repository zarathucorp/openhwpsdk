# OpenHwp Automation

[English documentation](README.md)

OpenHwp Automation은 로컬 PC에 설치된 한글 프로그램을 `COM/OLE`로 제어하기 위한 C# 래퍼와 CLI 샘플입니다.

이 저장소는 한컴의 별도 SDK 런타임을 감싸지 않습니다. 대신 현재 Windows PC에 설치된 한글이 노출하는 `HWPFrame.HwpObject` COM 객체를 사용합니다.

## 현재 범위

- 라이브러리: `src/OpenHwp.Automation`
- CLI 샘플: `src/OpenHwp.Automation.Cli`
- 빌드 스크립트: `build.cmd`

핵심 클래스는 `OpenHwp.Automation.HwpSession`입니다.

## 빌드

```bat
build.cmd
```

## 기본 CLI 예제

CLI 버전 확인:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe version
```

새 문서에 텍스트를 넣고 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe new-text C:\temp\hello.hwpx "Hello from OpenHwp"
```

기존 문서를 열어 다른 경로로 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe copy-save C:\temp\in.hwpx C:\temp\out.hwpx
```

PDF 내보내기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf C:\temp\in.hwpx C:\temp\out.pdf
```

문서 정보와 텍스트 읽기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe doc-info C:\temp\in.hwp
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp C:\temp\in.txt
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-page C:\temp\in.hwp 1
```

필드 조회와 쓰기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-exists C:\temp\form.hwp TEST
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-get C:\temp\form.hwp TEST
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-set C:\temp\form.hwpx TEST "updated text" C:\temp\form-out.hwpx
```

제네릭 HWP 액션 실행:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe action InsertText Text="raw action text" --save C:\temp\raw.hwpx
```

## HWPX Form Map 워크플로

`markdown-to-hwpx` 명령은 노출되어 있지 않습니다. 해당 이름의 명령은 Markdown을 HWP 구조와 문자 스타일로 렌더링해야 합니다. 예를 들어 heading은 큰 제목으로, 표는 HWP 표로, bold/code 같은 inline mark는 스타일이 적용된 텍스트로 처리해야 합니다.

기존 양식 템플릿은 cell/cursor 명령으로 채우고 `validate-layout`으로 레이아웃 보존 여부를 확인하는 흐름을 권장합니다.

템플릿 HWPX는 실제 package XML에서 form map을 추출한 뒤 HWP 자동화 또는 package mode로 채울 수 있습니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe extract-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\template-probe.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible apply-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled.hwpx --report C:\temp\filled-apply.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe apply-form-map --package C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled-package.hwpx --report C:\temp\filled-package-apply.md
```

생성된 map에서는 `writeText`, `writeImage`, 지원되는 field `writeValue` 요소만 수정합니다. map은 `content.hpf`, manifest/spine, XML parts, previews, metadata entries를 포함한 전체 HWPX package 구조를 기록하고, HWP paragraph/table 텍스트를 가진 모든 XML part의 write candidate를 표시합니다.

map은 병합/중첩 표를 인식합니다. 표 grid는 `cellAddr`와 `cellSpan`으로 재구성하고, 각 cell의 `currentText`는 nested table 텍스트를 제외합니다. HWP-backed writing 전에는 `probe-form-map`으로 mapped cell/anchor가 실제 HWP에서 선택 가능한지 확인합니다.

`apply-form-map --package`는 COM 없이 text write, package-level image embedding, package press-field write, package checkbox value write를 수행합니다. editor-backed 동작이 필요하면 HWP-backed path를 사용합니다. `--report`는 COM/package mode의 attempted/applied/failed/skipped 결과를 기록하고, package-mode layout validation은 `*.layout.md`로 함께 생성됩니다.

## 기능 커버리지

HWPX package 파일 또는 디렉터리를 스캔해 실제 corpus에 어떤 authoring feature가 있는지 확인합니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe scan-hwpx-features test test\out\hwpx_feature_scan.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe list-header-footer test\corpus\features\header-footer.hwpx test\out\header_footer_inventory.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe set-header-footer-text test\corpus\features\header-footer.hwpx test\out\header_footer_text_write.hwpx --kind header --section section0 --anchor "Header fixture" --text "Updated Header Fixture" --report test\out\header_footer_text_write.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible page-number-set C:\temp\template.hwpx C:\temp\page-numbered.hwpx --draw-pos 5 --side-char - --report C:\temp\page-number-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-fields C:\temp\template.hwpx C:\temp\field-inventory.md --com
```

tracked feature corpus를 재생성하고 스캔할 수 있습니다.

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File tools\New-HwpxFeatureFixtures.ps1
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe scan-hwpx-features test\corpus\features test\out\hwpx_feature_scan_features.md
```

feature scan report는 aggregate count, authoring coverage, missing corpus signal, per-file total, header/footer, field/form, references, notes, embedded object inventory를 포함합니다. Feature scan count는 inventory signal이며, 곧바로 광범위한 write/edit 지원을 의미하지는 않습니다.

## 제출 템플릿 프로파일

지원되는 startup R&D submission profile은 다음 명령으로 실행합니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe fill-submission-template C:\temp\template.hwpx C:\temp\source.md C:\temp\submission-filled.hwpx --profile r-and-d-startup-2026 --asset-root C:\temp --image-mode package --report C:\temp\submission-filled-report.md
```

기본값은 `--markdown-table-mode render`, `--image-mode package`입니다. 본문 Markdown 표는 가장 단순한 unmerged top-level template table style을 복제한 HWPX table object로 렌더링합니다. 원본 HWPX table count 보존이 table semantics보다 중요할 때만 `--markdown-table-mode text`를 사용합니다.

지원되는 Markdown image line은 기본적으로 profile-specific package image writer가 삽입합니다. 원본 image file을 embedding하고, `BinData`/`hp:pic`/manifest entry를 작성하며, image DPI와 96-DPI fallback으로 display size를 계산합니다. 자연 100% 크기가 document body area를 넘을 때만 축소합니다. HWP COM이 안정적이고 editor-backed image insertion이 필요할 때만 `--image-mode com`을 사용합니다.

fill report는 template/profile compatibility, table handling mode, rendered/converted table count, rebuilt row change, style-guard repair, image write result, mapped/unmapped image count, missing-target cause, sibling layout report를 포함합니다. `validate-layout`은 issue를 `expected-change`, `review-needed`, `blocking`으로 분류하며, blocking layout finding이 있을 때만 nonzero exit code를 반환합니다.

## Package 표/셀 편집

Markdown table 값은 기존 HWPX table cell에 넣을 수 있으며, table을 다시 만들 필요가 없습니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-table-list C:\temp\input.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-create-package C:\temp\template.hwpx C:\temp\new-table.hwpx --rows 2 --cols 3 --text "Header A|Header B|Header C;Value 1|Value 2|Value 3" --report C:\temp\new-table-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-row-package C:\temp\template.hwpx C:\temp\row-added.hwpx --table-index 4 --action add --row 1 --count 2 --text "R1C1|R1C2|R1C3|R1C4;R2C1|R2C2|R2C3|R2C4" --report C:\temp\row-added-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-column-package C:\temp\template.hwpx C:\temp\column-added.hwpx --table-index 4 --action add --column 1 --count 1 --text "HNEW;R1NEW;R2NEW" --report C:\temp\column-added-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-merge-package C:\temp\template.hwpx C:\temp\merged.hwpx --table-index 4 --row 1 --column 1 --row-span 2 --col-span 2 --text "Merged cell" --report C:\temp\merged-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-split-package C:\temp\merged.hwpx C:\temp\split.hwpx --table-index 4 --row 1 --column 1 --text "A|B;C|D" --report C:\temp\split-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-style-package C:\temp\template.hwpx C:\temp\styled-cell.hwpx --table-index 4 --row 1 --column 1 --border-fill-id 32 --report C:\temp\styled-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-align-package C:\temp\template.hwpx C:\temp\aligned-cell.hwpx --table-index 4 --row 1 --column 1 --horizontal right --vertical bottom --report C:\temp\aligned-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-background-package C:\temp\template.hwpx C:\temp\background-cell.hwpx --table-index 4 --row 1 --column 1 --color "#FFF2CC" --report C:\temp\background-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-diagonal-package C:\temp\template.hwpx C:\temp\diagonal-cell.hwpx --table-index 4 --row 1 --column 1 --direction both --width "0.15 mm" --color "#000000" --report C:\temp\diagonal-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-size-package C:\temp\template.hwpx C:\temp\equalized-table.hwpx --table-index 11 --equalize-widths --equalize-heights --report C:\temp\equalized-table-report.md
```

- `table-create-package`: HWP COM 없이 새 simple table을 생성합니다.
- `table-row-package`: simple top-level text-cell table의 행을 추가/삭제합니다.
- `table-column-package`: 선택한 body section의 안전한 text-cell table subset에서 열을 추가/삭제합니다.
- `table-merge-package`: 선택한 body section의 simple top-level text-cell table에서 직사각형 cell range를 병합합니다.
- `table-split-package`: 기존 merged top-left cell을 다시 1x1 cell로 분할합니다.
- `table-cell-style-package`: 기존 `Contents/header.xml` borderFill ID를 cell 또는 rectangular range에 적용합니다.
- `table-cell-align-package`: direct cell paragraph의 horizontal alignment 또는 cell vertical alignment를 변경합니다.
- `table-cell-background-package`: 공유 `borderFill` definition을 직접 변경하지 않고 solid cell background를 추가/변경/제거합니다.
- `table-cell-diagonal-package`: 공유 `borderFill` definition을 직접 변경하지 않고 cell diagonal line을 추가/변경/제거합니다.
- `table-cell-size-package`: simple unmerged top-level table의 column width 또는 row height를 균등화합니다.

Package table command는 특정 operation이 구조를 안전하게 보존할 수 없을 때 merged/nested/sparse/irregular/object-containing table을 거부합니다. 의도적인 행/열 변경은 `validate-layout --allow-table-row-change <index>` 또는 `validate-layout --allow-table-column-change <index>`로 검증합니다.

## Editor-backed Copy/Paste

기존 reference document에 필요한 table/control formatting이 이미 있다면 HWP COM-backed rich copy/paste를 사용합니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-controls C:\temp\reference.hwpx C:\temp\reference-controls.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx --source image:0 --target doc-end --report C:\temp\copy-probe.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx C:\temp\copied.hwpx --source image:0 --target doc-end --report C:\temp\copy-report.md
```

`copy-from-doc`는 whole document, paragraph-to-end text block, whole table, HWP가 graphical object control로 노출하는 image, generic control source selection을 지원합니다: `all`, `paragraph-to-end:<text>`, `table:<index>`, `image:<index>`, `control:<ctrlId>:<index>`.

target은 `doc-end`, `anchor:<text>`, `cell:<table,rowMove,colMove>`, `control:<ctrlId>:<index>`를 사용할 수 있습니다. Cell target은 robust absolute grid addressing이 아니라 첫 cell 기준 HWP movement-count selection을 사용하므로 merged/irregular table에서는 주의해야 합니다.

## C# 사용 예시

```csharp
using OpenHwp.Automation;

[STAThread]
static void Main()
{
    using (var hwp = HwpSession.Create(visible: false))
    {
        hwp.ConfigureForAutomation();
        hwp.InsertText("hello");
        hwp.SaveAs(@"C:\temp\hello.hwpx");
    }
}
```

## 설계 메모

- 기본 control object는 `HWPFrame.HwpObject`입니다.
- CLI는 generic action execution을 제공합니다.
- file-open security module 등록은 `RegisterModule("FilePathCheckDLL", "<registry value name>")` 방식을 사용합니다.
- `ConfigureForAutomation()`은 security-module registration 시도와 `SetMessageBoxMode(0x10)` 적용을 포함한 automation 기본 설정을 묶습니다.
- CLI는 `x86`으로 빌드합니다.
- 라이브러리는 COM late binding을 사용하므로 type-library reference가 필요하지 않습니다.

## 제한 사항

- Windows 전용입니다.
- 설치된 한글 버전에 따라 일부 action name 또는 parameter name이 다를 수 있습니다.
- hidden mode, new-window, tab 동작은 HWP 버전별 차이가 있을 수 있습니다.
- in-process `HwpCtrl.ocx` 또는 `HwpAutomation.dll`까지 확장하려면 `x86` host를 사용하는 편이 안전합니다.
