<h1 align="center">OpenHwp Automation</h1>

<p align="center">
  한컴 HWP/HWPX 문서를 Windows 환경에서 자동화하기 위한 C# 래퍼와 CLI 도구입니다.
</p>

<p align="center">
  <a href="https://zarathucorp.github.io/openhwpsdk/">문서 사이트</a>
  ·
  <a href="https://zarathucorp.github.io/openhwpsdk/getting-started/">시작하기</a>
  ·
  <a href="https://zarathucorp.github.io/openhwpsdk/cli-reference/">CLI 레퍼런스</a>
  ·
  <a href="README.md">English</a>
</p>

<p align="center">
  <a href="https://zarathucorp.github.io/openhwpsdk/">
    <img alt="Docs" src="https://img.shields.io/badge/docs-GitHub%20Pages-2ea44f">
  </a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0078D4">
  <img alt=".NET Framework" src="https://img.shields.io/badge/.NET%20Framework-4.0-512BD4">
  <img alt="HWPX" src="https://img.shields.io/badge/HWPX-package%20editing-2ea44f">
</p>

OpenHwp Automation은 로컬 PC에 설치된 한글 프로그램을 `HWPFrame.HwpObject` COM 인터페이스로 제어하고, 원본 구조 보존이 필요한 HWPX 패키지 작업을 CLI로 제공합니다.

이 저장소는 한컴의 별도 SDK 런타임을 포함하거나 감싸지 않습니다. 자동화 대상은 현재 Windows PC에 설치된 한글 프로그램입니다.

이 프로젝트는 다음 상황에 맞습니다.

- Windows C# 또는 CLI 워크플로에서 HWP/HWPX 문서를 자동화해야 할 때.
- 기존 공식 양식의 표 구조를 다시 만들지 않고 내용을 입력해야 할 때.
- 검토 가능한 리포트와 함께 HWPX 패키지 내용을 검사, 검증, 수정해야 할 때.

## 왜 필요한가

HWP/HWPX 자동화는 단순한 텍스트 변환 문제가 아닙니다. COM 안정성, 한글 편집기 동작, HWPX 패키지 XML, 병합표, 이미지 바이너리, 공문서/신청서 양식 보존이 함께 얽힙니다. 이 저장소는 그 경계를 명시적으로 다룹니다.

- HWP COM 자동화를 위한 C# 래퍼.
- 반복 가능한 문서 작업을 위한 Windows CLI.
- 원본 구조 보존이 중요한 경우를 위한 COM-free HWPX 패키지 편집.
- 문서를 조용히 바꾸는 대신 레이아웃/내용 검증 리포트 제공.
- HWP COM 등록, 프로세스 정리, 파일 열기 문제 진단.

## 빠른 시작

아래 명령을 실행하기 전에 COM 기반 명령에는 Windows와 로컬 한컴 HWP/Hanword 설치가 필요하고, 빌드에는 MSBuild 또는 Visual Studio Build Tools가 필요합니다. 먼저 임시 출력 폴더를 만듭니다.

```bat
mkdir C:\temp
```

CLI를 빌드합니다.

```bat
build.cmd Release
```

실행 파일을 확인합니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe version
```

간단한 HWPX 문서를 생성합니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe new-text C:\temp\hello.hwpx "Hello from OpenHwp"
```

편집기 기반 COM 작업 전에는 진단을 먼저 실행합니다.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible diagnose-com
```

## 주요 워크플로

| 워크플로 | 명령 그룹 | 사용 시점 |
| --- | --- | --- |
| 문서 읽기/저장 | `doc-info`, `read-text`, `copy-save`, `export-pdf` | 기본 HWP/HWPX 자동화가 필요할 때 |
| 기존 양식 입력 | `extract-form-map`, `probe-form-map`, `apply-form-map` | 원본 표와 구역 구조를 유지해야 할 때 |
| 지원 템플릿 입력 | `fill-submission-template` | 알려진 프로파일로 Markdown 내용을 공식 HWPX 양식에 넣을 때 |
| 생성 파일 검증 | `validate-layout`, `validate-content` | 출력이 레이아웃이나 필수 내용을 훼손하지 않았는지 확인할 때 |
| 기존 그림 교체 | `list-pictures`, `replace-image-control` | 지원되는 위치, 줄바꿈, 크기, 자르기, 고정점 속성 보존을 검증하면서 이미지만 바꿀 때 |
| 패키지 표 편집 | `table-*-package` | 한글 COM 없이 단순 HWPX 표를 편집할 때 |
| 편집기 기반 복사/붙여넣기 | `list-controls`, `probe-copy-from-doc`, `copy-from-doc` | 참조 문서의 서식이나 개체를 그대로 활용해야 할 때 |

예제는 [CLI 레퍼런스](https://zarathucorp.github.io/openhwpsdk/cli-reference/)에서 확인할 수 있습니다.

## 문서

문서 사이트는 GitHub Pages로 배포합니다.

- [시작하기](https://zarathucorp.github.io/openhwpsdk/getting-started/)
- [CLI 레퍼런스](https://zarathucorp.github.io/openhwpsdk/cli-reference/)
- [HWPX 검증 워크플로](https://zarathucorp.github.io/openhwpsdk/markdown-hwpx-validation-workflow/)
- [이미지 교체 워크플로](https://zarathucorp.github.io/openhwpsdk/image-replacement/)
- [로드맵과 기능 범위](https://zarathucorp.github.io/openhwpsdk/hwp-authoring-feature-roadmap/)

문서 사이트를 로컬에서 미리 보려면 다음을 실행합니다.

```bat
python -m pip install -r requirements-docs.txt
mkdocs serve
```

## 프로젝트 구조

```text
.
|-- src/
|   |-- OpenHwp.Automation/        C# COM 자동화 래퍼
|   `-- OpenHwp.Automation.Cli/    Windows CLI와 HWPX 패키지 유틸리티
|-- docs/                          GitHub Pages 문서 원본
|-- skills/                        로컬 워크플로용 Codex skill 패키지
|-- build.cmd                      Windows 빌드 진입점
`-- mkdocs.yml                     문서 사이트 설정
```

## 요구 사항

- Windows.
- COM 기반 명령을 사용할 경우 로컬 한컴 HWP/Hanword 설치.
- .NET Framework 프로젝트를 빌드할 수 있는 MSBuild 또는 Visual Studio Build Tools.
- CLI는 x86 host로 빌드됩니다.

COM-free HWPX 패키지 명령은 한글을 실행하지 않고 동작할 수 있지만, 편집기 기반 작업은 정상적인 로컬 HWP COM 환경이 필요합니다.

## 제한 사항

- HWP COM 동작은 설치된 한글 버전에 따라 달라질 수 있습니다.
- hidden mode, 새 창, 탭 동작은 환경 의존적입니다.
- 일부 패키지 수준 명령은 구조 보존을 증명할 수 없으면 복잡한 표나 공유 이미지 참조를 의도적으로 거부합니다.
- 이 저장소는 아직 모든 HWP 작성 기능에 대한 광범위한 지원을 선언하지 않습니다.

현재 지원 범위는 [로드맵](https://zarathucorp.github.io/openhwpsdk/hwp-authoring-feature-roadmap/)을 참고하세요.
