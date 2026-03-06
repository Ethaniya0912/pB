/*
    ================================================================================
    [File]          MadnessController.cs
    [Role]          Client Logic - 로컬 플레이어의 광기 시각화 브릿지 (Test Version)
    [Version]       2.2 (SRP & Compile Error Fixed)
    [Last Updated]  2026.02.12
    [Description]   
        - 데이터 동기화 계층(CharacterNetworkManager)에서 광기 수치를 읽어와 
          BiomeStateManager(시각화)로 직접 전달합니다.
        - 추후 시스템 연동 시 손쉽게 확장 가능하도록 결합도를 낮췄습니다.
    ================================================================================
*/

using UnityEngine;
using Unity.Netcode;
using TDA.Character; // CharacterNetworkManager 참조를 위해 추가

[RequireComponent(typeof(CharacterNetworkManager))] // 네트워크 데이터 계층 직접 참조
public class MadnessController : NetworkBehaviour
{
    [Header("Dependencies")]
    // SRP 적용: 스탯 연산 클래스를 거치지 않고, 값을 들고 있는 NetworkManager를 직접 참조
    [SerializeField] private CharacterNetworkManager networkManager;

    [Header("Visual Feedback Settings")]
    [Tooltip("광기 수치가 셰이더에 반영되는 민감도 곡선")]
    public AnimationCurve madnessVisualCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Tooltip("광기 수치 최고조일 때의 화면 왜곡/필터 강도")]
    [Range(0f, 2f)] public float maxDistortionIntensity = 1.0f;

    [Header("Debug")]
    [SerializeField] private float _currentVisualMadness = 0f;

    private void Awake()
    {
        if (networkManager == null)
            networkManager = GetComponent<CharacterNetworkManager>();
    }

    public override void OnNetworkSpawn()
    {
        // 로컬 플레이어의 화면에만 셰이더 효과를 주입해야 하므로,
        // 오너가 아닌 경우 업데이트를 중지시켜 자원을 절약합니다.
        if (!IsOwner)
        {
            this.enabled = false;
        }
    }

    private void Update()
    {
        // 최후의 방어 코드 (네트워크 스폰 이전 등 예외 상황 대비)
        if (!IsOwner || !IsSpawned) return;

        UpdateShaderGlobals();
    }

    /// <summary>
    /// 로컬 플레이어의 네트워크 데이터를 가져와 시각화 매니저(BiomeStateManager)에 전달
    /// </summary>
    private void UpdateShaderGlobals()
    {
        // 안정성 보완: 참조 누락 시 안전하게 종료
        if (networkManager == null || BiomeStateManager.Instance == null) return;

        // 1. 현재 광기 비율 계산 (0.0 ~ 1.0)
        // 안전하게 0으로 나누기 방지 (Mathf.Max 사용)
        float maxMadness = Mathf.Max(1f, networkManager.maxMadness.Value);
        float currentMadnessVal = networkManager.currentMadness.Value;

        float ratio = Mathf.Clamp01(currentMadnessVal / maxMadness);

        // 2. 시각적 커브 평가 및 강도 곱연산
        if (madnessVisualCurve != null && madnessVisualCurve.length > 0)
        {
            _currentVisualMadness = madnessVisualCurve.Evaluate(ratio) * maxDistortionIntensity;
        }
        else
        {
            _currentVisualMadness = ratio * maxDistortionIntensity;
        }

        // 3. 글로벌 시각화 매니저에 주입
        BiomeStateManager.Instance.SetLocalMadness(_currentVisualMadness);
    }
}