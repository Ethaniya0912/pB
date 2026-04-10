// =============================================================================
// BB_DiagnosticTest.cs  |  진단 스크립트 v3
// Run Diagnostic 체크 → UtilityWinner에 "Flee" 기록 + 매 프레임 유지
//
// [버그 수정 - Bug 3]
//   기존: bbVar.GetHashCode() == graphVar.GetHashCode() 로 동일성 비교
//   문제: GetHashCode()는 값 기반으로 오버라이드될 수 있어
//         서로 다른 객체가 동일한 해시를 반환할 수 있다.
//         즉 "Agent BB == Graph BB ? True"가 참조가 다른데도 True가 될 수 있어
//         진단 결과가 거짓 양성(false positive)을 낼 수 있다.
//   수정: object.ReferenceEquals(bbVar, graphVar) 로 실제 참조 동일성 비교.
// =============================================================================
using UnityEngine;
using Unity.Behavior;

public class BB_DiagnosticTest : MonoBehaviour
{
    [Header("━━━ 1회 진단 실행 ━━━")]
    [Tooltip("체크하면 1회 진단 로그 출력")]
    public bool runDiagnostic = false;

    [Header("━━━ 매 프레임 BB 강제 기록 ━━━")]
    [Tooltip("체크하면 매 프레임 UtilityWinner='Flee' 강제 기록")]
    public bool forceFleeEveryFrame = false;

    [Header("━━━ 기록할 값 ━━━")]
    [Tooltip("BB에 기록할 UtilityWinner 값. 기본='Flee'")]
    public string writeValue = "Flee";

    private BehaviorGraphAgent _agent;

    private void Start()
    {
        _agent = GetComponent<BehaviorGraphAgent>();
    }

    private void Update()
    {
        if (_agent == null)
        {
            _agent = GetComponent<BehaviorGraphAgent>();
            if (_agent == null) return;
        }

        // ── 매 프레임 강제 기록 모드 ──
        if (forceFleeEveryFrame)
        {
            _agent.SetVariableValue("UtilityWinner", writeValue);
        }

        // ── 1회 진단 ──
        if (!runDiagnostic) return;
        runDiagnostic = false;

        Debug.Log($"[BB진단] ===== {name} 진단 시작 =====");

        // BB에 "Flee" 기록 (4가지 방법 전부)
        _agent.SetVariableValue("UtilityWinner", writeValue);
        Debug.Log($"[BB진단] agent.SetVariableValue('UtilityWinner', '{writeValue}') = done");

        BlackboardVariable<string> bbVar = null;
        var bbRef = _agent.BlackboardReference;
        if (bbRef != null && bbRef.GetVariable("UtilityWinner", out bbVar))
        {
            Debug.Log($"[BB진단] bbRef var current='{bbVar.Value}' hash={bbVar.GetHashCode()}");
            bbVar.Value = writeValue;
            Debug.Log($"[BB진단] bbRef var.Value = '{writeValue}' → 확인: '{bbVar.Value}'");
        }

        var graph = _agent.Graph;
        if (graph?.BlackboardReference != null)
        {
            if (graph.BlackboardReference.GetVariable("UtilityWinner", out BlackboardVariable<string> graphVar))
            {
                Debug.Log($"[BB진단] Graph var current='{graphVar.Value}' hash={graphVar.GetHashCode()}");
                graphVar.Value = writeValue;
                Debug.Log($"[BB진단] Graph var.Value = '{writeValue}' → 확인: '{graphVar.Value}'");

                // [Bug 3 수정] GetHashCode() 비교 → ReferenceEquals() 로 교체
                // GetHashCode()는 값 기반으로 오버라이드될 수 있으므로
                // 서로 다른 객체가 같은 해시를 반환해 거짓 양성(True)이 나올 수 있다.
                // object.ReferenceEquals()는 메모리 주소를 직접 비교하므로
                // 진정한 참조 동일성(같은 인스턴스인지)을 보장한다.
                bool same = object.ReferenceEquals(bbVar, graphVar);
                Debug.Log($"[BB진단] Agent BB == Graph BB ? {same}  " +
                          $"(bbVar hash={bbVar?.GetHashCode()} / graphVar hash={graphVar.GetHashCode()})");

                if (!same)
                {
                    Debug.LogWarning(
                        $"[BB진단] ★ 경고: Agent BB와 Graph BB가 서로 다른 인스턴스입니다!\n" +
                        $"  bbVar    hash={bbVar?.GetHashCode()}\n" +
                        $"  graphVar hash={graphVar.GetHashCode()}\n" +
                        $"  PB4DecisionAdapter.SetBB<T>()가 BT 노드와 다른 BB에 기록하고 있을 수 있습니다.");
                }
            }
        }

        Debug.Log($"[BB진단] ===== 진단 완료. forceFleeEveryFrame={forceFleeEveryFrame} =====");
        Debug.Log($"[BB진단] → forceFleeEveryFrame을 체크하면 매 프레임 '{writeValue}'가 유지됩니다.");
    }
}