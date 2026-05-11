// =============================================================================
// AIPerceptionSystem.cs  |  pB×pC 통합 — Week 1 (pC 인지)
// Layer  : L3 Domain (AI)
// Namespace: TDA.PB4.AI.Perception
//
// 역할:
//   AI의 시각/청각/자취 인지를 통합 관리한다.
//   AwarenessState 4단계: Unaware → Suspicious → Alert → Combat
//
//   3분기 인지:
//     Case A(강제 조우): ScriptedEncounterTrigger가 직접 Combat 설정
//     Case B(자취 추적): FootprintTrail 발견 → Suspicious → 추적 → 시야 확인 → Combat
//     Case C(영속 추적): PersistentHuntDirector가 isPersistentHunt=true 설정 → 포기 불가
//
//   FactionDetectionSFXManager와 연동:
//     Suspicious 진입 시 OnFactionDetectedPlayer 이벤트 발행
//     → FactionDetectionSFXManager가 Pattern 1/2 SFX 재생
//
// =============================================================================
// BB 소유권 테이블 (예방 체크리스트 #2)
// 이 컴포넌트는 BT BB를 직접 쓰지 않는다.
// 인지 결과는 아래 두 경로로 전달된다:
//
//   lastKnownPosition (내부 필드)
//     Writer : OnSoundHeard(), TickVision(), TickTraceDetection()
//     Reader : PB4DecisionAdapter → BB["LastHeardPosition"] push (수색 모드)
//              GetSpiralSearchPosition() → Case C 나선형 수색
//   ※ BB["LastHeardPosition"]와 이 필드는 별개!
//      BB 갱신은 PB4DecisionAdapter가 담당.
//
//   currentPerceptionTarget (내부 필드)
//     Writer : TickVision() — 시야 확인 성공 시 설정
//     Reader : SetAwareness(Combat) 이후 brain.currentTarget에 복사
//
//   OnAwarenessChanged 이벤트 (공개)
//     Invoker: SetAwareness() 내부
//     Subscriber: 외부 시스템 (SFX, UI 등) 자유 구독
//
// externallyTicked 주의사항 (예방 체크리스트 #3):
//   이 컴포넌트는 MonoBehaviour Update()로 자체 틱.
//   BT의 externallyTicked 설정과 무관하게 매 프레임 실행됨.
//   따라서 BB 변경 → BT Observer 발동 타이밍 버그에 직접 관여하지 않음.
//   단, OnAwarenessChanged 이벤트 발행은 매 프레임 가능.
// =============================================================================
// [개선 이력]
//   - OnEnable/OnDisable: SoundEventEmitter.OnSoundEmitted 구독 추가 (핵심 버그 수정)
//     기존: OnSoundHeard()가 존재했지만 이벤트 구독이 없어 절대 호출되지 않았음
//     수정: HandleSoundEvent() 어댑터 메서드로 3인자 이벤트 → 2인자 OnSoundHeard() 연결
//
//   - OnSoundHeard(): 청각 로직 개선
//     기존: sourceRadius 없이 hearingRange만 비교 (Combat 상태에서도 청각 무시)
//     수정: SoundPerception과 동일하게 sourceRadius 적용
//           Combat 상태에서도 LastKnownPosition 갱신 (타겟 위치 보정용)
//           지각 임계값(perceptionThreshold) Inspector 노출
//
//   - TickVision(): 시야 로직 개선
//     기존: Combat 전환 조건이 Alert 상태 자체로만 판단 (즉시 Combat 전환 가능성)
//     수정: alertDurationForCombat 시간 이상 Alert 유지 후 Combat 전환
//           Combat 중에도 TickVision() 유지 (타겟 위치 갱신 필요)
//
//   - SetAwareness(): 이벤트 발행 개선
//     기존: 상태 전환 로그만 출력
//     수정: OnAwarenessChanged 공개 이벤트 발행 → 외부 시스템 연동 용이
//
//   - 중복 using 제거 (TDA.PB4.Core 두 번 선언)
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.AI.Mob;        // MobAIBrain → factionData
using TDA.PB4.AI.Perception;  // SoundEventEmitter
using TDA.PB4.Core;           // EventBus
using TDA.PB4.AI;

namespace TDA.PB4.AI.Perception
{
    /// <summary>AI 각성 상태 4단계.</summary>
    public enum AwarenessState
    {
        /// <summary>인지 없음. 평화 상태.</summary>
        Unaware = 0,
        /// <summary>의심. 자취/소리 감지. 조사 행동.</summary>
        Suspicious = 1,
        /// <summary>경계. 시야 확인. 전투 준비.</summary>
        Alert = 2,
        /// <summary>전투. 타겟 확정. 공격/추적.</summary>
        Combat = 3
    }

    public class AIPerceptionSystem : MonoBehaviour
    {
        // ==================================================================
        // 시각 인지 설정
        // ==================================================================
        [Header("━━━ 시각 인지 (Vision) ━━━━━━━━━━━━━━━")]
        [Tooltip("시각 인지 범위 (미터). 이 범위 내에서 시야각+벽차단 검사. " +
                 "20 = 20m. 기존 PatrolState.detectionRadius(15m)보다 넓게 설정 가능.")]
        [Range(5f, 50f)]
        public float visionRange = 20f;

        [Tooltip("시야각 (도). 전방 기준 좌우 절반씩. " +
                 "120 = 전방 120도 원뿔. 180 = 반구. 60 = 좁은 터널 시야.")]
        [Range(30f, 180f)]
        public float visionAngle = 120f;

        [Tooltip("시야 차단 레이어. 벽/지형만 포함. " +
                 "기존 PatrolState의 enviroLayers와 동일하게 설정.")]
        public LayerMask visionBlockLayer;

        [Tooltip("시각으로 감지할 대상 레이어. 플레이어 레이어 설정.")]
        public LayerMask targetLayer;

        [Tooltip("Alert 상태를 이 시간(초) 이상 유지한 후 Combat으로 전환. " +
                 "0 = Alert 진입 즉시 Combat. 1 = 1초 유지 후 Combat.\n" +
                 "[개선] 기존: Alert 상태 자체로 즉시 Combat 가능 → 이제 유지 시간 필요.")]
        [Range(0f, 5f)]
        public float alertDurationForCombat = 0.5f;

        // ==================================================================
        // 청각 인지 설정
        // ==================================================================
        [Header("━━━ 청각 인지 (Hearing) ━━━━━━━━━━━━━━")]
        [Tooltip("청각 인지 범위 (미터). SoundEvent의 sourceRadius와 이 값 중 작은 쪽이 실제 감지 거리. " +
                 "30 = 30m.")]
        [Range(5f, 60f)]
        public float hearingRange = 30f;

        [Tooltip("청각 민감도 (0~1). 소리 크기(volume)와 곱해져 감지 가능성 결정. " +
                 "1.0 = 모든 소리 감지. 0.3 = 큰 소리만 감지.")]
        [Range(0f, 1f)]
        public float hearingSensitivity = 0.5f;

        [Tooltip("청각 감지 최소 지각값 (0~1). 이 값 이하면 소리 무시.\n" +
                 "[개선] 기존 하드코딩 0.2f → Inspector 노출. " +
                 "높을수록 둔감, 낮을수록 예민.")]
        [Range(0.01f, 0.5f)]
        public float perceptionThreshold = 0.2f;

        // ==================================================================
        // 자취 인지 설정
        // ==================================================================
        [Header("━━━ 자취 인지 (Trace) ━━━━━━━━━━━━━━━━")]
        [Tooltip("자취(FootprintTrail) 발견 범위 (미터). " +
                 "10 = 발자국 마커가 10m 이내에 있으면 발견.")]
        [Range(2f, 30f)]
        public float traceDetectionRange = 10f;

        // ==================================================================
        // 인지 상태
        // ==================================================================
        [Header("━━━ 현재 인지 상태 (Read Only) ━━━━━━━━")]
        [Tooltip("현재 각성 상태. Unaware(0)→Suspicious(1)→Alert(2)→Combat(3).")]
        [SerializeField] private AwarenessState awarenessLevel = AwarenessState.Unaware;

        [Tooltip("마지막으로 인지한 타겟 위치. 시야에서 사라지면 이 위치로 이동.")]
        [SerializeField] private Vector3 lastKnownPosition;

        [Tooltip("현재 추적 중인 타겟. Combat 상태에서만 유효.")]
        [SerializeField] private Transform currentPerceptionTarget;

        // ==================================================================
        // Case C: 영속 추적
        // ==================================================================
        [Header("━━━ 영속 추적 (Case C) ━━━━━━━━━━━━━━━")]
        [Tooltip("true이면 Suspicious→Unaware 감쇄가 비활성화됨. " +
                 "PersistentHuntDirector가 설정. 절대 포기하지 않음.")]
        public bool isPersistentHunt = false;

        [Tooltip("Case C: 자취를 잃은 후 나선형 수색 반경. " +
                 "20 = lastKnownPosition 주변 20m 나선형 수색.")]
        [Range(5f, 50f)]
        public float spiralSearchRadius = 20f;

        // ==================================================================
        // 감쇄 설정
        // ==================================================================
        [Header("━━━ 감쇄 설정 ━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Suspicious 상태 유지 시간. 이 시간 내에 추가 자극이 없으면 Unaware로 복귀. " +
                 "isPersistentHunt=true이면 이 타이머는 작동하지 않음.")]
        [Range(3f, 30f)]
        public float suspicionDecayTime = 8f;

        [Tooltip("Alert→Suspicious 감쇄 시간. 타겟을 시야에서 놓친 후 이 시간 경과 시 Alert→Suspicious.")]
        [Range(3f, 20f)]
        public float alertDecayTime = 5f;

        // ==================================================================
        // 디버그
        // ==================================================================
        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━━━━")]
        [Tooltip("Console에 인지 상태 변화를 출력.")]
        public bool debugLog = true;

        [Tooltip("Scene View에 시야 원뿔/청각 범위/자취 범위를 Gizmo로 표시.")]
        public bool showGizmos = true;

        // ==================================================================
        // 내부 상태
        // ==================================================================
        private float suspicionTimer;
        private float alertTimer;
        private float alertElapsed;   // [개선] Alert 지속 시간 (alertDurationForCombat 비교용)
        private float spiralAngle;
        private BaseAIBrain brain;

        // ==================================================================
        // 공개 이벤트
        // [개선] 외부 시스템(SFX, UI, Camera 등)이 각성 상태 전환을 수신할 수 있도록 추가.
        //        기존: 상태 전환을 알리는 수단이 없어 외부에서 폴링(매 프레임 읽기)만 가능했음.
        // ==================================================================
        /// <summary>각성 상태가 변경됐을 때 발행. (이전 상태, 새 상태, 이유)</summary>
        public event Action<AwarenessState, AwarenessState, string> OnAwarenessChanged;

        // ==================================================================
        // 공개 프로퍼티
        // ==================================================================
        public AwarenessState CurrentAwareness => awarenessLevel;
        public Vector3 LastKnownPosition => lastKnownPosition;
        public Transform CurrentTarget => currentPerceptionTarget;

        // ==================================================================
        // 생명주기
        // ==================================================================
        private void Awake()
        {
            brain = GetComponent<BaseAIBrain>();
        }

        private void OnEnable()
        {
            // [개선] SoundEventEmitter.OnSoundEmitted 구독
            // 기존: OnSoundHeard()가 존재했지만 이 이벤트 구독 코드가 없어서
            //       SoundEventEmitter.EmitSound() 호출 시 AIPerceptionSystem이 전혀 반응하지 않았음.
            // 수정: HandleSoundEvent() 어댑터로 3인자 이벤트 → 기존 OnSoundHeard(2인자) 연결.
            SoundEventEmitter.OnSoundEmitted += HandleSoundEvent;
        }

        private void OnDisable()
        {
            // 구독 해제 — 오브젝트 비활성화 또는 씬 전환 시 이벤트 누수 방지
            SoundEventEmitter.OnSoundEmitted -= HandleSoundEvent;
        }

        private void Update()
        {
            // [개선] Combat 중에도 TickVision() 실행 유지
            // 기존: Combat 상태에서 Update 전체 스킵 → 타겟 위치 갱신 안됨
            // 수정: Combat 중에도 시야 업데이트로 lastKnownPosition 갱신.
            //       단, 자취/소리/감쇄는 Combat 중 불필요하므로 유지.
            TickVision();

            if (awarenessLevel != AwarenessState.Combat)
            {
                TickTraceDetection();
                DecayAwareness();
            }
        }

        // ==================================================================
        // 시각 인지: OverlapSphere + 시야각 + Linecast 벽차단
        // ==================================================================
        public void TickVision()
        {
            // [v3.4 §4 L2] 매 사이클 시작 — currentTarget 시야 검증
            //   기존: Combat 진입 시점에만 brain.currentTarget 설정, 이후 시야 잃어도 stale 유지.
            //   수정: 시야 검증 후 잃으면 sharedTarget fallback 시도 → 실패면 null.
            //   효과: Multi-target 시나리오에서 적 전환 자동 작동.
            ValidateCurrentTarget();

            Collider[] hits = Physics.OverlapSphere(transform.position, visionRange, targetLayer);

            // ── 진단 로그: OverlapSphere 결과 ──────────────────────────────────
            // OverlapSphere 히트가 0이면 targetLayer 미설정 or 플레이어 레이어 불일치 의심
            if (debugLog && hits.Length == 0 && awarenessLevel >= AwarenessState.Alert)
                Debug.LogWarning(
                    $"[Perception] {name}: TickVision OverlapSphere 히트 0." +
                    $" visionRange={visionRange}m targetLayer={targetLayer.value}" +
                    $" → targetLayer가 Inspector에서 설정됐는지 확인!");

            // [v3.4 §4 L2] 시야 통과 hit들 중 closest 추적 + 첫 통과 hit만 Alert/Combat 처리
            //   기존: 첫 통과 hit에서 return → 다른 hit 검사 안 함 → multi-target 시 적 우선순위 무시
            //   수정: closest 추적 후 루프 끝에서 Combat swap 검사 (hysteresis 적용)
            bool firstHandled = false;
            Transform closestVisible = null;
            float closestVisibleDist = float.MaxValue;

            foreach (var hit in hits)
            {
                Vector3 dirToTarget = (hit.transform.position - transform.position);
                dirToTarget.y = 0;
                float angle = Vector3.Angle(transform.forward, dirToTarget.normalized);

                // 시야각 밖
                if (angle > visionAngle * 0.5f)
                {
                    if (debugLog)
                        Debug.Log($"[Perception] {name}: 시야각 밖 — {hit.name} " +
                                  $"angle={angle:F0}° limit={visionAngle * 0.5f:F0}°");
                    continue;
                }

                // 벽 차단 검사
                Vector3 eyePos = transform.position + Vector3.up * 1.5f;
                Vector3 targetPos = hit.transform.position + Vector3.up * 1.0f;
                if (Physics.Linecast(eyePos, targetPos, visionBlockLayer))
                {
                    if (debugLog)
                        Debug.Log($"[Perception] {name}: 벽 차단 — {hit.name} 시야 가려짐");
                    continue;
                }

                // 시야 확인 성공
                float distance = dirToTarget.magnitude;

                // [v3.4 §4 L2] closest 추적 — 다른 시야 통과 hit과 비교
                if (distance < closestVisibleDist)
                {
                    closestVisible = hit.transform;
                    closestVisibleDist = distance;
                }

                // 첫 통과 hit만 Alert/Combat 처리 (중복 호출 방지)
                if (firstHandled) continue;
                firstHandled = true;

                currentPerceptionTarget = hit.transform;
                lastKnownPosition = hit.transform.position;

                if (awarenessLevel < AwarenessState.Alert)
                    SetAwareness(AwarenessState.Alert, $"시야 확인 ({hit.name}, {distance:F1}m)");

                // [개선] Alert → Combat 전환: alertDurationForCombat 경과 후 전환
                if (awarenessLevel == AwarenessState.Alert)
                {
                    alertElapsed += Time.deltaTime;
                    if (debugLog)
                        Debug.Log($"[Perception] {name}: Alert 유지 중 " +
                                  $"{alertElapsed:F2}s / {alertDurationForCombat:F1}s " +
                                  $"→ {hit.name} dist={distance:F1}m");

                    if (alertElapsed >= alertDurationForCombat)
                    {
                        SetAwareness(AwarenessState.Combat, $"전투 돌입 ({hit.name}, {distance:F1}m, alertElapsed={alertElapsed:F1}s)");

                        // pB-4 AI에 타겟 전달
                        if (brain != null)
                        {
                            var mob = brain as TDA.PB4.AI.Mob.MobAIBrain;
                            var hum = brain as TDA.PB4.AI.Humanoid.HumanoidAIBrain;
                            if (mob != null) mob.currentTarget = hit.transform;
                            if (hum != null) hum.currentTarget = hit.transform;

                            // ── [v3.3.8 §4 L1] 그룹에 타겟 발견 통보 ────────────────
                            if (mob != null)
                            {
                                var grp = TDA.PB4.AI.GroupAIManager.FindGroupOwning(mob);
                                grp?.NotifyTargetSpotted(mob, hit.transform);
                            }

                            // ── Adapter 즉시 동기화 ────────────────────────────────
                            var adapter = GetComponent<TDA.PB4.Bridge.PB4DecisionAdapter>();
                            if (adapter != null)
                            {
                                adapter.ForceSyncBB();
                                if (debugLog)
                                    Debug.Log($"[Perception] {name}: Combat 진입 → ForceSyncBB() 즉시 호출. " +
                                              $"BB[Target]={hit.name} 동기화 완료.");
                            }
                        }

                        // 팩션 감지 SFX 이벤트 발행
                        RaiseFactionDetectionEvent(distance);
                    }
                }
            }

            // [v3.4 §4 L2] 루프 끝 — 시야 통과 hit이 있었음 → Combat swap 검사
            if (firstHandled)
            {
                if (awarenessLevel == AwarenessState.Combat && closestVisible != null && brain != null)
                {
                    var mob = brain as TDA.PB4.AI.Mob.MobAIBrain;
                    var hum = brain as TDA.PB4.AI.Humanoid.HumanoidAIBrain;
                    Transform current = mob != null ? mob.currentTarget : hum?.currentTarget;

                    if (current != closestVisible)
                    {
                        float currentDist = current != null
                            ? Vector3.Distance(transform.position, current.position)
                            : float.MaxValue;

                        // Hysteresis — closest가 30% 이상 가까울 때만 swap (떨림 방지)
                        // 예: current=10m, closest=6.9m 이하여야 swap (≥31% 더 가까움)
                        if (closestVisibleDist < currentDist * 0.7f)
                        {
                            if (debugLog)
                                Debug.Log($"[Perception] {name}: 더 가까운 적 발견 → currentTarget swap " +
                                          $"({current?.name ?? "null"} {currentDist:F1}m → " +
                                          $"{closestVisible.name} {closestVisibleDist:F1}m)");

                            if (mob != null) mob.currentTarget = closestVisible;
                            if (hum != null) hum.currentTarget = closestVisible;

                            if (mob != null)
                            {
                                var grp = TDA.PB4.AI.GroupAIManager.FindGroupOwning(mob);
                                grp?.NotifyTargetSpotted(mob, closestVisible);
                            }

                            var adapter = GetComponent<TDA.PB4.Bridge.PB4DecisionAdapter>();
                            if (adapter != null) adapter.ForceSyncBB();
                        }
                    }
                }
                return;
            }

            // 시야에 타겟 없으면 lastKnownPosition 갱신
            if (awarenessLevel == AwarenessState.Alert && currentPerceptionTarget != null)
                lastKnownPosition = currentPerceptionTarget.position;

            // Alert 타이머 리셋 (시야 벗어나면 누적 초기화)
            if (awarenessLevel == AwarenessState.Alert)
                alertElapsed = 0f;
        }

        // ==================================================================
        // 청각 인지: SoundEventEmitter.OnSoundEmitted 이벤트 어댑터
        // [개선] OnEnable에서 이 메서드를 구독 등록.
        //        SoundEventEmitter.OnSoundEmitted는 (Vector3, SoundType, float) 3인자 이벤트.
        //        기존 OnSoundHeard(Vector3, float) 시그니처를 그대로 유지하기 위해
        //        어댑터 패턴으로 중계.
        // ==================================================================
        private void HandleSoundEvent(Vector3 soundPos, SoundType type, float volume)
        {
            // SoundType별 기본 전파 반경 (SoundPerception.HandleSoundEmitted와 동일 기준)
            float baseRadius = type switch
            {
                SoundType.Footstep => 15f,
                SoundType.Combat => 40f,
                SoundType.Mining => 25f,
                SoundType.Explosion => 60f,
                SoundType.Dialogue => 8f,
                _ => 20f
            };
            // volume × baseRadius = sourceRadius (실제 소리 도달 거리)
            OnSoundHeard(soundPos, volume, volume * baseRadius);
        }

        // ==================================================================
        // 청각 인지 처리
        // [개선] sourceRadius 파라미터 추가 — SoundPerception.OnSoundEvent와 동일 로직 적용.
        //        기존: sourceRadius 없이 hearingRange만 비교 → 프로브 볼륨 0.3 시
        //              sourceRadius=4.5m인데 hearingRange=30m로 통과되어 먼 곳도 반응 가능성
        //              (반대로 너무 작은 sourceRadius는 감지 자체가 안됨)
        //        수정: distance > min(hearingRange, sourceRadius) 이면 무시.
        //              Combat 중에도 LastKnownPosition 갱신 (타겟 위치 보정).
        // ==================================================================
        public void OnSoundHeard(Vector3 soundPos, float volume, float sourceRadius = -1f)
        {
            float distance = Vector3.Distance(transform.position, soundPos);

            // sourceRadius가 전달된 경우 두 조건 모두 통과해야 감지
            // sourceRadius 미전달(-1) 시 hearingRange만 사용 (하위 호환)
            float effectiveRange = sourceRadius > 0f
                ? Mathf.Min(hearingRange, sourceRadius)
                : hearingRange;

            if (distance > effectiveRange) return;

            float perception = volume * hearingSensitivity * (1f - distance / hearingRange);
            if (perception < perceptionThreshold) return;

            lastKnownPosition = soundPos;

            if (debugLog)
                Debug.Log($"[Perception] {name}: 소리 감지 vol={volume:F2} " +
                          $"dist={distance:F1}m effectiveRange={effectiveRange:F1}m " +
                          $"perc={perception:F2} sourceRadius={sourceRadius:F1}m");

            // [개선] Combat 중에도 LastKnownPosition 갱신
            // 기존: awarenessLevel < Suspicious 조건만 있어 Combat 중 소리 완전 무시
            // 수정: 어떤 상태든 lastKnownPosition 갱신. 상태 변경은 Suspicious 미만일 때만.
            if (awarenessLevel < AwarenessState.Suspicious)
                SetAwareness(AwarenessState.Suspicious,
                    $"소리 감지 (vol={volume:F1}, dist={distance:F0}m, perc={perception:F2})");
        }

        // ==================================================================
        // 자취 인지: FootprintTrailSystem에서 주변 마커 검색
        // ==================================================================
        // ==================================================================
        // [v3.4 §4 L2 + Memory Stub] currentTarget 시야 유효성 검증
        // 매 TickVision 사이클 시작 시 호출. 시야 잃은 stale target을 정리하고
        // sharedTarget fallback 시도 → multi-target 환경에서 적 자동 전환.
        //
        // [v3.4.1 메모리 stub] 시야 잃은 직후 즉시 null 처리하면 BT가 Attack 진입 못 함.
        //   해결: currentTargetMemoryDuration 초간 stale 유지 → fallback 또는 시야 복구 대기.
        //   (전투 메모리 시스템 일부이지만 핵심 동작 위해 stub만 적용. 본격 시스템은 후순위.)
        // ==================================================================
        [Header("━ Target Memory (v3.4.1 stub) ━━━━━━━━━━")]
        [Tooltip("자체 시야 잃은 후 currentTarget을 유지하는 시간 (초). " +
                 "이 동안 sharedTarget fallback 시도 + 자체 시야 복구 대기. " +
                 "0 = 즉시 null 정리 (이전 동작). 권장 2~3초.")]
        [SerializeField] private float currentTargetMemoryDuration = 3.0f;

        private float _lastTimeTargetInSight = 0f;

        private void ValidateCurrentTarget()
        {
            if (brain == null) return;
            var mob = brain as TDA.PB4.AI.Mob.MobAIBrain;
            var hum = brain as TDA.PB4.AI.Humanoid.HumanoidAIBrain;
            Transform current = mob != null ? mob.currentTarget : hum?.currentTarget;
            if (current == null) return;

            // 시야 검증 — 거리 + 시야각 + 벽 차단
            Vector3 dir = current.position - transform.position;
            dir.y = 0;
            float dist = dir.magnitude;
            float angle = Vector3.Angle(transform.forward, dir.normalized);

            bool inRange = dist <= visionRange;
            bool inSight = angle <= visionAngle * 0.5f;
            bool blocked = false;
            if (inRange && inSight)
            {
                Vector3 eyePos = transform.position + Vector3.up * 1.5f;
                Vector3 targetPos = current.position + Vector3.up * 1.0f;
                blocked = Physics.Linecast(eyePos, targetPos, visionBlockLayer);
            }

            if (inRange && inSight && !blocked)
            {
                _lastTimeTargetInSight = Time.time;  // 시야 안 — 메모리 갱신
                return;
            }

            // 자체 시야 잃음 → sharedTarget fallback 시도 (mob 전용)
            if (mob != null)
            {
                var grp = TDA.PB4.AI.GroupAIManager.FindGroupOwning(mob);
                var shared = grp?.GetSharedTarget(mob);
                if (shared != null && shared != current)
                {
                    if (debugLog)
                        Debug.Log($"[Perception] {name}: '{current.name}' 시야 잃음 " +
                                  $"(inRange={inRange} inSight={inSight} blocked={blocked} dist={dist:F1}m angle={angle:F0}°) " +
                                  $"→ sharedTarget으로 fallback: {shared.name}");
                    mob.currentTarget = shared;
                    _lastTimeTargetInSight = Time.time;  // shared로 갱신됐으니 메모리 reset
                    return;
                }
            }

            // [v3.4.1] 시야 잃음 + fallback 실패 → 메모리 유지 검사
            float memoryElapsed = Time.time - _lastTimeTargetInSight;
            if (memoryElapsed < currentTargetMemoryDuration)
            {
                // 일정 시간 stale 유지 — BT가 Attack 진입 + 위치 추적 가능
                if (debugLog)
                    Debug.Log($"[Perception] {name}: '{current.name}' 시야 잃음, " +
                              $"메모리 유지 ({memoryElapsed:F1}s / {currentTargetMemoryDuration:F1}s)");
                return;
            }

            // 메모리 timeout — null 정리
            if (debugLog)
                Debug.Log($"[Perception] {name}: '{current.name}' 메모리 timeout ({memoryElapsed:F1}s) → currentTarget=null");
            if (mob != null) mob.currentTarget = null;
            if (hum != null) hum.currentTarget = null;
        }

        // ==================================================================
        // 자취/소리 기반 인지 (Combat 외에서만)
        // ==================================================================
        private void TickTraceDetection()
        {
            if (awarenessLevel >= AwarenessState.Alert) return;

            var trailSystem = FootprintTrailSystem.Instance;
            if (trailSystem == null) return;

            var nearest = trailSystem.GetNearestTrail(transform.position, traceDetectionRange);
            if (nearest == null) return;

            lastKnownPosition = nearest.Value;

            if (awarenessLevel < AwarenessState.Suspicious)
                SetAwareness(AwarenessState.Suspicious,
                    $"자취 발견 ({Vector3.Distance(transform.position, nearest.Value):F1}m)");
        }

        // ==================================================================
        // 감쇄: 시간 경과 시 각성 레벨 감소
        // ==================================================================
        private void DecayAwareness()
        {
            if (isPersistentHunt) return; // Case C: 감쇄 비활성화

            if (awarenessLevel == AwarenessState.Suspicious)
            {
                suspicionTimer += Time.deltaTime;
                if (suspicionTimer >= suspicionDecayTime)
                {
                    SetAwareness(AwarenessState.Unaware, "의심 시간 초과 → Unaware");
                    suspicionTimer = 0f;
                }
            }
            else if (awarenessLevel == AwarenessState.Alert)
            {
                alertTimer += Time.deltaTime;
                if (alertTimer >= alertDecayTime)
                {
                    SetAwareness(AwarenessState.Suspicious, "경계 시간 초과 → Suspicious");
                    alertTimer = 0f;
                    alertElapsed = 0f; // [개선] alertElapsed도 함께 초기화
                }
            }
        }

        // ==================================================================
        // 외부 API
        // ==================================================================

        /// <summary>외부에서 강제로 각성 상태를 설정 (Case A/C에서 사용).</summary>
        public void ForceSetAwareness(AwarenessState state, string reason = "외부 강제")
        {
            SetAwareness(state, reason);
        }

        /// <summary>외부에서 lastKnownPosition을 설정 (Case C에서 사용).</summary>
        public void SetLastKnownPosition(Vector3 pos)
        {
            lastKnownPosition = pos;
        }

        /// <summary>Case C: 나선형 수색 위치를 반환. lastKnownPosition 주변을 나선형으로 탐색.</summary>
        public Vector3 GetSpiralSearchPosition()
        {
            spiralAngle += Time.deltaTime * 30f; // 30도/초
            float radius = Mathf.Min(spiralAngle * 0.1f, spiralSearchRadius);
            float rad = spiralAngle * Mathf.Deg2Rad;
            return lastKnownPosition + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
        }

        // ==================================================================
        // 내부 헬퍼
        // ==================================================================
        private void SetAwareness(AwarenessState newState, string reason)
        {
            if (awarenessLevel == newState) return;
            var oldState = awarenessLevel;
            awarenessLevel = newState;
            suspicionTimer = 0f;
            alertTimer = 0f;

            // Alert 진입 시 alertElapsed 초기화 (Combat 전환 타이머 리셋)
            if (newState == AwarenessState.Alert)
                alertElapsed = 0f;

            if (debugLog)
                Debug.Log($"[Perception] {name}: {oldState}→{newState} ({reason})");

            // [개선] 외부 이벤트 발행 — SFX·UI·Camera 등 외부 시스템이 폴링 없이 수신 가능
            OnAwarenessChanged?.Invoke(oldState, newState, reason);
        }

        private void RaiseFactionDetectionEvent(float distance)
        {
            // ── [C-3 stub 완성] EventBus.OnFactionDetectedPlayer 실제 발행 ──
            // MobAIBrain.factionData 를 취득하여 이벤트와 함께 전달합니다.
            // FactionDetectionSFXManager 등이 이 이벤트를 구독하여 반응합니다.
            var mobBrain = GetComponent<MobAIBrain>();
            if (mobBrain != null)
            {
                // MobFactionDataSO factionData 는 private — GetFactionData() 래퍼 또는
                // MobAIBrain 에 public getter 를 추가하거나 아래처럼 Reflection 없이
                // EventBus 에 Transform 만 전달하는 오버로드를 사용합니다.
                EventBus.RaiseFactionDetectedPlayer(transform);

                if (debugLog)
                    Debug.Log($"[Perception] {name}: FactionDetected 이벤트 발행 (dist={distance:F1}m)");
            }
            else
            {
                // HumanoidAIBrain 또는 Brain 없는 AI
                EventBus.RaiseFactionDetectedPlayer(transform);

                if (debugLog)
                    Debug.Log($"[Perception] {name}: FactionDetected 이벤트 발행 (Brain 없음, dist={distance:F1}m)");
            }
        }

        // ==================================================================
        // Gizmo
        // ==================================================================
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;

            // 시야 범위 (노란색 원뿔)
            Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, visionRange);
            Vector3 fwd = transform.forward * visionRange;
            float halfAngle = visionAngle * 0.5f * Mathf.Deg2Rad;
            Vector3 leftRay = Quaternion.Euler(0, -visionAngle * 0.5f, 0) * fwd;
            Vector3 rightRay = Quaternion.Euler(0, visionAngle * 0.5f, 0) * fwd;
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * 1.5f, leftRay);
            Gizmos.DrawRay(transform.position + Vector3.up * 1.5f, rightRay);

            // 청각 범위 (파란색)
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, hearingRange);

            // 자취 범위 (초록색)
            Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, traceDetectionRange);

            // lastKnownPosition (빨간 구체)
            if (awarenessLevel > AwarenessState.Unaware)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(lastKnownPosition, 0.5f);
                Gizmos.DrawLine(transform.position, lastKnownPosition);
            }

            // 나선형 수색 (Case C)
            if (isPersistentHunt && awarenessLevel == AwarenessState.Suspicious)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(lastKnownPosition, spiralSearchRadius);
            }
        }
#endif
    }
}