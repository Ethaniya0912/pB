// =============================================================================
// Week2ChecklistSO.cs  |  pB-4 Week 2 — Day 1 T1.2 (v2 개정)
// 역할: 25항목 체크리스트 자산. Week2ProgressTracker가 런타임으로 갱신.
//       요구사항 #8 (진척률 가시화) 의 핵심 데이터 구조.
// ⚠ week2_Tooling/ 폴더에 저장 (다른 7개 SO와 다름).
//
// [v2 코드리뷰 개정]
//   - [CL1] RelevantTotal 계산 시 runtimeStatus == ExternalDep 인 항목도 제외.
//           이전: ignoreInProgress=false인 ExternalDep 항목이 분모엔 포함, 분자엔 불포함.
//                 → 진척률 부당 하락.
//           현재: ignoreInProgress=true 이거나 runtimeStatus==ExternalDep 이면 분모에서 제외.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Tooling
{
    /// <summary>체크 항목 분류.</summary>
    public enum CheckCategory
    {
        Code,        // 파일 존재, 클래스 존재 등 컴파일 레벨
        Wired,       // 컴포넌트 부착, 이벤트 구독 등 연결 레벨
        Runtime,     // Play 모드 상태 검증
        Visual,      // Scene/Game 뷰에서 관찰 필요 (수동 검증)
        External     // 다른 팀의 산출물 필요 (지형/시나리오)
    }

    public enum CheckStatus
    {
        NotChecked = 0,  // 아직 평가 안 됨
        Passed = 1,      // 통과
        Failed = 2,      // 실패
        ExternalDep = 3  // 외부 의존성 (진척률 계산 제외)
    }

    [Serializable]
    public class ChecklistItem
    {
        [Tooltip("고유 ID. 예: 'WK2_C01'.")]
        public string id;

        [Tooltip("한국어 제목.")]
        public string title;

        [Tooltip("상세 설명.")]
        [TextArea(2, 4)] public string description;

        [Tooltip("분류.")]
        public CheckCategory category;

        [Tooltip("검증 방법 표현식. Week2ProgressTracker.EvaluateExpression이 해석.")]
        [TextArea(1, 2)] public string verifyMethod;

        [Tooltip("담당 Day. 'Day 1'/'Day 2'/... 또는 '지형팀'.")]
        public string assignedDay;

        [Tooltip("진척률 계산에서 제외 (외부 의존).")]
        public bool ignoreInProgress;

        // 런타임 상태 (에디터에서 편집 안 됨)
        [HideInInspector] public CheckStatus runtimeStatus;
        [HideInInspector] public string lastLogMessage;
        [HideInInspector] public string lastCheckTime;
    }

    [CreateAssetMenu(fileName = "Week2Checklist",
                     menuName = "pB-4/Tooling/Week2 Checklist")]
    public class Week2ChecklistSO : ScriptableObject
    {
        [Tooltip("25항목. Day 별로 점진 입력.")]
        public List<ChecklistItem> items = new List<ChecklistItem>();

        /// <summary>통과 항목 수.</summary>
        public int PassedCount
        {
            get
            {
                int count = 0;
                foreach (var item in items)
                {
                    if (item.runtimeStatus == CheckStatus.Passed) count++;
                }
                return count;
            }
        }

        /// <summary>진척률 계산에 포함되는 총 항목 수.</summary>
        /// <remarks>
        /// [CL1] 제외 조건 2가지:
        ///   1) Designer가 명시적으로 ignoreInProgress=true로 설정 (영구적 제외)
        ///   2) 런타임에 runtimeStatus == ExternalDep (동적 제외)
        /// 둘 중 하나라도 해당하면 분모에서 제외 → 진척률 왜곡 방지.
        /// </remarks>
        public int RelevantTotal
        {
            get
            {
                int count = 0;
                foreach (var item in items)
                {
                    if (item.ignoreInProgress) continue;
                    if (item.runtimeStatus == CheckStatus.ExternalDep) continue;
                    count++;
                }
                return count;
            }
        }

        /// <summary>현재 진척률 0~1.</summary>
        public float ProgressRatio => (float)PassedCount / Mathf.Max(1, RelevantTotal);

        /// <summary>디버그 요약 문자열.</summary>
        public string GetSummary()
        {
            return $"[{name}] {PassedCount}/{RelevantTotal} ({ProgressRatio:P0})";
        }

        /// <summary>특정 ID 검색.</summary>
        public ChecklistItem FindById(string id)
        {
            foreach (var item in items)
            {
                if (item.id == id) return item;
            }
            return null;
        }

        /// <summary>런타임 상태 전체 초기화 (테스트 재시작용).</summary>
        public void ResetRuntimeStatus()
        {
            foreach (var item in items)
            {
                item.runtimeStatus = CheckStatus.NotChecked;
                item.lastLogMessage = null;
                item.lastCheckTime = null;
            }
        }
    }
}
