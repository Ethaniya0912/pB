using UnityEngine;
using Unity.Netcode;
using SG;
using System;

public abstract class CookingStation : NetworkBehaviour
{
    [Header("Status")]
    public NetworkVariable<CookingState> currentCookingState = new NetworkVariable<CookingState>(CookingState.Empty);
    public NetworkVariable<float> cookingProgress = new NetworkVariable<float>(0f); // 0.0f ~ 1.0f

    [Header("Detection Proxies")]
    [SerializeField] private TriggerProxy heatDetectorProxy; // 바닥쪽 열원 감지기

    protected HeatSourceLogic currentHeatSource; // 감지된 열원
    protected Item currentIngredient; // 현재 올려진 아이템 (SO)
    public abstract CookingStationType StationType { get; } // 자식클래스(pot,grill)이 자신 타입 반환 설정.

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer && heatDetectorProxy != null)
        {
            heatDetectorProxy.OnProxyTriggerEnter += HandleHeatSourceEnter;
            Debug.Log("힛소스엔터구독완료");
            heatDetectorProxy.OnProxyTriggerExit += HandleHeatSourceExit;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer && heatDetectorProxy != null)
        {
            heatDetectorProxy.OnProxyTriggerEnter -= HandleHeatSourceEnter;
            heatDetectorProxy.OnProxyTriggerExit -= HandleHeatSourceExit;
        }
    }

    // Trigger로 열원 감지 (물리적 접촉 시)
    private void HandleHeatSourceEnter(Collider other)
    {
        if (other.GetComponentInParent<HeatSourceLogic>() is HeatSourceLogic heatSource)
        {
            currentHeatSource = heatSource;
            Debug.Log($"[Cooking] {gameObject.name}가 열원 위에 놓임");
        }
    }

    private void HandleHeatSourceExit(Collider other)
    {
        if (other.GetComponentInParent<HeatSourceLogic>() == currentHeatSource)
        {
                currentHeatSource = null;
        }
    }

    // 공통 로직: 상태 관리
    protected virtual void Update()
    {
        if (!IsServer) return;
        HandleCookingLogic();
    }

    protected abstract void HandleCookingLogic(); // 자식 클래스에서 구현

    // 아이템 배치 요청 (Dev B가 호출할 인터페이스)
    //[ServerRpc(RequireOwnership = false)]
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void PlaceItemServerRpc(int itemID)
    {
        // 아이템 DB에서 ID로 SO를 찾아 currentIngredient에 할당하는 로직
        currentCookingState.Value = CookingState.Raw;
    }

    // 완성된 아이템 가져가기
    //[ServerRpc(RequireOwnership = false)]
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void PickUpItemServerRpc()
    {
        // 인벤토리에 추가 로직
        currentCookingState.Value = CookingState.Empty;
        cookingProgress.Value = 0f;
    }

    public abstract void OnItemPlaced(GrabbableObject grabbable);

}