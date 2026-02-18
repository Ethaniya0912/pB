/*
    ================================================================================
    [File]          BiomeTrigger.cs
    [Role]          Environment - 물리적 공간 기반 바이옴 전환 트리거
    [Version]       2.1 (NGO 2.0 Authority Fixed)
    [Last Updated]  2026.02.12
    [Description]   
        - 플레이어가 Collider에 진입하면 서버를 통해 전역 바이옴 전환을 수행합니다.
        - 비트 연산을 통한 빠르고 안전한 레이어 마스크 감지를 구현했습니다.
    ================================================================================
*/

using UnityEngine;
using Unity.Netcode;
using SG;

[RequireComponent(typeof(BoxCollider))]
public class BiomeTrigger : MonoBehaviour
{
    [Header("Biome Settings")]
    [Tooltip("이 구역에 진입했을 때 적용될 바이옴 ID")]
    public int targetBiomeID = 0;

    [Tooltip("이 구역의 안개 밀도 오버라이드 (선택적 구현용)")]
    public float fogDensityOverride = -1f;

    [Header("Trigger Settings")]
    [Tooltip("감지할 플레이어 레이어")]
    public LayerMask detectionLayer;

    private void OnTriggerEnter(Collider other)
    {
        // 1. 레이어 필터링 (비트 연산의 안전성 강화)
        if (((1 << other.gameObject.layer) & detectionLayer.value) == 0) return;

        // 2. 플레이어 컴포넌트 확인
        CharacterManager character = other.GetComponent<CharacterManager>();
        if (character == null || character.characterNetworkManager == null) return;

        // 3. 로컬 클라이언트 피드백
        if (character.IsOwner)
        {
            // 예: 로컬 UI 표시나 전용 사운드 재생
        }

        // 4. 네트워크 상태 동기화 요청
        // 서버인 경우 직접 상태를 변경하고, 클라이언트인 경우 서버에 RPC를 보냅니다.
        if (NetworkManager.Singleton.IsServer)
        {
            if (BiomeStateManager.Instance != null && BiomeStateManager.Instance.currentBiomeID.Value != targetBiomeID)
            {
                BiomeStateManager.Instance.currentBiomeID.Value = targetBiomeID;
                Debug.Log($"[BiomeTrigger] Server updated Biome to {targetBiomeID}");
            }
        }
        else if (character.IsOwner)
        {
            // 클라이언트 소유의 캐릭터가 진입했음을 서버에 알림
            if (BiomeStateManager.Instance != null && BiomeStateManager.Instance.currentBiomeID.Value != targetBiomeID)
            {
                BiomeStateManager.Instance.RequestBiomeChangeServerRpc(targetBiomeID);
            }
        }
    }

    private void OnDrawGizmos()
    {
        // 에디터 가시성 확보용 기즈모
        Gizmos.color = new Color(0, 1, 0, 0.2f);
        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}