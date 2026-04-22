// =============================================================================
// TestTrustBridgeListener.cs  |  pB-4 Day 2 T2.4 F6 임시 검증 스크립트
// 
// 역할:
//   EventBus.OnTrustTierChanged 이벤트가 실제로 발행되는지 확인.
//   TrustMatrix의 Tier 변경 → EventBus.RaiseTrustTierChanged → 이 리스너 수신 흐름 검증.
//
// 사용법:
//   1. 이 파일을 Assets/Scripts/pB-4/week2_Bootstrap/ 에 배치 (또는 적당한 위치)
//   2. Scene의 아무 GameObject에 컴포넌트 추가 (예: HumanoidBootstrapper GameObject)
//   3. Play → Skeleton의 TrustMatrix에서 ContextMenu "Set to BlindTrust (95)" 클릭
//   4. Console에 "[F6 TEST] ..." 로그 나오면 성공
//   5. 검증 완료 후 이 파일 삭제
//
// F6 성공 기준:
//   Trust Tier 변경 시 Console에 아래 3개 로그가 모두 나옴:
//   - [Trust] ⚠ 단계 변경! Cooperation → BlindTrust (TrustMatrix가 출력)
//   - [Trust] ... (이미 기존 Trust 로그)
//   - [F6 TEST] Skeleton_Humanoid_01: Cooperation(2) → BlindTrust(3)  ← 이 줄이 F6 핵심
// =============================================================================
using UnityEngine;
using TDA.PB4.Core;

public class TestTrustBridgeListener : MonoBehaviour
{
    private void OnEnable()
    {
        EventBus.OnTrustTierChanged += HandleTrustTierChanged;
        Debug.Log("[F6 TEST] 리스너 활성화 — EventBus.OnTrustTierChanged 구독 시작");
    }

    private void OnDisable()
    {
        EventBus.OnTrustTierChanged -= HandleTrustTierChanged;
        Debug.Log("[F6 TEST] 리스너 비활성화 — 구독 해제");
    }

    private void HandleTrustTierChanged(string npcId, int oldTier, int newTier)
    {
        string oldName = TierName(oldTier);
        string newName = TierName(newTier);
        Debug.Log($"[F6 TEST] {npcId}: {oldName}({oldTier}) → {newName}({newTier})");
    }

    private static string TierName(int idx)
    {
        switch (idx)
        {
            case 0: return "Hostility";
            case 1: return "Doubt";
            case 2: return "Cooperation";
            case 3: return "BlindTrust";
            default: return $"Unknown({idx})";
        }
    }
}
