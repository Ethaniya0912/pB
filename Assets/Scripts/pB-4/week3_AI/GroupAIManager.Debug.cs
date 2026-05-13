// =============================================================================
// GroupAIManager.Debug.cs  |  A4 Morale 시뮬 ContextMenu + A5 Print Diag
// Layer  : Cyber (Debug 영역)
// Namespace: TDA.PB4.AI
//
// 항목: A4 + A5 (Wk3 잔여 마무리, 5월 11일 협의)
//
// 적용 절차:
//   1. 사용자 측 GroupAIManager.cs 의 line 169:
//      public class GroupAIManager : MonoBehaviour, IGroupAIInfo
//      ↓
//      public partial class GroupAIManager : MonoBehaviour, IGroupAIInfo
//      (★ 한 줄 변경 — partial 키워드만 추가)
//   
//   2. 본 파일을 같은 폴더 (Scripts/pB-4/week3_AI/) 에 배치
//
// 동작:
//   A4 — Inspector 우클릭 → 5 종 ContextMenu (Morale ±0.3 / Panic / Reset / Cycle 강제)
//   A5 — Inspector 우클릭 → Print Diag (등록 상태 / 정책 / 멤버 콘솔 출력)
// =============================================================================
using System.Text;
using UnityEngine;

namespace TDA.PB4.AI
{
    public partial class GroupAIManager
    {
        // ════════════════════════════════════════════════════════
        // A4 — Morale 시뮬 ContextMenu
        // ════════════════════════════════════════════════════════
        
        [ContextMenu("★ Wk3 A4/Morale 시뮬 — Drop -0.3")]
        private void SimulateMoraleDrop()
        {
            float prev = currentMorale;
            currentMorale = Mathf.Clamp01(currentMorale - 0.3f);
            Log(COL_MORALE, "MoraleSim",
                $"[A4 시뮬] Drop -0.3: {prev:F3} → <b>{currentMorale:F3}</b>");
            PushEvent("MoraleSim", $"디버그 Drop -0.3 ({prev:F2} → {currentMorale:F2})",
                new Color(1.0f, 0.55f, 0.4f));
        }
        
        [ContextMenu("★ Wk3 A4/Morale 시뮬 — Boost +0.3")]
        private void SimulateMoraleBoost()
        {
            float prev = currentMorale;
            currentMorale = Mathf.Clamp01(currentMorale + 0.3f);
            Log(COL_MORALE, "MoraleSim",
                $"[A4 시뮬] Boost +0.3: {prev:F3} → <b>{currentMorale:F3}</b>");
            PushEvent("MoraleSim", $"디버그 Boost +0.3 ({prev:F2} → {currentMorale:F2})",
                new Color(0.4f, 1.0f, 0.55f));
        }
        
        [ContextMenu("★ Wk3 A4/Morale 시뮬 — Trigger Panic Chain")]
        private void SimulatePanicChain()
        {
            if (members == null || members.Count == 0)
            {
                LogW("[A4 시뮬] PanicChain — 멤버 0 명. 시뮬 불가.");
                return;
            }
            
            // 사기 강제 하락 (PANIC 임계 진입 유도)
            float prev = currentMorale;
            currentMorale = 0.15f;   // 임계 미만으로 강제
            
            Log(COL_MORALE, "MoraleSim",
                $"[A4 시뮬] PanicChain: 사기 강제 {prev:F3} → <b>{currentMorale:F3}</b> " +
                $"(임계 미만 → BT 의 패닉 분기 트리거)");
            
            PushEvent("MoraleSim", $"디버그 PanicChain 시뮬 ({prev:F2} → {currentMorale:F2})",
                new Color(1.0f, 0.3f, 0.3f));
            
            // 각 멤버 MobAIBrain 의 패닉 트리거 (있으면)
            int triggered = 0;
            foreach (var m in members)
            {
                if (m == null || m.brain == null) continue;
                // MobAIBrain.SimulatePanicTrigger 호출 — 본 매니저 측 partial 에서 정의
                m.brain.SendMessage("SimulatePanicTrigger",
                    SendMessageOptions.DontRequireReceiver);
                triggered++;
            }
            
            Log(COL_MORALE, "MoraleSim",
                $"[A4 시뮬] {triggered}/{members.Count} 명 패닉 트리거 호출 완료");
        }
        
        [ContextMenu("★ Wk3 A4/Morale 시뮬 — Reset to 1.0")]
        private void ResetMorale()
        {
            float prev = currentMorale;
            currentMorale = 1.0f;
            Log(COL_MORALE, "MoraleSim",
                $"[A4 시뮬] Reset: {prev:F3} → <b>{currentMorale:F3}</b>");
            PushEvent("MoraleSim", $"디버그 Reset ({prev:F2} → 1.00)",
                new Color(0.5f, 1.0f, 0.5f));
        }
        
        [ContextMenu("★ Wk3 A4/Morale 시뮬 — Force Update Cycle")]
        private void ForceMoraleUpdateCycle()
        {
            Log(COL_MORALE, "MoraleSim",
                "[A4 시뮬] 강제 UpdateMorale 사이클 (1 초 대기 없이 즉시 호출)");
            UpdateMorale();
        }
        
        // ════════════════════════════════════════════════════════
        // A5 — Print Diag ContextMenu
        // ════════════════════════════════════════════════════════
        
        [ContextMenu("★ Wk3 A5/Print Diagnostics")]
        public void PrintDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"═══════ GroupAIManager Diagnostics ═══════");
            sb.AppendLine($"  GameObject     : {gameObject.name}");
            sb.AppendLine($"  Active         : {isActiveAndEnabled}");
            sb.AppendLine();
            sb.AppendLine($"  ── 정책 바인딩");
            sb.AppendLine($"  policy         : {(policy != null ? policy.GetType().Name : "<null>")}");
            sb.AppendLine($"  policySO       : {(policySO != null ? policySO.name : "<null>")}");
            sb.AppendLine();
            sb.AppendLine($"  ── 상태 변수");
            sb.AppendLine($"  currentMorale  : {currentMorale:F3} ({MoraleLabel(currentMorale)})");
            sb.AppendLine($"  combatElapsed  : {combatElapsedTime:F2} s");
            sb.AppendLine($"  members        : {(members != null ? members.Count : 0)} 명");
            sb.AppendLine();
            sb.AppendLine($"  ── 토큰 / 사이클 타이머");
            sb.AppendLine($"  moraleTimer    : {moraleTimer:F2} / 1.00 s");
            
            if (members != null && members.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  ── 멤버 상태");
                int aliveCount = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (m == null) continue;
                    bool alive = m.brain != null;
                    if (alive) aliveCount++;
                    sb.AppendLine($"    [{i}] {(alive ? "✓" : "✗")} " +
                                  $"brain={(m.brain != null ? m.brain.name : "<null>")}");
                }
                sb.AppendLine($"  생존 멤버      : {aliveCount}/{members.Count}");
            }
            
            sb.AppendLine("══════════════════════════════════════════");
            Debug.Log(sb.ToString());
        }
        
        [ContextMenu("★ Wk3 A5/Print Members Only")]
        public void PrintMembersOnly()
        {
            if (members == null || members.Count == 0)
            {
                Debug.Log("[GroupAIManager] members = 0 명");
                return;
            }
            
            var sb = new StringBuilder();
            sb.AppendLine($"═══ GroupAIManager Members ({members.Count}) ═══");
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m == null) { sb.AppendLine($"  [{i}] <null>"); continue; }
                sb.AppendLine($"  [{i}] brain={(m.brain != null ? m.brain.name : "<null>")} " +
                              $"role={m.currentRole} hasAttackToken={m.hasAttackToken}");
            }
            Debug.Log(sb.ToString());
        }
        
        // ════════════════════════════════════════════════════════
        // 헬퍼 — Morale 레이블 (Log 컬러 분기용, 본체에 이미 있으면 중복 X)
        // ════════════════════════════════════════════════════════
        
        // ※ 만약 본체에 MoraleLabel 이 있으면 본 메서드 삭제 (중복 정의 에러)
        // 본체에서 MoraleLabel 메서드 사용 중 (line 1261 부근). 본체 정의 그대로 사용.
        // 본 partial 에서는 별도 정의 X.
    }
}
