// ─────────────────────────────────────────────────────────────────────
// pB-4 Week 3 D3 AM — Wk3_NaturalEmergenceScene 자동 셋팅 스크립트
// ─────────────────────────────────────────────────────────────────────
// 위치: Assets/Editor/Wk3SceneSetupTool.cs
// 메뉴: pB-4 / Setup / Wk3 Natural Emergence Scene
//
// 기능:
//   1. Layer "Terrain" 자동 생성/확인
//   2. Ground (40×30m 통합 바닥) 생성
//   3. NarrowCorridor (WallLeft + WallRight, 폭 1.4m, 길이 2.5m) 생성
//   4. HighPlatform (5×3×5m, y=1.5) 생성
//   5. DenseForest (Cylinder × 20개 자동 배치) 생성
//   6. NPC_SpawnPoint (Empty, -5,0,0) 생성
//   7. NavMeshSurface 생성 + Bake 실행
//   8. Week3Bootstrapper GameObject 생성
//
// 사용자 추가 작업 (스크립트 실행 후):
//   - NPC 프리팹을 NPC_SpawnPoint 위치에 드래그
//   - NPC GameObject에 BlackboardUpdater + ContextManager 컴포넌트 추가
//   - Week3Bootstrapper Inspector에 ContextManager 할당
// ─────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;

namespace TDA.PB4.Editor
{
    public static class Wk3SceneSetupTool
    {
        // ── 매직 넘버 금지: 모든 좌표/크기 const 선언 ──────────────
        private const string TERRAIN_LAYER = "Terrain";
        private const string SCENE_PATH = "Assets/Scenes/Wk3_NaturalEmergenceScene.unity";

        // Ground (통합 바닥)
        private static readonly Vector3 GROUND_POS = new Vector3(0, -0.05f, 0);
        private static readonly Vector3 GROUND_SCALE = new Vector3(40, 0.1f, 30);

        // NarrowCorridor (좁은 통로 폭 1.4m, 길이 2.5m)
        private static readonly Vector3 WALL_LEFT_POS = new Vector3(-0.8f, 1, 0);
        private static readonly Vector3 WALL_RIGHT_POS = new Vector3(+0.8f, 1, 0);
        private static readonly Vector3 WALL_SCALE = new Vector3(0.2f, 2, 2.5f);

        // HighPlatform (5×3×5, y=1.5)
        private static readonly Vector3 HIGH_PLATFORM_POS = new Vector3(-10, 1.5f, 0);
        private static readonly Vector3 HIGH_PLATFORM_SCALE = new Vector3(5, 3, 5);

        // DenseForest (중심 (0,0,-10), 트리 20개, 반경 3m)
        private static readonly Vector3 DENSE_FOREST_CENTER = new Vector3(0, 0, -10);
        private const int DENSE_FOREST_TREE_COUNT = 20;
        private const float DENSE_FOREST_RADIUS = 3f;
        private static readonly Vector3 TREE_SCALE = new Vector3(0.5f, 2, 0.5f);

        // NPC_SpawnPoint (-5, 0, 0)
        private static readonly Vector3 NPC_SPAWN_POS = new Vector3(-5, 0, 0);

        // NavMesh Agent 설정 (Humanoid 기본값)
        private const float AGENT_RADIUS = 0.5f;
        private const float AGENT_HEIGHT = 2.0f;

        // ─────────────────────────────────────────────────────────
        // 메인 메뉴 항목
        // ─────────────────────────────────────────────────────────

        [MenuItem("pB-4/Setup/Wk3 Natural Emergence Scene")]
        public static void SetupScene()
        {
            // 사용자 확인
            bool confirm = EditorUtility.DisplayDialog(
                "Wk3 자연 발현 씬 자동 생성",
                "현재 씬에 Wk3_NaturalEmergenceScene 자산을 자동 생성합니다.\n" +
                "기존 GameObject 중 같은 이름이 있으면 덮어씁니다.\n\n" +
                "계속하시겠습니까?",
                "생성", "취소"
            );
            if (!confirm) return;

            try
            {
                // 1. Layer "Terrain" 확보
                int terrainLayer = EnsureTerrainLayer();
                if (terrainLayer < 0)
                {
                    EditorUtility.DisplayDialog("실패",
                        "Layer 'Terrain' 생성 실패. ProjectSettings/TagManager.asset 확인 필요.", "OK");
                    return;
                }

                // 2. 기존 동명 GameObject 정리
                CleanupExistingObjects();

                // 3. Ground 생성
                var ground = CreateGround(terrainLayer);

                // 4. NarrowCorridor 생성
                var corridor = CreateNarrowCorridor(terrainLayer);

                // 5. HighPlatform 생성
                var highPlatform = CreateHighPlatform(terrainLayer);

                // 6. DenseForest 생성
                var denseForest = CreateDenseForest(terrainLayer);

                // 7. NPC_SpawnPoint 생성
                var spawnPoint = CreateNPCSpawnPoint();

                // 8. NavMeshSurface 생성 + Bake
                var navMeshSurface = CreateNavMeshSurface(terrainLayer);

                // 9. Week3Bootstrapper GameObject 생성
                var bootstrapper = CreateWeek3Bootstrapper();

                // 10. 씬 저장
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

                // 완료 안내
                EditorUtility.DisplayDialog("완료",
                    "Wk3 자연 발현 씬 자동 생성 완료.\n\n" +
                    "다음 사용자 작업:\n" +
                    "1. NPC 프리팹을 NPC_SpawnPoint 위치에 드래그\n" +
                    "2. NPC에 BlackboardUpdater + ContextManager 컴포넌트 추가\n" +
                    "3. Week3Bootstrapper Inspector에 ContextManager 할당\n" +
                    "4. BG editor BB 변수 7개 등록 (BG_EDITOR_GUIDE.md 참조)\n" +
                    "5. Play 진입 → 자연 발현 검증",
                    "OK");

                // 생성된 GameObject 선택
                Selection.activeGameObject = bootstrapper;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Wk3SceneSetup] 실패: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("실패", $"오류 발생: {ex.Message}", "OK");
            }
        }

        // ─────────────────────────────────────────────────────────
        // 1. Layer "Terrain" 확보 (TagManager 직접 편집)
        // ─────────────────────────────────────────────────────────

        private static int EnsureTerrainLayer()
        {
            // 이미 존재하면 인덱스 반환
            int existing = LayerMask.NameToLayer(TERRAIN_LAYER);
            if (existing >= 0)
            {
                Debug.Log($"[Wk3SceneSetup] Layer '{TERRAIN_LAYER}' 이미 존재 (index={existing})");
                return existing;
            }

            // TagManager.asset 편집
            var tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAsset == null || tagManagerAsset.Length == 0)
            {
                Debug.LogError("[Wk3SceneSetup] TagManager.asset 로드 실패");
                return -1;
            }

            var tagManager = new SerializedObject(tagManagerAsset[0]);
            var layersProp = tagManager.FindProperty("layers");

            // User Layer 8 ~ 31 중 빈 슬롯에 추가
            for (int i = 8; i < 32; i++)
            {
                var layerProp = layersProp.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layerProp.stringValue))
                {
                    layerProp.stringValue = TERRAIN_LAYER;
                    tagManager.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[Wk3SceneSetup] Layer '{TERRAIN_LAYER}' 신규 생성 (index={i})");
                    return i;
                }
            }

            Debug.LogError("[Wk3SceneSetup] User Layer 슬롯 부족 (8~31 모두 사용 중)");
            return -1;
        }

        // ─────────────────────────────────────────────────────────
        // 2. 기존 동명 GameObject 정리
        // ─────────────────────────────────────────────────────────

        private static void CleanupExistingObjects()
        {
            string[] names = {
                "Ground", "NarrowCorridor", "HighPlatform", "DenseForest",
                "NPC_SpawnPoint", "NavMeshSurface", "Week3Bootstrapper",
                "OpenArea", "OpenArea_Floor"  // 구버전 이름도 정리
            };

            foreach (var name in names)
            {
                var existing = GameObject.Find(name);
                if (existing != null)
                {
                    Object.DestroyImmediate(existing);
                    Debug.Log($"[Wk3SceneSetup] 기존 '{name}' 제거");
                }
            }
        }

        // ─────────────────────────────────────────────────────────
        // 3. Ground (통합 바닥, 40×0.1×30)
        // ─────────────────────────────────────────────────────────

        private static GameObject CreateGround(int terrainLayer)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = GROUND_POS;
            ground.transform.localScale = GROUND_SCALE;
            ground.layer = terrainLayer;

            // 시각 구분: 회색 머티리얼
            ApplyMaterial(ground, new Color(0.6f, 0.6f, 0.6f));

            Debug.Log("[Wk3SceneSetup] Ground 생성 (40×0.1×30, Terrain Layer)");
            return ground;
        }

        // ─────────────────────────────────────────────────────────
        // 4. NarrowCorridor (좁은 통로, 폭 1.4m + 길이 2.5m)
        // ─────────────────────────────────────────────────────────

        private static GameObject CreateNarrowCorridor(int terrainLayer)
        {
            var corridor = new GameObject("NarrowCorridor");
            corridor.transform.position = Vector3.zero;

            // Cube 사용 사유:
            //   1. BoxCollider 자동 생성 (CapsuleCollider의 둥근 영역 회피)
            //   2. Cube mesh 1×1×1 → Scale 값이 그대로 m 단위로 적용
            //   3. NavMesh 베이크 시 깔끔한 직육면체 회피 영역
            var wallLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallLeft.name = "WallLeft";
            wallLeft.transform.SetParent(corridor.transform);
            wallLeft.transform.position = WALL_LEFT_POS;
            wallLeft.transform.localScale = WALL_SCALE;
            wallLeft.layer = terrainLayer;
            ApplyMaterial(wallLeft, new Color(0.8f, 0.2f, 0.2f));  // 빨강

            var wallRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallRight.name = "WallRight";
            wallRight.transform.SetParent(corridor.transform);
            wallRight.transform.position = WALL_RIGHT_POS;
            wallRight.transform.localScale = WALL_SCALE;
            wallRight.layer = terrainLayer;
            ApplyMaterial(wallRight, new Color(0.8f, 0.2f, 0.2f));

            Debug.Log("[Wk3SceneSetup] NarrowCorridor 생성 (Cube 벽, 폭 1.4m, 길이 2.5m, 두께 0.2m)");
            return corridor;
        }

        // ─────────────────────────────────────────────────────────
        // 5. HighPlatform (고지, 5×3×5, y=1.5)
        // ─────────────────────────────────────────────────────────

        private static GameObject CreateHighPlatform(int terrainLayer)
        {
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "HighPlatform";
            platform.transform.position = HIGH_PLATFORM_POS;
            platform.transform.localScale = HIGH_PLATFORM_SCALE;
            platform.layer = terrainLayer;
            ApplyMaterial(platform, new Color(0.4f, 0.6f, 0.4f));  // 초록

            Debug.Log("[Wk3SceneSetup] HighPlatform 생성 (5×3×5, y=1.5)");
            return platform;
        }

        // ─────────────────────────────────────────────────────────
        // 6. DenseForest (Cylinder × 20개)
        // ─────────────────────────────────────────────────────────

        private static GameObject CreateDenseForest(int terrainLayer)
        {
            var forest = new GameObject("DenseForest");
            forest.transform.position = DENSE_FOREST_CENTER;

            // 결정적 배치 (Random.InitState로 같은 결과 보장)
            Random.InitState(42);

            var treeMaterial = CreateMaterial(new Color(0.4f, 0.3f, 0.1f));  // 갈색

            for (int i = 0; i < DENSE_FOREST_TREE_COUNT; i++)
            {
                var tree = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tree.name = $"Tree_{i:00}";
                tree.transform.SetParent(forest.transform);
                tree.transform.localPosition = new Vector3(
                    Random.Range(-DENSE_FOREST_RADIUS, DENSE_FOREST_RADIUS),
                    1f,
                    Random.Range(-DENSE_FOREST_RADIUS, DENSE_FOREST_RADIUS)
                );
                tree.transform.localScale = TREE_SCALE;
                tree.layer = terrainLayer;

                var renderer = tree.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = treeMaterial;
            }

            Debug.Log($"[Wk3SceneSetup] DenseForest 생성 ({DENSE_FOREST_TREE_COUNT}개 트리, 반경 {DENSE_FOREST_RADIUS}m)");
            return forest;
        }

        // ─────────────────────────────────────────────────────────
        // 7. NPC_SpawnPoint (Empty)
        // ─────────────────────────────────────────────────────────

        private static GameObject CreateNPCSpawnPoint()
        {
            var spawn = new GameObject("NPC_SpawnPoint");
            spawn.transform.position = NPC_SPAWN_POS;

            // 시각 디버깅: 작은 Sphere 자식
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker_Visual";
            marker.transform.SetParent(spawn.transform);
            marker.transform.localPosition = Vector3.up * 0.5f;
            marker.transform.localScale = Vector3.one * 0.3f;
            // 마커는 콜라이더 비활성 (NavMesh 영향 X)
            var col = marker.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            ApplyMaterial(marker, new Color(0.2f, 0.8f, 0.2f));  // 밝은 초록

            Debug.Log("[Wk3SceneSetup] NPC_SpawnPoint 생성 (-5, 0, 0)");
            return spawn;
        }

        // ─────────────────────────────────────────────────────────
        // 8. NavMeshSurface 생성 + Bake
        // ─────────────────────────────────────────────────────────

        private static GameObject CreateNavMeshSurface(int terrainLayer)
        {
            var navMeshGO = new GameObject("NavMeshSurface");
            navMeshGO.transform.position = Vector3.zero;

            var surface = navMeshGO.AddComponent<NavMeshSurface>();

            // 설정: Humanoid Agent + Terrain Layer
            surface.agentTypeID = 0;  // Humanoid (기본 인덱스 0)
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = (1 << terrainLayer);
            surface.minRegionArea = 0;  // 작은 NavMesh 영역도 보존

            // useGeometry는 SerializedObject로 안전 설정
            // (NavMeshCollectGeometry enum이 internal일 수 있어 직접 접근 회피)
            // 0 = RenderMeshes (기본값, Cube/Cylinder의 mesh와 collider 일치 → 차이 없음)
            // 1 = PhysicsColliders
            try
            {
                var so = new SerializedObject(surface);
                var useGeomProp = so.FindProperty("m_UseGeometry");
                if (useGeomProp != null)
                {
                    useGeomProp.enumValueIndex = 1;  // PhysicsColliders
                    so.ApplyModifiedProperties();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Wk3SceneSetup] useGeometry 설정 실패 (기본값 RenderMeshes 사용): {ex.Message}");
            }

            // Bake 실행
            surface.BuildNavMesh();

            Debug.Log("[Wk3SceneSetup] NavMeshSurface 생성 + Bake 완료");
            return navMeshGO;
        }

        // ─────────────────────────────────────────────────────────
        // 9. Week3Bootstrapper GameObject
        // ─────────────────────────────────────────────────────────

        private static GameObject CreateWeek3Bootstrapper()
        {
            var bootstrapper = new GameObject("Week3Bootstrapper");
            bootstrapper.transform.position = Vector3.zero;

            // Week3Bootstrapper 컴포넌트는 사용자 코드에 의존
            // 자동 추가 시도 (타입이 컴파일되어 있어야 함)
            var bootstrapperType = System.Type.GetType("TDA.PB4.AI.Bootstrap.Week3Bootstrapper, Assembly-CSharp");
            if (bootstrapperType != null)
            {
                bootstrapper.AddComponent(bootstrapperType);
                Debug.Log("[Wk3SceneSetup] Week3Bootstrapper 컴포넌트 자동 추가");
            }
            else
            {
                Debug.LogWarning("[Wk3SceneSetup] Week3Bootstrapper 타입 미발견. " +
                                 "수동으로 Add Component 필요 (D2 3차 코드 배치 후 다시 실행).");
            }

            return bootstrapper;
        }

        // ─────────────────────────────────────────────────────────
        // 헬퍼: 머티리얼 적용
        // ─────────────────────────────────────────────────────────

        private static void ApplyMaterial(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = CreateMaterial(color);
        }

        private static Material CreateMaterial(Color color)
        {
            // URP 또는 Built-in 자동 감지
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }

        // ─────────────────────────────────────────────────────────
        // 추가 메뉴: 씬 정리만
        // ─────────────────────────────────────────────────────────

        [MenuItem("pB-4/Setup/Cleanup Wk3 Scene Objects")]
        public static void CleanupOnly()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Wk3 씬 객체 정리",
                "Wk3 자동 생성 GameObject를 모두 제거합니다.\n계속하시겠습니까?",
                "정리", "취소"
            );
            if (!confirm) return;

            CleanupExistingObjects();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Wk3SceneSetup] 정리 완료");
        }

        // ─────────────────────────────────────────────────────────
        // 추가 메뉴: NavMesh 재 Bake
        // ─────────────────────────────────────────────────────────

        [MenuItem("pB-4/Setup/Rebake NavMesh")]
        public static void RebakeNavMesh()
        {
            var navMeshGO = GameObject.Find("NavMeshSurface");
            if (navMeshGO == null)
            {
                EditorUtility.DisplayDialog("실패",
                    "NavMeshSurface GameObject 미존재. 'Setup Wk3 Natural Emergence Scene' 먼저 실행.", "OK");
                return;
            }

            var surface = navMeshGO.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                EditorUtility.DisplayDialog("실패", "NavMeshSurface 컴포넌트 없음.", "OK");
                return;
            }

            surface.BuildNavMesh();
            Debug.Log("[Wk3SceneSetup] NavMesh 재 Bake 완료");
        }

        // ─────────────────────────────────────────────────────────
        // 추가 메뉴: 기존 Cylinder 벽을 Cube로 빠른 교체
        // (이전 버전으로 셋팅한 씬 재셋팅 없이 수정)
        // ─────────────────────────────────────────────────────────

        [MenuItem("pB-4/Setup/Fix Wall Colliders (Cylinder → Cube)")]
        public static void FixWallColliders()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "벽 콜라이더 수정",
                "기존 NarrowCorridor의 WallLeft/WallRight를 Cube로 교체합니다.\n" +
                "(CapsuleCollider 둥근 영역이 NavMesh 회피 영역을 키우는 문제 수정)\n\n" +
                "계속하시겠습니까?",
                "수정", "취소"
            );
            if (!confirm) return;

            var corridor = GameObject.Find("NarrowCorridor");
            if (corridor == null)
            {
                EditorUtility.DisplayDialog("실패", "NarrowCorridor GameObject 미존재.", "OK");
                return;
            }

            int terrainLayer = LayerMask.NameToLayer(TERRAIN_LAYER);
            if (terrainLayer < 0)
            {
                EditorUtility.DisplayDialog("실패", "Layer 'Terrain' 미존재.", "OK");
                return;
            }

            // 기존 자식 정리 후 Cube로 재생성
            for (int i = corridor.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(corridor.transform.GetChild(i).gameObject);
            }

            var wallLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallLeft.name = "WallLeft";
            wallLeft.transform.SetParent(corridor.transform);
            wallLeft.transform.position = WALL_LEFT_POS;
            wallLeft.transform.localScale = WALL_SCALE;
            wallLeft.layer = terrainLayer;
            ApplyMaterial(wallLeft, new Color(0.8f, 0.2f, 0.2f));

            var wallRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wallRight.name = "WallRight";
            wallRight.transform.SetParent(corridor.transform);
            wallRight.transform.position = WALL_RIGHT_POS;
            wallRight.transform.localScale = WALL_SCALE;
            wallRight.layer = terrainLayer;
            ApplyMaterial(wallRight, new Color(0.8f, 0.2f, 0.2f));

            Debug.Log("[Wk3SceneSetup] WallLeft/WallRight를 Cube로 교체 완료");

            // NavMesh 자동 재 Bake
            var navMeshGO = GameObject.Find("NavMeshSurface");
            if (navMeshGO != null)
            {
                var surface = navMeshGO.GetComponent<NavMeshSurface>();
                if (surface != null)
                {
                    surface.BuildNavMesh();
                    Debug.Log("[Wk3SceneSetup] NavMesh 재 Bake 완료");
                }
            }

            EditorUtility.DisplayDialog("완료", "벽 콜라이더 수정 + NavMesh 재 Bake 완료.", "OK");
        }
    }
}
#endif