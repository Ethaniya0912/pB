# T2.6 BiomeMapDebugger + T2.7 체크리스트 Day 2 항목

## 파일 구성

| 파일 | 용도 |
|------|------|
| `BiomeMapDebugger.cs` | T2.6 — Scene 뷰 Gizmo MonoBehaviour |
| `Week2Checklist_Day2_items.yaml` | T2.7 — Week2Checklist.asset 추가 항목 6개 (참조용) |

## 1. BiomeMapDebugger 배치

### 파일 위치 (Editor 폴더 아님!)

```
Assets/Scripts/pB-4/week2_Tooling/BiomeMapDebugger.cs
```

⚠ **"Editor" 폴더 아닌 상위에 배치** — MonoBehaviour이므로 런타임 코드. T2.5 PB4DebugDashboard와 다른 위치.

### Scene에 부착

1. Hierarchy에서 빈 GameObject 생성 (이름: "BiomeMapDebugger")
2. 이 GameObject 선택 → Add Component → `BiomeMapDebugger`
3. Inspector 설정:
   - `Draw Tag Labels`: ✓
   - `Draw Intensity Heatmap`: ✓
   - `Draw Bounds Always`: ✓
   - `Visual Radius`: 30m (기본값)
   - `Plane Height`: 0.1m (기본값)
4. Transform position을 확인 시야에 들어오도록 조정

### 시각화 동작

| 상태 | 표시 |
|------|------|
| **Edit 모드** | 회색 와이어프레임 박스만 (배치 위치 확인용) |
| **Play + Blackboard OK** | Intensity 색상 Plane + 와이어프레임 + 태그 Label |
| **Play + Blackboard null** | 핑크 와이어프레임 + "⚠ GameBlackboard.Instance = null" 경고 |
| **Selected** | 노란 두꺼운 와이어프레임 + 중앙 노란 구 |

### Intensity 색상

- **0.0** → 초록색 `#33CC33` (낮음)
- **0.5** → 노랑-주황 (중간)
- **1.0** → 빨강 `#CC3333` (높음)

선형 보간 (Color.Lerp) 적용.

### 태그 Label 예시

```
[Terrain Tags] cave, dark, wet
[Intensity]   0.65
[Bounds]      30m
```

## 2. T2.7 체크리스트 추가

### 방법 A — Inspector 수동 입력 (권장)

1. Unity Project → `Assets/Data/SO/Checklist/Week2Checklist.asset` 선택
2. Inspector의 **items 배열 →  "+" 버튼 6회 클릭**
3. 생성된 6 항목에 `Week2Checklist_Day2_items.yaml` 참조하여 값 입력:

| id | title | verifyMethod | category | assignedDay |
|-----|-------|-------------|---------|-----------|
| WK2_C01 | PersonalityMatrixSO 6 프리셋 존재 | `FileExists:Data/SO/Personality/Coward.asset` | Code (0) | Day 2 |
| WK2_C03 | 15 태그 규칙 .asset 입력 완료 | `FileExists:Data/SO/TagRules/HumanoidTagRules.asset` | Code (0) | Day 2 |
| WK2_C04 | 5 행동 Config .asset 입력 완료 | `FileExists:Data/SO/ActionConfig/HumanoidActionConfig.asset` | Code (0) | Day 2 |
| WK2_C05 | 태그 발현 런타임 검증 (2+ 태그) | `TagsCount:2` | Runtime (2) | Day 2 |
| WK2_C06 | PB4DebugDashboard 스크립트 존재 | `FileExists:Scripts/pB-4/week2_Tooling/Editor/PB4DebugDashboard.cs` | Code (0) | Day 2 |
| WK2_C07 | BiomeMapDebugger 씬 부착 확인 | `HasComponent:BiomeMapDebugger` | Wired (1) | Day 2 |

**Category enum 값**:
- 0 = Code
- 1 = Wired
- 2 = Runtime
- 3 = Visual
- 4 = External

### 방법 B — YAML 직접 편집 (고급)

Unity 종료 후 .asset 파일 텍스트 편집. YAML 들여쓰기 주의. snippet 파일의 주석 참조.

## 3. T2.7 검증 절차

1. Play 모드 진입
2. 5초 대기
3. Console 확인 → 기대 로그:
   ```
   [Tracker] 진척률: 11/11 (100%)
   ```
   또는 1개 실패 시:
   ```
   [Tracker] 진척률: 10/11 (91%)
   ```

4. 실패 항목 있을 시:
   - Week2ProgressTracker GameObject 우클릭 → **Dump All Items**
   - Console에서 Failed 항목 원인 확인

## 4. 스크린샷 3장 촬영

| # | 내용 | 위치 |
|---|------|------|
| 1 | Dashboard 전체 뷰 (6 패널 모두, NPC 선택 상태) | `Documentation/screenshots/Week2_Day2_01_Dashboard.png` |
| 2 | Scene 뷰 + BiomeMapDebugger Gizmo (태그 + Intensity) | `Documentation/screenshots/Week2_Day2_02_BiomeGizmo.png` |
| 3 | Console 로그 (Bootstrap + Tracker 11/11 + Tier 전이) | `Documentation/screenshots/Week2_Day2_03_Console.png` |

## 5. Git 커밋

```bash
git add .
git commit -m "Week 2 Day 2 완료: T2.1~T2.7

- T2.1: HumanoidTagRules.asset (15 태그 규칙)
- T2.2: HumanoidActionConfig.asset (5 행동 + 10 모디파이어)
- T2.3: Resolver/Formula 하드코딩 제거 + 단일 파일 통합
- T2.4: TrustMatrix/TraumaSystem 인터페이스 구현 + F6 EventBus 브릿지
- T2.5: PB4DebugDashboard v3 (3×2 퓨처리스틱 그리드 + EN/KR 토글)
- T2.6: BiomeMapDebugger (Scene 뷰 Gizmo)
- T2.7: Week2Checklist Day 2 6항목 추가 → 11/11 통과

추가 성과:
- HumanoidBootstrapper v3 (debugLog 자동 켬 + Inspector 토글)
- PB4DecisionAdapter hotfix (BT 없어도 Brain 틱 복구)
- 매뉴얼 v4 결함 17건 발견/수정"
```

## 트러블슈팅

| 증상 | 원인 | 해결 |
|-----|-----|-----|
| Scene 뷰에 아무것도 안 보임 | BiomeMapDebugger 미부착 | Scene에 GameObject 생성 + 컴포넌트 추가 |
| "GameBlackboard.Instance = null" 핑크 경고 | GameBlackboard prefab 없음 | Scene에 GameBlackboard 추가 |
| Intensity 색상이 바뀌지 않음 | Blackboard의 GlobalIntensityScore 계산 안 됨 | Week 3에서 지형팀이 구현 예정. 현재는 0.0 기본값 |
| 태그 Label 글자 깨짐 | Unity 폰트 설정 | Handles.Label은 기본 폰트 사용 (보통 정상) |
| `WK2_C07` 실패 | Scene에 BiomeMapDebugger 미부착 | 위 2단계 절차 확인 |
