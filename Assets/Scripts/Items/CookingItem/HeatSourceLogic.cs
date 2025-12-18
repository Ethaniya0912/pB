using UnityEngine;
using Unity.Netcode;

public class HeatSourceLogic : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private HeatSourceSO heatSourceData; // 기본 데이터(최대 온도, 연료 소모율 등)

    [Header("Network State")]
    public NetworkVariable<bool> IsTurnedOn = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> CurrentTemperature = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> CurrentFuel = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CurrentTemperature.Value = 0f;
        }
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
}