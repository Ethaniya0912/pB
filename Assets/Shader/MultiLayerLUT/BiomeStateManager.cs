/*
    ================================================================================
    [File]          BiomeStateManager.cs
    [Role]          Network Manager - 바이옴 및 전역 상태 동기화
    [Version]       2.1 (NGO 2.0 Verified)
    ================================================================================
*/

using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class BiomeStateManager : NetworkBehaviour
{
    public static BiomeStateManager Instance;

    [Header("Network Sync")]
    public NetworkVariable<int> currentBiomeID = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Visual Settings")]
    public float transitionDuration = 3.0f;
    public float nearFarThreshold = 15.0f;
    private float _currentTransition = 1.0f;

    [Header("Global Weight Overrides")]
    [Range(0, 1)] public float enemyStatusWeight = 0f;
    [Range(0, 1)] public float poiStatusWeight = 0f;
    [Range(0, 1)] public float playerStatusWeight = 0f;

    private float _localMadness = 0f;

    [Header("Debug")]
    public bool showDebugGUI = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        currentBiomeID.OnValueChanged += (oldVal, newVal) =>
        {
            StartDimensionShift();
        };
    }

    private void Update()
    {
        Shader.SetGlobalFloat("_TransitionFactor", _currentTransition);
        Shader.SetGlobalFloat("_GlobalMadness", _localMadness);
        Shader.SetGlobalFloat("_DistanceLimit", nearFarThreshold);

        Shader.SetGlobalFloat("_EnemyStatusWeight", enemyStatusWeight);
        Shader.SetGlobalFloat("_POIStatusWeight", poiStatusWeight);
        Shader.SetGlobalFloat("_PlayerStatusWeight", playerStatusWeight);
    }

    public void SetLocalMadness(float value)
    {
        _localMadness = Mathf.Clamp01(value);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestBiomeChangeServerRpc(int newID)
    {
        if (currentBiomeID.Value != newID)
        {
            currentBiomeID.Value = newID;
        }
    }

    public void StartDimensionShift()
    {
        StopAllCoroutines();
        StartCoroutine(TransitionRoutine());
    }

    private IEnumerator TransitionRoutine()
    {
        _currentTransition = 0f;
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float duration = Mathf.Max(0.01f, transitionDuration);
            _currentTransition = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        _currentTransition = 1.0f;
    }

    private void OnGUI()
    {
        if (!showDebugGUI) return;
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Box("Biome Debugger");
        GUILayout.Label($"Biome ID: {currentBiomeID.Value}");
        GUILayout.Label($"Transition: {_currentTransition:F2}");
        GUILayout.Label($"Local Madness: {_localMadness:F2}");
        if (IsServer && GUILayout.Button("Next Biome")) currentBiomeID.Value++;
        GUILayout.EndArea();
    }
}