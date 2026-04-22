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
//
// [버그 수정 — Flee→Attack 트랜지션 불가 (Bug C)]
//   문제:
//     ProtectNavMeshInfra() 의 updatePosition = false 설정으로
//     Flee 단계 동안 NavMeshAgent 내부 위치(저장된 위치)와 실제 캐릭터 위치가 desync.
//     FleeDuelAction 이후 Attack 분기 진입 시 StalkAction.OnStart() 의
//     isOnNavMesh 체크가 false 를 반환 → StalkAction Failure → TryInOrder 가
//     Flee/Patrol 조건 모두 불충족 → Idle 낙하.
//     사용자는 "Flee → Attack 트랜지션이 안됨" 으로 인식.
//
//   수정:
//     FleeDuelAction.OnStart() 말미에서 isStopped = false 와 함께
//     navMeshAgent.nextPosition = transform.position 를 명시적으로 동기화.
//     이후 StalkAction.OnStart() 의 isOnNavMesh 체크가 올바르게 통과됩니다.
//
// [버그 수정 — Bug 2: 10m 밖에서 돌진]
//   문제:
//     Flee 진입 시 dist > EngageRange(10m)이어도 FleeDuelAction 이 즉시
//     brain.fear = InitialFear(0.25) → brain.UpdateDecision() → CurrentState = Attack
//     → ForceSyncBB → UtilityWinner = "Attack" 으로 전환.
//     그 결과 BT 가 즉시 Attack 분기 → StalkAction 이 10m+ 거리에서 타겟을 향해 돌진.
//     오크의 반격 의도는 맞지만 Engage Range 밖에서의 급격한 방향 전환이
//     "도망 안 가고 갑자기 돌진" 으로 보임.
//
//   수정:
//     EngageRange BB 변수 추가.
//     OnStart() 에서 dist > EngageRange 이면 즉시 fear 리셋/ForceSyncBB 하지 않고
//     Status.Running 을 반환하여 OnUpdate() 에서 직접 타겟을 향해 접근.
//     dist <= EngageRange 도달 시 fear 리셋 + ForceSyncBB + Success.
//     → Engage Range 내부에서 반격으로 전환되어 자연스러운 돌진-반격 연출.
// =============================================================================
using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;   // NavMeshAgent (Bug 2 수정 — OnUpdate 접근 이동)
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

    /// <summary>InitialFear 상한값. 이 값 초과 시 강제 보정.</summary>
    [SerializeReference] public BlackboardVariable<float> MaxInitialFear = new(0.28f);

    /// <summary>
    /// [Bug 2 수정] Stalk → CircleStrafe 전환 기준 거리 (m).
    /// dist > EngageRange 인 상태에서 Flee 가 트리거되면
    /// 즉시 fear 를 리셋하지 않고 OnUpdate() 에서 타겟을 향해 접근합니다.
    /// dist <= EngageRange 도달 후 fear 리셋 + Attack 전환.
    /// PB4DecisionAdapter 가 combatProfile.engageRange 를 BB 에 push 합니다.
    /// </summary>
    [Tooltip("Engage Range(m). 이 거리 이내 진입 후 반격 전환.\n" +
             "PB4DecisionAdapter → BB EngageRange → 이 변수로 연결하세요.")]
    [SerializeReference] public BlackboardVariable<float> EngageRange = new(10f);

    // [버그 수정] Fear, UtilityWinner BB 변수 필드 제거.
    // 이 Action에서 직접 BB에 역기록하지 않습니다.
    // brain.fear → PB4DecisionAdapter → BB Fear / UtilityWinner 단방향 흐름을 유지합니다.

    // =========================================================================
    // 내부 상태
    // =========================================================================

    /// <summary>[Bug 2 수정] 접근 중 NavMeshAgent 캐시.</summary>
    private NavMeshAgent _nav;

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
    ///
    /// [Bug 2 수정]
    /// dist > EngageRange 이면 즉시 반격 전환하지 않고 Status.Running 반환.
    /// OnUpdate() 에서 타겟을 향해 접근하다가 dist <= EngageRange 도달 시 반격.
    /// </summary>
    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("FleeDuelAction: Self가 null입니다.");
            return Status.Failure;
        }

        // ── [타이밍 버그 수정 ③ / Bug C] NavMeshAgent 복원 ─────────────────────
        var aiMgr = Self.Value.GetComponent<AICharacterManager>();
        if (aiMgr?.navMeshAgent != null)
        {
            aiMgr.navMeshAgent.isStopped = false;
            aiMgr.navMeshAgent.stoppingDistance = 0f;
            if (aiMgr.navMeshAgent.isOnNavMesh)
                aiMgr.navMeshAgent.nextPosition = aiMgr.transform.position;
        }

        // [Bug 2 수정] EngageRange 거리 무관 즉시 반격.
        // 기존 OnUpdate 접근 로직 제거 — StalkAction(Attack 분기)과 동일한 동작 중복이었음.
        // fear 리셋 → UpdateDecision → Attack → BT Attack 분기 → StalkAction 접근.
        var brain = Self.Value.GetComponent<BaseAIBrain>();

        Debug.Log(
            $"[FleeDuel][ONSTART] {Self.Value.name}: fear={brain?.fear:F3} → CounterAttack 즉시 실행");

        return CounterAttack(brain);
    }

    protected override Status OnUpdate()
    {
        // OnStart에서 항상 Success 반환 → OnUpdate 미호출.
        // (EngageRange 접근 로직은 Bug 2 수정으로 제거됨. StalkAction이 담당.)
        return Status.Success;
    }

    /// <summary>
    /// 반격 시퀀스: fear 리셋 → UpdateDecision() → ForceSyncBB() → Success.
    /// OnStart() 및 OnUpdate() 양쪽에서 호출됩니다.
    /// </summary>
    private Status CounterAttack(BaseAIBrain brain)
    {
        // [Bug 2 수정] InitialFear 상한선 강제 적용
        // BT 에디터에 저장된 InitialFear=0.40이면 fear=0.40 → u_flee=0.32 > u_attack=0.21
        // → Flee가 계속 이겨 FleeDuelAction이 무한 루프.
        // Attack 임계값: fear < 0.304 (Orc 기준). 0.20으로 제한.
        // ★ BT 에디터 FleeDuelAction 노드의 InitialFear 값도 0.20으로 변경하세요.
        float safeInitialFear = Mathf.Min(InitialFear.Value, MaxInitialFear.Value);

        if (InitialFear.Value > MaxInitialFear.Value)
            Debug.LogWarning(
                $"[FleeDuel][COUNTER] {Self.Value?.name}: InitialFear={InitialFear.Value:F2} 상한 초과.\n" +
                $"  → {safeInitialFear:F2}로 강제 보정. BT 에디터에서도 수정 필요.");

        Debug.Log(
            $"[FleeDuel][COUNTER] {Self.Value?.name}: 반격 전환 시작. " +
            $"fear={brain?.fear:F3} → {safeInitialFear:F2}");

        if (brain != null)
        {
            brain.fear = Mathf.Clamp01(safeInitialFear);

            // ── [타이밍 버그 수정 ①] brain.UpdateDecision() 즉시 호출 ──────────
            // 기존 문제: fear 를 낮춘 뒤 PB4DecisionAdapter 의 다음 틱(최대 0.5초)까지
            //            UpdateDecision() 이 호출되지 않아 brain.CurrentState = Flee 고정.
            //            externallyTicked = true 로 Brain 자체 틱도 꺼져 있어,
            //            BT 는 BB UtilityWinner = "Flee" 를 읽고 Flee 분기를 공전 반복.
            // 수정: 즉시 UpdateDecision() 호출 → CurrentState = Attack 전환.
            brain.UpdateDecision();

            var mobBrain = brain as TDA.PB4.AI.Mob.MobAIBrain;
            Debug.Log(
                $"[FleeDuel][COUNTER] {Self.Value?.name}: UpdateDecision 완료 → " +
                $"CurrentState={(mobBrain != null ? mobBrain.CurrentState.ToString() : "N/A")} " +
                $"fear={brain.fear:F3}");
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
            Debug.Log($"[FleeDuel][COUNTER] {Self.Value?.name}: ForceSyncBB 완료.");
        }
        else
        {
            Debug.LogError(
                $"<color=red>[FleeDuel][COUNTER] {Self.Value?.name}: PB4DecisionAdapter==null!\n" +
                $"  ForceSyncBB 불가 → BB UtilityWinner가 갱신 안 됨 → BT Flee 루프 지속 가능</color>");
        }

        // 즉시 Success → TryInOrder 재평가 → BB 갱신 완료 상태에서 Attack 분기 활성화
        return Status.Success;
    }
}