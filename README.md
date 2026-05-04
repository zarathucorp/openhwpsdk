# OpenHwp Automation

설치된 한글 프로그램을 `COM/OLE`로 제어하기 위한 C# 래퍼와 CLI 샘플입니다.

이 저장소는 한컴의 별도 SDK 런타임을 감싸지 않습니다. 대신 현재 PC에 설치된 한글이 노출하는 `HWPFrame.HwpObject`를 사용합니다.

## 현재 구현

- 라이브러리: `src/OpenHwp.Automation`
- CLI 샘플: `src/OpenHwp.Automation.Cli`
- 빌드 스크립트: `build.cmd`

핵심 클래스는 `OpenHwp.Automation.HwpSession` 입니다.

## 빌드

```bat
build.cmd
```

## CLI 예제

버전 확인:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe version
```

새 문서에 텍스트를 넣고 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe new-text C:\temp\hello.hwpx "Hello from OpenHwp"
```

기존 문서를 열어서 다른 이름으로 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe copy-save C:\temp\in.hwpx C:\temp\out.hwpx
```

기존 문서를 PDF로 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf C:\temp\in.hwpx C:\temp\out.pdf
```

문서 정보 읽기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe doc-info C:\temp\in.hwp
```

문서 전체 텍스트 읽기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp
```

문서 전체 텍스트를 UTF-8 파일로 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp C:\temp\in.txt
```

특정 페이지 텍스트 읽기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-page C:\temp\in.hwp 1
```

필드 존재 여부 및 값 조회:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-exists C:\temp\form.hwp TEST
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-get C:\temp\form.hwp TEST
```

필드 값 쓰기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-set C:\temp\form.hwpx TEST "updated text" C:\temp\form-out.hwpx
```

제네릭 액션 실행:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe action InsertText Text="raw action text" --save C:\temp\raw.hwpx
```

Markdown 내용을 HWPX 템플릿으로 옮기고, HWPX 레이아웃 보존 여부를 검사:

`markdown-to-hwpx` is not exposed. A command with that name must render Markdown as HWP structure and character styles: headings as larger headings, tables as HWP tables, and inline marks such as bold/code as styled text.

Existing form templates should be filled through cell/cursor commands, then checked with `validate-layout`.

Template HWPX files can first be mapped from the real package XML, then filled through HWP automation or text-only package mode:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe extract-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\template-probe.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible apply-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled.hwpx
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe apply-form-map --package C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled-package.hwpx --report C:\temp\filled-package-layout.md
```

Edit only the generated map's `writeText` and `writeImage` elements. The map records the full HWPX package structure (`content.hpf`, manifest/spine, XML parts, previews, metadata entries) and marks write candidates from every XML part that contains HWP paragraph/table text. Use `probe-form-map` before HWP-backed writing to confirm every mapped cell/anchor can be selected in HWP. Use `apply-form-map --package` for text-only writes; use the HWP-backed path for image insertion and editor-backed behavior.

Markdown 내용을 한 줄씩 `InsertText`로 문서 끝에 추가:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible append-markdown-lines C:\temp\template.hwpx C:\temp\input.md C:\temp\linewise-out.hwpx
```

Markdown table values can be inserted into existing HWPX table cells without recreating the table:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-table-list C:\temp\input.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible fill-markdown-table C:\temp\template.hwpx C:\temp\input.md C:\temp\table-out.hwpx 8 3 1 0 1 2 5
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible table-cell-set C:\temp\template.hwpx C:\temp\cell-out.hwpx 3 1 1 "cell text"
```

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe demo-list
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe demo-feature insertTable Rows=2 Cols=3 TreatAsChar=false --save C:\temp\table.hwpx
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe demo-feature pageNumbering DrawPos=5 SideChar=- --open C:\temp\table.hwpx --save C:\temp\table-numbered.hwpx
```

실제 한글 창을 띄운 상태로 테스트:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible copy-save C:\temp\in.hwpx C:\temp\out.hwpx
```

## 코드 사용 예시

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

- 기본 제어 객체는 `HWPFrame.HwpObject`
- 액션 실행은 generic wrapper 제공
- 파일 열기 보안 모듈은 `RegisterModule("FilePathCheckDLL", "<registry value name>")` 방식 사용
- 자동화용 기본 설정은 `ConfigureForAutomation()`으로 묶음
- `ConfigureForAutomation()`은 보안 모듈 등록 시도 후 `SetMessageBoxMode(0x10)` 적용
- 읽기 기능은 `GetDocumentText()`, `GetPageText()`, `FieldExists()`, `GetFieldText()`, `GetFieldListRaw()` 제공
- CLI는 `x86`으로 빌드
- 라이브러리는 COM late binding 기반이라 타입 라이브러리 참조 없이 동작

## 제한 사항

- Windows 전용입니다.
- 설치된 한글 버전에 따라 일부 액션 이름/파라미터 이름이 달라질 수 있습니다.
- 숨김 상태 처리, 새 창/새 탭 동작은 버전별 차이가 있을 수 있습니다.
- 인프로세스 `HwpCtrl.ocx` 또는 `HwpAutomation.dll`까지 확장하려면 호스트를 `x86`으로 맞추는 편이 안전합니다.
