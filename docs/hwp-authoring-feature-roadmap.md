# HWP Authoring Feature Roadmap

작성일: 2026-05-11

## 목적

이 문서는 현재 `openhwpsdk`가 실제 한/글 문서 작성 프로그램의 자주 쓰는 기능과 비교해 어디까지 왔고, 무엇을 다음에 구현해야 하는지 정리한다. 목표는 "대부분의 HWP 문서 작성 기능을 한 번에 구현"이 아니라, 실제 문서 작성에서 자주 쓰는 기능을 작은 개발 단위로 나누고 각 단위마다 검증 가능한 fixture와 합격 기준을 두는 것이다.

## 근거

- 현재 repo 기능 근거: `scan-hwpx-features test test\out\hwpx_feature_scan.md`, `Program.cs` CLI 표면, `HwpxFormMap`, `HwpxTableModel`, `HwpxPackageImageInserter`, `HwpxLayoutValidator`.
- 현재 test corpus 결과: HWPX 69개, package/XML parse error 0, 표 3,049개, 셀 70,558개, 병합셀 20,360개, 중첩표 184개, 그림 94개, 필드/양식 492개, header/footer 4개, note 4개, reference 128개, embedded object 5개.
- 한컴 공식 도움말 기준:
  - [입력 탭](https://help.hancom.com/hoffice/multi/ko_kr/hwp/view/toolbar/menu_insert.htm): 표, 차트, 도형, 그림, 스크린샷, 글맵시, 수식, 동영상, 문단 띠, 양식 개체, 누름틀, 각주/미주, 책갈피, 상호 참조, 하이퍼링크, 문자표, 한자 입력.
  - [표 메뉴](https://help.hancom.com/hoffice/multi/ko_kr/hwp/menu/table.htm): 표 만들기/그리기, 문자열-표 변환, 표/셀 속성, 테두리/배경, 줄/칸 추가/삭제, 셀 나누기/합치기, 표 계산식.
  - [머리말/꼬리말](https://help.hancom.com/hoffice/multi/ko_kr/hwp/format/header/header.htm): 쪽마다 반복되는 영역이며, 본문과 별개로 문자, 그림, 표, 그리기 개체와 문단/글자 모양을 가질 수 있음.
  - [누름틀](https://help.hancom.com/hoffice/multi/ko_kr/hwp/insert/madanginfo/madanginfo%28press%29.htm): 안내문, 메모, 필드 이름이 있는 입력 필드.
  - [차례/색인](https://help.hancom.com/hoffice/multi/ko_kr/hwp/tools/index/index.htm): 제목/표/그림/수식 차례와 색인 자동 생성.
  - [상호 참조](https://help.hancom.com/hoffice/multi/ko_kr/hwp/insert/cross_reference/cross_reference.htm): 표, 그림, 수식, 각주/미주, 개요, 책갈피 등을 참조하고 번호/쪽 변화에 대응.
  - [수식 명령어](https://help.hancom.com/hoffice/multi/ko_kr/hwp/insert/equation/equation%28script%29.htm): 분수, 근호, 적분, 행렬 등 스크립트 기반 수식 입력 체계.
  - [HWPX 구조](https://tech.hancom.com/hwpxformat/): HWPX는 ZIP 기반 XML 패키지이며 `BinData`, `Contents/content.hpf`, `Contents/header.xml`, `Contents/section*.xml` 등이 핵심.

## 현재 강한 영역

1. 기존 제출서식형 HWPX 보존 작성
   - `extract-form-map`, `probe-form-map`, `apply-form-map`, `apply-form-map --package`.
   - package text/image write, style guard, layout/content validation.

2. 표 중심 템플릿 자동 입력
   - `cellAddr`/`cellSpan` 기반 grid reconstruction.
   - 병합셀 guard, 중첩표 텍스트 분리, 기존 셀 currentText 검증.

3. 그림 삽입
   - COM 기반 그림 삽입.
   - package 기반 `BinData`/`hp:pic`/manifest 삽입.

4. 제출서식 profile
   - `fill-submission-template --profile r-and-d-startup-2026`.
   - Markdown 표 렌더링, 이미지 anchor queue, 보고서/검증 흐름.

## 큰 공백

현재 구현은 "제출서식형 HWPX의 본문/표/병합셀/중첩표/이미지 작성"에 강하다. 반대로 실제 한/글에서 자주 쓰지만 아직 약하거나 없는 축은 다음이다.

- 머리말/꼬리말, 쪽 번호, 구역별 쪽 설정.
- 각주/미주, 메모, 덧말.
- 누름틀/양식 개체/필드의 package-level 추출과 입력.
- 책갈피, 캡션, 상호 참조, 차례/색인.
- 일반 도형, 글상자, 글맵시, 문단 띠, 그룹, 회전, z-order, wrapping.
- 수식, 차트, OLE, 동영상/소리.
- 표 작성의 전 범위: 새 표 만들기, 표 그리기, 셀 나누기/합치기, 줄/칸 추가/삭제, 테두리/배경, 계산식.
- 일반 Markdown-to-HWP renderer: 제목, 개요 번호, 목록, 인라인 스타일, 표, 그림, 캡션, 링크를 모두 HWP native 구조로 생성.
- PDF/시각 회귀 검증 자동화.

## 로드맵

### Phase 0. Corpus 확장과 기능 재현성

목표: "없다/된다"를 말할 수 있는 최소 fixture를 확보한다.

개발 단위:

1. `test/corpus/features/` 구조 추가
   - `header-footer.hwpx`
   - `footnote-endnote.hwpx`
   - `press-field-form.hwpx`
   - `caption-crossref-toc.hwpx`
   - `equation.hwpx`
   - `shape-textbox.hwpx`
   - `chart-ole.hwpx`
   - `table-authoring.hwpx`

2. `scan-hwpx-features` 확장
   - header/footer 본문 part 감지.
   - footnote/endnote body 감지.
   - field/form object 종류별 count.
   - shape type별 count: line, rect, ellipse, text box, group, container.
   - equation/chart/OLE/media count.
   - caption/bookmark/cross reference/TOC/index marker count.

합격 기준:

- 각 fixture가 최소 하나 이상의 대상 feature를 가진다.
- `scan-hwpx-features` report가 feature별 count를 사람이 확인 가능한 표로 출력한다.
- corpus가 없어서 구현 여부를 판단하지 못하는 항목을 report에서 별도 표시한다.

우선순위: P0

구현 상태(2026-05-11):

- `test/corpus/features/*.hwpx` 8개 fixture와 `tools/New-HwpxFeatureFixtures.ps1` 생성 스크립트를 추가했다.
- `scan-hwpx-features` report는 aggregate counts, authoring coverage, detailed feature groups, missing corpus signals, per-file totals를 출력한다.
- 상세 inventory report는 header/footer, field/form, reference, note 신호를 파일/part/type/text 단위로 보여준다.
- 이 단계는 corpus inventory이며, 해당 기능의 write/edit 지원을 의미하지 않는다. 실제 작성 기능은 각 phase의 별도 개발 단위에서 검증한다.

복붙 자동화 상태(2026-05-11):

- HWP COM 기반 `list-controls`, `probe-copy-from-doc`, `copy-from-doc` 명령을 추가했다.
- 현재 rich copy source는 `all`, `paragraph-to-end:<text>`, `table:<index>`, `image:<index>`, `control:<ctrlId>:<index>`를 지원한다.
- target은 `doc-end`, `anchor:<text>`, `cell:<table,rowMove,colMove>`, `control:<ctrlId>:<index>`를 지원한다.
- 실제 제출서식 smoke에서 `table:0`을 대상 문서 끝에 붙여넣고, 표 48개 -> 49개, changed core tables 0, layout verdict pass를 확인했다.
- `image:<index>`는 `list-controls`의 전역 `index`가 아니라 `gso` 행의 `typeIndex` 값을 source-only로 복사한다.

### Phase 1. 머리말/꼬리말 및 쪽/구역 모델

목표: 실무 문서에서 가장 자주 쓰는 쪽 기반 반복 영역을 안전하게 읽고 쓴다.

개발 단위:

1. header/footer inventory
   - section별 header/footer reference 추출.
   - 홀수/짝수/양쪽 위치 구분.
   - 텍스트/표/그림/도형 count report.

2. header/footer text write
   - 기존 header/footer paragraph anchor에 텍스트 쓰기.
   - package mode와 COM mode 둘 다 지원하되, 우선 package text write.

3. page number support
   - COM command로 쪽 번호 삽입 smoke test.
   - HWPX package에서는 inventory/보존 검증부터 시작.

합격 기준:

- header/footer가 있는 fixture에서 본문 table count/style drift가 변하지 않는다.
- header/footer 텍스트가 PDF export에서 보인다.
- 여러 header/footer가 있을 때 section/위치가 report에 명확히 나온다.

우선순위: P0/P1

### Phase 2. 누름틀/필드/양식 개체

목표: 제안서/공문/계약서 템플릿에서 가장 흔한 "빈칸 채우기"를 named field 기반으로 안정화한다.

개발 단위:

1. field inventory
   - COM `field-list-raw` 결과와 HWPX package scan 결과를 한 report에 병합.
   - field name, 안내문, 현재 값, 위치, 인쇄 여부를 가능한 범위에서 추출.

2. `extract-form-map` field targets 추가
   - `field` / `press` target kind 추가.
   - 기존 cell/anchor target과 분리.

3. package-level press field fill
   - named field currentText 검증.
   - placeholder style 유지 또는 정상 본문 style override 옵션.

4. form object inventory
   - checkbox, radio, combo, edit box는 우선 보존/검증.
   - 값 쓰기는 COM smoke test 이후 별도.

합격 기준:

- 같은 안내문이 여러 번 있어도 field name으로 정확히 채운다.
- 누름틀 안내문 삭제/사용자 입력값 삽입이 HWP에서 열어도 유지된다.
- global replace 없이 target별 report가 남는다.

우선순위: P0/P1

### Phase 3. 표 authoring 전 범위

목표: 기존 표 채우기에서 "표를 새로 만들고 조작하는 기능"으로 확장한다.

개발 단위:

1. table create package writer
   - 단순 표 `rows x cols` 생성.
   - template table style clone 옵션.
   - cell border/background 기본값 설정.

2. row/column operations
   - 줄/칸 추가/삭제.
   - 셀 높이/너비 균등화.
   - 기존 table model의 `cellAddr`/`cellSpan` 재계산.

3. merge/split operations
   - 단순 rectangular merge.
   - split back to grid.
   - overlap validator 강화.

4. table style operations
   - border/background.
   - diagonal line.
   - paragraph alignment, vertical alignment.

5. formulas
   - 쉬운 계산식/블록 계산식은 우선 inventory와 COM smoke test.
   - package writer는 formula object 구조 파악 후 진행.

합격 기준:

- 새 표가 HWP에서 실제 표로 열린다.
- `validate-layout`가 table grid overlap을 잡는다.
- merge/split 후 `scan-hwpx-features`와 PDF export가 일치한다.

우선순위: P1

### Phase 4. 캡션, 책갈피, 상호 참조, 차례/색인

목표: 보고서/논문/제안서에서 그림/표 번호와 차례를 자동화한다.

개발 단위:

1. caption inventory
   - 표/그림/수식 caption 추출.
   - caption 없는 object report.

2. caption insert
   - COM path로 표/그림 caption 삽입.
   - package path는 보존 검증부터.

3. bookmark and cross-reference inventory
   - 대상 종류: 표, 그림, 수식, 각주/미주, 개요, 책갈피.
   - reference text와 target id 매핑.

4. TOC/index generation workflow
   - COM command smoke test.
   - package writer는 직접 생성보다 "기존 차례 새로고침/검증" 중심.

합격 기준:

- 표/그림 번호가 삽입 후 PDF에서 보인다.
- 앞쪽에 새 그림/표를 추가해도 COM refresh 후 reference가 업데이트된다.
- 차례/색인은 직접 생성보다 HWP COM 새로고침을 기본 경로로 둔다.

우선순위: P1/P2

### Phase 5. 각주/미주/메모/덧말

목표: 학술 문서와 법률/계약서 문서에서 필요한 주석 계층을 지원한다.

개발 단위:

1. note inventory
   - footnote/endnote part 감지.
   - 본문 note marker와 note body 연결 report.

2. note insert via COM
   - 각주/미주 삽입 smoke test.
   - note body text 입력.

3. package-level note preservation
   - 기존 note가 있는 문서에 package text write를 해도 note marker/body가 유지되는지 검증.

4. memo/comment inventory
   - 검토 메모는 우선 보존/검증.

합격 기준:

- 본문 marker와 note body가 양쪽 모두 보존된다.
- PDF export에서 각주/미주 위치가 정상이다.
- package text write가 note 구조를 손상하지 않는다.

우선순위: P1/P2

### Phase 6. 도형/글상자/글맵시/문단 띠

목표: 실제 문서에서 자주 쓰는 비본문 개체를 최소한 보존하고, 이후 생성/편집으로 확장한다.

개발 단위:

1. shape inventory diff
   - type, id, zOrder, anchor paragraph, size, position, wrapping.
   - `validate-layout`에 shape/control inventory diff 추가.

2. text box read/write
   - 글상자 내부 텍스트 추출.
   - existing text box content replacement.

3. simple shape create
   - line/rect/ellipse 기본 생성.
   - explicit HWPX unit size/position.

4. object transform
   - rotate, group, z-order, wrap은 inventory/보존 검증 후 단계적으로.

합격 기준:

- shape가 있는 fixture에서 package write 후 shape count/id/type/position이 유지된다.
- 글상자 텍스트 replacement가 HWP에서 보인다.
- 새 line/rect가 PDF에서 보인다.

우선순위: P2

### Phase 7. 수식

목표: 수식이 있는 문서의 보존부터 시작하고, 스크립트 기반 삽입을 제공한다.

개발 단위:

1. equation inventory
   - equation object count, anchor, caption 여부, raw script 가능 여부.

2. equation insert via COM
   - 한컴 수식 스크립트 문자열 입력 smoke test.
   - 예: `1 over 2`, `sqrt 2`, `matrix`.

3. package preservation validator
   - equation이 있는 문서에 text/image/table write 후 equation object 보존.

4. package-level equation writer
   - HWPX 구조 확인 후 별도 진행.

합격 기준:

- 수식 fixture에서 object count가 보존된다.
- COM 삽입 수식이 HWP/PDF에서 보인다.
- caption/cross reference와 연결될 수 있는 식별자를 report한다.

우선순위: P2

### Phase 8. 차트/OLE/동영상/소리

목표: 작성보다는 손상 방지와 inventory를 먼저 한다.

개발 단위:

1. binary object inventory
   - `BinData`의 image/OLE/media 구분.
   - manifest entry와 object reference 연결.

2. preservation validator
   - package write 후 binary entry, manifest item, object reference가 유지되는지 검사.

3. chart inventory
   - chart object count, source data 가능 여부.

4. COM-based insert smoke tests
   - 차트/OLE/동영상/소리는 직접 package writer보다 COM 경로 우선.

합격 기준:

- OLE/chart/media가 있는 문서를 text write해도 열림/보존된다.
- binary object 손상 시 validate가 blocking으로 잡는다.

우선순위: P2/P3

### Phase 9. 일반 Markdown-to-HWP renderer

목표: 기존 template fill이 아닌 새 문서/일반 문서 생성에서 HWP native 구조를 만든다.

개발 단위:

1. Markdown AST 도입
   - heading, paragraph, list, table, image, link, code, bold/italic.
   - string manipulation 금지, parser 기반.

2. style profile
   - 제목/본문/목록/캡션 style mapping.
   - 기본 HWPX template에서 style clone.

3. native object rendering
   - heading as paragraph style.
   - list as paraHead/numbering.
   - table as `tbl/tr/tc`.
   - image as `hp:pic`.
   - link as hyperlink field/control.

4. round-trip tests
   - Markdown -> HWPX -> read-text/scan/PDF.

합격 기준:

- raw Markdown artifact가 문서에 남지 않는다.
- 표/그림/목록이 실제 HWP 구조다.
- PDF 대표 페이지가 비어 있지 않고 layout 검증을 통과한다.

우선순위: P3

### Phase 10. 시각 회귀 검증

목표: "검증 report는 pass인데 한글에서 보면 깨짐"을 줄인다.

개발 단위:

1. export-pdf batch
   - template/candidate 자동 PDF export.

2. PDF render
   - 대표 페이지 PNG render.
   - page count와 key page thumbnails.

3. visual metrics
   - blank page detection.
   - image/object count mismatch.
   - text overflow heuristic.
   - table boundary shift heuristic.

4. golden fixture review
   - corpus별 expected report.

합격 기준:

- HWP COM/PDF export가 가능한 환경에서 대표 페이지 이미지가 생성된다.
- 레이아웃 깨짐이 있으면 markdown report에 페이지/대상 단위로 표시된다.

우선순위: P1/P2

## 추천 개발 순서

1. Phase 0 완료분 유지보수: real-world fixture를 계속 추가하고 expected report를 고정한다.
2. Phase 10 일부: PDF visual smoke harness.
3. Phase 1: header/footer text write와 page number support.
4. Phase 2: field/누름틀 named fill.
5. Phase 3: table authoring 기본 create/add/delete/merge/split.
6. Phase 4: caption/cross-reference COM insert/refresh workflow.
7. Phase 5: footnote/endnote preservation과 COM insert.
8. Phase 6: shape inventory diff와 text box write.
9. Phase 7: equation inventory와 COM insert.
10. Phase 8: chart/OLE/media preservation.
11. Phase 9: 일반 Markdown-to-HWP renderer.

## 바로 다음 커밋 후보

### 후보 A: PDF visual smoke harness

- `test/corpus/features`의 대표 fixture를 PDF로 export하는 smoke command 정리.
- scan report와 PDF export 결과를 같은 report 묶음으로 남긴다.
- HWP COM이 불안정하면 `diagnose-com`과 visible export를 우선 실행한다.

이유: scanner inventory는 구조 신호만 보므로, 실제 한글/ PDF 표시 여부를 별도 검증해야 한다.

### 후보 B: Header/footer inventory

- 기존 header/footer paragraph anchor에 text write.
- section별 header/footer/page number smoke test.

이유: 공문/제안서/보고서에서 머리말, 꼬리말, 쪽 번호는 매우 흔하다.

### 후보 C: 누름틀/필드 map target

- COM `field-list-raw`와 package scan 결과를 비교하는 report.
- `extract-form-map`에 `fields` section 추가.

이유: 실제 한글 양식 자동화는 좌표/문자열 치환보다 named field/누름틀 fill이 훨씬 안전하다.

## 당장 하지 말아야 할 것

- package XML로 차트/OLE/수식/복잡한 도형을 바로 생성하기.
- header/footer/각주/누름틀 fixture 없이 구현 완료라고 주장하기.
- 전체 Markdown 문서를 `replace-markdown` 류로 밀어 넣고 HWP native 구조라고 부르기.
- HWP COM으로 한 번 열렸다는 사실만으로 package 구조 보존을 생략하기.
