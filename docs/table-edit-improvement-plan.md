# Table Edit Improvement Plan

Status note: this is a historical design note for inaccurate HWP table-cell targeting, especially COM workflows that rely on `tableIndex + row/col` moves. Since this note was written, the HWPX package path gained a merge/nested-table-aware table model for form-map extraction and the submission-template filler. That newer HWPX path reconstructs grids from `cellAddr` plus `cellSpan`, separates parent-cell direct text from nested table text, and clones multi-row record groups when expanding known profile tables. The COM-side label resolver and generic `dump-table` / `table-get` / `table-set-by-label` APIs described below are still future work.

## 배경

초기 로컬 fixture 문서에 마크다운 내용을 적용하는 과정에서 표 셀을 정확히 선택하지 못하는 문제가 확인됐다.
현재 구현은 `tableIndex + rowMoveCount + columnMoveCount` 방식으로 셀을 고르는데, 실제 신청서 양식은 병합 셀과 불규칙한 표 구조가 많아서 이 방식만으로는 안정적인 편집이 어렵다.

관련 확인 결과:

- 원본 문서 첫 표 덤프: 로컬 검증 artifact였으며 공개 repo에는 포함하지 않는다.
- 현재 표 선택 로직: `src/OpenHwp.Automation/HwpSession.cs`
- 현재 CLI 진입점: `src/OpenHwp.Automation.Cli/Program.cs`

## 현재 문제

### 1. 표가 단순 격자가 아니다

첫 표만 봐도 다음 현상이 있다.

- 같은 텍스트가 여러 좌표에서 반복된다.
- 병합 셀 때문에 `row/col` 이동이 논리 셀과 일치하지 않는다.
- 빈 셀처럼 보여도 실제로는 병합 영역 일부일 수 있다.
- 아래 행으로 내려갈수록 동일한 긴 블록이 반복 선택된다.

즉 지금 방식의 `SelectTableCell(r, c, tableIndex)`는 "시각적 표의 r행 c열"을 보장하지 않는다.

### 2. 현재 수정 경로가 내용 중심이다

초기 로컬 fixture 적용 경로는 크게 두 가지였다.

- 전역 치환: 안전한 문자열을 문서 전체에서 치환
- 구간 교체: `8. 사업목적` 이후를 평문으로 재삽입

이 방식은 내용 반영에는 유효하지만, 다음 문제가 있다.

- 표 셀 기준으로는 어디가 바뀌었는지 보장되지 않는다.
- 병합 셀 양식에서 잘못된 셀을 건드릴 수 있다.
- 본문 구간 교체는 서식을 유지하지 못한다.

### 3. 현재 관측 API가 약하다

현재는 다음 수준까지는 가능하다.

- 표 선택
- 셀 블록 선택
- 복사 후 클립보드 텍스트 읽기
- 문서 전체 텍스트 읽기

하지만 아직 없다.

- 특정 표의 논리 셀 구조 추출
- 셀별 고유 식별자
- 라벨 셀과 값 셀의 관계 분석
- "기관명 옆 값 셀" 같은 의미 기반 선택

## 원인 정리

핵심 원인은 `편집`보다 `관측`이 부족한 것이다.

현재는:

- 표를 선택할 수는 있음
- 셀을 대충 이동할 수는 있음
- 하지만 현재 셀이 문서상 어떤 의미를 가지는지는 모름

결과적으로 AI가 "기관명 옆 셀에 회사명을 넣는다"가 아니라 "어딘가의 4행 5열쯤 되는 셀"을 건드리게 된다.

## 목표

표 편집을 다음 수준으로 끌어올린다.

1. 문서의 표를 안전하게 덤프한다.
2. 병합 셀을 포함한 논리 셀 모델을 만든다.
3. 라벨 기반으로 값 셀을 찾는다.
4. 값 셀만 수정한다.
5. 수정 전후를 다시 덤프해서 검증한다.

즉 최종 목표는 `좌표 기반 편집`이 아니라 `의미 기반 편집`이다.

## 제안 아키텍처

### 1. TableDump 계층

새 기능:

- `DumpTable(tableIndex)`
- `DumpTableRange(tableIndex, maxRows, maxCols)`
- `ReadSelectedCellText()`
- `ReadTableCell(tableIndex, row, col)`

출력 포맷 예시:

```json
{
  "tableIndex": 0,
  "cells": [
    { "row": 0, "col": 0, "text": "1) 공고번호", "selectionOk": true },
    { "row": 0, "col": 1, "text": "제2026-0295호", "selectionOk": true }
  ]
}
```

초기에는 완전한 병합 정보 없이도 괜찮다.
우선 "현재 좌표에서 선택했을 때 읽히는 텍스트"를 안정적으로 얻는 게 먼저다.

### 2. LogicalTable 계층

`DumpTable` 결과를 후처리해서 논리 테이블 모델을 만든다.

핵심 규칙:

- 동일 텍스트가 넓게 반복되면 병합 셀 후보로 본다.
- 라벨 후보와 값 후보를 분리한다.
- 빈 셀과 병합 확장 셀을 구분한다.
- 인접한 라벨-값 패턴을 추정한다.

예시:

```json
{
  "labelValuePairs": [
    { "label": "기관명", "value": "차라투(주)", "tableIndex": 0, "valueCell": { "row": 4, "col": 5 } },
    { "label": "사업자등록번호", "value": "624086-01323", "tableIndex": 0, "valueCell": { "row": 5, "col": 4 } }
  ]
}
```

### 3. Label Resolver 계층

새 기능:

- `FindCellByLabel(tableIndex, "기관명")`
- `FindValueCellByLabel(tableIndex, "기관명")`
- `FindBestMatchingCell("사업자등록번호")`

매칭 규칙:

- 완전일치 우선
- 공백/개행 정규화 후 비교
- `기관명`, `사업자등록번호`, `부서/직위` 같은 핵심 라벨 사전 지원
- 중복 시 같은 행 또는 우측 셀 우선

### 4. Safe Write 계층

새 기능:

- `WriteTableCell(tableIndex, row, col, text)`
- `WriteValueByLabel(tableIndex, label, text)`
- `ReplaceValueNextToLabel(tableIndex, label, expectedOldText, newText)`

쓰기 규칙:

- 먼저 현재 셀 텍스트를 읽는다.
- 예상값과 다르면 바로 쓰지 않는다.
- diff 로그를 남긴다.
- 수정 후 다시 읽어서 검증한다.

## 구현 순서

### Phase 1. 관측 강화

먼저 구현할 것:

- `ReadSelectedCellText()`
- `DumpTable(tableIndex, maxRows, maxCols)`
- CLI `dump-table <inputPath> <tableIndex> [maxRows] [maxCols]`

산출물:

- JSON dump
- 사람이 읽을 수 있는 `.txt` dump

이 단계가 끝나면 수동으로 대상 문서의 표 맵을 만들 수 있다.

### Phase 2. 의미 기반 셀 찾기

다음 구현:

- `FindValueCellByLabel(...)`
- 라벨 정규화 함수
- 표 내 라벨-값 관계 탐색기

우선 대상 라벨:

- `기관명`
- `사업자등록번호`
- `주 소`
- `부서/직위`
- `정부출연금`
- `현금`
- `현물`
- `합   계`

### Phase 3. 안전한 셀 쓰기

다음 구현:

- `WriteTableCell(...)`
- `WriteValueByLabel(...)`
- `UpdateFormSummary(test1 mapping spec)`

여기서는 전역 치환을 줄이고, 앞쪽 신청서/요약서 표는 전부 셀 쓰기로 바꾼다.

### Phase 4. 본문 편집 개선

표와 별개로 본문도 현재는 평문 치환만 된다.
나중에는 다음이 필요하다.

- 섹션 경계 탐색
- 섹션 단위 선택
- 선택 영역 삭제 후 삽입
- 문단 스타일 복사/붙여넣기

하지만 우선순위는 표다.

## 로컬 fixture에 필요했던 매핑 전략

해당 로컬 fixture는 다음 순서로 적용하는 것이 맞다.

1. 표 덤프 생성
2. 앞쪽 신청서 표 라벨-값 맵 작성
3. 요약서 표 라벨-값 맵 작성
4. 셀 단위 수정
5. 문서 전체 텍스트 검증
6. 본문 섹션 적용

즉 fixture 전용 구현 순서는:

- `DumpTable(0)`
- `DumpTable(1)` 또는 요약서 표 index 확인
- 라벨 기반 수정기 작성
- fixture 적용 스크립트 작성

## 권장 API 초안

라이브러리:

```csharp
public string ReadSelectedCellText();
public TableDump DumpTable(int tableIndex, int maxRows = 30, int maxCols = 10);
public TableCellRef FindValueCellByLabel(int tableIndex, string label);
public bool WriteTableCell(int tableIndex, int row, int col, string text);
public bool WriteValueByLabel(int tableIndex, string label, string expectedOldText, string newText);
```

CLI:

```bat
OpenHwp.Automation.Cli.exe dump-table input.hwp 0
OpenHwp.Automation.Cli.exe table-get input.hwp 0 4 5
OpenHwp.Automation.Cli.exe table-set input.hwp 0 4 5 "차라투 주식회사" out.hwpx
OpenHwp.Automation.Cli.exe table-set-by-label input.hwp 0 "기관명" "차라투 주식회사" out.hwpx
```

## 검증 전략

표 편집은 반드시 3단계 검증이 필요하다.

1. 수정 전 셀 읽기
2. 수정 실행
3. 수정 후 같은 셀 다시 읽기

추가 검증:

- 문서 전체 `read-text`
- 핵심 문자열 출현 횟수 확인
- 필요 시 visible 모드에서 육안 확인

## 개발 시 주의점

- 병합 셀에서는 `row/col`이 시각적 그리드와 다를 수 있다.
- 선택 후 `Copy` + 클립보드 읽기는 현재 가장 실용적인 관측 수단이다.
- 전역 치환은 표 수정의 보조 수단으로만 써야 한다.
- `replace-after-marker`는 본문 내용 적용에는 쓸 수 있지만, 서식 보존형 편집으로 간주하면 안 된다.
- 표 관련 기능은 항상 `tableIndex`를 명시적으로 받는 쪽이 낫다.

## 결론

현재 문제는 "편집 기능 부족"보다 "정확한 표 관측 모델 부재"에 가깝다.

다음 개발은 반드시 이 순서로 가야 한다.

1. 표 덤프
2. 라벨 기반 셀 탐색
3. 셀 단위 안전 쓰기
4. fixture 전용 매핑 적용

이 순서를 지키면 복잡한 양식 문서도 전역 치환에 덜 의존하고 안정적으로 수정할 수 있다.
