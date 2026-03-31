// =============================================================================
// FinisherData.cs  |  TDA Project
// Layer  : Data (ScriptableObject)
// 경로   : Assets/Scripts/Data/FinisherData.cs
//
// 역할:
//   피니셔(페어 애니메이션) 한 종류에 대한 모든 데이터를 담는 SO입니다.
//   PlayerExecutionManager 가 BeginExecution() 시점에 이 데이터를 읽어
//   victim 워핑, Animator 동시 재생을 처리합니다.
//
// 사용 방법:
//   1. Project 우클릭 → Create → TDA → Finisher → Finisher Data
//   2. attackerClip / victimClip 에 애니메이션 클립 연결
//   3. victimOffset 은 에디터 툴(FinisherDataEditor) 버튼으로 자동 추출하거나
//      씬 더미 오브젝트로 시각 보정 후 수동 입력
//   4. PlayerExecutionManager 인스펙터의 finisherDataList 에 SO 추가
//
// victimOffset 추출 방법:
//   Generic 리그: 에디터 툴로 victim 클립 0프레임 RootT.x/y/z 커브 자동 파싱
//   Humanoid 리그: 씬 더미 오브젝트로 시각 보정 후 수동 기입
//
// 아키텍처 규약:
//   - 오프셋 수치를 코드에 하드코딩 금지. 모든 수치는 이 SO 에서만 관리합니다.
//   - targetTier 열거형으로 QTE 단계 분기 — combatLevel 직접 비교 하드코딩 금지.
// =============================================================================
using UnityEngine;

namespace TDA.Character
{
    /// <summary>
    /// 처형 대상 캐릭터의 종류 분류 열거형.
    /// PlayerExecutionManager.ResolveFinisherData() / ResolveQTEPhases() 에서
    /// 대상 캐릭터의 combatLevel 과 매칭하여 적절한 데이터를 선택합니다.
    ///
    /// [개선] 기존 combatLevel >= 8 하드코딩 → 이 열거형으로 교체.
    /// 향후 AICharacterManager 에 tier 필드가 직접 추가되면
    /// DetermineTargetTier() 메서드만 수정하면 됩니다.
    /// </summary>
    public enum ExecutionTargetTier
    {
        Humanoid = 0,   // 일반 인간형 적 (combatLevel < 5)
        LargeMonster = 1,   // 대형 몬스터   (combatLevel 5~7)
        Boss = 2,   // 보스           (combatLevel >= 8)
    }

    [CreateAssetMenu(fileName = "FinisherData_New",
                     menuName = "TDA/Finisher/Finisher Data")]
    public class FinisherData : ScriptableObject
    {
        // =====================================================================
        // 애니메이션 클립
        // =====================================================================
        [Header("Animation Clips")]
        [Tooltip("공격자(플레이어)가 재생할 처형 애니메이션 클립.\n" +
                 "BeginExecution() 에서 attacker.animator.Play(attackerClip.name) 으로 호출됩니다.")]
        public AnimationClip attackerClip;

        [Tooltip("피격자(victim)가 재생할 처형 애니메이션 클립.\n" +
                 "BeginExecution() 에서 victim.animator.Play(victimClip.name) 으로 호출됩니다.")]
        public AnimationClip victimClip;

        // =====================================================================
        // 위치 동기화 오프셋
        // =====================================================================
        [Header("Victim Warp Offset (Attacker 로컬 좌표 기준)")]
        [Tooltip("BeginExecution() 시점에 victim 을 이동시킬 attacker 로컬 좌표.\n\n" +
                 "설정 방법:\n" +
                 "  Generic 리그: 에디터 툴(FinisherDataEditor) 버튼으로 클립 0프레임\n" +
                 "                RootT.x/y/z 커브를 파싱해 자동 기입.\n" +
                 "  Humanoid 리그: 씬 더미 오브젝트로 시각 보정 후 수동 입력.\n\n" +
                 "기본값 (0, 0, 1.2) 로 시작 후 씬 뷰에서 확인하며 조정합니다.")]
        public Vector3 victimOffset = new Vector3(0f, 0f, 1.2f);

        [Tooltip("victim 의 회전 오프셋 (Y축, 도).\n" +
                 "attacker 를 마주보게 하려면 180 (기본), 같은 방향이면 0.")]
        [Range(-180f, 180f)]
        public float victimYawOffset = 180f;

        [Tooltip("워핑 보간 시간(초). 0 이면 즉시 텔레포트.\n" +
                 "현재는 즉시 텔레포트(0)를 기본으로 하며, 향후 코루틴 보간으로 확장 가능합니다.")]
        [Range(0f, 1f)]
        public float warpDuration = 0f;

        // =====================================================================
        // 분류
        // =====================================================================
        [Header("Classification")]
        [Tooltip("이 피니셔가 적용될 대상의 티어.\n" +
                 "PlayerExecutionManager.ResolveFinisherData() 에서 combatLevel 과 매칭합니다.\n\n" +
                 "  Humanoid     : combatLevel < 5 (일반 인간형)\n" +
                 "  LargeMonster : combatLevel 5~7 (대형 몬스터)\n" +
                 "  Boss         : combatLevel >= 8 (보스)")]
        public ExecutionTargetTier targetTier = ExecutionTargetTier.Humanoid;

        // =====================================================================
        // 에디터 전용 메모
        // =====================================================================
#if UNITY_EDITOR
        [Header("Editor Memo")]
        [TextArea(2, 4)]
        [Tooltip("이 피니셔에 대한 메모 (에디터 전용, 런타임 미사용).\n" +
                 "예: '기사 처형 — 무릎 꿇림 후 목 베기', '보스 Phase1 처형 클립'")]
        public string editorMemo;
#endif
    }
}