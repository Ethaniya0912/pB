# PB4DebugDashboard v2 — 3×2 Futuristic Grid

## 무엇이 바뀌었나

### 이전 (v1)
- 세로 스크롤 단일 컬럼
- Unity 기본 Inspector 스타일 (회색)
- 8 패널 순차 배치

### 현재 (v2)
- **3×2 그리드** (매뉴얼 mockup 정확 매칭)
- **퓨처리스틱 다크 테마** (네온 시안/마젠타/앰버/그린/레드)
- **Utility는 BT State + Log 패널 안에 요약 통합** (매뉴얼 의도 반영)

## 레이아웃 구조

```
┌─────────────────────────────────────────────────────────────┐
│ ◆ pB-4 HUMANOID DASHBOARD v0.3     ■Auto-refresh ⟲Repaint │
├─────────────────────────────────────────────────────────────┤
│ ① Target NPC: [Skeleton_Humanoid_01 ▼] [↻]                │
├─────────────────┬───────────────┬──────────────────────────┤
│ ② Personality   │ ③ Needs       │ ④ ActiveTags            │
│  Control   ■■■░ │  fear   ■■░░  │   ┌Coward┐ ┌Diplomatic┐ │
│  Stability ■░░░ │  hunger ■░░░  │                          │
│  ...            │  obedience    │                          │
│ [RESOLVE] [RESET]│                │                          │
├─────────────────┼───────────────┼──────────────────────────┤
│ ⑤ Trust         │ ⑥ Trauma      │ ⑦ BT State + Log        │
│ ◆ TIER: Coop[2] │ ◆ STAGE:      │ currentState: Flee       │
│ ■■■■■■■░░ 60/100│   AcuteShock[1]│ U_atk=0.18 U_flee=0.82  │
│ [+15][+20][-40] │ FearMult: ×1.5│ ★ Winner: Flee (2.4)     │
│ [Hos][Dbt][Coop][Bli]│ [Inflict][Expl][Reset]│ [최근 로그 5줄] │
└─────────────────┴───────────────┴──────────────────────────┘
```

## 퓨처리스틱 테마 색상

| 역할 | 색상 | HEX |
|------|------|------|
| 배경 | 깊은 네이비 | #0B0F1A |
| 카드 배경 | 어두운 슬레이트 | #131926 |
| 네온 시안 (제목/슬라이더) | 밝은 청록 | #00E5FF |
| 네온 마젠타 (obedience/message) | 핫 핑크 | #FF2E9A |
| 네온 앰버 (state/winner) | 오렌지 옐로 | #FFB300 |
| 네온 그린 (Cooperation/Log) | 네온 민트 | #00FF9C |
| 네온 레드 (Hostility/Danger) | 네온 레드 | #FF3B5C |

## Tier/Stage 색상 매핑

| Trust Tier | 색상 |
|-----------|------|
| Hostility (0) | 🔴 Neon Red |
| Doubt (1) | 🟠 Neon Amber |
| Cooperation (2) | 🟢 Neon Green |
| BlindTrust (3) | 🔵 Neon Cyan |

| Trauma Stage | 색상 |
|-------------|------|
| None (0) | ⚫ Dim |
| AcuteShock (1) | 🟠 Neon Amber |
| Crossroads (2) | 🟣 Neon Magenta |
| PermScarring (3) | 🔴 Neon Red |

## 특수 효과

### 네온 버튼
- Hover 시 배경 밝아짐 (0.15 → 0.3 알파)
- 테두리 + 반투명 배경으로 발광 느낌
- 텍스트는 테마 색상

### 그라디언트 진행 막대
- 20 세그먼트로 점진적 밝기 증가
- 끝단에 1px 흰색 하이라이트 (반짝임 효과)

### 카드 테두리
- 4방향 1px 네온 색 테두리
- 제목 아래 얇은 네온 라인 (alpha 0.4)

### 태그 Pill
- 네온 시안 발광 아웃라인 (2px 알파 0.25)
- 내부 반투명 채우기
- 자동 줄바꿈 (태그 많으면 아래 줄로)

## 파일 배치

```
Assets/Scripts/pB-4/week2_Tooling/
└── Editor/
    └── PB4DebugDashboard.cs
```

⚠ **"Editor" 폴더 필수** — 없으면 빌드 에러

## 윈도우 열기

Unity 상단: **Window → pB-4 → Humanoid Dashboard**

최소 크기: **1080×760** (3열이므로 넓은 너비 필요)

## 기존 v03 Dashboard와의 관계

- **기존**: `week3_Editor/PB4DebugDashboard_v03.cs` — 시스템 개요 (GroupAI/HNGS 등)
- **신규 (이것)**: `week2_Tooling/Editor/PB4DebugDashboard.cs` — 단일 NPC 심층

두 도구는 **공존**. 서로 다른 메뉴:
- `pB-4 / Debug Dashboard v0.3` → 기존 Foldout 스타일
- `Window / pB-4 / Humanoid Dashboard` → 새 퓨처리스틱 그리드

## 검증 체크리스트

| # | 동작 | 기대 결과 |
|---|------|---------|
| 1 | 메뉴에서 열기 | 3×2 그리드 창 열림 (1080×760) |
| 2 | NPC 선택 | 6 패널 모두 데이터 렌더링 |
| 3 | Personality 슬라이더 드래그 | 슬라이더 네온 시안 채워짐, 태그 실시간 변경 |
| 4 | "BlindTrust (95)" 버튼 | 네온 시안 배지, TIER: BlindTrust [3] |
| 5 | "Inflict 0.8" 버튼 | Stage 네온 앰버 → AcuteShock [1], FearMult ×1.50 |
| 6 | BT State + Log 패널 | currentState 네온 앰버 + Winner + 최근 로그 (네온 그린) |

## 성능

- Auto-refresh 0.3s → 초당 ~3 Repaint
- 3×2 = 6 패널 × 각 ~50 DrawRect/GUI 호출 → 약 300 ops/frame
- Editor 전용이므로 성능 문제 없음 (Play 모드 fps에 영향 0.1% 이하)

## 트러블슈팅

| 증상 | 원인 | 해결 |
|-----|-----|------|
| 윈도우가 좁아 레이아웃 깨짐 | minSize 미적용 | 창 수동 확장 (1080 이상) |
| 슬라이더 드래그해도 값 안 바뀜 | SerializedProperty 연결 실패 | Play 재시작, NPC 재선택 |
| 네온 색상 안 보임 | Unity 다크 테마 아님 | Preferences → General → Editor Theme → Dark |
| 레이아웃 매번 다름 | 패널 min height 작음 | 이미 설정됨 (Row1=280, Row2=310) |
