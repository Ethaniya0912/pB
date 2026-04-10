// =============================================================================
// FleeDuelAction.cs  |  pB-4 커스텀 BT Action — 오크 반격 전환
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   오크(DuelGroupPolicy)의 '도주' 행동.
//   실제로는 도주하지 않고 fear를 리셋하여 Attack으로 복귀합니다.
//   오크의 '명예를 중시하는' 성격을 표현합니다.
//
// Switch 노드에서:
//   FactionPolicyType='Duel' case에 연결됩니다.
//
// 동작:
//   OnStart에서 즉시 brain.fear를 초기값으로 낮춤 → 다음 Brain 틱에서
//   u_attack > u_flee → Brain.CurrentState = Attack → PB4Adapter가 BB 갱신.
//
// [버그 수정 — BB 역기록 루프]
//   기존 문제:
//     Fear.Value = InitialFear, UtilityWinner.Value = "Attack" 을 BB에 직접 기록.
//
//   왜 문제인가:
//     BB 'Fear' 변수와 brain.fear 는 완전히 별개의 값입니다.
//     PB4DecisionAdapter는 매 틱마다 brain.fear 를 읽어 BB Fear 에 단방향 복사합니다.
//     BT 노드에서 BB Fear 를 역방향으로 써도 brain.fear 는 변하지 않으므로
//     다음 Adapter 틱(최대 0.5초 후)에서 brain.fear 값으로 즉시 덮어써집니다.
//     마찬가지로 BB UtilityWinner = "Attack" 역기록도 Adapter가 brain.CurrentState
//     (여전히 Flee) 를 읽어 "Flee" 로 되돌립니다.
//     결과: 0.5초마다 "Attack" ↔ "Flee" 가 진동하며 BT 분기가 혼란에 빠집니다.
//
//   수정:
//     BB 직접 기록 제거.
//     brain.fear 를 직접 InitialFear 값으로 낮춤.
//     → 다음 MobAIBrain.UpdateDecision() 에서 u_flee = fear * fleeThreshold 감소
//     → u_attack > u_flee → brain.CurrentState = Attack
//     → PB4DecisionAdapter 가 BB UtilityWinner = "Attack" / BB Fear = 낮은값 전파.
//     BB 역기록 없이 자연스러운 단방향 흐름(Brain → Adapter → BB) 유지.
//
// [버그 수정 — 구조적 타이밍 버그 (Freeze)]
//   문제:
//     brain.fear 를 낮춰도 PB4DecisionAdapter 의 다음 틱(최대 0.5초)까지
//     UpdateDecision() 이 호출되지 않아 brain.CurrentState 가 Flee 로 고정됨.
//     externallyTicked = true 상태에서 Brain 자체 틱도 꺼져 있어,
//     BT 는 BB UtilityWinner = "Flee" 를 읽고 Flee 분기를 반복하는 공전 루프에 빠짐.
//     FleeDuelAction 이 NavMesh / Animator 명령도 내리지 않아 Orc 가 그 자리에 정지.
//
//   수정:
//     ① brain.fear 설정 직후 brain.UpdateDecision() 즉시 호출
//        → 0.5초 Adapter 틱 대기 없이 CurrentState = Attack 전환.
//     ② PB4DecisionAdapter.ForceSyncBB() 호출
//        → BB UtilityWinner / Fear 즉시 동기화.
//        → ForceSyncBB() 는 PB4DecisionAdapter.cs 에 추가된 공개 메서드.
//     ③ NavMeshAgent.isStopped = false 명시적 복원
//        → 이전 Action(StalkAction 등)이 남긴 정지 상태 해제.
//     ④ InitialFear 기본값 0.4f → 0.25f 조정
//        → Attack 우세 조건: (1-F) * aggTh * (factionAgg/10) > F * fleeTh
//        → Orc(aggressionThreshold=0.5, fleeThreshold=0.8, factionAgg≈7) 기준
//           안전 상한값 ≈ 0.304. 0.4f 는 경계 초과 위험 있음.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using TDA.PB4.AI;       // BaseAIBrain
using TDA.PB4.Bridge;   // PB4DecisionAdapter (ForceSyncBB — 타이밍 버그 수정 ②)
using TDA.Character.AI; // AICharacterManager  (NavMesh 복원  — 타이밍 버그 수정 ③)
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "FleeDuel",
    story: "[Self] refuses to flee and counterattacks",
    category: "pB-4/Flee",
    id: "pb4_flee_duel_action")]
public partial class FleeDuelAction : Action
{
    // =========================================================================
    // Blackboard 변수
    // =========================================================================

    [SerializeReference] public BlackboardVariable<GameObject> Self;

    /// <summary>
    /// 반격 전환 시 brain.fear 를 이 값으로 낮춥니다.
    /// 낮은 fear → u_attack 상승 → Brain 이 자연스럽게 Attack 으로 전환.
    /// 완전히 0 이 아닌 초기값으로 복원하여 오크의 용맹함을 표현합니다.
    ///
    /// [타이밍 버그 수정 ④] 기본값 0.4f → 0.25f 조정.
    /// Attack 우세 조건: (1 - InitialFear) × aggressionThreshold × (factionAgg/10)
    ///                   > InitialFear × fleeThreshold
    /// Orc 기준(aggressionThreshold=0.5, fleeThreshold=0.8, factionAgg≈7) 안전 상한값 ≈ 0.304.
    /// 0.4f 는 factionAgg 가 낮은 Orc 변종에서 Attack 전환 불가 위험 있음.
    /// </summary>
    [Tooltip("반격 전환 시 brain.fear를 이 값으로 리셋.\n" +
             "Attack 우세 조건 상한값 ≈ 0.304 (Orc 기준).\n" +
             "기본 0.25 (여유 마진 확보)")]
    [SerializeReference] public BlackboardVariable<float> InitialFear = new(0.25f);

    // [버그 수정] Fear, UtilityWinner BB 변수 필드 제거.
    // 이 Action에서 직접 BB에 역기록하지 않습니다.
    // brain.fear → PB4DecisionAdapter → BB Fear / UtilityWinner 단방향 흐름을 유지합니다.

    // =========================================================================
    // 생명주기
    // =========================================================================

    /// <summary>
    /// 즉시 실행: brain.fear 리셋 → UpdateDecision() / ForceSyncBB() 즉시 호출 → Success.
    /// 오크는 절대 도주하지 않습니다. 대신 분노로 전환합니다.
    ///
    /// [버그 수정] 기존 UtilityWinner.Value = "Attack" / Fear.Value = InitialFear 제거.
    /// BB에 직접 역기록하면 PB4DecisionAdapter 와 0.5초마다 충돌합니다.
    /// brain.fear 를 낮추면 MobAIBrain.UpdateDecision() 에서
    /// u_flee = fear * fleeThreshold 가 줄어들어 자연스럽게 Attack 으로 전환됩니다.
    ///
    /// [타이밍 버그 수정]
    /// ① brain.UpdateDecision() 즉시 호출 — 0.5s Adapter 틱 공전 루프 차단.
    /// ② adapter.ForceSyncBB()          — BB UtilityWinner 즉시 "Attack" 반영.
    /// ③ navMeshAgent.isStopped = false  — 이전 Action 잔류 정지 상태 해제.
    /// </summary>
    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("FleeDuelAction: Self가 null입니다.");
            return Status.Failure;
        }

        // ── [버그 수정] brain.fear 직접 수정 (BB 역기록 제거) ──
        // BB Fear / UtilityWinner 변수를 직접 쓰지 않습니다.
        //
        // 수정 전 코드 (문제):
        //   Fear.Value = InitialFear.Value;          // BB Fear 역기록 → 0.5초 후 덮어써짐
        //   UtilityWinner.Value = "Attack";           // BB UtilityWinner 역기록 → 0.5초 후 덮어써짐
        //
        // 수정 후 코드:
        //   brain.fear = InitialFear.Value 직접 설정
        //   → 다음 Brain.UpdateDecision() 에서 u_flee 감소 → CurrentState = Attack
        //   → PB4DecisionAdapter 가 BB Fear / UtilityWinner 를 올바른 값으로 전파
        var brain = Self.Value.GetComponent<BaseAIBrain>();
        if (brain != null)
        {
            brain.fear = Mathf.Clamp01(InitialFear.Value);

            // ── [타이밍 버그 수정 ①] brain.UpdateDecision() 즉시 호출 ──────────
            // 기존 문제: fear 를 낮춘 뒤 PB4DecisionAdapter 의 다음 틱(최대 0.5초)까지
            //            UpdateDecision() 이 호출되지 않아 brain.CurrentState = Flee 고정.
            //            externallyTicked = true 로 Brain 자체 틱도 꺼져 있어,
            //            BT 는 BB UtilityWinner = "Flee" 를 읽고 Flee 분기를 공전 반복.
            // 수정: 즉시 UpdateDecision() 호출 → CurrentState = Attack 전환.
            brain.UpdateDecision();

            Debug.Log($"[BT] {Self.Value.name}: 오크 반격 전환! " +
                      $"brain.fear={InitialFear.Value:F2} → UpdateDecision() 즉시 호출 → Attack 복귀.");
        }
        else
        {
            // Fallback: brain 참조 없으면 경고만 출력하고 Success 반환
            // (BT 구조상 Attack 분기 재평가로 자연 복귀)
            Debug.LogWarning($"[FleeDuelAction] {Self.Value.name}: BaseAIBrain을 찾을 수 없어 " +
                             "fear 리셋 불가. GetComponent<BaseAIBrain>() 반환값 null.");
        }

        // ── [타이밍 버그 수정 ②] PB4DecisionAdapter.ForceSyncBB() 즉시 호출 ──
        // brain.CurrentState = Attack 으로 전환됐어도 BB UtilityWinner 가 갱신되지 않으면
        // BT 는 여전히 "Flee" 분기를 탐. ForceSyncBB() 로 0.5s 타이머 없이 즉시 BB 동기화.
        var adapter = Self.Value.GetComponent<PB4DecisionAdapter>();
        if (adapter != null)
        {
            adapter.ForceSyncBB();
        }
        else
        {
            Debug.LogWarning($"[FleeDuelAction] {Self.Value.name}: PB4DecisionAdapter 를 찾을 수 없어 " +
                             "BB 즉시 동기화 불가. 다음 Adapter 틱까지 최대 0.5초 대기합니다.");
        }

        // ── [타이밍 버그 수정 ③] NavMeshAgent 정지 상태 명시적 복원 ────────────
        // 이전 Action(StalkAction, CircleStrafeAction 등)이 isStopped 를 남겨두었을 수 있음.
        // ProtectNavMeshInfra() 에서 updatePosition = false 가 적용 중이므로,
        // isStopped 만 false 로 초기화하여 다음 Attack 분기의 이동 재개를 보장.
        var aiMgr = Self.Value.GetComponent<AICharacterManager>();
        if (aiMgr?.navMeshAgent != null)
        {
            aiMgr.navMeshAgent.isStopped = false;
        }

        // 즉시 Success → TryInOrder 재평가 → BB 갱신 완료 상태에서 Attack 분기 활성화
        return Status.Success;
    }

    protected override Status OnUpdate()
    {
        // OnStart에서 즉시 Success를 반환하므로 OnUpdate는 호출되지 않음
        return Status.Success;
    }
}