# unity-cli 쿡북 (R3) — 검증된 명령 템플릿

> 실행 단계에서 참조한다. 모든 변경성 명령 전 `status` 로 `ready` 를 확인한다(busy 면 대기).
> 멀티 인스턴스 혼선 방지: 필요 시 `--project <ProjectRoot>` 로 대상을 고정한다(`--port` 는 v0.3 에서 제거됨).
> `<...>` 는 치환 자리. 읽기 전용 질의는 `exec` 로. **v0.3.x 기준 문법**(`--json` 전역 플래그 없음 — 출력은 텍스트).

## 목차
0. 설치 (1회 셋업)
1. 상태·게이팅
2. 존재 확인 (타입/에셋/오브젝트)
3. 에셋 트리 덤프
4. 씬·프리팹 셋업
5. 구현 루프 (컴파일·플레이·콘솔)
6. 커스텀 툴
7. 스냅샷 덤프 (as-is)
8. 검증 증빙 수집 (⑧ result · evidence/)
9. 대용량 출력 처리 (컨텍스트 절약)
10. 테스트 씬 셋업 (/test-run · 더미 stub·씬·교체)

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
또는 `Packages/manifest.json` 직접 편집(버전 고정은 `#v0.3.22` 식 태그 추가):
```json
"com.youngwoocho02.unity-cli-connector": "https://github.com/youngwoocho02/unity-cli.git?path=unity-connector"
```
Connector 는 Unity 기동 시 자동 활성화된다(별도 설정 불필요, 기본 포트 8090).

---

## 1. 상태·게이팅
```bash
unity-cli status                 # 상태 · 프로젝트 경로 · Unity/Connector 버전 · PID
```
- 변경 전 항상 호출. `compiling`/`reloading` 이면 unity-cli 가 자체 대기한다.
- CLI·Connector 버전 불일치 시 에러 — Connector 패키지를 같은 버전으로 갱신(임시 우회 `--ignore-version-mismatch`).

## 2. 존재 확인 (③ scope)
```bash
# 타입(클래스) 존재 — 네임스페이스 포함
unity-cli exec "System.Type.GetType(\"Game.Inventory.InventorySystem, Assembly-CSharp\")!=null"

# 에셋 경로 존재
unity-cli exec "System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath,\"Prefabs/Player.prefab\"))"

# AssetDatabase 로 GUID 조회(존재 시 비어있지 않음)
unity-cli exec "UnityEditor.AssetDatabase.AssetPathToGUID(\"Assets/Prefabs/Player.prefab\")"

# 씬 내 오브젝트 존재
unity-cli exec "UnityEngine.GameObject.Find(\"Player\")!=null"

# ★ 배치 질의(권장) — 여러 건을 exec 1회로 (왕복 절감, SKILL §8.1-1)
unity-cli exec "return string.Join(\"\\n\", new[]{
  \"InventorySystem=\"+(System.Type.GetType(\"Game.Inventory.InventorySystem, Assembly-CSharp\")!=null),
  \"Player.prefab=\"+(UnityEditor.AssetDatabase.AssetPathToGUID(\"Assets/Prefabs/Player.prefab\")!=\"\"),
  \"PlayerGO=\"+(UnityEngine.GameObject.Find(\"Player\")!=null) });"
```

## 3. 에셋 트리 덤프 (R1 컨텍스트)
```bash
# 특정 폴더의 에셋 경로 목록
unity-cli exec "return string.Join(\"\\n\", UnityEditor.AssetDatabase.FindAssets(\"\", new[]{\"Assets/Scripts\"}).Select(g=>UnityEditor.AssetDatabase.GUIDToAssetPath(g)))" --usings System.Linq

# 특정 타입만(예: 프리팹)
unity-cli exec "return string.Join(\"\\n\", UnityEditor.AssetDatabase.FindAssets(\"t:Prefab\").Select(g=>UnityEditor.AssetDatabase.GUIDToAssetPath(g)))" --usings System.Linq

# 로드된 씬의 루트 오브젝트
unity-cli exec "return string.Join(\"\\n\", UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().Select(g=>g.name))" --usings System.Linq
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
unity-cli console --type error              # 에러 항목만(테스트 판정, --lines N 로 제한 가능)
unity-cli editor play --wait                # 플레이모드 진입 + 로딩 대기
unity-cli console --type error              # 플레이 중/후 에러 재확인
unity-cli editor stop                        # 플레이모드 종료
```
- 판정: console 에러 0 + 의도한 동작 확인 → task 통과(`07_plan.md` `- [x]`).
- ⚠ **`console --type error` 만으로는 missing-script 를 못 잡는다**: "The referenced script ... is missing!" 는
  **warning/log** 으로 뜬다(2026-06-12 NetSimController 회귀가 이 사각으로 빠짐). 런타임 부착 컴포넌트를
  검증할 땐 아래 5.1 센서스를 쓰고, warning 도 확인한다: `unity-cli console --type warning,log`.
  (PostToolUse 훅이 `.cs` 저장 시 이 패턴을 자동 스캔하고, 파일명=클래스명 위반도 작성 시점에 린트한다 — `.harness/hooks/post-asset-edit.sh`.)

## 5.1 컴포넌트 센서스 (런타임 부착 생존 점검)
> `RuntimeInitializeOnLoadMethod` 등으로 런타임 부착한 컴포넌트가 **도메인 리로드를 생존하는지** 확인한다.
> **`GameObject.Find` 금지** — DontSave/DontDestroyOnLoad 좀비(이전 세션 잔존분)를 잡아 오진을 부른다.
```bash
# 동명 GO 전수 조회 + 에디터 잔존(IsPersistent) 필터 + missing-script 카운트.
# missing=진짜 null(파괴/husk) → 도메인 리로드 후 파괴됐다는 신호. goCount>1 이면 좀비 누적.
unity-cli exec "var gos=UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>().Where(g=>g.name==\"[NetDiagnostics]\"&&!UnityEditor.EditorUtility.IsPersistent(g)).ToArray(); var sb=new System.Text.StringBuilder(); sb.Append(\"goCount=\").Append(gos.Length).Append(\"\\n\"); foreach(var g in gos){ var all=g.GetComponents<UnityEngine.MonoBehaviour>(); sb.Append(\"id=\").Append(g.GetInstanceID()).Append(\" total=\").Append(all.Length).Append(\" missing=\").Append(UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g)).Append(\"\\n\"); } return sb.ToString();" --usings System.Linq

# MonoScript 바인딩 직접 확인 — GetClass()==null 또는 의도와 다른 클래스면 파일명=클래스명 위반.
unity-cli exec "var ms=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.MonoScript>(\"Assets/.../Foo.cs\"); return ms==null?\"no-monoscript\":(ms.GetClass()==null?\"GetClass=NULL(고아)\":ms.GetClass().Name);"
```
- 도메인 리로드 재현(원 트리거): 플레이 중 `editor refresh --compile --force` → 다시 센서스. husk 면 `missing` 증가.
- 규칙: **MonoBehaviour/ScriptableObject 는 파일명=클래스명 단독 파일**. 한 파일에 MB 2개거나, 파일명과
  동명의 다른 타입이 있으면 MonoScript 바인딩을 빼앗겨 리로드 시 missing-script 가 된다. [[Unity 리로드 생존 규칙]]

## 6. 커스텀 툴 (반복 셋업 승격)
```bash
unity-cli list                               # 사용 가능 명령 + [UnityCliTool] 프로젝트 툴 목록
unity-cli <ToolName> --params '<json>'       # 커스텀 툴 직접 호출
```
- 자주 쓰는 셋업(테스트 리그 구성 등)은 `[UnityCliTool]` 로 고정해 재사용한다.

## 7. 스냅샷 덤프 (as-is, /cycle-start 가 호출)
```bash
# ★ 파일 직접 쓰기(권장) — exec 안에서 File.WriteAllLines 로 저장하면 stdout 잘림(~6KB)이 원천 회피된다
unity-cli exec "var ps=UnityEditor.AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith(\"Assets/\")).OrderBy(p=>p).ToArray(); System.IO.File.WriteAllLines(\".harness/snapshots/<날짜>_<slug>_before.txt\", ps); return \"saved \"+ps.Length;" --usings System.Linq

# (구) stdout 수신 방식 — 소규모 질의에만. 대용량은 위 파일 쓰기로.
unity-cli exec "return UnityEditor.AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith(\"Assets/\")).ToArray()" --usings System.Linq
```
- 스냅샷 diff: `diff before.txt after.txt` (정렬돼 있어 안정적). 전체 diff 의 권위는 여전히 git.

## 8. 검증 증빙 수집 (⑧ result — `_conventions.md` §10)
> 모든 출력은 사이클 폴더 `evidence/` 에 저장하고 08_result.md 에서 발췌+링크한다.
> `<CID>` = `.harness/cycles/<id>` (프로젝트 루트 기준 상대경로).
```bash
# (a) 검증 시각 기록 — 루프 시작/종료 시 1회씩
date '+%Y-%m-%d %H:%M'                                   # → result "검증 환경·시각" 표에 기입

# (b) 검증 환경 — Unity/Connector 버전 증빙
unity-cli status > "<CID>/evidence/status.txt"

# (c) 콘솔 덤프 — 판정 근거 저장(에러만 / 최근 N줄)
unity-cli console --type error > "<CID>/evidence/console_error.txt"
unity-cli console --lines 100  > "<CID>/evidence/console_tail.txt"

# (d) 플레이 중 스크린샷 — HUD·화면 증빙 (경로는 프로젝트 루트 기준, 플레이모드에서만 동작)
unity-cli exec "UnityEngine.ScreenCapture.CaptureScreenshot(\".harness/cycles/<id>/evidence/play_smoke.png\"); return \"shot\";"
# 저장은 비동기(다음 프레임) — 1~2초 후 파일 존재를 확인한다. supersize 인자(2)로 2배 해상도 가능.

# (e) 특정 상태 증빙 — exec 결과를 그대로 파일로 (예: 부착 컴포넌트 목록)
unity-cli exec "var go=GameObject.Find(\"[NetDiagnostics]\"); return go==null?\"NO-GO\":string.Join(\",\", go.GetComponents<MonoBehaviour>().Select(c=>c.GetType().Name));" --usings System.Linq,UnityEngine > "<CID>/evidence/components.txt"
```
- 판정 직후 바로 저장한다(나중에 재현 불가). md 임베드: `![..](evidence/play_smoke.png)`.

## 9. 대용량 출력 처리 (컨텍스트 절약 — SKILL §8.1-2)
> 원칙: **대화(stdout)로 받지 말고 파일로 쓰게 한 뒤 발췌만 읽는다.**
```bash
# (a) CLI 출력 리다이렉트 — 콘솔·목록류
unity-cli console --lines 200 > "<CID>/evidence/console_full.txt"   # 이후 grep/head 로 발췌
# (b) exec 결과를 에디터가 직접 파일로 — stdout 잘림 회피(§7 패턴)
unity-cli exec "System.IO.File.WriteAllText(\"<경로>\", <문자열식>); return \"saved\";"
# (c) 발췌 읽기 — 전체 Read 금지
grep -m5 "error CS" "<CID>/evidence/console_full.txt"
head -30 "<CID>/evidence/console_full.txt"
```
- exec stdout 한도는 ~6KB(실측) — 그 이상은 (b) 필수. 산출물 md 에는 발췌 + 파일 링크만 싣는다.

## 10. 테스트 씬 셋업 (`/test-run` — `_conventions.md` §17)
> 복잡한 셋업은 **stdin 파이프**(`echo '…C#…' | unity-cli exec`)로 보내 셸 이스케이프를 피한다.
> `<C>` = cycle-id. 테스트 에셋 루트 = `Assets/_TestRuns/<C>/assets`. 모든 생성 후 `AssetDatabase.SaveAssets()`.
```bash
# (a) 폴더 구조 생성 (.meta 정합 — mkdir 대신 AssetDatabase)
echo 'foreach(var p in new[]{"Assets/_TestRuns","Assets/_TestRuns/<C>","Assets/_TestRuns/<C>/assets","Assets/_TestRuns/<C>/assets/Prefabs","Assets/_TestRuns/<C>/assets/Materials"}){ if(!UnityEditor.AssetDatabase.IsValidFolder(p)){ var i=p.LastIndexOf("/"); UnityEditor.AssetDatabase.CreateFolder(p.Substring(0,i), p.Substring(i+1)); } } UnityEditor.AssetDatabase.SaveAssets(); return "folders ok";' | unity-cli exec

# (b) 동작 stub 머티리얼
echo 'var m=new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(m,"Assets/_TestRuns/<C>/assets/Materials/Mat_S1__DUMMY.mat"); UnityEditor.AssetDatabase.SaveAssets(); return "mat";' | unity-cli exec

# (c) 동작 stub 프리팹 (primitive 메시 + 컴포넌트 — 로딩·참조 검증되게)
echo 'var go=UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube); go.name="S1__DUMMY"; /* 사이클 명시 컴포넌트 있으면 go.AddComponent<T>() */ var p=UnityEditor.PrefabUtility.SaveAsPrefabAsset(go,"Assets/_TestRuns/<C>/assets/Prefabs/S1__DUMMY.prefab"); UnityEngine.Object.DestroyImmediate(go); return p!=null?"prefab ok":"FAIL";' | unity-cli exec

# (d) 동작 stub ScriptableObject (타입이 존재할 때만)
echo 'var t=System.Type.GetType("Game.MyData, Assembly-CSharp"); if(t==null) return "no-type"; var so=UnityEngine.ScriptableObject.CreateInstance(t); UnityEditor.AssetDatabase.CreateAsset(so,"Assets/_TestRuns/<C>/assets/Data_S2__DUMMY.asset"); return "so";' | unity-cli exec

# (e) 새 테스트 씬 생성 + Hierarchy 배치 + 프리팹 인스턴스화 + 저장
echo 'var s=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects, UnityEditor.SceneManagement.NewSceneMode.Single); var grp=new UnityEngine.GameObject("━━━━ Test Layer ━━━━"); var pf=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_TestRuns/<C>/assets/Prefabs/S1__DUMMY.prefab"); var inst=(UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(pf); inst.transform.SetParent(grp.transform); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s,"Assets/_TestRuns/<C>/<C>_TestScene.unity"); return "scene ok";' | unity-cli exec

# (f) 에셋 로딩·참조 검증 (플레이 전 정합)
echo 'var pf=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_TestRuns/<C>/assets/Prefabs/S1__DUMMY.prefab"); if(pf==null) return "LOAD-FAIL"; int miss=0; foreach(var c in pf.GetComponentsInChildren<UnityEngine.Component>(true)) if(c==null) miss++; return "load ok, missing="+miss;' | unity-cli exec

# (g) 더미↔실제 교체 판정 (실제 있으면 더미 안 씀)
echo 'var real="Assets/_TestRuns/<C>/assets/Prefabs/S1.prefab"; var dummy="Assets/_TestRuns/<C>/assets/Prefabs/S1__DUMMY.prefab"; return System.IO.File.Exists(System.IO.Path.Combine(UnityEngine.Application.dataPath,"../"+real))?("use-real:"+real):("use-dummy:"+dummy);' | unity-cli exec
```
- 씬 셋업 후 `unity-cli reserialize Assets/_TestRuns/<C>/<C>_TestScene.unity` 로 정합화(또는 PostToolUse 훅).
- 플레이 검증: `unity-cli editor play --wait` → `console --type error` → 스크린샷(쿡북 §8). test-runner 위임 권장.
- **테스트 씬은 빌드 씬 목록에 넣지 않는다**(EditorBuildSettings 미수정).

---
### 주의 (v0.3.x)
- `exec` 단일 식은 자동 return, 멀티스테이트먼트는 명시적 `return`, 추가 네임스페이스는 `--usings`(반복 가능).
- **복잡한 C# 는 stdin 파이프가 안전**(셸 이스케이프 회피): `echo 'Debug.Log("hi"); return null;' | unity-cli exec`
- async/코루틴/지연 콜백은 기본 차단 — 필요 시 `--allow-async`.
- 전역 플래그: `--project <경로>`(인스턴스 선택), `--timeout <ms>`(기본 120000). `--port`/`--json` 은 없음.
- 파괴적 패턴(reserialize 대량/DeleteAsset/Destroy 등)은 **G4 가드가 가로채 ask/deny** 한다.
