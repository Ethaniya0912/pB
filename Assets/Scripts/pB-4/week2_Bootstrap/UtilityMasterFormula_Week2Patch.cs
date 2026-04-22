// =============================================================================
// UtilityMasterFormula_Week2Patch.cs  |  pB-4 Week 2 — Day 1 T1.4 패치
// 역할: 기존 UtilityMasterFormula.cs에 SetConfig 메서드 추가 (Bootstrapper DI용).
//       partial class로 작성하여 기존 파일 수정 없이 확장.
//
// ⚠ 이 파일이 동작하려면 UtilityMasterFormula.cs의 클래스 선언이
//     public partial class UtilityMasterFormula : MonoBehaviour
//   로 수정되어야 함 (partial 키워드 추가).
//
// 수정 방법 (UtilityMasterFormula.cs 109번째 줄):
//   기존: public class UtilityMasterFormula : MonoBehaviour
//   변경: public partial class UtilityMasterFormula : MonoBehaviour
//
// 필드 접근 제약:
//   원본 actionConfigs / modifierRules 가 private이면 partial class에서 접근 불가.
//   원본이 [SerializeField] private이므로 접근 가능하도록 아래 두 필드를 internal로 변경 필요.
//   또는 본 파일 내 별도 필드 사용 후 SetConfig가 내부적으로 값 교체.
//
// ⭐ 이 파일은 partial class + 원본 파일 수정이 필요하므로 작업 순서:
//   (1) UtilityMasterFormula.cs 109번째 줄의 "public class"를 "public partial class"로
//   (2) 117/123번째 줄의 [SerializeField] private를 [SerializeField] internal로 변경
//        (partial class가 private 필드에 접근하도록)
//   (3) 본 파일을 Scripts/pB-4/week2_AI/ 폴더에 추가
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using TDA.PB4.Data;

namespace TDA.PB4.AI
{
    public partial class UtilityMasterFormula
    {
        [Header("━━━ Week 2 T1.4 외부 설정 (선택) ━━━━")]

        [Tooltip("UtilityActionConfigSO .asset. 지정되면 SetConfig로 Awake 시 하드코딩 덮어쓰기.")]
        [SerializeField] private UtilityActionConfigSO externalConfig;

        /// <summary>외부 SO로부터 actions/modifiers 로드. Bootstrapper가 호출.</summary>
        /// <remarks>
        /// 주의: 이 메서드는 원본 actionConfigs와 modifierRules 필드에 접근해야 함.
        ///       원본 필드가 private이면 internal로 변경 필요.
        /// </remarks>
        public void SetConfig(UtilityActionConfigSO config)
        {
            if (config == null)
            {
                Debug.LogError($"[UtilityMasterFormula] {name}: SetConfig에 null 전달");
                return;
            }

            externalConfig = config;
            LoadFromConfig(config);
        }

        private void LoadFromConfig(UtilityActionConfigSO config)
        {
            // 원본 필드 actionConfigs / modifierRules에 값 복사.
            // 주의: 필드가 internal이어야 접근 가능.
            actionConfigs.Clear();
            actionConfigs.AddRange(config.actions);

            modifierRules.Clear();
            modifierRules.AddRange(config.modifiers);

            Debug.Log($"[UtilityMasterFormula] {name}: {config.actions.Count}개 action + " +
                      $"{config.modifiers.Count}개 modifier 로드됨 (from {config.name})");
        }

        /// <summary>현재 설정 요약 반환.</summary>
        public string GetConfigSummary()
        {
            return $"[Formula] actions={actionConfigs.Count}, modifiers={modifierRules.Count}" +
                   (externalConfig != null ? $", SO={externalConfig.name}" : ", hardcoded");
        }
    }
}
