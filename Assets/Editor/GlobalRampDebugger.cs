using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

/// <summary>
/// 키워드 상태 및 드림코어 라이팅 파라미터를 정밀 진단하는 디버거입니다.
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

    private bool _keywordExists = false;
    private bool _isKeywordActiveOnMaterial = false;
    private int _urpLightLimit = 0;
    private LightRenderingMode _lightRenderingMode;

    private List<string> _diagnostics = new List<string>();

    [MenuItem("Window/Analysis/Global Ramp Debugger")]
    public static void ShowWindow() => GetWindow<GlobalRampDebugger>("Ramp Debugger");

    private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void OnGUI()
    {
        GUILayout.Label("1. System & Global Parameters", EditorStyles.boldLabel);

        // 전역 변수 읽기
        Texture globalTex = Shader.GetGlobalTexture("_GlobalRampTex");
        float madness = Shader.GetGlobalFloat("_GlobalMadness");
        float gamma = Shader.GetGlobalFloat("_GlobalRampGamma");
        float steps = Shader.GetGlobalFloat("_GlobalSteps");

        EditorGUILayout.BeginVertical("box");

        // 키워드 상태 표시 (에러가 아니라 상태로 표시하도록 수정)
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Keyword [_ADDITIONAL_LIGHTS]:", GUILayout.Width(180));
        if (_keywordExists)
        {
            GUI.color = Color.green;
            GUILayout.Label("DECLARED (URP Standard)", EditorStyles.boldLabel);
        }
        else
        {
            GUI.color = Color.white;
            GUILayout.Label("NOT FOUND");
        }
        GUI.color = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Material Active State:", GUILayout.Width(180));
        if (_isKeywordActiveOnMaterial)
        {
            GUI.color = Color.green;
            GUILayout.Label("ACTIVE (Point Light Enabled)", EditorStyles.boldLabel);
        }
        else
        {
            GUI.color = Color.yellow;
            GUILayout.Label("INACTIVE (Main Light Only)");
        }
        GUI.color = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
        if (globalTex != null)
        {
            EditorGUILayout.LabelField($"Texture: {globalTex.name} | Gamma: {gamma:F2} | Madness: {madness:F2}");
            Rect rampRect = GUILayoutUtility.GetRect(256, 20);
            EditorGUI.DrawPreviewTexture(rampRect, globalTex);
        }

        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        GUILayout.Label("2. Scene Probe & Diagnostics", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");
        _enableProbe = EditorGUILayout.Toggle("Enable Probe", _enableProbe);
        if (_enableProbe)
        {
            EditorGUILayout.LabelField("Target Object:", _targetObjectName);
            EditorGUILayout.LabelField("Calculated Intensity (U):", _finalIntensityU.ToString("F4"));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Sampled Color:", GUILayout.Width(130));
            EditorGUILayout.ColorField(_sampledColor);
            EditorGUILayout.EndHorizontal();

            if (_diagnostics.Count > 0)
            {
                foreach (var d in _diagnostics) EditorGUILayout.HelpBox(d, MessageType.Info);
            }
        }
        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Force Re-sync")) Repaint();
        _autoRefresh = EditorGUILayout.Toggle("Auto Refresh", _autoRefresh);
    }

    void OnSceneGUI(SceneView sceneView)
    {
        UpdateURPSettings();
        if (!_enableProbe) return;

        Event e = Event.current;
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            _hitPoint = hit.point;
            _hitNormal = hit.normal;
            _targetObjectName = hit.collider.name;
            _diagnostics.Clear();

            Renderer rend = hit.collider.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                Shader s = rend.sharedMaterial.shader;

                // 키워드 체크 로직 (중복 에러 판단이 아닌 존재 여부 판단으로 변경)
                _keywordExists = false;
                LocalKeywordSpace ks = s.keywordSpace;
                foreach (var kw in ks.keywords)
                {
                    if (kw.name == "_ADDITIONAL_LIGHTS")
                    {
                        _keywordExists = true;
                        break;
                    }
                }

                _isKeywordActiveOnMaterial = rend.sharedMaterial.IsKeywordEnabled("_ADDITIONAL_LIGHTS");

                // 노멀맵 검증
                if (rend.sharedMaterial.HasProperty("_BumpMap") && Vector3.Dot(_hitNormal, Vector3.up) > 0.99f)
                {
                    _diagnostics.Add("표면이 너무 평평합니다. Unpack Normal 노드 연결을 확인하세요.");
                }

                // 텍스처 Read/Write 검증
                Texture2D tex = Shader.GetGlobalTexture("_GlobalRampTex") as Texture2D;
                if (tex != null && !tex.isReadable)
                {
                    _diagnostics.Add("램프 텍스처의 'Read/Write Enabled'가 꺼져있어 디버거 색상이 부정확할 수 있습니다.");
                }
            }

            CalculateLighting();
            sceneView.Repaint();
        }
        if (_autoRefresh) Repaint();
    }

    void UpdateURPSettings()
    {
        UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset != null)
        {
            _urpLightLimit = urpAsset.maxAdditionalLightsCount;
            _lightRenderingMode = urpAsset.additionalLightsRenderingMode;
        }
    }

    void CalculateLighting()
    {
        Texture2D tex2d = Shader.GetGlobalTexture("_GlobalRampTex") as Texture2D;
        if (tex2d == null) return;

        float madness = Shader.GetGlobalFloat("_GlobalMadness");
        float gamma = Shader.GetGlobalFloat("_GlobalRampGamma");
        float steps = Shader.GetGlobalFloat("_GlobalSteps");

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
                // URP 감쇄 근사
                atten = Mathf.Clamp01(1.0f - Mathf.Pow(d / l.range, 2.0f));
            }

            float ndotl = Mathf.Clamp01(Vector3.Dot(_hitNormal, lDir));
            float influence = ndotl * atten;

            // 셰이더 로직(Gamma, Steps) 동기화
            float u = Mathf.Pow(influence, Mathf.Max(0.01f, gamma));
            if (steps > 0.1f) u = Mathf.Floor(u * steps) / steps;

            maxU = Mathf.Max(maxU, u);
            acc += tex2d.GetPixelBilinear(u, madness) * l.color * l.intensity;
        }

        _finalIntensityU = maxU;
        _sampledColor = acc;
    }
}