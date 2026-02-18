using UnityEngine;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 3D LUT 생성을 위한 파라미터와 베이킹 로직을 담은 ScriptableObject입니다.
/// 기본적인 보정 항목 외에 색상 기반 명암 조절인 VOLUME 기능과 커브 기능이 포함되어 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "NewLUTPreset", menuName = "LUT Tool/LUT Preset")]
public class LUTPreset : ScriptableObject
{
    [Header("1. Global Tone & Color")]
    [Range(-1f, 1f)] public float brightness = 0f;
    [Range(0f, 2f)] public float contrast = 1f;
    [Range(0f, 5f)] public float exposure = 1f;
    [Range(0f, 2f)] public float saturation = 1f;
    [Range(-1f, 1f)] public float temperature = 0f;
    [Range(-1f, 1f)] public float tint = 0f;
    [Range(0f, 2f)] public float vibrance = 1f;

    [Header("2. Advanced Filmic S-Curve")]
    [Tooltip("섀도우의 묵직함 (Toe)")]
    [Range(0f, 1f)] public float toeStrength = 0.2f;
    [Tooltip("하이라이트의 부드러운 롤오프 (Shoulder)")]
    [Range(0f, 1f)] public float shoulderStrength = 0.2f;
    [Tooltip("중간 톤 대비 기울기 (Dynamic Range)")]
    [Range(0.5f, 2.0f)] public float curveSlope = 1.0f;

    [Header("3. Tonal Range (Lift, Gamma, Gain)")]
    public Vector2[] wheelPositions = new Vector2[3] { Vector2.zero, Vector2.zero, Vector2.zero };
    public float[] lumaValues = new float[3] { 0f, 0f, 0f };

    [Header("4. Curves")]
    public AnimationCurve masterCurve = AnimationCurve.Linear(0, 0, 1, 1);
    public AnimationCurve redCurve = AnimationCurve.Linear(0, 0, 1, 1);
    public AnimationCurve greenCurve = AnimationCurve.Linear(0, 0, 1, 1);
    public AnimationCurve blueCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("5. Volume (Color Specific Contrast)")]
    [Range(0f, 1f)] public float volumeHue = 0.5f;       // 대상 색상
    [Range(0.01f, 0.5f)] public float volumeRange = 0.2f; // 색상 범위 넓이
    [Range(0.5f, 2.0f)] public float volumeLighten = 1.0f; // 대상 색상의 밝은 쪽 강조
    [Range(0.5f, 2.0f)] public float volumeDarken = 1.0f;  // 대상 색상의 어두운 쪽 강조
    [Range(0.1f, 5.0f)] public float volumeGamma = 1.0f;   // 대상 색상의 영역 감마

    [Header("6. Output Settings")]
    public string lutName = "Filmic_LUT";
    public string outputFolder = "Assets/GeneratedLuts";
    [Range(16, 64)] public int lutQuality = 32;

    [Header("7. Baked Result")]
    public Texture2D bakedLUT;

    // --- 핵심 보정 로직 (ApplyAdjustments) ---
    public Color ApplyAdjustments(Color col)
    {
        // 1. Exposure & Brightness
        col.r = (col.r * exposure) + brightness;
        col.g = (col.g * exposure) + brightness;
        col.b = (col.b * exposure) + brightness;

        // 2. Volume 기능 적용
        col = ApplyVolume(col);

        // 3. Advanced Filmic S-Curve
        col.r = ApplyFilmic(col.r);
        col.g = ApplyFilmic(col.g);
        col.b = ApplyFilmic(col.b);

        // 4. Contrast
        col.r = (col.r - 0.5f) * contrast + 0.5f;
        col.g = (col.g - 0.5f) * contrast + 0.5f;
        col.b = (col.b - 0.5f) * contrast + 0.5f;

        // 5. Tonal Range
        float lum = (col.r + col.g + col.b) / 3.0001f;
        float sW = Mathf.Clamp01(1f - lum);
        float hW = Mathf.Clamp01(lum);
        float mW = Mathf.Max(0, 1f - Mathf.Abs(lum - 0.5f) * 2f);

        Color liftCol = GetColorFromWheelPosition(wheelPositions[0]);
        Color gammaCol = GetColorFromWheelPosition(wheelPositions[1]);
        Color gainCol = GetColorFromWheelPosition(wheelPositions[2]);

        col.r += (liftCol.r - 1f) * sW + lumaValues[0] * sW + (gainCol.r - 1f) * hW + lumaValues[2] * hW + (gammaCol.r - 1f) * mW + lumaValues[1] * mW;
        col.g += (liftCol.g - 1f) * sW + lumaValues[0] * sW + (gainCol.g - 1f) * hW + lumaValues[2] * hW + (gammaCol.g - 1f) * mW + lumaValues[1] * mW;
        col.b += (liftCol.b - 1f) * sW + lumaValues[0] * sW + (gainCol.b - 1f) * hW + lumaValues[2] * hW + (gammaCol.b - 1f) * mW + lumaValues[1] * mW;

        // 6. Curves 적용
        col.r = masterCurve.Evaluate(Mathf.Clamp01(redCurve.Evaluate(Mathf.Clamp01(col.r))));
        col.g = masterCurve.Evaluate(Mathf.Clamp01(greenCurve.Evaluate(Mathf.Clamp01(col.g))));
        col.b = masterCurve.Evaluate(Mathf.Clamp01(blueCurve.Evaluate(Mathf.Clamp01(col.b))));

        // 7. White Balance
        col.r += temperature * 0.1f;
        col.b -= temperature * 0.1f;
        col.g += tint * 0.05f;

        // 8. Saturation & Vibrance
        float h, s, v;
        Color.RGBToHSV(col, out h, out s, out v);
        s *= saturation * (1.0f + (1.0f - s) * (vibrance - 1.0f));
        col = Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));

        return new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b));
    }

    private Color ApplyVolume(Color col)
    {
        float h, s, v;
        Color.RGBToHSV(col, out h, out s, out v);
        float hueDist = Mathf.Abs(h - volumeHue);
        if (hueDist > 0.5f) hueDist = 1.0f - hueDist;
        float mask = Mathf.Clamp01(1.0f - (hueDist / Mathf.Max(0.01f, volumeRange))) * s;
        if (mask > 0)
        {
            if (v > 0.5f) v = Mathf.Pow(v, 1.0f / (1.0f + (volumeLighten - 1.0f) * mask));
            else v = Mathf.Pow(v, 1.0f + (volumeDarken - 1.0f) * mask);
            v = Mathf.Pow(v, 1.0f / (1.0f + (volumeGamma - 1.0f) * mask));
        }
        return Color.HSVToRGB(h, s, Mathf.Clamp01(v));
    }

    private float ApplyFilmic(float x)
    {
        float toe = Mathf.Pow(Mathf.Max(0, x), 1f + toeStrength);
        float shoulder = 1f - Mathf.Exp(-x * (1f / Mathf.Max(0.01f, 1f - shoulderStrength)));
        float res = Mathf.Lerp(toe, shoulder, Mathf.Clamp01(x));
        return Mathf.Pow(res, 1f / curveSlope);
    }

    private Color GetColorFromWheelPosition(Vector2 pos)
    {
        if (pos == Vector2.zero) return Color.white;
        float angle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360f;
        return Color.HSVToRGB(angle / 360f, pos.magnitude, 1f);
    }

    public void ResetToDefaults()
    {
        brightness = 0f; contrast = 1f; exposure = 1f; saturation = 1f; temperature = 0f; tint = 0f; vibrance = 1f;
        toeStrength = 0.2f; shoulderStrength = 0.2f; curveSlope = 1.0f;
        volumeHue = 0.5f; volumeRange = 0.2f; volumeLighten = 1f; volumeDarken = 1f; volumeGamma = 1f;
        wheelPositions = new Vector2[3] { Vector2.zero, Vector2.zero, Vector2.zero };
        lumaValues = new float[3] { 0f, 0f, 0f };
        masterCurve = AnimationCurve.Linear(0, 0, 1, 1);
        redCurve = AnimationCurve.Linear(0, 0, 1, 1);
        greenCurve = AnimationCurve.Linear(0, 0, 1, 1);
        blueCurve = AnimationCurve.Linear(0, 0, 1, 1);
        lutQuality = 32;
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    public void Bake()
    {
#if UNITY_EDITOR
        int q = lutQuality;
        int width = q * q;
        Texture2D lutStrip = new Texture2D(width, q, TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * q];

        // 파란색 왜곡 방지를 위한 정확한 루프 순서 (Green: Y, BlueBlocks: Z, Red: X)
        for (int y = 0; y < q; y++)
        {
            for (int z = 0; z < q; z++)
            {
                for (int x = 0; x < q; x++)
                {
                    float r = (float)x / (q - 1);
                    float g = (float)y / (q - 1);
                    float b = (float)z / (q - 1);
                    pixels[y * width + (z * q + x)] = ApplyAdjustments(new Color(r, g, b));
                }
            }
        }

        lutStrip.SetPixels(pixels);
        lutStrip.Apply();
        if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
        string fullPath = $"{outputFolder}/{(string.IsNullOrEmpty(lutName) ? name : lutName)}.png";
        File.WriteAllBytes(fullPath, lutStrip.EncodeToPNG());
        AssetDatabase.ImportAsset(fullPath);
        TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
        if (importer != null)
        {
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = false; importer.textureType = TextureImporterType.Default; importer.SaveAndReimport();
        }
        bakedLUT = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
        EditorUtility.SetDirty(this); AssetDatabase.SaveAssets();
#endif
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(LUTPreset))]
public class LUTPresetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        LUTPreset preset = (LUTPreset)target;
        EditorGUILayout.Space(10);
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 30), new Color(0.24f, 0.52f, 0.95f));
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = Color.white } };
        EditorGUI.LabelField(GUILayoutUtility.GetLastRect(), "LUT PRESET WORKBENCH", headerStyle);
        EditorGUILayout.Space(10);

        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(20);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("RESET ALL", GUILayout.Height(40))) preset.ResetToDefaults();
        if (GUILayout.Button("BAKE 3D LUT (PNG)", GUILayout.Height(40))) preset.Bake();
        EditorGUILayout.EndHorizontal();

        if (preset.bakedLUT != null)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Current Baked LUT Preview", EditorStyles.centeredGreyMiniLabel);
            GUI.DrawTexture(GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 40, 20), preset.bakedLUT, ScaleMode.StretchToFill);
        }
    }
}
#endif