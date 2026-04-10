// =============================================================================
// FleePhalanxAction.cs  |  pB-4 커스텀 BT Action — 스켈레톤 도주 불가
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   스켈레톤(PhalanxGroupPolicy)의 '도주' 행동.
//   언데드는 공포 면역 — fear를 강제 0으로 리셋합니다.
//   도주가 절대 불가능하며, 파괴 시 인접 개체가 슬롯을 메웁니다.
//
// Switch 노드에서:
//   FactionPolicyType='Phalanx' case에 연결됩니다.
//
// 참고:
//   실제로는 스켈레톤의 fleeThreshold=1.0이므로
//   Flee Guard 자체가 True가 되기 어렵지만,
//   버그나 극단적 상황에서 진입 시 안전장치로 동작합니다.
//
// [버그 수정 — BB 역기록 루프]
//   기존 문제:
//     Fear.Value = 0f, UtilityWinner.Value = "Attack" 을 BB에 직접 기록.
//
//   왜 문제인가:
//     BB 'Fear' 변수와 brain.fear 는 완전히 별개의 값입니다.
//     PB4DecisionAdapter는 매 틱마다 brain.fear 를 읽어 BB Fear 에 단방향 복사합니다.
//     BT 노드에서 BB Fear 를 역방향으로 써도 brain.fear 는 변하지 않으므로
//     다음 Adapter 틱에서 brain.fear 값으로 즉시 덮어써집니다.
//     마찬가지로 BB UtilityWinner = "Attack" 역기록도 Adapter가 brain.CurrentState
//     (여전히 Flee) 를 읽어 "Flee" 로 되돌립니다.
//     결과: 0.5초마다 BB 값이 "Attack" ↔ "Flee" 로 진동합니다.
//
//   수정:
//     BB 직접 기록 제거.
//     brain.fear = 0f 직접 수정.
//     → u_flee = 0 → brain.CurrentState = Attack/Patrol
//     → PB4DecisionAdapter 가 BB를 올바르게 전파.
//
// 추가 권장 설정:
//   스켈레톤의 MobAIBrain Inspector 에서 fleeThreshold = 1.0 으로 설정하면
//   fear 가 최대(1.0) 여도 u_flee = 1.0 * 1.0 = 1.0 이고
//   u_attack = (1.0 - fear) * ... = 0 이 되어 다른 방식으로 Flee 분기 억제가 필요합니다.
//   따라서 fleeThreshold 를 매우 크게 (예: 99f) 설정하거나
//   MobFactionDataSO.aggression 을 높여 u_attack 을 u_flee 보다 크게 유지하는 것을 권장합니다.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using TDA.PB4.AI;       // BaseAIBrain
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "FleePhalanx",
    story: "[Self] cannot flee — fear reset to zero",
    category: "pB-4/Flee",
    id: "pb4_flee_phalanx_action")]
public partial class FleePhalanxAction : Action
{
    // =========================================================================
    // Blackboard 변수
    // =========================================================================

    [SerializeReference] public BlackboardVariable<GameObject> Self;

    // [버그 수정] Fear, UtilityWinner BB 변수 필드 제거.
    // brain.fear 를 직접 수정하므로 BB 역기록 필드가 불필요합니다.
    // brain.fear → PB4DecisionAdapter → BB 단방향 흐름을 유지합니다.

    // =========================================================================
    // 생명주기
    // =========================================================================

    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("FleePhalanxAction: Self가 null입니다.");
            return Status.Failure;
        }

        // ── [버그 수정] brain.fear 직접 0으로 강제 (BB 역기록 제거) ──
        // BB Fear / UtilityWinner 변수를 직접 쓰지 않습니다.
        //
        // 수정 전 코드 (문제):
        //   Fear.Value = 0f;                          // BB Fear 역기록 → 0.5초 후 덮어써짐
        //   UtilityWinner.Value = "Attack";           // BB UtilityWinner 역기록 → 0.5초 후 덮어써짐
        //
        // 수정 후 코드:
        //   brain.fear = 0f 직접 설정
        //   → MobAIBrain.UpdateDecision() 에서 u_flee = 0 → CurrentState = Attack/Patrol
        //   → PB4DecisionAdapter 가 BB Fear=0 / UtilityWinner="Attack" 전파
        var brain = Self.Value.GetComponent<BaseAIBrain>();
        if (brain != null)
        {
            // 언데드: 공포 면역 — brain.fear 강제 0
            brain.fear = 0f;
            Debug.Log($"[BT] {Self.Value.name}: 스켈레톤 도주 불가. brain.fear=0 강제. 포위 대형 유지.");
        }
        else
        {
            Debug.LogWarning($"[FleePhalanxAction] {Self.Value.name}: BaseAIBrain을 찾을 수 없어 " +
                             "fear 리셋 불가. GetComponent<BaseAIBrain>() 반환값 null.");
        }

        return Status.Success;
    }

    protected override Status OnUpdate()
    {
        // OnStart에서 즉시 Success를 반환하므로 OnUpdate는 호출되지 않음
        return Status.Success;
    }
}