// =============================================================================
// BubbleFollower.cs  |  pB-4 Week 2 Day 4
// Layer  : L4 Event/Coordination (감각적 연출)
// Owner  : Person A
//
// 역할:
//   DialogueRenderer 의 프리팹 말풍선 모드에서 런타임 부착되는 추적 헬퍼.
//   Bubble 을 NPC 자식으로 두지 않고 매 프레임 위치만 추적 + 카메라 빌보드.
//
// 분리 이유 (파일명 = 클래스명):
//   MonoBehaviour 는 .cs 파일당 MonoScript(m_Script) 가 파일명과 동명의 클래스
//   하나에만 바인딩된다. 과거 이 클래스는 DialogueRenderer.cs 안에 함께 있었고,
//   파일명과 같은 DialogueRenderer 가 바인딩을 가져가 BubbleFollower 는 고아였다.
//   AddComponent 직후엔 동작하지만, 말풍선 생존 중 도메인 리로드가 끼면 직렬화
//   복원에 실패해 missing-script 로 파괴된다(2026-06-12 NetSimController 회귀와
//   동일 메커니즘). 그래서 단독 파일로 분리해 m_Script 바인딩을 확보한다.
// =============================================================================
using UnityEngine;

namespace TDA.PB4.AI.Speech
{
    /// <summary>
    /// [FIX] Armature scale 영향 회피용 추적 헬퍼.
    /// Bubble을 NPC 자식으로 두지 않고, 매 프레임 위치만 추적.
    /// 카메라 빌보드 처리 포함.
    ///
    /// Pause 대응:
    ///   [ExecuteAlways] — Edit 모드에서도 Update 실행
    ///   EditorApplication.update — Pause 상태에서도 호출되는 유일한 훅
    ///   → Pause 상태로 Scene 뷰에서 카메라 회전해도 Bubble이 따라옴
    /// </summary>
    [ExecuteAlways]
    public class BubbleFollower : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset;

#if UNITY_EDITOR
        void OnEnable()
        {
            // Pause 상태에서도 동작하려면 EditorApplication.update 구독
            // (Update/LateUpdate는 Pause 시 멈추지만 EditorApplication.update는 계속 호출됨)
            UnityEditor.EditorApplication.update += EditorTick;
        }

        void OnDisable()
        {
            UnityEditor.EditorApplication.update -= EditorTick;
        }

        /// <summary>Editor 전용 — Pause 상태에서도 호출되는 업데이트 훅.</summary>
        private void EditorTick()
        {
            // 게임이 Play 중이고 Pause 아닌 상태면 LateUpdate가 처리하므로 스킵
            if (Application.isPlaying && !UnityEditor.EditorApplication.isPaused)
                return;

            UpdateTransform();
        }
#endif

        void LateUpdate()
        {
            // 일반 Play 중 — 매 프레임 LateUpdate에서 처리
            UpdateTransform();
        }

        /// <summary>위치 추적 + 카메라 빌보드. LateUpdate와 EditorTick에서 공유 호출.</summary>
        private void UpdateTransform()
        {
            if (target == null)
            {
                if (Application.isPlaying)
                    Destroy(gameObject);
                return;
            }

            transform.position = target.position + offset;

            // 빌보드 — Scene 뷰와 Game 뷰 모두 지원
            Camera billboardCam = GetBillboardCamera();
            if (billboardCam != null)
                transform.rotation = billboardCam.transform.rotation;
        }

        /// <summary>
        /// 활성 카메라 반환. 우선순위:
        ///   1. Pause 상태 + Scene 뷰 보임 → Scene 카메라 (디버깅 전용)
        ///   2. Edit 모드 (Play 중 아님) → Scene 카메라
        ///   3. 그 외 (Play 중) → Camera.main (Game 뷰 우선)
        ///
        /// 핵심: Play 중에는 Pause가 아닌 한 항상 Camera.main 사용.
        /// Game 뷰 포커스 여부와 무관하게 게임 카메라 우선.
        ///
        /// 빌드 타겟에서는 Camera.main만 사용 (Editor 코드 자동 제외).
        /// </summary>
        private Camera GetBillboardCamera()
        {
#if UNITY_EDITOR
            // Pause 또는 Edit 모드일 때만 Scene 카메라 사용
            // (Play 중 정상 동작 시에는 항상 Camera.main 우선)
            bool isEditModeOrPaused = !Application.isPlaying
                                    || UnityEditor.EditorApplication.isPaused;

            if (isEditModeOrPaused)
            {
                var sceneView = UnityEditor.SceneView.lastActiveSceneView;
                if (sceneView != null && sceneView.camera != null)
                    return sceneView.camera;
            }
#endif
            return Camera.main;
        }
    }
}
