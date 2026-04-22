// =============================================================================
// HumanoidBootstrapperEditor.cs  |  pB-4 Week 2 — Day 1 T1.4 (v3: Custom Inspector)
// 배치: Assets/Scripts/pB-4/week2_Bootstrap/Editor/HumanoidBootstrapperEditor.cs
//       (반드시 "Editor" 폴더 안에 — 빌드 시 자동 제외됨)
//
// 역할: Inspector에서 debugLog 실시간 토글 버튼을 Humanoid Bootstrapper 상단에 표시.
//       Play 모드 / Edit 모드 모두에서 동작. 4개 컴포넌트(Formula/Resolver/Trust/Trauma)
//       의 debugLog를 한 번에 켜고/끄는 버튼.
//
// 의존: Editor 전용 API (UnityEditor)이므로 #if UNITY_EDITOR 가드 불필요 —
//       Editor 폴더 안에 있으면 빌드 시 자동 제외.
// =============================================================================
using UnityEditor;
using UnityEngine;
using TDA.PB4.AI;
using TDA.PB4.AI.Humanoid;

namespace TDA.PB4.Bootstrap.Editor
{
    [CustomEditor(typeof(HumanoidBootstrapper))]
    public class HumanoidBootstrapperEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var bootstrapper = (HumanoidBootstrapper)target;

            // ═══ 상단: DebugLog 제어 패널 ═══════════════════
            DrawDebugLogControlPanel(bootstrapper);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(4);

            // ═══ 나머지: 기본 Inspector ═════════════════════
            base.OnInspectorGUI();
        }

        private void DrawDebugLogControlPanel(HumanoidBootstrapper bootstrapper)
        {
            EditorGUILayout.BeginVertical("box");

            // 헤더
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.3f, 0.5f, 0.8f) }
            };
            EditorGUILayout.LabelField("🔍 DebugLog 일괄 제어", headerStyle);

            // 현재 상태 파악
            var status = ReadDebugLogStatus();

            // 상태 표시
            string statusText = status.brainCount == 0
                ? "NPC 없음 (Play 모드 아니거나 씬 비어있음)"
                : $"NPC {status.brainCount}개 | Formula ON {status.formulaOn} | Resolver ON {status.resolverOn} | Trust ON {status.trustOn} | Trauma ON {status.traumaOn}";

            var statusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = status.AllOn
                    ? new Color(0.2f, 0.7f, 0.2f)
                    : (status.AllOff ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.9f, 0.6f, 0.1f)) }
            };
            EditorGUILayout.LabelField(statusText, statusStyle);

            EditorGUILayout.Space(2);

            // 3개 버튼 (토글 / 전체 ON / 전체 OFF)
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.6f, 0.8f, 1.0f);
            if (GUILayout.Button("🔄 Toggle All", GUILayout.Height(24)))
            {
                bootstrapper.ToggleAllDebugLog();
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
            if (GUILayout.Button("✅ Enable All", GUILayout.Height(24)))
            {
                bootstrapper.SetAllDebugLog(true);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(0.95f, 0.6f, 0.6f);
            if (GUILayout.Button("🔇 Disable All", GUILayout.Height(24)))
            {
                bootstrapper.SetAllDebugLog(false);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            // 안내
            var hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };
            EditorGUILayout.LabelField(
                "Play 모드에서 4개 컴포넌트(Formula/Resolver/Trust/Trauma)의 debugLog 일괄 제어. " +
                "아래 enableDebugLogOnBootstrap 체크 시 부트스트랩 시점에 자동 ON.",
                hintStyle);

            EditorGUILayout.EndVertical();
        }

        private DebugLogStatus ReadDebugLogStatus()
        {
            var status = new DebugLogStatus();

            if (!Application.isPlaying) return status;

            var brains = Object.FindObjectsByType<HumanoidAIBrain>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            status.brainCount = brains.Length;

            foreach (var brain in brains)
            {
                var formula = brain.GetComponent<UtilityMasterFormula>();
                var resolver = brain.GetComponent<PersonalityTagResolver>();
                var trust = brain.GetComponent<TrustMatrix>();
                var trauma = brain.GetComponent<TraumaSystem>();

                if (formula != null && formula.debugLog) status.formulaOn++;
                if (resolver != null && resolver.debugLog) status.resolverOn++;
                if (trust != null && trust.debugLog) status.trustOn++;
                if (trauma != null && trauma.debugLog) status.traumaOn++;
            }

            return status;
        }

        private struct DebugLogStatus
        {
            public int brainCount;
            public int formulaOn, resolverOn, trustOn, traumaOn;

            public bool AllOn => brainCount > 0 &&
                formulaOn == brainCount && resolverOn == brainCount &&
                trustOn == brainCount && traumaOn == brainCount;

            public bool AllOff => brainCount > 0 &&
                formulaOn == 0 && resolverOn == 0 && trustOn == 0 && traumaOn == 0;
        }
    }
}
