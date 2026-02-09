using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

/// <summary>
/// URP의 실제 렌더링 상태와 키워드 활성화 여부를 정밀 분석하는 개선된 디버거입니다.
/// </summary>
public class GlobalRampDebugger : EditorWindow
{
    private bool _autoRefresh = true;
    private bool _enableProbe = false;

    private Vector3 _hitPoint;
    private Vector3 _hitNormal;
    private float _finalIntensityU;
    private Color _sampledColor;
    private string _targetObjectName = "None";

    private bool _receiveShadowsEnabled = false;
    private string[] _materialKeywords;
    private string _shaderName = "";

    [MenuItem("Window/Analysis/Global Ramp Debugger")]
    public static void ShowWindow() => GetWindow<GlobalRampDebugger>("Ramp Debugger");

    private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void OnGUI()
    {
        GUILayout.Label("1. URP Pipeline & Shadow Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null)
        {
            EditorGUILayout.LabelField("Shadow Distance:", $"{urpAsset.shadowDistance} units");
            EditorGUILayout.LabelField("Main Light Shadows:", urpAsset.supportsMainLightShadows ? "ENABLED" : "DISABLED");
            EditorGUILayout.LabelField("Additional Light Shadows:", urpAsset.supportsAdditionalLightShadows ? "ENABLED" : "DISABLED");
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("2. Target Object Diagnostics", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("Selected:", _targetObjectName);
        EditorGUILayout.LabelField("Shader:", _shaderName);

        GUI.color = _receiveShadowsEnabled ? Color.green : Color.red;
        EditorGUILayout.LabelField("Receive Shadows (MeshRenderer):", _receiveShadowsEnabled ? "ON" : "OFF");
        GUI.color = Color.white;

        EditorGUILayout.Space(5);
        GUILayout.Label("Active Keywords (Shader Variant):", EditorStyles.miniBoldLabel);
        if (_materialKeywords != null && _materialKeywords.Length > 0)
        {
            foreach (var kw in _materialKeywords)
            {
                if (kw.Contains("SHADOW") || kw.Length <= 3) GUI.color = Color.cyan;
                EditorGUILayout.LabelField("- " + kw);
                GUI.color = Color.white;
            }
        }
        else EditorGUILayout.LabelField("(No exposed keywords or Standard Lit)");

        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("3. Simulated Shadow Probe", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        _enableProbe = EditorGUILayout.Toggle("Enable Probe", _enableProbe);
        if (_enableProbe)
        {
            EditorGUILayout.HelpBox("알림: Intensity(U) 수치가 변화한다면 시뮬레이션은 정상입니다. 그림자가 보이지 않는다면 GPU 셰이더 내부의 shadowCoord 전달 문제를 확인하세요.", MessageType.Info);
            EditorGUILayout.LabelField("Calculated Intensity (U):", _finalIntensityU.ToString("F4"));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Simulated Color:", GUILayout.Width(130));
            EditorGUILayout.ColorField(_sampledColor);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);
        GUILayout.Label("4. Final Troubleshooting Step", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("디버거의 수치는 나오는데 그림자가 안 보인다면?\n\n1. 셰이더 그래프 'Graph Settings'에서 'Receive Shadows'가 꺼져 있는지 확인.\n2. Custom Function 노드의 'WorldPos' 핀에 'Position(World Space)' 노드가 제대로 연결되어 있는지 확인.\n3. 라이트의 'Shadow Strength'가 1인지 확인.", MessageType.Warning);

        if (GUILayout.Button("Force Re-sync")) Repaint();
        _autoRefresh = EditorGUILayout.Toggle("Auto Refresh", _autoRefresh);
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!_enableProbe) return;

        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            _hitPoint = hit.point;
            _hitNormal = hit.normal;
            _targetObjectName = hit.collider.name;

            Renderer rend = hit.collider.GetComponent<Renderer>();
            if (rend != null)
            {
                _receiveShadowsEnabled = rend.receiveShadows;
                if (rend.sharedMaterial != null)
                {
                    _shaderName = rend.sharedMaterial.shader.name;
                    _materialKeywords = rend.sharedMaterial.shaderKeywords;
                }
            }

            CalculateLightingSimulation();
            sceneView.Repaint();
        }
        if (_autoRefresh) Repaint();
    }

    void CalculateLightingSimulation()
    {
        Texture2D tex2d = Shader.GetGlobalTexture("_GlobalRampTex") as Texture2D;
        float gamma = Shader.GetGlobalFloat("_GlobalRampGamma");
        float madness = Shader.GetGlobalFloat("_GlobalMadness");

        Color acc = Color.black;
        Light[] lights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
        float maxU = 0;

        foreach (var l in lights)
        {
            if (!l.enabled || l.intensity <= 0) continue;

            float atten = 1.0f;
            Vector3 lDir = (l.type == LightType.Directional) ? -l.transform.forward : (l.transform.position - _hitPoint).normalized;

            if (l.type == LightType.Point)
            {
                float d = Vector3.Distance(l.transform.position, _hitPoint);
                atten = Mathf.Clamp01(1.0f - Mathf.Pow(d / l.range, 4.0f));
            }

            float ndotl = Mathf.Clamp01(Vector3.Dot(_hitNormal, lDir));
            float u = Mathf.Pow(ndotl * atten, Mathf.Max(0.01f, gamma));

            maxU = Mathf.Max(maxU, u);
            if (tex2d != null) acc += tex2d.GetPixelBilinear(u, madness) * l.color * l.intensity;
        }

        _finalIntensityU = maxU;
        _sampledColor = acc;

        Handles.color = Color.cyan;
        Handles.SphereHandleCap(0, _hitPoint, Quaternion.identity, 0.05f, EventType.Repaint);
    }
}