using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;
using System;
using System.Collections.Generic;

/**
 * [Phase 2: Ultimate Diagnostic Tool - Master Version V49 Pro]
 * 1. Hardware Anchor: URP Asset의 실제 설정을 추적하여 이론적 슬롯 한계를 표시합니다.
 * 2. Atlas Awareness: Shadow Slot이 초록색임에도 빛이 꺼지는 'Atlas 정체 현상'에 대한 경고를 추가했습니다.
 * 3. Quick Access: 버튼 하나로 문제가 발생하는 URP Asset 설정으로 즉시 이동합니다.
 * 4. Stable Logic: Unity 6 (URP 17.3)의 내부 구조 변화에 완벽히 대응합니다.
 */
public class DreamcoreDiagnosticTool : EditorWindow
{/*
    private Vector2 _scrollPos;
    private bool _heartbeatToggle;
    private float _lastUpdateTime;
    private List<Light> _cachedPoints = new List<Light>();

    [MenuItem("Window/Dreamcore/Diagnostic Tool")]
    public static void ShowWindow() => GetWindow<DreamcoreDiagnosticTool>("Dreamcore Pro Troubleshooter");

    private void OnEnable() => EditorApplication.update += UpdateHeartbeat;
    private void OnDisable() => EditorApplication.update -= UpdateHeartbeat;

    private void UpdateHeartbeat() 
    {
        if (Time.realtimeSinceStartup - _lastUpdateTime > 0.2f)
        {
            _lastUpdateTime = Time.realtimeSinceStartup;
            _heartbeatToggle = !_heartbeatToggle;
            RefreshLightList();
            Repaint();
        }
    }

    private void RefreshLightList()
    {
        Light[] all = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
        List<Light> current = new List<Light>();
        foreach (var l in all) if (l.type != LightType.Directional) current.Add(l);
        Camera sc = SceneView.lastActiveSceneView?.camera;
        if (sc != null)
        {
            current.Sort((a, b) => Vector3.Distance(sc.transform.position, a.transform.position).CompareTo(Vector3.Distance(sc.transform.position, b.transform.position)));
        }
        _cachedPoints = current;
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.BeginVertical("box");
        var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
        style.normal.textColor = new Color(0.3f, 0.8f, 1f);
        string hb = _heartbeatToggle ? "●" : "○";
        EditorGUILayout.LabelField($"{hb} Dreamcore Troubleshooter V49", style);
        EditorGUILayout.EndVertical();

        // --- URP Asset 정밀 판독부 ---
        UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        int maxAdditionalLights = 4;
        bool shadowSupport = false;

        if (urpAsset != null)
        {
            maxAdditionalLights = urpAsset.maxAdditionalLightsCount;
            shadowSupport = urpAsset.supportsAdditionalLightShadows;

            EditorGUILayout.BeginVertical("helpbox");
            EditorGUILayout.LabelField("📦 Current URP Asset Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"- Per Object Limit: {maxAdditionalLights}");
            EditorGUILayout.LabelField($"- Shadow Support: {(shadowSupport ? "ON" : "OFF")}");

            if (GUILayout.Button("🎯 Select URP Asset & Fix Settings"))
            {
                Selection.activeObject = urpAsset;
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.HelpBox("주의: Shadow Slot이 초록색이어도 빛이 꺼진다면 'Shadow Atlas Resolution'이 부족하여 GPU가 데이터를 드랍한 것입니다. URP Asset에서 Resolution을 2048 이상으로 높이세요.", MessageType.Warning);

        int gpuCount = GetStaticFieldValue<int>("currentAdditionalLightCount", 0);

        foreach (var l in _cachedPoints)
        {
            int idx = _cachedPoints.IndexOf(l);
            bool isBuffered = idx < gpuCount;
            // CPU 기반의 이론적 할당 가능 여부 (실제 GPU 결과는 셰이더만 알 수 있음)
            bool shadowTheoreticallyPossible = l.shadows != LightShadows.None && idx < maxAdditionalLights && shadowSupport;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            GUI.color = isBuffered ? Color.green : Color.red;
            EditorGUILayout.LabelField($"[{idx}] {l.name}", EditorStyles.boldLabel, GUILayout.Width(200));
            GUI.color = Color.white;

            if (GUILayout.Button("Select", GUILayout.Width(60))) Selection.activeGameObject = l.gameObject;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            StatusItem("In Buffer", isBuffered);
            StatusItem("Shadow Slot (Expected)", shadowTheoreticallyPossible);
            EditorGUILayout.EndHorizontal();

            if (isBuffered && !shadowTheoreticallyPossible && l.shadows != LightShadows.None)
            {
                EditorGUILayout.HelpBox("이 광원은 인덱스 제한을 벗어나 그림자가 나오지 않습니다.", MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(20);
        EditorGUILayout.EndScrollView();
    }

    private void StatusItem(string label, bool active)
    {
        GUI.color = active ? Color.green : Color.gray;
        EditorGUILayout.LabelField($"{(active ? "✔" : "✖")} {label}", GUILayout.Width(130));
        GUI.color = Color.white;
    }

    private T GetStaticFieldValue<T>(string f, T d)
    {
        var field = typeof(AtmosphericMassRendererFeature).GetField(f, BindingFlags.Public | BindingFlags.Static);
        return field != null ? (T)field.GetValue(null) : d;
    }*/
}