// =============================================================================
// MobAIBrain.Debug.cs  |  A4 개별 Mob 패닉 트리거 + Print Diag
// Layer  : Physics (Mob Behaviour Debug)
// Namespace: TDA.PB4.AI.Mob
//
// 항목: A4 (Wk3 잔여 마무리)
//
// 적용 절차:
//   1. 사용자 측 MobAIBrain.cs 의 line 50:
//      public class MobAIBrain : BaseAIBrain
//      ↓
//      public partial class MobAIBrain : BaseAIBrain
//      (★ 한 줄 변경 — partial 키워드만 추가)
//   
//   2. 본 파일을 같은 폴더 (Scripts/pB-4/week1_AI_MobAI/) 에 배치
//
// 주의:
//   fear 필드가 BaseAIBrain 의 protected/public 멤버임을 가정.
//   만약 private 이라면 BaseAIBrain 측에서 가시성 조정 필요 (protected 권장).
// =============================================================================
using UnityEngine;

namespace TDA.PB4.AI.Mob
{
    public partial class MobAIBrain
    {
        // ════════════════════════════════════════════════════════
        // A4 — 개별 Mob 패닉 트리거 (GroupAIManager.SimulatePanicChain 에서 호출)
        // ════════════════════════════════════════════════════════
        
        /// <summary>
        /// GroupAIManager.SimulatePanicChain 이 SendMessage 로 호출.
        /// fear 를 1.0 으로 강제 → u_flee = 1.0 → BT 의 도주 분기 트리거.
        /// </summary>
        [ContextMenu("★ Wk3 A4/Mob 패닉 트리거")]
        private void SimulatePanicTrigger()
        {
            float prev = fear;
            fear = 1.0f;
            Debug.Log($"[MobAIBrain] {name} 패닉 시뮬 — fear: {prev:F3} → <b>1.000</b> " +
                      $"(u_flee 최대화 → BT 도주 분기 진입)");
        }
        
        [ContextMenu("★ Wk3 A4/Mob fear Reset")]
        private void SimulateFearReset()
        {
            float prev = fear;
            // 팩션 기본값 복원 (Awake 의 초기화 로직 참조)
            if (factionData != null)
                fear = Mathf.Clamp01(1.0f - factionData.aggression / 10f);
            else
                fear = 0.5f;   // factionData 없으면 중간값
            
            Debug.Log($"[MobAIBrain] {name} fear Reset — {prev:F3} → <b>{fear:F3}</b>");
        }
        
        [ContextMenu("★ Wk3 A5/Mob Print Diag")]
        private void PrintMobDiag()
        {
            string factionName = factionData != null ? factionData.name : "<null>";
            string tierName = tierData != null ? tierData.name : "<null>";
            
            Debug.Log($"═══ MobAIBrain Diag: {name} ═══\n" +
                      $"  factionData = {factionName}\n" +
                      $"  tierData    = {tierName}\n" +
                      $"  fear        = {fear:F3}\n" +
                      $"  fatigue     = {fatigue:F3}\n" +
                      $"  u_attack    = {u_attack:F3}\n" +
                      $"  u_flee      = {u_flee:F3}\n" +
                      $"  u_patrol    = {u_patrol:F3}\n" +
                      $"  aggressionThreshold = {aggressionThreshold:F2}\n" +
                      $"  fleeThreshold       = {fleeThreshold:F2}");
        }
    }
}
