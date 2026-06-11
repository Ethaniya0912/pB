# unity-cli 쿡북 (R3) — 검증된 명령 템플릿

> 실행 단계에서 참조한다. 모든 변경성 명령 전 `status` 로 `ready` 를 확인한다(busy 면 대기).
> 멀티 인스턴스 혼선 방지: 필요 시 `--project <ProjectRoot>` 로 대상을 고정한다.
> `<...>` 는 치환 자리. 읽기 전용 질의는 `--json exec` 로, 응답은 `--json` 으로 안정 파싱.

## 목차
0. 설치 (1회 셋업)
1. 상태·게이팅
2. 존재 확인 (타입/에셋/오브젝트)
3. 에셋 트리 덤프
4. 씬·프리팹 셋업
5. 구현 루프 (컴파일·플레이·콘솔)
6. 커스텀 툴
7. 스냅샷 덤프 (as-is)

---

## 0. 설치 (1회 셋업) — 공식: github.com/youngwoocho02/unity-cli
```powershell
# CLI 바이너리 — Windows (PowerShell)
irm https://raw.githubusercontent.com/youngwoocho02/unity-cli/master/install.ps1 | iex
```
```bash
# CLI 바이너리 — Linux/macOS
curl -fsSL https://raw.githubusercontent.com/youngwoocho02/unity-cli/master/install.sh | sh
# 업데이트
unity-cli update          # (--check 로 확인만)
```
**Unity Connector 패키지** — Package Manager → `+` → *Add package from git URL*:
```
https://github.com/youngwoocho02/unity-cli.git?path=unity-connector
```
또는 `Packages/manifest.json` 직접 편집(버전 고정은 `#v0.2.21` 식 태그 추가):
```json
"com.youngwoocho02.unity-cli-connector": "https://github.com/youngwoocho02/unity-cli.git?path=unity-connector"
```
Connector 는 Unity 기동 시 자동 활성화된다(별도 설정 불필요, 기본 포트 8090).

---

## 1. 상태·게이팅
```bash
unity-cli --json status                 # ready/compiling/reloading · 버전 · 포트
```
- 변경 전 항상 호출. `compiling`/`reloading` 이면 unity-cli 가 자체 대기한다.

## 2. 존재 확인 (③ scope)
```bash
# 타입(클래스) 존재 — 네임스페이스 포함
unity-cli --json exec "System.Type.GetType(\"Game.Inventory.InventorySystem, Assembly-CSharp\")!=null"

# 에셋 경로 존재
unity-cli --json exec "System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath,\"Prefabs/Player.prefab\"))"

# AssetDatabase 로 GUID 조회(존재 시 비어있지 않음)
unity-cli --json exec "UnityEditor.AssetDatabase.AssetPathToGUID(\"Assets/Prefabs/Player.prefab\")"

# 씬 내 오브젝트 존재
unity-cli --json exec "UnityEngine.GameObject.Find(\"Player\")!=null"
```

## 3. 에셋 트리 덤프 (R1 컨텍스트)
```bash
# 특정 폴더의 에셋 경로 목록
unity-cli --json exec "return string.Join(\"\\n\", UnityEditor.AssetDatabase.FindAssets(\"\", new[]{\"Assets/Scripts\"}).Select(g=>UnityEditor.AssetDatabase.GUIDToAssetPath(g)))" --usings System.Linq

# 특정 타입만(예: 프리팹)
unity-cli --json exec "return string.Join(\"\\n\", UnityEditor.AssetDatabase.FindAssets(\"t:Prefab\").Select(g=>UnityEditor.AssetDatabase.GUIDToAssetPath(g)))" --usings System.Linq

# 로드된 씬의 루트 오브젝트
unity-cli --json exec "return string.Join(\"\\n\", UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().Select(g=>g.name))" --usings System.Linq
```

## 4. 씬·프리팹 셋업 (⑥ test_env)
> 우선순위: **텍스트 편집(Edit/Write) → PostToolUse 훅이 reserialize+compile**.
> exec 직접 변경은 절차적 생성이 더 쉬울 때만 사용(파괴적이면 G4 가드가 ask).
```bash
# 빈 GameObject 생성 + 컴포넌트 부착(런타임/에디터)
unity-cli exec "var go=new UnityEngine.GameObject(\"TestRig\"); go.AddComponent<Game.Inventory.InventorySystem>(); return go.name;"

# 프리팹 인스턴스화
unity-cli exec "var p=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(\"Assets/Prefabs/Player.prefab\"); return UnityEngine.Object.Instantiate(p).name;"

# 텍스트 편집 후 수동 정합화가 필요한 경우(보통은 훅이 자동 수행)
unity-cli reserialize Assets/Scenes/Test.unity
unity-cli editor refresh --compile
```

## 5. 구현 루프 (⑦ 이후)
```bash
unity-cli editor refresh --compile          # 에셋 갱신 + 재컴파일 후 대기
unity-cli --json console --filter error     # 에러/예외 로그(테스트 판정)
unity-cli editor play --wait                # 플레이모드 진입 + 로딩 대기
unity-cli --json console --filter error     # 플레이 중/후 에러 재확인
unity-cli editor stop                        # 플레이모드 종료
```
- 판정: console 에러 0 + 의도한 동작 확인 → task 통과(`07_plan.md` `- [x]`).

## 6. 커스텀 툴 (반복 셋업 승격)
```bash
unity-cli tool list                          # [UnityCliTool] 로 노출된 프로젝트 툴
unity-cli tool help <ToolName>
unity-cli tool call <ToolName> --args '<json>'
```
- 자주 쓰는 셋업(테스트 리그 구성 등)은 `[UnityCliTool]` 로 고정해 재사용한다.

## 7. 스냅샷 덤프 (as-is, /cycle-start 가 호출)
```bash
# 프로젝트 에셋 인벤토리를 JSON 으로 — snapshots/<ts>.json 에 저장
unity-cli --json exec "return UnityEditor.AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith(\"Assets/\")).ToArray()" --usings System.Linq
```

---
### 주의
- `exec` 단일 식은 자동 return, 멀티스테이트먼트는 명시적 `return`, 추가 네임스페이스는 `--usings`.
- C# 문자열의 `"` 는 셸에서 `\"` 로 이스케이프. 복잡하면 작은따옴표로 전체를 감싼다.
- 파괴적 패턴(reserialize 대량/DeleteAsset/Destroy 등)은 **G4 가드가 가로채 ask/deny** 한다.
