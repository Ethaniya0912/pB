using UnityEngine;

namespace TDA.PB4.Helpers
{
    // ════════════════════════════════════════════════════════════════════
    // DontDestroyOnLoadHelper — 자식 GameObject → root + DontDestroyOnLoad
    // ════════════════════════════════════════════════════════════════════
    //
    // 【사용 시점】
    //   매니저가 컨테이너 GameObject (Cyber Layer / Bridge System 등) 의
    //   자식으로 배치되어 있어 `DontDestroyOnLoad` 가 실패하는 경우.
    //
    // 【Unity 동작 규칙】
    //   `DontDestroyOnLoad` 는 root GameObject 에만 작동.
    //   자식이면 호출해도 무시됨 → 씬 전환 시 부모와 함께 파괴.
    //
    // 【사용 방법】
    //   1. Hierarchy 에서 매니저 GameObject 선택
    //      (예: WorldGameStateManager, WorldAIContextManager 등)
    //   2. Inspector > Add Component > DontDestroyOnLoadHelper 추가
    //   3. 끝 — 매니저 코드 변경 X
    //
    // 【동작】
    //   Awake 시점에:
    //   - root 가 아니면 → 자동 root 이동 (transform.SetParent(null))
    //   - DontDestroyOnLoad 호출
    //   - 이미 root 이거나 DontDestroyOnLoad 호출된 매니저도 안전 (idempotent)
    //
    // 【Script Execution Order】
    //   본 Helper 의 Awake 는 일반 매니저보다 먼저 실행되어야 안전.
    //   현재는 ExecutionOrder = -800 (Bridge -500 보다 먼저, World -1000 보다 뒤).
    //
    // 【주의】
    //   - NGO NetworkManager 상속 매니저: NGO 가 자체 처리 — 본 Helper 적용 시 충돌 가능 (검증 권장)
    //   - 씬 스코프 매니저 (씬마다 reset 필요한 매니저): 적용 X
    // ════════════════════════════════════════════════════════════════════
    
    [DefaultExecutionOrder(-800)]
    public class DontDestroyOnLoadHelper : MonoBehaviour
    {
        [SerializeField, Tooltip("로그 출력")]
        private bool _verboseLogging = true;
        
        private void Awake()
        {
            // ── 1. root 강제 ──
            if (transform.parent != null)
            {
                if (_verboseLogging)
                    Debug.Log(
                        $"[DDOLHelper] {gameObject.name}: root 가 아님 (부모: {transform.parent.name}). " +
                        $"root 로 자동 이동.", this);
                
                transform.SetParent(null);
            }
            
            // ── 2. DontDestroyOnLoad ──
            DontDestroyOnLoad(gameObject);
            
            if (_verboseLogging)
                Debug.Log($"[DDOLHelper] {gameObject.name}: DontDestroyOnLoad 적용.", this);
        }
    }
}
