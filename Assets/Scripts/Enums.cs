using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    #endregion

    #region [300 ~ 599] 오디오 및 사운드 피드백 (Audio / SFX)

    // --- 발소리 (Footsteps - Locomotion 연동) ---
    PlayFootstep_L = 300,       // 왼쪽 발 접지 사운드 (걷기, 뛰기 공통)
    PlayFootstep_R = 301,       // 오른쪽 발 접지 사운드
    PlayFootstep_Drag_L = 302,  // 전투 Strafe 중 왼쪽 발을 질질 끄는 사운드
    PlayFootstep_Drag_R = 303,  // 전투 Strafe 중 오른쪽 발을 질질 끄는 사운드
    PlayFootstep_Pivot = 304,   // 제자리 턴(Turn 90) 시 축이 되는 발바닥 마찰음

    // --- 무기 스윙 및 마찰 (Weapon Swings) ---
    PlaySFX_Swing_Light = 310,  // 가벼운 무기 (단검 등) 휘두르는 소리
    PlaySFX_Swing_Heavy = 311,  // 무거운 무기 (대검 등) 공기 가르는 소리
    PlaySFX_Swing_Blunt = 312,  // 둔기 붕붕거리는 소리
    PlaySFX_Weapon_Draw = 313,  // 무기 꺼낼 때 마찰음 (스르릉)
    PlaySFX_Weapon_Sheathe = 314, // 무기 집어넣을 때 소리

    // --- 타격 및 살점 (Impact & Gore) ---
    PlaySFX_Impact_Flesh = 330, // 살덩이 베이는 소리 (질척한 소리)
    PlaySFX_Impact_Bone = 331,  // 뼈가 부러지거나 절단되는 타격음 (크런치)
    PlaySFX_Impact_Armor = 332, // 금속 갑옷을 때리는 둔탁한 쇳소리
    PlaySFX_Parry_Clang = 333,  // 패링 성공 시 날카로운 쇳소리 (고주파)
    PlaySFX_Guard_Thud = 334,   // 가드 위로 막았을 때의 둔탁한 소리

    // --- 보이스 및 호흡 (Voices & Breathing) ---
    PlayVoice_Attack = 350,     // 공격 기합 소리
    PlayVoice_Stagger = 351,    // 피격 시 고통스러운 신음 소리 (Stagger 연동)
    PlayVoice_Death = 352,      // 사망 단말마
    PlayVoice_Breathe = 353,    // 대기(Idle) 중 희박한 확률로 나오는 거친 숨소리

    // --- 시스템 및 상태 이상 (System UI/UX) ---
    PlaySFX_Stamina_Out = 390,  // 스태미나 고갈 시 헐떡임/경고음
    PlaySFX_Stamina_Full = 391, // 스태미나 회복 완료 (UI 연동)
    PlaySFX_Stamina_Recovered = 392,    // [P1] 스태미나 회복 완료 시점 (UI/이펙트 트리거용 2차 신호)
    PlaySFX_Stamina_Exhausted = 393,     // 스태미나 완전 소진 시점 (UI/이펙트 트리거용 2차 신호)
    PlaySFX_UI_LockOn = 394,    // 락온 성공 시 특유의 레트로 사운드

    #endregion

    #region [600 ~ 899] 시각 효과 및 카메라 연출 (Visuals, Camera & VFX)

    // --- 화면 흔들림 및 카메라 연출 (Camera Kicks) ---
    CameraShake_Light = 600,    // 약한 타격/피격 시 미세한 흔들림
    CameraShake_Heavy = 601,    // 강공격 타격/피격 시 강한 지진 효과
    CameraShake_Roll = 602,     // 구르기 후 바닥에 닿을 때 카메라 충격
    CameraKick_FOV_In = 603,    // 강공격 차지 시 FOV를 좁혀 집중감 부여
    CameraKick_FOV_Out = 604,   // 타격 임팩트 시 FOV를 튕겨내어 타격감 폭발

    // --- 무기 검기 및 궤적 (Weapon Trails) ---
    Trail_Enable_15fps = 620,   // [Dual Framerate] 레트로 감성의 15fps 스텝 검기 켜기
    Trail_Disable_15fps = 621,  // 15fps 검기 끄기
    Trail_Enable_Smooth = 622,  // 부드러운 일반 검기 켜기
    Trail_Disable_Smooth = 623, // 일반 검기 끄기
    Spawn_Spark_Clash = 624,    // 패링/가드 충돌 시 불꽃 스파크 60fps 파티클 스폰

    // --- 혈흔 및 고어 이펙트 (Blood & Gore) ---
    VFX_Blood_Splatter = 640,   // 기본 피 튀김 이펙트 (60fps)
    VFX_Blood_Directional = 641, // 8방향 궤적에 맞춰 흩뿌려지는 혈흔
    VFX_Blood_Pool = 642,       // 사망/절단 시 바닥에 고이는 웅덩이 생성
    VFX_Gore_Chunks = 643,      // 신체 훼손 시 튀어오르는 고기 조각들

    // --- 렌더링 및 화면 필터 (Screen FX) ---
    ScreenFX_Whiteout_1F = 670, // [Hit Feedback] 타격 성공 시 1프레임 전체 화면 화이트아웃
    ScreenFX_Hit_Vignette = 671,// 피격 시 화면 테두리 붉은 피네팅(Vignette) 효과 점멸
    ScreenFX_Chromatic = 672,   // 공포 상황이나 그로기 시 색수차(Chromatic Aberration) 펌핑

    // --- 환경 상호작용 (Environment) ---
    VFX_Dust_Footstep = 690,    // 뛰거나 구를 때 발밑 먼지 파티클
    VFX_Ground_Crater = 691,    // 대검으로 땅을 내리찍었을 때 데칼/파편 생성

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

// 데미지 기반 공격 타입을 계산하는데 활용.
public enum AttackType
{
    LightAttack01, LightAttack02, HeavyAttack01, HeavyAttack02, ChargeAttack01, ChargeAttack02,
}

public enum LockOnDirection { Left, Right, None }

public enum SwithchWeaponSide { Left, Right }

public enum CookingState
{
    Empty,      // 비어있음
    Raw,        // 재료 투입됨 (조리 전)
    Cooking,    // 조리 중 (끓는 중/ 굽는 중)
    Cooked,     // 조리 완료
    Burnt       // 탐 (굽기 전용)
}

public enum CookingStationType
{
    Pot,        // 냄비 (끓이기)
    Grill,      // 석쇠 (굽기)
}

public enum GameState
{
    Normal, Chase, LockOn, Inventory, Table, Cooking, CinematicFocus,
}

// 장착 슬롯 정의
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
// [Funnel 아키텍처 연동] 액션 및 리액션 라우팅 인덱스 (ActionID)
// 체계적으로 10단위 그룹핑하여 번호를 부여. (pC 단계의 8방향 확장을 대비함)
// =========================================================================================
public enum ActionID
{
    None = 0,

    // --- 전투 및 기본 공격 (Combat) [1 ~ 19] ---
    // (pC 단계 8방향 적용을 위해 1~8번 대역을 기본 베기로 예약. 현재는 좌/우만 사용)
    Attack_Right_01 = 1,      // 우에서 좌로 베기
    Attack_Left_01 = 2,       // 좌에서 우로 베기

    // --- 특수기 및 방어 기술 (Special Combat) [20 ~ 29] ---
    Shield_Bash = 21,         // 방패 밀치기 (포이즈 붕괴)
    Parry_Counter = 22,       // 패링 성공 시 카운터 (또는 몬스터 전용 카운터)

    // --- 회피 및 이동 액션 (Evasion & Locomotion) [30 ~ 49] ---
    Roll_Forward = 30,
    Back_Step = 31,
    Jump = 32,

    // --- 상호작용 및 시스템 (Interaction) [50 ~ 99] ---
    Item_Grab = 50,
    Weapon_Swap = 51,

    // --- 피격 리액션 (Hit Reactions - onHit Trigger 연동) [100 ~ 199] ---
    // (현재 3방향 피격 시스템 적용)
    Stagger_Backward = 100,   // 정면에서 타격받아 뒤로 밀림
    Stagger_Left = 101,       // 우측에서 타격받아 좌측으로 밀림
    Stagger_Right = 102,      // 좌측에서 타격받아 우측으로 밀림

    // (특수 피격 상태)
    Deflected_Bounce = 110,   // 가드가 처냄(Deflect) 당해 방패가 뒤로 튕기는 모션
    Guard_Break_Stun = 111,   // Poise가 0이 되어 그로기(Stun)에 빠진 무방비 상태

    // --- 사망 및 처형 (Death & Execution) [200 ~ 299] ---
    Dead_01 = 200,            // 기본 사망 래그돌 진입점
    Dead_Execution = 201      // 특수 처형 컷신에 의해 사망
}