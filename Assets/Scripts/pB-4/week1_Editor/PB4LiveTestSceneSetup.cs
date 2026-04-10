// =============================================================================
// PB4LiveTestSceneSetup.cs  |  pB×pC 통합 — Week 1 (에디터 도구)
// Layer  : Editor
// Namespace: TDA.PB4.Editor
//
// 역할:
//   실증 테스트 씬을 자동으로 설정하는 에디터 도구.
//   기존 AI 프리팹에 PB4DecisionAdapter + AIPerceptionSystem을 일괄 부착하고
//   GameBlackboard, EventBus, FootprintTrailSystem 등을 자동 배치한다.
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using TDA.PB4.Core;
using TDA.PB4.AI.Perception;
using TDA.PB4.Audio;
using TDA.PB4.Bridge;
using TDA.Character.AI;

namespace TDA.PB4.Editor
{
    public class PB4LiveTestSceneSetup : EditorWindow
    {
        [MenuItem("pB-4/Live Test Scene Setup")]
        public static void ShowWindow() => GetWindow<PB4LiveTestSceneSetup>("pB-4 Live Test Setup");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("pB-4 실증 테스트 씬 설정 도구", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            EditorGUILayout.HelpBox(
                "이 도구는 현재 씬에 pB-4 실증 테스트에 필요한 컴포넌트를 자동으로 배치합니다.\n" +
                "• GameBlackboard (없으면 생성)\n" +
                "• FootprintTrailSystem (없으면 생성)\n" +
                "• FactionDetectionSFXManager (없으면 생성)\n" +
                "• PersistentHuntDirector (없으면 생성)\n" +
                "• 모든 AICharacterManager에 PB4DecisionAdapter + AIPerceptionSystem 부착",
                MessageType.Info);

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Step 1: pB-4 인프라 배치", GUILayout.Height(30)))
                SetupInfrastructure();

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Step 2: AI 프리팹에 어댑터 일괄 부착", GUILayout.Height(30)))
                AttachAdaptersToAllAI();

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Step 3: FootprintTrail을 Player에 부착", GUILayout.Height(30)))
                SetupFootprintTrail();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("현재 씬 상태:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"  GameBlackboard: {(FindFirstObjectByType<GameBlackboard>() != null ? "✅" : "❌")}");
            EditorGUILayout.LabelField($"  FootprintTrailSystem: {(FindFirstObjectByType<FootprintTrailSystem>() != null ? "✅" : "❌")}");
            EditorGUILayout.LabelField($"  FactionDetectionSFXManager: {(FindFirstObjectByType<FactionDetectionSFXManager>() != null ? "✅" : "❌")}");
            EditorGUILayout.LabelField($"  PersistentHuntDirector: {(FindFirstObjectByType<PersistentHuntDirector>() != null ? "✅" : "❌")}");

            var adapters = FindObjectsByType<PB4DecisionAdapter>(FindObjectsSortMode.None);
            EditorGUILayout.LabelField($"  PB4DecisionAdapter: {adapters.Length}개");

            var perceptions = FindObjectsByType<AIPerceptionSystem>(FindObjectsSortMode.None);
            EditorGUILayout.LabelField($"  AIPerceptionSystem: {perceptions.Length}개");
        }

        private void SetupInfrastructure()
        {
            // GameBlackboard
            if (FindFirstObjectByType<GameBlackboard>() == null)
            {
                var go = new GameObject("PB4_GameBlackboard");
                go.AddComponent<GameBlackboard>();
                Debug.Log("[LiveTest] GameBlackboard 생성 완료");
            }

            // FootprintTrailSystem
            if (FindFirstObjectByType<FootprintTrailSystem>() == null)
            {
                var go = new GameObject("PB4_FootprintTrail");
                go.AddComponent<FootprintTrailSystem>();
                Debug.Log("[LiveTest] FootprintTrailSystem 생성 완료");
            }

            // FactionDetectionSFXManager
            if (FindFirstObjectByType<TDA.PB4.Audio.FactionDetectionSFXManager>() == null)
            {
                var go = new GameObject("PB4_FactionSFX");
                go.AddComponent<TDA.PB4.Audio.FactionDetectionSFXManager>();
                Debug.Log("[LiveTest] FactionDetectionSFXManager 생성 완료");
            }

            // PersistentHuntDirector
            if (FindFirstObjectByType<PersistentHuntDirector>() == null)
            {
                var go = new GameObject("PB4_HuntDirector");
                go.AddComponent<PersistentHuntDirector>();
                Debug.Log("[LiveTest] PersistentHuntDirector 생성 완료");
            }

            Debug.Log("[LiveTest] ✅ Step 1 완료: pB-4 인프라 배치");
        }

        private void AttachAdaptersToAllAI()
        {
            var aiManagers = FindObjectsByType<AICharacterManager>(FindObjectsSortMode.None);
            int count = 0;
            foreach (var ai in aiManagers)
            {
                if (ai.GetComponent<PB4DecisionAdapter>() == null)
                {
                    ai.gameObject.AddComponent<PB4DecisionAdapter>();
                    count++;
                }
                if (ai.GetComponent<AIPerceptionSystem>() == null)
                {
                    ai.gameObject.AddComponent<AIPerceptionSystem>();
                }
            }
            Debug.Log($"[LiveTest] ✅ Step 2 완료: {count}개 AI에 어댑터+인지 부착 (총 {aiManagers.Length}개 AI)");
        }

        private void SetupFootprintTrail()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                Debug.LogWarning("[LiveTest] 'Player' 태그 오브젝트를 찾을 수 없습니다.");
                return;
            }

            var trail = FindFirstObjectByType<FootprintTrailSystem>();
            if (trail != null)
            {
                trail.playerTransform = player.transform;

                // SoundEventEmitter 부착 (발소리 자동 발행)
                if (player.GetComponent<SoundEventEmitter>() == null)
                {
                    var emitter = player.AddComponent<SoundEventEmitter>();
                    emitter.defaultSoundType = SoundType.Footstep;
                    emitter.defaultVolume = 0.3f;
                    emitter.autoEmitInterval = 0.5f;
                }
            }

            Debug.Log($"[LiveTest] ✅ Step 3 완료: Player({player.name})에 FootprintTrail+SoundEmitter 연결");
        }
    }
}
#endif