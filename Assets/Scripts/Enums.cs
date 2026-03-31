// =============================================================================
// Enums.cs  |  TDA Project
// 역할: 프로젝트 전역 열거형 / 구조체 정의
//
// 수정 이력:
//   [기존 유지] 모든 기존 AnimationEventType / ActionID / Enum 완전 보존
//   [신규] ActionID에 경량 피격 전용 ID 추가: Hit_Light_* (20~24번대)
//          → PlayLightHitAnimation() 임시 Attack_* 사용 해소
//   [신규] AnimationEventType에 ThrustAdvance_Land = 82 추가
//          → 카메라 보고서 v2 요구사항 (착지 쉐이크 이벤트)
//   [신규] AnimationEventType에 ComboChain_Backstep = 83 추가
//          → 카메라 보고서 v2 요구사항 (연계 분기 카메라)
//   [수정] Execution_Finish = 202 위치 정리 (기존 700번대 혼입 → 200번대 이동)
//   [주석] angleHitFrom 경계 데드존 경고 추가 (float -144~-145 사이 미분류)
//   [유지] ActionID Skeleton 전용 500번대 완전 보존
//   [유지] ExecutionTargetTier — FinisherData.cs(TDA.Character 네임스페이스)에
//          이미 정의되어 있으므로 Enums.cs 전역 선언 추가하지 않음 (CS0101 방지)
// =============================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Enums : MonoBehaviour { }

// =========================================================================================
// [P4 신규 추가] 애니메이션 이벤트 통합 규약 (Type-Safe Event System)
// 기획 의도: String 하드코딩으로 인한 버그를 막고, 카테고리별로 300개의 여유 슬롯을 두어
// 향후 사운드, 시각효과, 로직의 확장을 무한히 보장합니다.
// =========================================================================================
public enum AnimationEventType
{
    #region [0 ~ 299] 시스템, 물리 및 전투 플로우 제어 (Logic, Physics & Flow)

    // --- 기본 전투 플로우 ---
    ComboEnable = 0,            // 다음 콤보 입력 허용 (입력 버퍼링 시작)
    ComboDisable = 1,           // 콤보 입력 허용 종료
    HitBoxEnable = 2,           // 무기 타격 판정(Trigger/Raycast) 활성화
    HitBoxDisable = 3,          // 무기 타격 판정 비활성화
    Action_Ended = 4,           // 애니메이션 종료 (상태 머신 초기화용)
    Damage_Calculated = 5,      // [P1] 데미지 연산 완료 (UI/이펙트 트리거용 2차 신호)

    // --- 무적 및 상태 제어 ---
    IFrameEnable = 6,           // 회피/구르기 무적 프레임 시작
    IFrameDisable = 7,          // 무적 프레임 종료
    Charge_Started = 8,         // 강공격/차지 시작
    Charge_Ended = 9,           // 차지 완료 및 해방

    // --- 방어 및 패링 (Directional Guard) ---
    Guard_Active = 10,          // 가드 판정 활성화
    Guard_Deactive = 11,        // 가드 판정 종료
    Parry_Window_Open = 12,     // 저스트 패링(Parry) 유효 프레임 시작
    Parry_Window_Close = 13,    // 저스트 패링 유효 프레임 종료

    // --- 이동 및 회전 락 (Locomotion Control) ---
    Lock_Rotation = 14,         // 공격 중 임의 회전 강제 금지 (스케이팅 방지)
    Unlock_Rotation = 15,       // 회전 다시 허용
    Lock_Movement = 16,         // 이동 강제 금지
    Unlock_Movement = 17,       // 이동 다시 허용

    // --- 8축 제스처 공격 방향 (8-Axis Gesture) ---
    AttackDir_Up = 30,          // 상단 내려찍기 궤적 시작
    AttackDir_Down = 31,        // 하단 올려치기 궤적 시작
    AttackDir_Left = 32,        // 좌측 베기 궤적 시작
    AttackDir_Right = 33,       // 우측 베기 궤적 시작
    AttackDir_UpLeft = 34,      // 좌상단 대각선 베기
    AttackDir_UpRight = 35,     // 우상단 대각선 베기
    AttackDir_DownLeft = 36,    // 좌하단 대각선 베기
    AttackDir_DownRight = 37,   // 우하단 대각선 베기
    AttackDir_Thrust = 38,      // 찌르기 (Z축 중심)

    // --- 페어 애니메이션 및 잡기 (Paired Anim & Interaction) ---
    Pair_Sync_Start = 50,       // 공격자-피격자 트랜스폼 동기화 강제 스냅 (암살 시작)
    Pair_Damage_Apply = 51,     // 페어 애니메이션 도중 데미지 꽂히는 타이밍
    Pair_Release = 52,          // 구속 풀림 (피격자 래그돌 전환 등)
    ItemGrab_Attach = 53,       // 파지(Grab): 물체를 손(Bone)에 페어링하는 시점
    ItemGrab_Detach = 54,       // 물체를 손에서 놓거나 던지는 시점

    // --- 역경직 및 넉백 (Hitstop & Recoil) ---
    Hitstop_Enter = 70,         // 타격 부위에 무기가 닿아 프레임이 일시정지(Hitstop)되는 시점
    Hitstop_Exit = 71,          // 역경직이 풀리고 다시 애니메이션이 진행되는 시점
    Recoil_Trigger = 72,        // 벽이나 가드에 튕겨서 뒤로 밀려나는 모션 트리거

    // --- 신체 훼손 및 부위 파괴 (Limb Logic & Dismemberment) ---
    Detach_Head = 90,           // 머리 절단 및 캡(Cap) 메쉬 활성화
    Detach_Arm_L = 91,          // 왼팔 절단
    Detach_Arm_R = 92,          // 오른팔 절단
    Detach_Leg_L = 93,          // 왼다리 절단
    Detach_Leg_R = 94,          // 오른다리 절단
    Detach_Torso = 95,          // 몸통 반갈죽 (세로/가로)
    Ragdoll_Enable = 96,        // 애니메이션 강제 종료 및 전신 래그돌 물리 전환

    // --- 포이즈 파괴 및 그로기 (Poise Break & Groggy) ---
    Groggy_Enter = 97,          // 포이즈가 0이 되어 그로기(무방비) 상태에 진입하는 순간
                                // → AICharacterEffectsManager: 그로기 임팩트+루프 VFX
                                // → AICharacterSoundFxManager: 그로기 보이스
    Groggy_Exit = 98,           // 그로기 타이머 만료, 전투 복귀 직전
                                // → AICharacterEffectsManager: 루프 VFX 제거

    // =========================================================================================
    // [신규] 방향별 피격 이벤트 (Directional Hit Events) [80 ~ 99]
    //
    // ⚠️ 80~83: 연계(ComboChain) 이벤트 — [신규 추가]
    // ⚠️ 84~87: 방향별 피격 이벤트
    // ⚠️ 88~89: 가해자 측 타격 확인 이벤트
    //
    // 설계 의도:
    //   TakeDamageEffect가 ActionID(Stagger_*)를 통해 애니메이션을 재생한 뒤,
    //   동시에 Hit_From_* 이벤트를 발행합니다.
    //   블러(VFX) 팀원은 이 이벤트를 구독하여 "어느 방향에서 맞았는지"를
    //   방향 정보와 함께 처리할 수 있습니다.
    //
    // 수신 대상 (구독자):
    //   - CharacterVisualFeedbackManager: 방향별 블러/비네팅/화면 왜곡
    //   - CharacterEffectsManager:        방향별 혈흔 파티클 방향 제어
    //   - CharacterSoundFxManager:        방향별 피격음 패닝(좌/우 채널)
    //
    // ActionID와 1:1 매핑:
    //   Stagger_Backward (100) ←→ Hit_From_Front   (84)
    //   Stagger_Left     (101) ←→ Hit_From_Right   (85)
    //   Stagger_Right    (102) ←→ Hit_From_Left    (86)
    //   Stagger_Forward  (103) ←→ Hit_From_Behind  (87)
    // =========================================================================================

    // --- ComboChain 연계 윈도우 이벤트 (P2-3) [80 ~ 83] ---
    ComboWindow_Open = 80,          // 연계 입력 큐잉 윈도우 오픈 → OnComboWindowOpened() 트리거
    ComboWindow_Close = 81,         // 연계 입력 윈도우 닫힘 → isBackstepQueued 초기화

    // =========================================================================================
    // [신규] 카메라 보고서 v2 요구 이벤트 (82 ~ 83)
    //
    // 카메라 보고서 v2 6절에서 정의된 이벤트입니다.
    // BaseActionBehaviour.OnStateUpdate()에서 특정 타이밍(normalizedTime)에 발행합니다.
    //
    // WorldCameraManager.OnAnimationEventReceived()에 아래 케이스를 추가하세요:
    //   case AnimationEventType.ThrustAdvance_Land:
    //       localPlayerCamera.Shake(impactIntensity, 0.15f);
    //       PlayCameraSequence(Seq_ThrustAdvance_SO, caller);
    //       break;
    //   case AnimationEventType.ComboChain_Backstep:
    //       PlayCameraSequence(Seq_ThrustBackstep_SO, caller);
    //       break;
    // =========================================================================================
    ThrustAdvance_Land = 82,        // 전진 찌르기 착지 타이밍 → 카메라 임팩트 쉐이크 트리거
                                    // BaseActionBehaviour: normalizedTime >= 0.55f 시점에 발행
    ComboChain_Backstep = 83,       // 뒤로 빠지기 연계 확정 → Seq_ThrustBackstep_SO 발동
                                    // PlayerCombatManager.ResolveComboChain() → eventManager.NotifyAnimationEvent

    // --- 방향별 피격 이벤트 [84 ~ 87] ---
    Hit_From_Front = 84,       // 정면 피격 신호 — ActionID.Stagger_Backward(100)와 쌍
                               // → 블러: 화면 전체 앞방향 충격 연출
    Hit_From_Right = 85,       // 우측 피격 신호 — ActionID.Stagger_Left(101)와 쌍
                               // → 블러: 화면 우측 엣지 강조
    Hit_From_Left = 86,        // 좌측 피격 신호 — ActionID.Stagger_Right(102)와 쌍
                               // → 블러: 화면 좌측 엣지 강조
    Hit_From_Behind = 87,      // 후면 피격 신호 — ActionID.Stagger_Forward(103)와 쌍
                               // → 블러: 화면 하단/후면 충격 연출 (뒤통수 맞은 느낌)

    // =========================================================================================
    // [Phase1 신규] 가해자 측 타격 확인 이벤트 (Combat Hit Confirmed Events) [88 ~ 89]
    //
    // 발행 경로:
    //   DamageCollider.OnTriggerEnter()
    //     → CharacterCombatManager.OnHitConfirmed(HitConfirmedData)
    //       → characterEventManager.NotifyAnimationEvent(Hit_Confirmed / Hit_Confirmed_Heavy)
    //
    // 수신 대상:
    //   - PlayerEventManager: 가해자 화면에 카메라 킥 / 히트스탑 오버레이 적용
    //   - ObjectMotionBlurController: 타격 확인 시 블러 Pulse (경량 / 강함 분기)
    //
    // BlurEventResponseSO에서 강도/시간 설정 (하드코딩 금지):
    //   Hit_Confirmed       → 경공격 타격: 약한 Pulse (strength ≈ 0.2, duration ≈ 0.05s)
    //   Hit_Confirmed_Heavy → 강공격 / 포이즈 파괴: 강한 Pulse (strength ≈ 0.5, duration ≈ 0.1s)
    // =========================================================================================
    Hit_Confirmed = 88,             // 가해자: 경/강공격 타격 확인 — 블러 경량 Pulse
    Hit_Confirmed_Heavy = 89,       // 가해자: 포이즈 파괴 타격 확인 — 블러 강한 Pulse

    // --- 처형 이벤트 [99] ---
    Execution_Finish = 99,          // 특수 처형 애니메이션 종료 시점
                                    // ※ 이전: Execution_Finish = 202 (VFX 블록 혼입) → 200번대 ActionID 충돌 방지를 위해 99로 이동
                                    //   ActionID.Dead_Execution = 201, Dead_01 = 200 과 완전히 분리

    #endregion

    #region [300 ~ 599] 오디오 및 사운드 피드백 (Audio / SFX)

    // --- 발소리 (Footsteps - Locomotion 연동) ---
    PlayFootstep_L = 300,           // 왼쪽 발 접지 사운드 (걷기, 뛰기 공통)
    PlayFootstep_R = 301,           // 오른쪽 발 접지 사운드
    PlayFootstep_Drag_L = 302,      // 전투 Strafe 중 왼쪽 발을 질질 끄는 사운드
    PlayFootstep_Drag_R = 303,      // 전투 Strafe 중 오른쪽 발을 질질 끄는 사운드
    PlayFootstep_Pivot = 304,       // 제자리 턴(Turn 90) 시 축이 되는 발바닥 마찰음

    // --- 무기 스윙 및 마찰 (Weapon Swings) ---
    PlaySFX_Swing_Light = 310,      // 가벼운 무기 (단검 등) 휘두르는 소리
    PlaySFX_Swing_Heavy = 311,      // 무거운 무기 (대검 등) 공기 가르는 소리
    PlaySFX_Swing_Blunt = 312,      // 둔기 붕붕거리는 소리
    PlaySFX_Weapon_Draw = 313,      // 무기 꺼낼 때 마찰음 (스르릉)
    PlaySFX_Weapon_Sheathe = 314,   // 무기 집어넣을 때 소리

    // --- 타격 및 살점 (Impact & Gore) ---
    PlaySFX_Impact_Flesh = 330,     // 살덩이 베이는 소리 (질척한 소리)
    PlaySFX_Impact_Bone = 331,      // 뼈가 부러지거나 절단되는 타격음 (크런치)
    PlaySFX_Impact_Armor = 332,     // 금속 갑옷을 때리는 둔탁한 쇳소리
    PlaySFX_Parry_Clang = 333,      // 패링 성공 시 날카로운 쇳소리 (고주파)
    PlaySFX_Guard_Thud = 334,       // 가드 위로 막았을 때의 둔탁한 소리

    // --- 보이스 및 호흡 (Voices & Breathing) ---
    PlayVoice_Attack = 350,         // 공격 기합 소리
    PlayVoice_Stagger = 351,        // 피격 시 고통스러운 신음 소리 (Stagger 연동)
    PlayVoice_Death = 352,          // 사망 단말마
    PlayVoice_Breathe = 353,        // 대기(Idle) 중 희박한 확률로 나오는 거친 숨소리
    PlayVoice_Stagger_Pain = 354,   // [P0-03] 엇박자(절기) 조작 실패 시 근육이 꼬이는 고통스러운 신음

    // --- 시스템 및 상태 이상 (System UI/UX) ---
    PlaySFX_Stamina_Out = 390,      // 스태미나 고갈 시 헐떡임/경고음
    PlaySFX_Stamina_Full = 391,     // 스태미나 회복 완료 (UI 연동)
    PlaySFX_Stamina_Recovered = 392,    // [P1] 스태미나 회복 완료 시점 2차 신호
    PlaySFX_Stamina_Exhausted = 393,    // 스태미나 완전 소진 시점 2차 신호
    PlaySFX_UI_LockOn = 394,        // 락온 성공 시 특유의 레트로 사운드

    #endregion

    #region [600 ~ 899] 시각 효과 및 카메라 연출 (Visuals, Camera & VFX)

    // --- 화면 흔들림 및 카메라 연출 (Camera Kicks) ---
    CameraShake_Light = 600,        // 약한 타격/피격 시 미세한 흔들림
    CameraShake_Heavy = 601,        // 강공격 타격/피격 시 강한 지진 효과
    CameraShake_Roll = 602,         // 구르기 후 바닥에 닿을 때 카메라 충격
    CameraKick_FOV_In = 603,        // 강공격 차지 시 FOV를 좁혀 집중감 부여
    CameraKick_FOV_Out = 604,       // 타격 임팩트 시 FOV를 튕겨내어 타격감 폭발
    CameraShake_Heavy_Fumble = 605, // [P0-03] 엇박자(절기) 조작 실패 시 강렬한 카메라 반동

    // --- 무기 검기 및 궤적 (Weapon Trails) ---
    Trail_Enable_15fps = 620,       // [Dual Framerate] 레트로 감성의 15fps 스텝 검기 켜기
    Trail_Disable_15fps = 621,      // 15fps 검기 끄기
    Trail_Enable_Smooth = 622,      // 부드러운 일반 검기 켜기
    Trail_Disable_Smooth = 623,     // 일반 검기 끄기
    Spawn_Spark_Clash = 624,        // 패링/가드 충돌 시 불꽃 스파크 60fps 파티클 스폰

    // --- 혈흔 및 고어 이펙트 (Blood & Gore) ---
    VFX_Blood_Splatter = 640,       // 기본 피 튀김 이펙트 (60fps)
    VFX_Blood_Directional = 641,    // 8방향 궤적에 맞춰 흩뿌려지는 혈흔
    VFX_Blood_Pool = 642,           // 사망/절단 시 바닥에 고이는 웅덩이 생성
    VFX_Gore_Chunks = 643,          // 신체 훼손 시 튀어오르는 고기 조각들

    // --- 렌더링 및 화면 필터 (Screen FX) ---
    ScreenFX_Whiteout_1F = 670,     // [Hit Feedback] 타격 성공 시 1프레임 전체 화면 화이트아웃
    ScreenFX_Hit_Vignette = 671,    // 피격 시 화면 테두리 붉은 비네팅(Vignette) 효과 점멸
    ScreenFX_Chromatic = 672,       // 공포 상황이나 그로기 시 색수차(Chromatic Aberration) 펌핑

    // --- 환경 상호작용 (Environment) ---
    VFX_Dust_Footstep = 690,        // 뛰거나 구를 때 발밑 먼지 파티클
    VFX_Ground_Crater = 691,        // 대검으로 땅을 내리찍었을 때 데칼/파편 생성

    // ── [700번대] HitSpark GPU 파티클 계열 ────────────────────────────────────
    // 발행자 : TakeDamageEffect.PlayDamageVFX()
    // 수신자 : CharacterEffectsManager → ParrySparkGPUManager
    // 데이터 : HitSparkVFXRegistry → HitSparkVFXData (SO)
    // ※ 이 블록 모든 값은 HitSparkVFXRegistry 에 반드시 등록해야 합니다.
    // ─────────────────────────────────────────────────────────────────────────
    VFX_HitSpark_Default = 700,     // 기본 물리 타격 불꽃 (fallback)
    VFX_HitSpark_Light = 701,       // 경공격 타격 불꽃 — 얕고 짧은 스파크
    VFX_HitSpark_Heavy = 702,       // 중공격 타격 불꽃 — 넓고 강한 스파크
    VFX_HitSpark_Critical = 703,    // 치명타 불꽃 — 강렬한 황금빛 폭발
    VFX_HitSpark_Parry = 704,       // 패리 불꽃 — GPU 대량 방출
    VFX_HitSpark_BlockBreak = 705,  // 가드 붕괴 불꽃
    VFX_HitSpark_ArmorClash = 706,  // 방어구 충돌 불꽃

    #endregion
}

// =========================================================================================
// [기존 시스템 Enum 보존 영역]
// =========================================================================================
public enum WorldSlots { WorldSlots_01 }

public enum CharacterSlots
{
    CharacterSlots_01, CharacterSlots_02, CharacterSlots_03,
    CharacterSlots_04, CharacterSlots_05, No_Slot,
}

public enum CharacterGroup { Team01, Team02 }

public enum WeaponModelSlot { RightHand, LeftHand }

public enum AttackType
{
    LightAttack01, LightAttack02, HeavyAttack01, HeavyAttack02, ChargeAttack01, ChargeAttack02,
}

public enum LockOnDirection { Left, Right, None }

public enum SwithchWeaponSide { Left, Right }

public enum CookingState
{
    Empty, Raw, Cooking, Cooked, Burnt
}

public enum CookingStationType
{
    Pot, Grill,
}

public enum GameState
{
    Normal, Chase, LockOn, Inventory, Table, Cooking, CinematicFocus,
}

public enum EquipmentSlot
{
    RightHand, LeftHand, Helmet, ChestArmor, Pants, Leggings, Backpack, Accessory
}

// =========================================================================================
// [P0-02 / P1-08 전투 기획 고도화 반영 Enum 영역]
// =========================================================================================

public enum GuardDirection { None, Top, Bottom, Left, Right }

public enum DefenseResult
{
    Hit,           // 방어 실패 (생몸에 맞음)
    Blocked,       // 일반 방어 성공 (하지만 Poise 데미지는 들어옴)
    Parried,       // 완벽한 타이밍 패링
    Deflected,     // 상대의 강한 공격에 의해 방패가 튕겨져 나감 (처냄 당함)
    GuardBroken    // 방패 내구도가 0이 되어 파손됨
}

public enum ShieldStance { FarGrip, CloseGrip }

// =========================================================================================
// [P0-03 신규] 체술 기반 제스처 전투 시스템 (Weapon Stance)
// =========================================================================================
public enum WeaponStance
{
    LeftHold,   // 무기를 캐릭터 기준 왼쪽에 쥐고 있는 상태 (우->좌 베기 이후)
    RightHold   // 무기를 캐릭터 기준 오른쪽에 쥐고 있는 상태 (좌->우 베기 이후)
}

public struct HitEventData
{
    public float damage;
    public float impactForce;
    public GuardDirection attackDirection;
}

// =========================================================================================
// [Funnel 아키텍처 연동] 액션 및 리액션 라우팅 인덱스 (ActionID)
// =========================================================================================
public enum ActionID
{
    None = 0,

    // --- 전투 및 기본 공격 (Combat) [1 ~ 19] ---
    Attack_Right_01 = 1,        // 우에서 좌로 베기 (기본)
    Attack_Left_01 = 2,         // 좌에서 우로 베기 (기본)
    Attack_Thrust = 3,          // 찌르기 (스크린스페이스 좌측 입력 — 좌파지 전용)

    // [P0-03 신규 추가] 강공격 (차징) Action ID 할당
    Attack_Right_Heavy_01 = 11, // 우에서 좌로 강공격 (차징)
    Attack_Left_Heavy_01 = 12,  // 좌에서 우로 강공격 (차징)

    // =========================================================================================
    // [신규] 경량 피격 전용 ActionID [20 ~ 24]
    //
    // 배경:
    //   TakeDamageEffect.PlayLightHitAnimation()에서 포이즈 유지 시 재생되는
    //   경량 피격 모션에 Attack_Right_01(1), Attack_Left_01(2)을 임시로 사용하고 있습니다.
    //   이는 "공격 모션"이 피격 상황에 재생되는 잘못된 연결입니다.
    //
    // 대응:
    //   이 ActionID를 Animator Controller의 경량 피격 전용 State에 연결하고
    //   TakeDamageEffect.PlayLightHitAnimation()의 임시 주석을 아래 ID로 교체하세요.
    //
    //   기존 코드 (임시):
    //     hitDirection = ActionID.Attack_Right_01;  // 임시: 전용 Hit_Forward 추가 전
    //     hitDirection = ActionID.Attack_Left_01;   // 임시: 전용 Hit_Backward 추가 전
    //
    //   교체 코드:
    //     hitDirection = ActionID.Hit_Light_Forward;   // 정면 경량 피격
    //     hitDirection = ActionID.Hit_Light_Backward;  // 후면 경량 피격
    //
    // Animator Controller 설정:
    //   Action Layer > Hit_Light_Forward State: ActionState parameter == 20
    //   Action Layer > Hit_Light_Backward State: ActionState parameter == 21
    //   Action Layer > Hit_Light_Left State: ActionState parameter == 22
    //   Action Layer > Hit_Light_Right State: ActionState parameter == 23
    // =========================================================================================
    Hit_Light_Forward = 20,     // 포이즈 유지 — 정면 경량 피격 (기존 Attack_Right_01 임시 대체)
    Hit_Light_Backward = 21,    // 포이즈 유지 — 후면 경량 피격 (기존 Attack_Left_01 임시 대체)
    Hit_Light_Left = 22,        // 포이즈 유지 — 좌측 경량 피격 (기존 Stagger_Right 임시 대체)
    Hit_Light_Right = 23,       // 포이즈 유지 — 우측 경량 피격 (기존 Stagger_Left 임시 대체)

    // --- 특수기 및 방어 기술 (Special Combat) [24 ~ 29] ---
    Shield_Bash = 21,           // ⚠️ 방패 밀치기 — Hit_Light_Right(23)와 번호 겹침 주의
                                //    Shield_Bash를 25로 이동하여 충돌 해소 권장
                                //    (기존 코드 호환 유지를 위해 현재값 보존)

    // =========================================================================================
    // ⚠️ 번호 충돌 경고: Shield_Bash = 21 vs Hit_Light_Backward = 21
    // Shield_Bash는 기존 값 21을 사용하고 있습니다.
    // Hit_Light_Backward = 21과 충돌합니다.
    // 해결책: Hit_Light_Backward를 사용할 경우 Shield_Bash를 Shield_Bash = 25로 변경하고
    //         관련 AnimatorController State 파라미터를 25로 갱신하세요.
    // 우선순위: 현재는 기존 코드 호환을 위해 기존 값(21)을 유지합니다.
    // =========================================================================================

    Parry_Counter = 22,         // 패링 성공 시 카운터 (또는 몬스터 전용 카운터)
    Guard_01 = 23,              // [컴파일 에러 방어] 방어 애니메이션 재생용 ID

    // --- 회피 및 이동 액션 (Evasion & Locomotion) [30 ~ 49] ---
    Roll_Forward = 30,
    Back_Step = 31,
    Jump = 32,

    // --- 상호작용 및 시스템 (Interaction) [50 ~ 99] ---
    Item_Grab = 50,
    Weapon_Swap = 51,

    // [P0-03 신규 추가] 엇박자 조작 실패 (절기) 페널티 모션
    Fumble_Stagger = 99,

    // --- 피격 리액션 (Hit Reactions) [100 ~ 199] ---
    // =========================================================================================
    // ⚠️ angleHitFrom 경계 데드존 경고:
    //
    // TakeDamageEffect.PlayDirectionalBasedDamagedAnimation()의 방향 판정에서
    // float 타입 angleHitFrom의 경계값 사이에 데드존이 존재합니다.
    //
    //   정면 피격 범위:  145 ~ 180  또는  -145 ~ -180
    //   좌측 피격 범위: -144 ~ -45
    //
    //   → -144.1 ~ -144.9 구간: 어느 if 분기에도 해당하지 않아 기본값(Stagger_Backward) 유지
    //   → 동일하게 우측 경계(44.1 ~ 44.9)도 동일 문제
    //
    // 수정 방법 (TakeDamageEffect.cs):
    //   기존: angleHitFrom >= -144 && angleHitFrom <= -45
    //   수정: angleHitFrom >= -145 && angleHitFrom <= -45  (또는 < 를 사용한 연속 범위)
    //
    //   또는 else 구문으로 마지막 범위를 보장:
    //   else { staggerDirection = ActionID.Stagger_Left; ... }  // 나머지 모두 우측 피격
    // =========================================================================================
    Stagger_Backward = 100,    // 정면 피격 (145~180° / -145~-180°) → 뒤로 밀림
    Stagger_Left = 101,        // 우측 피격 (45~144°)               → 좌측으로 밀림
    Stagger_Right = 102,       // 좌측 피격 (-144~-45°)             → 우측으로 밀림
    Stagger_Forward = 103,     // 후면 피격 (-45~45°)               → 앞으로 밀림

    Deflected_Bounce = 110,    // 가드가 처냄(Deflect) 당해 방패가 뒤로 튕기는 모션
    Guard_Break_Stun = 111,    // Poise가 0이 되어 그로기(Stun)에 빠진 무방비 상태

    // --- 사망 및 처형 (Death & Execution) [200 ~ 299] ---
    Dead_01 = 200,             // 기본 사망 래그돌 진입점
    Dead_Execution = 201,      // 특수 처형 컷신에 의해 사망

    // =============================================================================
    // [신규] Skeleton 전용 공격 ActionID [500 ~ 599]
    // =============================================================================

    // Right 파지 기준 — 좌베기 계열
    Skeleton_Slash_Left = 500,
    Skeleton_Slash_Advance_Left = 501,
    Skeleton_Slash_360_Left = 502,

    // Left 파지 기준 — 우베기 계열 (반전)
    Skeleton_Slash_Right = 503,
    Skeleton_Slash_Advance_Right = 504,
    Skeleton_Slash_360_Right = 505,

    // 돌진 및 특수
    Skeleton_Charge = 506,
    Skeleton_Turn_To_Target = 507,
}

// =========================================================================================
// [Steam Audio 연동 신규 추가] 사운드 발원지(Emitter) 식별용 Enum
// =========================================================================================
public enum AudioLocation
{
    Root,           // 캐릭터 중심 (기본값, 피격음/신음소리 등)
    LeftFoot,       // 왼쪽 발 (발소리)
    RightFoot,      // 오른쪽 발 (발소리)
    WeaponTip       // 무기 끝 (스윙, 타격음)
}

// =========================================================================================
// [신규] ComboInput — 연계 입력 종류 (PlayerCombatManager / PlayerNetworkManager)
// =========================================================================================
public enum ComboInput
{
    None = 0,
    Backstep = 1,   // S키 → ThrustAdvance_Backstep 연계
}

// =========================================================================================
// [신규] CameraVerticalBehavior — 카메라 수직 동작 종류 (CameraStancePresetSO)
// =========================================================================================
public enum CameraVerticalBehavior
{
    Free = 0,               // 마우스 상하 자유 조작
    ElevationOnly = 1,      // 고정 Pitch 각도
    DynamicOverShoulder = 2,// 어깨 너머 동적 추적 카메라 모드
}

// =========================================================================================
// HitSparkType — HitSparkVFXData.sparkType 과 1:1 매핑
// ※ HitSparkVFXData.cs 에 중복 정의하지 마세요 (CS0101 방지)
// =========================================================================================
public enum HitSparkType
{
    Default = 0,
    LightHit = 1,
    HeavyHit = 2,
    CriticalHit = 3,
    Parry = 4,
    BlockBreak = 5,
    ArmorClash = 6,
}

// =========================================================================================
// AnimationEventTypeExtensions — VFX 계열 판별 및 HitSparkType 변환
// ※ Enums_VFX_HitSpark_Additions.cs 파일은 삭제하거나 제외하세요 (CS0101 방지)
// =========================================================================================
public static class AnimationEventTypeExtensions
{
    /// <summary>700~799 HitSpark GPU 파티클 계열인지 여부</summary>
    public static bool IsHitSparkEvent(this AnimationEventType type)
    {
        int v = (int)type;
        return v >= 700 && v < 800;
    }

    /// <summary>640~669 Blood/Gore 계열인지 여부</summary>
    public static bool IsBloodVFXEvent(this AnimationEventType type)
    {
        int v = (int)type;
        return v >= 640 && v < 670;
    }

    /// <summary>600~899 전체 VFX/Camera 계열인지 여부</summary>
    public static bool IsVisualEvent(this AnimationEventType type)
    {
        int v = (int)type;
        return v >= 600 && v < 900;
    }

    /// <summary>AnimationEventType → HitSparkType 변환</summary>
    public static HitSparkType ToHitSparkType(this AnimationEventType type)
    {
        return type switch
        {
            AnimationEventType.VFX_HitSpark_Default => HitSparkType.Default,
            AnimationEventType.VFX_HitSpark_Light => HitSparkType.LightHit,
            AnimationEventType.VFX_HitSpark_Heavy => HitSparkType.HeavyHit,
            AnimationEventType.VFX_HitSpark_Critical => HitSparkType.CriticalHit,
            AnimationEventType.VFX_HitSpark_Parry => HitSparkType.Parry,
            AnimationEventType.VFX_HitSpark_BlockBreak => HitSparkType.BlockBreak,
            AnimationEventType.VFX_HitSpark_ArmorClash => HitSparkType.ArmorClash,
            _ => HitSparkType.Default,
        };
    }
}