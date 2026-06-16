---
title: 버전관리-git-lfs
tags: [tooling]
status: done
source:
  - .gitattributes
  - .gitignore
  - ProjectSettings/EditorSettings.asset
verified: 2026-06-15
---

# 버전관리-git-lfs

Unity는 바이너리 에셋이 많아 초기 Git/LFS 설정이 후반 히스토리 건전성을 좌우한다. **pB의 현재 설정을 실측한 결과를 기록한다.**

## 현황 (pB)

### `.gitattributes` 실측 내용 (전체)

```gitattributes
# 하네스 셸 스크립트는 항상 LF 로 유지(Windows 체크아웃 시 CRLF 변환 방지 → bash 훅 보호)
*.sh text eol=lf
.harness/hooks/** text eol=lf
```

**Unity 바이너리 에셋(`.png`, `.fbx`, `.wav`, `.psd`, `.mp3`, `.asset` 등)에 대한 LFS 트래킹 패턴이 전혀 없다.**

### `.gitignore` 실측 요약

Unity 표준 `.gitignore` 기반:
- 제외: `Library/`, `Temp/`, `Obj/`, `Build/`, `Logs/`, `UserSettings/`, `*.log`
- 제외: `.blend1` (Blender 백업)
- 제외: `MemoryCaptures/`, `Recordings/`
- 제외: `*.csproj`, `*.sln`, `*.suo`, `*.user` (IDE 생성 파일)
- 제외: Unity 자동생성 메타 파일 일부
- **개별 추가**: `Assets/_Recovery/0 (16~29).unity`, 일부 대형 텍스처 파일(4096px 업스케일 PNG)이 특정 경로별로 명시 제외됨

### `EditorSettings.asset` 실측

```yaml
m_ExternalVersionControlSupport: Visible Meta Files
m_SerializationMode: 2   # Force Text (YAML 직렬화)
```

- **Force Text 직렬화 활성화됨** — 씬/프리팹/에셋이 텍스트(YAML) 포맷으로 저장돼 git diff 가능
- `.meta` 파일 버전관리 활성화됨

### SmartMerge(UnityYAMLMerge) 설정 상태

미설정. `.gitattributes`에 UnityYAMLMerge 관련 드라이버 설정 없음.

### 브랜치 전략

코드로 확인 불가. git log 확인 결과 `main` 브랜치 운영 중. PR 리뷰 규칙 미문서화.

## 설계·결정

- **Force Text 직렬화**: 씬/프리팹 파일을 YAML 텍스트로 저장 → git diff 가능, merge conflict 가시화
- **셸 스크립트 LF 강제**: 하네스 훅(`*.sh`, `.harness/hooks/**`)을 Windows에서 체크아웃해도 bash가 실행될 수 있도록 `eol=lf` 강제 — 이것이 현재 `.gitattributes`의 유일한 설정

## ⚠ 비판·리스크

- **심각도 높음**: **LFS가 설정되지 않았다.** `.gitattributes`에 바이너리 에셋 패턴이 없다. `.png`, `.fbx`, `.wav`, `.mp3`, `.psd`, `.psb`, `.asset`(ScriptableObject 바이너리 포함) 등 모든 바이너리가 git 오브젝트로 직접 추적 중이다. 개발이 진행될수록 레포 크기가 선형 증가하며, 향후 `git clone` 시간이 길어지고 일부 바이너리의 히스토리 재작성이 어렵다.
  - **권고**: 즉시 `.gitattributes`에 Unity 표준 LFS 패턴 추가:
    ```
    *.png filter=lfs diff=lfs merge=lfs -text
    *.jpg filter=lfs diff=lfs merge=lfs -text
    *.psd filter=lfs diff=lfs merge=lfs -text
    *.fbx filter=lfs diff=lfs merge=lfs -text
    *.wav filter=lfs diff=lfs merge=lfs -text
    *.mp3 filter=lfs diff=lfs merge=lfs -text
    *.ogg filter=lfs diff=lfs merge=lfs -text
    *.unity filter=lfs diff=lfs merge=lfs -text
    *.prefab filter=lfs diff=lfs merge=lfs -text
    ```
    단, 이미 git에 추적된 파일은 `git lfs migrate import`가 필요하다(히스토리 재작성).
- **심각도 높음**: `.gitignore`에 특정 4096px 업스케일 PNG 파일들이 경로별로 명시 제외됐다(`Assets/Arts/Models/ranger_without_cape_rigged_v2/upscale/`). 이는 LFS 대신 해당 파일을 아예 추적하지 않는 임시방편이다. 에셋이 레포에 없으면 팀원 간 공유가 불가능하고 빌드 재현이 불가능하다.
- **심각도 보통**: SmartMerge(UnityYAMLMerge)가 설정되지 않았다. Force Text 직렬화로 씬/프리팹 충돌이 발생할 때 Unity 씬 구조를 이해하는 3-way merge가 아닌 일반 텍스트 merge가 사용된다. 멀티플레이 구조가 복잡해질수록 씬 충돌 위험이 올라간다.
  - **권고**: `.gitattributes`에 merge driver 등록:
    ```
    *.unity merge=unityyamlmerge
    *.prefab merge=unityyamlmerge
    *.asset merge=unityyamlmerge
    *.mat merge=unityyamlmerge
    ```
    `.git/config`(또는 글로벌 `.gitconfig`)에 `[merge "unityyamlmerge"]` 섹션 추가.
- **심각도 보통**: 브랜치 전략·PR 리뷰 규칙이 미문서화. 1인 개발이면 현재 문제없지만, 협업 시 merge 정책이 없으면 `main`에 직접 push 등 사고가 발생한다.
- **심각도 낮음**: `com.unity.collab-proxy` 2.10.2 패키지가 설치돼 있다(Unity Version Control 통합). Git과 병행 사용하면 충돌 위험이 있다. 실제 사용 여부 확인 필요.

## 관련 문서

- [[project-structure|프로젝트-구조]]
- [[build-automation|빌드-자동화]]

---
← [[01-foundation-hub|01 · 기반 (협업/구조/컨벤션)]] · [[index|인덱스]]
