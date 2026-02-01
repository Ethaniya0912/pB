using UnityEngine;
using UnityEditor;
using System.IO;

public class LutGeneratorEditor : EditorWindow
{
    // --- 설정 값: Global Tone & Color ---
    private float brightness = 0f;
    private float contrast = 1f;
    private float exposure = 1f;
    private float saturation = 1f;
    private float temperature = 0f;
    private float tint = 0f;
    private float vibrance = 1f;

    // --- 설정 값: Advanced Filmic Tone Mapping (S-Curve) ---
    private float toeStrength = 0.2f;      // 섀도우의 묵직함 (Toe)
    private float shoulderStrength = 0.2f; // 하이라이트 부드러운 롤오프 (Shoulder)
    private float curveSlope = 1.0f;       // 중간 톤 대비 기울기 (Dynamic Range)

    // --- 설정 값: Tonal Range (Lift, Gamma, Gain) ---
    private int selectedTonalRange = 0; // 0: Lift, 1: Gamma, 2: Gain
    private Vector2[] wheelPositions = new Vector2[3] { Vector2.zero, Vector2.zero, Vector2.zero };
    private float[] lumaValues = new float[3] { 0f, 0f, 0f };

    // --- 설정 값: Curves ---
    private AnimationCurve masterCurve = AnimationCurve.Linear(0, 0, 1, 1);
    private AnimationCurve redCurve = AnimationCurve.Linear(0, 0, 1, 1);
    private AnimationCurve greenCurve = AnimationCurve.Linear(0, 0, 1, 1);
    private AnimationCurve blueCurve = AnimationCurve.Linear(0, 0, 1, 1);

    // --- 출력 설정 ---
    private string lutName = "Filmic_LUT";
    private string outputFolder = "Assets/GeneratedLuts";
    private int lutQuality = 32;

    // --- 성능 최적화 데이터 (새로 통합된 성능 옵션) ---
    private bool useLowResPreview = false;
    private double lastPreviewUpdateTime = 0;
    private const double PREVIEW_UPDATE_INTERVAL = 1.0 / 24.0; // 24 FPS 제한
    private bool needsUpdate = false;

    // --- 데이터 ---
    private Texture2D previewImage;
    private Texture2D smallPreview;
    private Texture2D processedSource;
    private Texture2D modifiedPreview;
    private Texture2D lutStrip;
    private Texture2D colorWheelTex;
    private Vector2 leftScrollPos;
    private Vector2 rightScrollPos;
    private bool showSplitView = false;

    // --- 스타일 ---
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle sectionBoxStyle;
    private GUIStyle infoTextStyle;
    private Color themeBlue = new Color(0.24f, 0.52f, 0.95f, 1f);
    private Color bgDark = new Color(0.12f, 0.12f, 0.12f, 1f);

    [MenuItem("Tools/LUT Editor Pro")]
    public static void ShowWindow()
    {
        LutGeneratorEditor window = GetWindow<LutGeneratorEditor>("LUT Editor Pro");
        window.minSize = new Vector2(1150, 850);
    }

    private void OnEnable()
    {
        if (!Directory.Exists(outputFolder)) outputFolder = "Assets";
        CreateColorRingTexture();

        EditorApplication.delayCall += () => {
            if (previewImage == null) CaptureSceneView();
            GenerateLutTexture();
        };
    }

    private void CreateColorRingTexture()
    {
        int size = 256;
        colorWheelTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerRadius = size / 2f - 2f;
        float innerRadius = outerRadius * 0.82f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 pos = new Vector2(x, y);
                float dist = Vector2.Distance(pos, center);
                if (dist <= outerRadius && dist >= innerRadius)
                {
                    float angle = Mathf.Atan2(y - center.y, x - center.x) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;
                    float alpha = Mathf.Clamp01((outerRadius - dist) / 1.5f);
                    Color col = Color.HSVToRGB(angle / 360f, 1f, 1f);
                    col.a = alpha;
                    colorWheelTex.SetPixel(x, y, col);
                }
                else colorWheelTex.SetPixel(x, y, Color.clear);
            }
        }
        colorWheelTex.Apply();
    }

    private void OnGUI()
    {
        InitStyles();
        DrawTopToolbar();

        EditorGUILayout.BeginHorizontal();

        // --- 좌측 패널: 설정 (너비 고정) ---
        EditorGUILayout.BeginVertical(GUILayout.Width(380));
        leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos);
        DrawSettingsPanel();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // --- 우측 패널: 프리뷰 (가변 너비, 스크롤 가능) ---
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        rightScrollPos = EditorGUILayout.BeginScrollView(rightScrollPos);
        DrawPreviewPanel();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();

        if (needsUpdate) Repaint();
    }

    private void InitStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, margin = new RectOffset(0, 0, 15, 10) };
            headerStyle.normal.textColor = Color.white;
            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            subHeaderStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            sectionBoxStyle = new GUIStyle("helpBox") { padding = new RectOffset(15, 15, 15, 15), margin = new RectOffset(5, 5, 5, 5) };
            infoTextStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11 };
            infoTextStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        }
    }

    private void DrawTopToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("Export Normal LUT", EditorStyles.toolbarButton)) SaveLutAsPng();
        if (GUILayout.Button("Quick Bake", EditorStyles.toolbarButton)) SaveLutAsPng(true);
        GUILayout.Space(10);
        if (GUILayout.Button("Grab Scene View", EditorStyles.toolbarButton)) CaptureSceneView();

        GUILayout.FlexibleSpace();

        // [핵심] 상단 툴바에 Low-Res 버튼 배치 (활성화 시 노란색 강조)
        Color oldColor = GUI.backgroundColor;
        if (useLowResPreview) GUI.backgroundColor = new Color(1f, 0.8f, 0.2f);
        EditorGUI.BeginChangeCheck();
        useLowResPreview = GUILayout.Toggle(useLowResPreview, "Low-Res Optimization", EditorStyles.toolbarButton);
        if (EditorGUI.EndChangeCheck())
        {
            PrepareProcessedSource();
            UpdatePreview(true);
        }
        GUI.backgroundColor = oldColor;
        GUILayout.Space(5);

        showSplitView = GUILayout.Toggle(showSplitView, "Toggle Preview", EditorStyles.toolbarButton);
        if (GUILayout.Button("Documentation", EditorStyles.toolbarButton)) Application.OpenURL("https://docs.unity3d.com");
        EditorGUILayout.EndHorizontal();
    }

    private void DrawSettingsPanel()
    {
        EditorGUI.BeginChangeCheck();

        // [1] 성능 및 품질 설정 (섹션 최상단 배치)
        EditorGUILayout.LabelField("Performance & Optimization", headerStyle);
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        EditorGUI.BeginChangeCheck();
        useLowResPreview = EditorGUILayout.Toggle("Enable Low-Res Mode", useLowResPreview);
        if (EditorGUI.EndChangeCheck())
        {
            PrepareProcessedSource();
            UpdatePreview(true);
        }
        EditorGUILayout.HelpBox(useLowResPreview ? "성능 최적화: 512px 미리보기 사용 중" : "고품질 모드: 원본 해상도 사용 중", MessageType.None);
        EditorGUILayout.EndVertical();

        GUILayout.Space(5);
        EditorGUILayout.LabelField("Filmic Color Grading", headerStyle);

        // 1. Advanced Filmic (S-Curve)
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        DrawSectionHeader("Advanced Filmic (S-Curve)", () => { toeStrength = 0.2f; shoulderStrength = 0.2f; curveSlope = 1.0f; });
        EditorGUILayout.HelpBox("Shoulder는 하이라이트를 부드럽게 만들고, Toe는 그림자의 묵직한 볼륨감을 형성합니다.", MessageType.None);
        toeStrength = DrawProSlider("Toe (Shadows)", toeStrength, 0f, 1f);
        shoulderStrength = DrawProSlider("Shoulder (Highs)", shoulderStrength, 0f, 1f);
        curveSlope = DrawProSlider("Dynamic Range", curveSlope, 0.5f, 2.0f);
        EditorGUILayout.EndVertical();

        // 2. Global Tone
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        DrawSectionHeader("Global Tone", () => { brightness = 0; contrast = 1; exposure = 1; });
        brightness = DrawProSlider("Brightness", brightness, -1f, 1f);
        contrast = DrawProSlider("Contrast", contrast, 0f, 2f);
        exposure = DrawProSlider("Exposure", exposure, 0f, 5f);
        EditorGUILayout.EndVertical();

        // 3. Global Color
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        DrawSectionHeader("Global Color", () => { saturation = 1; temperature = 0; tint = 0; vibrance = 1; });
        saturation = DrawProSlider("Saturation", saturation, 0f, 2f);
        vibrance = DrawProSlider("Vibrance", vibrance, 0f, 2f);
        temperature = DrawProSlider("Temperature", temperature, -1f, 1f);
        tint = DrawProSlider("Tint", tint, -1f, 1f);
        EditorGUILayout.EndVertical();

        // 4. Tonal Range
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        DrawSectionHeader("Tonal Range Adjustments", () => {
            wheelPositions = new Vector2[3] { Vector2.zero, Vector2.zero, Vector2.zero };
            lumaValues = new float[3] { 0f, 0f, 0f };
        });
        selectedTonalRange = GUILayout.Toolbar(selectedTonalRange, tabs);
        GUILayout.Space(15);
        DrawSelectedTonalWheel();
        EditorGUILayout.EndVertical();

        // 5. Curves
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        DrawSectionHeader("Curves", () => {
            masterCurve = AnimationCurve.Linear(0, 0, 1, 1); redCurve = AnimationCurve.Linear(0, 0, 1, 1);
            greenCurve = AnimationCurve.Linear(0, 0, 1, 1); blueCurve = AnimationCurve.Linear(0, 0, 1, 1);
        });
        masterCurve = EditorGUILayout.CurveField("Master", masterCurve, Color.white, new Rect(0, 0, 1, 1));
        redCurve = EditorGUILayout.CurveField("Red", redCurve, Color.red, new Rect(0, 0, 1, 1));
        greenCurve = EditorGUILayout.CurveField("Green", greenCurve, Color.green, new Rect(0, 0, 1, 1));
        blueCurve = EditorGUILayout.CurveField("Blue", blueCurve, Color.blue, new Rect(0, 0, 1, 1));
        EditorGUILayout.EndVertical();

        if (EditorGUI.EndChangeCheck())
        {
            needsUpdate = true;
            UpdatePreview(false); // 24fps 제한 적용하여 업데이트 시도
            GenerateLutTexture();
        }

        GUILayout.Space(20);

        // 6. Bake Settings
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        EditorGUILayout.LabelField("Bake Output Settings", subHeaderStyle);
        lutQuality = EditorGUILayout.IntPopup("LUT Quality", lutQuality, new string[] { "16x16", "32x32", "64x64" }, new int[] { 16, 32, 64 });
        lutName = EditorGUILayout.TextField("LUT Name", lutName);

        GUI.backgroundColor = themeBlue;
        if (GUILayout.Button("BAKE FILMIC LUT", GUILayout.Height(40))) SaveLutAsPng();
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndVertical();
        GUILayout.Space(20);
    }

    private void DrawSectionHeader(string title, System.Action resetAction)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(title, subHeaderStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(50)))
        {
            resetAction.Invoke();
            GenerateLutTexture();
            UpdatePreview(true);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);
    }

    private float DrawProSlider(string label, float value, float min, float max)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(110));
        Rect rect = GUILayoutUtility.GetRect(130, 20);
        float progress = Mathf.Clamp01((value - min) / (max - min));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 8, rect.width, 4), bgDark);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + 8, rect.width * progress, 4), themeBlue);
        float handleX = rect.x + (rect.width * progress) - 4;
        Rect handleRect = new Rect(handleX, rect.y + 2, 8, 16);
        float newValue = GUI.HorizontalSlider(rect, value, min, max, GUIStyle.none, GUIStyle.none);
        EditorGUI.DrawRect(handleRect, Color.white);
        newValue = EditorGUILayout.FloatField(newValue, GUILayout.Width(40));
        EditorGUILayout.EndHorizontal();
        return newValue;
    }

    private void DrawSelectedTonalWheel()
    {
        string labelText = selectedTonalRange == 0 ? "Lift (Shadows)" : (selectedTonalRange == 1 ? "Gamma (Midtones)" : "Gain (Highlights)");
        EditorGUILayout.LabelField(labelText, EditorStyles.boldLabel);
        GUILayout.Space(10);

        float wheelSide = 180f;
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        Rect wheelRect = GUILayoutUtility.GetRect(wheelSide, wheelSide);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUI.DrawTexture(wheelRect, colorWheelTex);
        Vector2 center = wheelRect.center;
        Vector2 currentWheelPos = wheelPositions[selectedTonalRange];
        Vector2 handlePos = center + currentWheelPos * (wheelSide / 2f);

        Handles.BeginGUI();
        Handles.color = new Color(1, 1, 1, 0.15f);
        Handles.DrawLine(new Vector3(center.x, wheelRect.y, 0), new Vector3(center.x, wheelRect.yMax, 0));
        Handles.DrawLine(new Vector3(wheelRect.x, center.y, 0), new Vector3(wheelRect.xMax, center.y, 0));
        Handles.EndGUI();

        Event e = Event.current;
        if (wheelRect.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            Vector2 dir = e.mousePosition - center;
            float dist = dir.magnitude / (wheelSide / 2f);
            wheelPositions[selectedTonalRange] = (dist <= 1f) ? dir / (wheelSide / 2f) : dir.normalized;
            e.Use(); needsUpdate = true; GUI.changed = true;
        }

        EditorGUI.DrawRect(new Rect(handlePos.x - 7, handlePos.y - 0.5f, 14, 1), Color.white);
        EditorGUI.DrawRect(new Rect(handlePos.x - 0.5f, handlePos.y - 7, 1, 14), Color.white);

        GUILayout.Space(15);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(20);
        Color currentWheelColor = GetColorFromWheelPosition(currentWheelPos);
        EditorGUI.DrawRect(GUILayoutUtility.GetRect(44, 44), currentWheelColor);
        GUILayout.Space(12);
        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField("Color Info", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField($"RGB: {currentWheelColor.r:F2}, {currentWheelColor.g:F2}, {currentWheelColor.b:F2}", infoTextStyle);
        string hex = $"#{ColorUtility.ToHtmlStringRGB(currentWheelColor)}";
        EditorGUILayout.LabelField(hex, infoTextStyle);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(45))) GUIUtility.systemCopyBuffer = hex;
        if (GUILayout.Button($"Reset {tabs[selectedTonalRange]}", EditorStyles.miniButton))
        {
            wheelPositions[selectedTonalRange] = Vector2.zero;
            needsUpdate = true;
            GUI.changed = true;
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(12);
        lumaValues[selectedTonalRange] = DrawProSlider("Luma", lumaValues[selectedTonalRange], -1f, 1f);
    }

    private string[] tabs = { "Lift", "Gamma", "Gain" };

    private Color GetColorFromWheelPosition(Vector2 pos)
    {
        if (pos == Vector2.zero) return Color.white;
        float angle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
        if (angle < 0) angle += 360f;
        return Color.HSVToRGB(angle / 360f, pos.magnitude, 1f);
    }

    private void DrawPreviewPanel()
    {
        GUILayout.Space(10);
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        EditorGUILayout.LabelField("ACTIVE PREVIEW SOURCE", EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        previewImage = (Texture2D)EditorGUILayout.ObjectField(previewImage, typeof(Texture2D), false);
        if (EditorGUI.EndChangeCheck() && previewImage != null)
        {
            PrepareProcessedSource();
            UpdatePreview(true);
        }
        EditorGUILayout.EndVertical();

        if (processedSource == null)
        {
            EditorGUILayout.HelpBox("Use 'Grab Scene View' to capture current screen.", MessageType.Info);
        }
        else
        {
            float availWidth = position.width - 430f;
            float aspect = (float)processedSource.width / processedSource.height;
            float maxPossibleHeight = position.height - 250f;
            if (showSplitView) maxPossibleHeight /= 2.2f;

            float viewWidth = availWidth;
            float viewHeight = viewWidth / aspect;

            if (viewHeight > maxPossibleHeight)
            {
                viewHeight = maxPossibleHeight;
                viewWidth = viewHeight * aspect;
            }

            EditorGUILayout.BeginVertical();
            if (!showSplitView)
            {
                DrawFrame("FILMIC ADJUSTED (REALTIME)", modifiedPreview ?? processedSource, viewWidth, viewHeight);
            }
            else
            {
                DrawFrame("ORIGINAL", processedSource, viewWidth, viewHeight);
                GUILayout.Space(10);
                DrawFrame("FILMIC ADJUSTED", modifiedPreview ?? processedSource, viewWidth, viewHeight);
            }
            EditorGUILayout.EndVertical();

            GUILayout.Space(20);

            EditorGUILayout.BeginVertical(sectionBoxStyle, GUILayout.Height(75));
            EditorGUILayout.LabelField("FILMIC LUT STRIP OUTPUT (Linear Space)", EditorStyles.miniBoldLabel);
            if (lutStrip != null)
            {
                Rect stripRect = GUILayoutUtility.GetRect(availWidth, 32);
                GUI.DrawTexture(stripRect, lutStrip, ScaleMode.StretchToFill);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(10);
        }
    }

    private void DrawFrame(string label, Texture2D tex, float w, float h)
    {
        EditorGUILayout.BeginVertical("box", GUILayout.Width(w + 10));
        EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
        Rect r = GUILayoutUtility.GetRect(w, h);
        GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
        EditorGUILayout.EndVertical();
    }

    private void CaptureSceneView()
    {
        if (SceneView.lastActiveSceneView == null) return;
        Camera cam = SceneView.lastActiveSceneView.camera;
        if (cam == null) return;

        RenderTexture rt = new RenderTexture(cam.pixelWidth, cam.pixelHeight, 24);
        cam.targetTexture = rt;
        cam.Render();

        if (previewImage != null && !AssetDatabase.Contains(previewImage)) DestroyImmediate(previewImage);
        previewImage = new Texture2D(cam.pixelWidth, cam.pixelHeight, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        previewImage.ReadPixels(new Rect(0, 0, cam.pixelWidth, cam.pixelHeight), 0, 0);
        previewImage.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        PrepareProcessedSource();
        UpdatePreview(true);
        Repaint();
    }

    private void PrepareProcessedSource()
    {
        if (previewImage == null) return;

        if (!useLowResPreview)
        {
            if (processedSource != null && processedSource != previewImage && processedSource != smallPreview) DestroyImmediate(processedSource);
            processedSource = previewImage;
        }
        else
        {
            int targetWidth = 512;
            int targetHeight = Mathf.RoundToInt(targetWidth * ((float)previewImage.height / previewImage.width));

            if (smallPreview == null || smallPreview.width != targetWidth || smallPreview.height != targetHeight)
            {
                if (smallPreview != null) DestroyImmediate(smallPreview);
                smallPreview = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            }

            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
            Graphics.Blit(previewImage, rt);
            RenderTexture activeRT = RenderTexture.active;
            RenderTexture.active = rt;
            smallPreview.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            smallPreview.Apply();
            RenderTexture.active = activeRT;
            RenderTexture.ReleaseTemporary(rt);

            processedSource = smallPreview;
        }
    }

    private void GenerateLutTexture()
    {
        int width = lutQuality * lutQuality;
        if (lutStrip == null || lutStrip.width != width || lutStrip.height != lutQuality)
            lutStrip = new Texture2D(width, lutQuality, TextureFormat.RGB24, false);

        Color[] pixels = new Color[width * lutQuality];
        for (int z = 0; z < lutQuality; z++)
        {
            for (int y = 0; y < lutQuality; y++)
            {
                for (int x = 0; x < lutQuality; x++)
                {
                    float r = (float)x / (lutQuality - 1);
                    float g = (float)y / (lutQuality - 1);
                    float b = (float)z / (lutQuality - 1);
                    Color processed = ApplyAdjustments(new Color(r, g, b));
                    pixels[y * width + (z * lutQuality + x)] = processed;
                }
            }
        }
        lutStrip.SetPixels(pixels);
        lutStrip.Apply();
    }

    private void UpdatePreview(bool force)
    {
        if (processedSource == null) return;

        double currentTime = EditorApplication.timeSinceStartup;
        if (!force && currentTime - lastPreviewUpdateTime < PREVIEW_UPDATE_INTERVAL) return;

        lastPreviewUpdateTime = currentTime;
        needsUpdate = false;

        if (modifiedPreview == null || modifiedPreview.width != processedSource.width || modifiedPreview.height != processedSource.height)
        {
            if (modifiedPreview != null) DestroyImmediate(modifiedPreview);
            modifiedPreview = new Texture2D(processedSource.width, processedSource.height, TextureFormat.RGB24, false);
        }

        Color[] pixels = processedSource.GetPixels();
        for (int i = 0; i < pixels.Length; i++) pixels[i] = ApplyAdjustments(pixels[i]);
        modifiedPreview.SetPixels(pixels);
        modifiedPreview.Apply();
    }

    private Color ApplyAdjustments(Color col)
    {
        // 1. Exposure & Brightness
        col.r = (col.r * exposure) + brightness;
        col.g = (col.g * exposure) + brightness;
        col.b = (col.b * exposure) + brightness;

        // 2. Advanced Filmic S-Curve
        col.r = ApplyFilmic(col.r);
        col.g = ApplyFilmic(col.g);
        col.b = ApplyFilmic(col.b);

        // 3. Contrast
        col.r = (col.r - 0.5f) * contrast + 0.5f;
        col.g = (col.g - 0.5f) * contrast + 0.5f;
        col.b = (col.b - 0.5f) * contrast + 0.5f;

        // 4. Tonal Range
        float lum = (col.r + col.g + col.b) / 3.0001f;
        float sW = 1f - lum;
        float hW = lum;
        float mW = Mathf.Max(0, 1f - Mathf.Abs(lum - 0.5f) * 2f);

        Color liftCol = GetColorFromWheelPosition(wheelPositions[0]);
        Color gammaCol = GetColorFromWheelPosition(wheelPositions[1]);
        Color gainCol = GetColorFromWheelPosition(wheelPositions[2]);

        col.r += (liftCol.r - 1f) * sW + lumaValues[0] * sW + (gainCol.r - 1f) * hW + lumaValues[2] * hW + (gammaCol.r - 1f) * mW + lumaValues[1] * mW;
        col.g += (liftCol.g - 1f) * sW + lumaValues[0] * sW + (gainCol.g - 1f) * hW + lumaValues[2] * hW + (gammaCol.g - 1f) * mW + lumaValues[1] * mW;
        col.b += (liftCol.b - 1f) * sW + lumaValues[0] * sW + (gainCol.b - 1f) * hW + lumaValues[2] * hW + (gammaCol.b - 1f) * mW + lumaValues[1] * mW;

        // 5. Curves
        col.r = masterCurve.Evaluate(Mathf.Clamp01(redCurve.Evaluate(Mathf.Clamp01(col.r))));
        col.g = masterCurve.Evaluate(Mathf.Clamp01(greenCurve.Evaluate(Mathf.Clamp01(col.g))));
        col.b = masterCurve.Evaluate(Mathf.Clamp01(blueCurve.Evaluate(Mathf.Clamp01(col.b))));

        // 6. White Balance & Saturation
        col.r += temperature * 0.1f;
        col.b -= temperature * 0.1f;
        col.g += tint * 0.05f;

        float h, s, v;
        Color.RGBToHSV(col, out h, out s, out v);
        s *= saturation * (1.0f + (1.0f - s) * (vibrance - 1.0f));
        col = Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));

        return new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b));
    }

    private float ApplyFilmic(float x)
    {
        float toe = Mathf.Pow(Mathf.Max(0, x), 1f + toeStrength);
        float shoulder = 1f - Mathf.Exp(-x * (1f / Mathf.Max(0.01f, 1f - shoulderStrength)));
        float res = Mathf.Lerp(toe, shoulder, x);
        return Mathf.Pow(res, 1f / curveSlope);
    }

    private void SaveLutAsPng(bool quick = false)
    {
        if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
        string fullPath = quick ? $"{outputFolder}/{lutName}.png" : EditorUtility.SaveFilePanelInProject("Save Filmic LUT", lutName, "png", "Save", outputFolder);
        if (string.IsNullOrEmpty(fullPath)) return;

        byte[] bytes = lutStrip.EncodeToPNG();
        File.WriteAllBytes(fullPath, bytes);
        AssetDatabase.ImportAsset(fullPath);

        TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
        if (importer != null)
        {
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        if (!quick) EditorUtility.DisplayDialog("Success", "Filmic LUT Export Successful!", "OK");
    }
}