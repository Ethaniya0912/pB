using UnityEngine;

[CreateAssetMenu(fileName = "HeatSource", menuName = "Scriptable Objects/HeatSourceSO")]
public class HeatSourceSO : ScriptableObject
{
    [Header("Heat Properties")]
    [Tooltip("열원의 이름 (예: 숯불, 용암, 마법불)")]
    public string heatSourceName;

    [Tooltip("화력 계수 (1.0 = 기본, 높을수록 조리 속도 증가)")]
    public float heatPower = 1.0f;

    [Tooltip("최대 온도")]
    public float maxTemperature = 250.0f;

    [Tooltip("연료 소모 속도 (초당 소모량)")]
    public float fuelConsumptionRate = 1.0f;

    [Tooltip("열원 식는 속도")]
    public float coolDownSpeed = 0.33f;

    [Tooltip("최대 연료 수용량")]
    public float maxFuelCapacity = 100f;

    [Tooltip("무한 열원 여부 (용암 등)")]
    public bool isInfiniteSource = false;
}
