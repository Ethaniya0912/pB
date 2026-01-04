using UnityEngine;
using Unity.Netcode;
using System;

public class HeatSourceLogic : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private HeatSourceSO heatSourceData; // 기본 데이터(최대 온도, 연료 소모율 등)
    [SerializeField] private GameObject heatZoneObject; // 트리거 콜라이더가 있는 자식 오브젝트.

    [Header("Network State")]
    public NetworkVariable<bool> IsTurnedOn = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> CurrentTemperature = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> CurrentFuel = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        // 1. 상태 변화 감지 이벤트 구독 (모든 클라이언트 및 서버에서 실행)
        IsTurnedOn.OnValueChanged += OnToggleStateChangedClientRpc;
        // 2. 초기 상태 동기화(나중에 접속한 클라이언트를 위함)
        RefreshVisualState(IsTurnedOn.Value);

        if (IsServer)
        {
            CurrentTemperature.Value = 0f;
            IsTurnedOn.OnValueChanged += OnToggleStateChangedClientRpc;
        }
    }

    public override void OnNetworkDespawn()
    {
        IsTurnedOn.OnValueChanged -= OnToggleStateChangedClientRpc;
    }


    private void Update()
    {
        if (!IsServer) return; // 로직은 서버에서만 수행

        HandleTemperatureLogic();
    }

    private void HandleTemperatureLogic()
    {
        if (IsTurnedOn.Value && CurrentFuel.Value > 0)
        {
            // 연료 소모 및 온도 상승
            CurrentFuel.Value -= heatSourceData.fuelConsumptionRate * Time.deltaTime;
            CurrentTemperature.Value = Mathf.MoveTowards(CurrentTemperature.Value, heatSourceData.maxTemperature, heatSourceData.heatPower * Time.deltaTime);

            if (CurrentFuel.Value <= 0)
            {
                CurrentFuel.Value = 0f;
                IsTurnedOn.Value = false; // 연료 고갈
            }
        }
        else
        {
            // 식는 과정
            CurrentTemperature.Value = Mathf.MoveTowards(CurrentTemperature.Value, 0f, heatSourceData.coolDownSpeed * Time.deltaTime);
        }
    }

    // 클라이언트가 열원을 켜거나 끄는 요청
    //[ServerRpc(RequireOwnership = false)]
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ToggleHeatSourceServerRpc(bool turnOn)
    {
        if (turnOn && CurrentFuel.Value > 0)
        {
            IsTurnedOn.Value = true;
        }
        else
        {
            IsTurnedOn.Value = false;
        }
    }
    [Rpc(SendTo.Everyone)]
    private void OnToggleStateChangedClientRpc(bool previousValue, bool newValue)
    {
        RefreshVisualState(newValue);
    }

    private void RefreshVisualState(bool isOn)
    {
        if (heatZoneObject != null)
        {
            heatZoneObject.SetActive(isOn);
        }
    }
}