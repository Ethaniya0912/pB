using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class RampGeneratorWindow : EditorWindow
{
    private Gradient _rampGradient = new Gradient();
    private int _textureWidth = 256;
    private string _fileName = "ToonRamp_New";
    private Texture2D _previewTex;

    // 불러오기용 변수
    private Texture2D _sourceTexture;

    [MenuItem("Tools/Lighting Ramp Generator")]
    public static void ShowWindow()
    {
        RampGeneratorWindow window = GetWindow<RampGeneratorWindow>("Ramp Gen");
        window.minSize = new Vector2(350, 450);
    }

    private void OnEnable()
    {
        // 초기 기본값 설정
        if (_rampGradient.colorKeys.Length <= 2 && _rampGradient.colorKeys[0].color == Color.white)
        {
            _rampGradient.colorKeys = new GradientColorKey[] {
                new GradientColorKey(Color.black, 0.0f),
                new GradientColorKey(Color.white, 1.0f)
            };
        }
        UpdatePreview();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        GUILayout.Label("Lighting Ramp Generator", EditorStyles.boldLabel);

        // --- 섹션 1: 기존 텍스처 불러오기 ---
        EditorGUILayout.BeginVertical("helpbox");
        GUILayout.Label("Load Existing Texture", EditorStyles.miniBoldLabel);
        _sourceTexture = (Texture2D)EditorGUILayout.ObjectField("Target Texture", _sourceTexture, typeof(Texture2D), false);

        if (GUILayout.Button("Load Colors from Texture"))
        {
            LoadTextureToGradient();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // --- 섹션 2: 램프 수정 및 설정 ---
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Ramp Settings", EditorStyles.miniBoldLabel);

        EditorGUI.BeginChangeCheck();

        _fileName = EditorGUILayout.TextField("File Name", _fileName);
        _textureWidth = EditorGUILayout.IntSlider("Resolution", _textureWidth, 2, 2048);
        _rampGradient = EditorGUILayout.GradientField("Edit Gradient", _rampGradient);

        if (EditorGUI.EndChangeCheck())
        {
            UpdatePreview();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // --- 섹션 3: 실시간 프리뷰 ---
        GUILayout.Label("Live Preview (1D)", EditorStyles.miniBoldLabel);
        if (_previewTex != null)
        {
            Rect rect = GUILayoutUtility.GetRect(10, 60, GUILayout.ExpandWidth(true));
            EditorGUI.DrawPreviewTexture(rect, _previewTex);
        }

        EditorGUILayout.Space(20);

        // --- 섹션 4: 저장 ---
        GUI.color = Color.cyan;
        if (GUILayout.Button("Save as PNG", GUILayout.Height(40)))
        {
            SaveRamp();
        }
        GUI.color = Color.white;
    }

    private void LoadTextureToGradient()
    {
        if (_sourceTexture == null)
        {
            EditorUtility.DisplayDialog("Error", "불러올 텍스처를 선택해주세요.", "OK");
            return;
        }

        string path = AssetDatabase.GetAssetPath(_sourceTexture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        // 픽셀 데이터를 읽으려면 Read/Write가 켜져 있어야 함
        if (importer != null && !importer.isReadable)
        {
            if (EditorUtility.DisplayDialog("Read/Write Required", "텍스처를 읽으려면 'Read/Write' 설정이 필요합니다. 활성화할까요?", "Yes", "No"))
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            else return;
        }

        // 텍스처에서 8개 지점을 샘플링 (유니티 Gradient 키 한계)
        int sampleCount = 8;
        GradientColorKey[] newColors = new GradientColorKey[sampleCount];
        GradientAlphaKey[] newAlphas = new GradientAlphaKey[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (sampleCount - 1);
            // 가로축 샘플링
            int x = Mathf.FloorToInt(t * (_sourceTexture.width - 1));
            Color sampledColor = _sourceTexture.GetPixel(x, 0);

            newColors[i] = new GradientColorKey(sampledColor, t);
            newAlphas[i] = new GradientAlphaKey(sampledColor.a, t);
        }

        _rampGradient.SetKeys(newColors, newAlphas);
        _fileName = _sourceTexture.name + "_Edited";
        UpdatePreview();

        Debug.Log($"<color=yellow>[RampGen]</color> '{_sourceTexture.name}'로부터 색상 데이터를 복원했습니다.");
    }

    private void UpdatePreview()
    {
        if (_previewTex == null || _previewTex.width != _textureWidth)
        {
            _previewTex = new Texture2D(_textureWidth, 1, TextureFormat.RGBA32, false);
            _previewTex.wrapMode = TextureWrapMode.Clamp;
            _previewTex.filterMode = FilterMode.Bilinear;
        }

        for (int i = 0; i < _textureWidth; i++)
        {
            float t = (float)i / (_textureWidth - 1);
            _previewTex.SetPixel(i, 0, _rampGradient.Evaluate(t));
        }
        _previewTex.Apply();
    }

    private void SaveRamp()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Ramp Texture", _fileName, "png", "저장 위치를 선택하세요.");

        if (!string.IsNullOrEmpty(path))
        {
            byte[] bytes = _previewTex.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            Debug.Log($"<color=cyan>[RampGen]</color> 성공적으로 저장되었습니다: <b>{path}</b>");
        }
    }
}