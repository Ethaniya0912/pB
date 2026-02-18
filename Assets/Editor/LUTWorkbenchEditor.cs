using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// LUTPreset ScriptableObject와 연동되어 실시간 보정 및 베이킹을 지원하는 통합 에디터 윈도우입니다.
/// 이미지 샘플링, SCENE/GAME 캡처 분화, 컬러 체커 생성 기능이 포함되었습니다.
/// </summary>
public class LUTWorkbenchEditor : EditorWindow
{
    // --- 연동 및 데이터 관리 ---
    private LUTPreset activePreset;
    private LUTPreset workingCopy;
    private bool isDirty = false;

    // --- 언어 설정 (Localization) ---
    private bool isEnglish = false;

    // --- 비동기 및 성능 상태 ---
    private bool isProcessingAsync = false;
    private bool needsUpdateAfterProcess = false;
    private float lastInteractionTime = 0f;
    private float lastDragUpdateTime = 0f;
    private const float UPDATE_DELAY = 0.03f;
    private bool needsUpdate = false;

    // --- 에디터 UI 상태 ---
    private int activeTab = 0; // 0: BASIC, 1: A/B GRID
    private int selectedTonalRange = 0;

    // --- A/B Grid 데이터 및 설정 ---
    private enum GridShape { Circle, Square }
    private GridShape currentGridShape = GridShape.Circle;
    private const int AB_GRID_RES_H = 12;
    private const int AB_GRID_RES_S = 8;
    private Vector2[,] abGridPoints;
    private Vector2[,] abOriginalPoints;
    private bool[,] abGridPinned;
    private List<Vector2Int> selectedGridPoints = new List<Vector2Int>();
    private Vector2Int hoveredGridPoint = new Vector2Int(-1, -1);
    private bool isDraggingGrid = false;
    private Vector2 lastMousePos;

    // --- 이미지 샘플링 제어 ---
    private bool isSamplingFromImage = false;
    private Vector2 samplingStartPos;

    // --- 워프 캐시 ---
    private Vector2[,] warpCache;
    private const int CACHE_RES = 64;
    private bool needsCacheUpdate = true;

    // --- 3D 시각화 설정 및 데이터 ---
    private Vector2 cubeRotation = new Vector2(20, -45);
    private float cubeScale = 140f;
    private Vector2 cubePanOffset = Vector2.zero;
    private Color[] cachedCubeColors;
    private Vector3[] cachedCubePositions;
    private bool needsCubeRecalc = true;

    public enum DensityOption { Full_32 = 1, High_16 = 2, Medium_8 = 4, Low_4 = 8 }
    public enum PointSizeOption { Tiny = 0, Small = 1, Normal = 2, Large = 3 }
    private DensityOption currentDensity = DensityOption.High_16;
    private PointSizeOption currentPointSize = PointSizeOption.Normal;
    private float[] pointSizeValues = { 0.5f, 0.8f, 1.2f, 2.0f };
    private bool autoScaleCube = true;

    // --- 성능 및 프리뷰 최적화 ---
    private bool useLowResPreview = true;

    // --- 텍스처 데이터 ---
    private Texture2D previewImage;
    private Texture2D smallPreview;
    private Texture2D processedSource;
    private Texture2D modifiedPreview;
    private Texture2D realtimeLutStrip;
    private Texture2D neutralLutStrip;
    private Texture2D colorWheelTex;
    private Texture2D abGridBackground;

    // --- UI 스타일 ---
    private Vector2 leftScrollPos;
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle sectionBoxStyle;
    private GUIStyle infoTextStyle;
    private GUIStyle labelWhiteStyle;
    private GUIStyle reportTextStyle;
    private GUIStyle metaBoxStyle;
    private Texture2D helpIcon;

    private Color colorBgMain = new Color(0.07f, 0.07f, 0.08f, 1f);
    private Color colorPanel = new Color(0.12f, 0.12f, 0.13f, 1f);
    private Color colorHeader = new Color(0.16f, 0.16f, 0.18f, 1f);
    private Color colorAccent = new Color(0.25f, 0.42f, 0.68f, 1f);
    private Color colorTextDim = new Color(0.45f, 0.45f, 0.50f, 1f);
    private Color activeColor = new Color(0.20f, 0.60f, 0.35f, 1f);

    [MenuItem("Tools/LUT Workbench Pro")]
    public static void ShowWindow()
    {
        LUTWorkbenchEditor window = GetWindow<LUTWorkbenchEditor>("LUT Workbench Pro");
        window.minSize = new Vector2(1250, 900);
    }

    private void OnEnable()
    {
        helpIcon = (Texture2D)EditorGUIUtility.IconContent("_Help").image;
        CreateColorRingTexture();
        CreateNeutralStrip();
        InitializeABGrid();
        CreateABBackgroundTexture();

        EditorApplication.delayCall += () => {
            if (previewImage == null) GenerateColorChecker(); // 초기 상태에 컬러체커 로드
            TriggerFullUpdate();
        };
    }

    private void Update()
    {
        if (needsUpdate && !isProcessingAsync && Time.realtimeSinceStartup - lastInteractionTime > UPDATE_DELAY)
        {
            TriggerFullUpdate();
        }
    }

    private string T(string kr, string en) => isEnglish ? en : kr;

    private async void TriggerFullUpdate()
    {
        if (isProcessingAsync || workingCopy == null) return;
        isProcessingAsync = true;
        needsUpdate = false;

        if (needsCacheUpdate)
        {
            var newWarpCache = await Task.Run(() => ComputeWarpCacheAsync());
            warpCache = newWarpCache;
            needsCacheUpdate = false;
        }

        Color[] sourcePixels = (processedSource != null) ? processedSource.GetPixels() : null;
        int sourceW = (processedSource != null) ? processedSource.width : 0;
        int sourceH = (processedSource != null) ? processedSource.height : 0;

        var results = await Task.Run(() => ComputeAllAsync(sourcePixels));

        if (results.lutPixels != null)
        {
            if (realtimeLutStrip == null)
            {
                realtimeLutStrip = new Texture2D(32 * 32, 32, TextureFormat.RGB24, false);
                realtimeLutStrip.filterMode = FilterMode.Point;
            }
            realtimeLutStrip.SetPixels(results.lutPixels);
            realtimeLutStrip.Apply();
        }

        if (results.previewResult != null && sourcePixels != null)
        {
            if (modifiedPreview == null || modifiedPreview.width != sourceW || modifiedPreview.height != sourceH)
                modifiedPreview = new Texture2D(sourceW, sourceH, TextureFormat.RGB24, false);

            modifiedPreview.SetPixels(results.previewResult);
            modifiedPreview.Apply();
        }

        cachedCubePositions = results.cubePos;
        cachedCubeColors = results.cubeCols;

        if (autoScaleCube)
        {
            float maxDim = Mathf.Max(results.boundsMax.x - results.boundsMin.x,
                                    Mathf.Max(results.boundsMax.y - results.boundsMin.y,
                                             results.boundsMax.z - results.boundsMin.z));
            if (maxDim > 0.01f) cubeScale = 180f / maxDim;
        }

        needsCubeRecalc = false;
        isProcessingAsync = false;
        Repaint();

        if (needsUpdateAfterProcess)
        {
            needsUpdateAfterProcess = false;
            needsUpdate = true;
        }
    }

    private Vector2[,] ComputeWarpCacheAsync()
    {
        Vector2[,] cache = new Vector2[CACHE_RES, CACHE_RES];
        for (int y = 0; y < CACHE_RES; y++)
        {
            for (int x = 0; x < CACHE_RES; x++)
            {
                Vector2 targetPos = new Vector2((float)x / (CACHE_RES - 1) * 2f - 1f, (float)y / (CACHE_RES - 1) * 2f - 1f);
                Vector2 offset = Vector2.zero;
                float totalWeight = 0;
                for (int i = 0; i <= AB_GRID_RES_S; i++)
                {
                    for (int j = 0; j < AB_GRID_RES_H; j++)
                    {
                        float d = Vector2.SqrMagnitude(targetPos - abOriginalPoints[i, j]);
                        float w = Mathf.Exp(-d * 15.0f);
                        offset += (abGridPoints[i, j] - abOriginalPoints[i, j]) * w;
                        totalWeight += w;
                    }
                }
                cache[x, y] = offset / Mathf.Max(totalWeight, 0.0001f);
            }
        }
        return cache;
    }

    private struct AsyncResults
    {
        public Color[] lutPixels;
        public Vector3[] cubePos;
        public Color[] cubeCols;
        public Color[] previewResult;
        public Vector3 boundsMin;
        public Vector3 boundsMax;
    }

    private AsyncResults ComputeAllAsync(Color[] sourcePixels)
    {
        int q = 32;
        AsyncResults res = new AsyncResults();
        res.lutPixels = new Color[q * q * q];
        res.cubePos = new Vector3[q * q * q];
        res.cubeCols = new Color[q * q * q];
        res.boundsMin = Vector3.one * 1000f;
        res.boundsMax = Vector3.one * -1000f;

        int idx = 0;
        for (int z = 0; z < q; z++)
        {
            for (int y = 0; y < q; y++)
            {
                for (int x = 0; x < q; x++)
                {
                    float r = (float)x / (q - 1), g = (float)y / (q - 1), b = (float)z / (q - 1);
                    Color warped = InternalApplyWarp(workingCopy.ApplyAdjustments(new Color(r, g, b)));
                    res.lutPixels[y * (q * q) + (z * q + x)] = warped;

                    Vector3 pos = new Vector3(warped.r - 0.5f, warped.g - 0.5f, warped.b - 0.5f);
                    res.cubePos[idx] = pos;
                    res.cubeCols[idx] = warped;

                    res.boundsMin = Vector3.Min(res.boundsMin, pos);
                    res.boundsMax = Vector3.Max(res.boundsMax, pos);
                    idx++;
                }
            }
        }

        if (sourcePixels != null)
        {
            res.previewResult = new Color[sourcePixels.Length];
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                res.previewResult[i] = InternalApplyWarp(workingCopy.ApplyAdjustments(sourcePixels[i]));
            }
        }
        return res;
    }

    private Color InternalApplyWarp(Color col)
    {
        if (warpCache == null) return col;
        float h, s, v;
        Color.RGBToHSV(col, out h, out s, out v);
        Vector2 coord = (currentGridShape == GridShape.Circle) ? new Vector2(Mathf.Cos(h * Mathf.PI * 2) * s, Mathf.Sin(h * Mathf.PI * 2) * s) : new Vector2(col.r * 2f - 1f, col.g * 2f - 1f);
        float uRaw = (coord.x + 1f) * 0.5f * (CACHE_RES - 1);
        float vRaw = (coord.y + 1f) * 0.5f * (CACHE_RES - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(uRaw), 0, CACHE_RES - 1);
        int x1 = Mathf.Min(x0 + 1, CACHE_RES - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(vRaw), 0, CACHE_RES - 1);
        int y1 = Mathf.Min(y0 + 1, CACHE_RES - 1);
        Vector2 off = Vector2.Lerp(Vector2.Lerp(warpCache[x0, y0], warpCache[x1, y0], uRaw - x0), Vector2.Lerp(warpCache[x0, y1], warpCache[x1, y1], uRaw - x0), vRaw - y0);
        Vector2 warped = coord + off;
        if (currentGridShape == GridShape.Circle) return Color.HSVToRGB((Mathf.Atan2(warped.y, warped.x) * Mathf.Rad2Deg + 360f) % 360f / 360f, Mathf.Clamp01(warped.magnitude), v);
        else return new Color(Mathf.Clamp01((warped.x + 1f) * 0.5f), Mathf.Clamp01((warped.y + 1f) * 0.5f), col.b);
    }

    private void OnGUI()
    {
        InitStyles();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colorBgMain);
        DrawTopToolbar();

        if (activePreset == null)
        {
            leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos, GUILayout.Width(400));
            DrawAssetMetaPanel();
            DrawEmptyState();
            EditorGUILayout.EndScrollView();
            return;
        }
        if (workingCopy == null) workingCopy = Instantiate(activePreset);

        EditorGUILayout.BeginHorizontal();
        Rect leftPanelRect = EditorGUILayout.BeginVertical(GUILayout.Width(400), GUILayout.MinWidth(400));
        EditorGUI.DrawRect(leftPanelRect, colorPanel);
        GUILayout.Space(5);

        int prevTab = activeTab;
        activeTab = GUILayout.Toolbar(activeTab, new string[] { T("기본 보정", "BASIC CONTROLS"), T("A/B 그리드", "A/B COLOR GRID") }, GUILayout.Height(25));
        if (prevTab != activeTab) GUI.FocusControl(null);
        GUILayout.Space(5);

        leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos);
        if (activeTab == 0) DrawSettingsAndAnalysis();
        else DrawABGridTab();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        DrawMainLivePreview();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();

        if (needsUpdate || isDraggingGrid || isProcessingAsync || isSamplingFromImage) Repaint();
    }

    private void DrawTopToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUI.backgroundColor = activeColor;
        GUI.enabled = isDirty;
        if (GUILayout.Button(T("저장", "SAVE"), EditorStyles.toolbarButton, GUILayout.Width(50))) ApplyToAsset();

        GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
        if (GUILayout.Button(T("취소", "UNDO"), EditorStyles.toolbarButton, GUILayout.Width(50))) { workingCopy = Instantiate(activePreset); isDirty = false; MarkNeedsUpdate(); }

        GUI.backgroundColor = colorHeader;
        GUI.enabled = true;
        if (GUILayout.Button(T("초기화", "RESET"), EditorStyles.toolbarButton, GUILayout.Width(50))) ResetToStandard();

        GUI.backgroundColor = Color.white;
        GUILayout.Space(15);

        EditorGUILayout.LabelField("PRO", EditorStyles.boldLabel, GUILayout.Width(35));
        GUILayout.Space(5);

        // --- 요청: 캡처 및 컬러체커 버튼들 ---
        if (GUILayout.Button(T("SCENE", "SCENE"), EditorStyles.toolbarButton, GUILayout.Width(50))) CaptureSceneView();
        if (GUILayout.Button(T("GAME", "GAME"), EditorStyles.toolbarButton, GUILayout.Width(50))) CaptureGameView();
        if (GUILayout.Button(T("체커", "CHECKER"), EditorStyles.toolbarButton, GUILayout.Width(60))) GenerateColorChecker();

        if (GUILayout.Button(T("생성", "CREATE"), EditorStyles.toolbarButton, GUILayout.Width(60))) CreateNewPreset();

        GUILayout.FlexibleSpace();

        if (GUILayout.Button(isEnglish ? "🇺🇸 EN" : "🇰🇷 KR", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            isEnglish = !isEnglish;
            Repaint();
        }

        useLowResPreview = GUILayout.Toggle(useLowResPreview, T("최적화", "Optimization"), EditorStyles.toolbarButton);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEmptyState()
    {
        EditorGUILayout.BeginVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginVertical(sectionBoxStyle, GUILayout.Width(350));
        EditorGUILayout.LabelField(T("LUT 워크벤치 프로", "LUT Workbench Pro"), headerStyle);
        EditorGUILayout.HelpBox(T("보정을 시작하려면 LUTPreset 에셋을 선택하세요.", "Select a LUTPreset asset to begin."), MessageType.Info);
        if (GUILayout.Button(T("새 프리셋 생성", "Create New Preset"), GUILayout.Height(30))) CreateNewPreset();
        EditorGUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();
    }

    private void DrawSettingsAndAnalysis()
    {
        DrawAssetMetaPanel();

        EditorGUI.BeginChangeCheck();
        DrawHeaderWithTooltip(T("색상 변환", "TRANSFORMATIONS"), T("전역적인 색상 및 톤 조절 항목입니다.", "Basic global color and tone adjustments."));
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        workingCopy.contrast = DrawSliderWithTooltip(T("대비", "Contrast"), workingCopy.contrast, 0f, 2f, T("밝은 곳과 어두운 곳의 차이를 조절합니다.", "Adjusts the difference between light and dark areas."));
        workingCopy.saturation = DrawSliderWithTooltip(T("채도", "Saturation"), workingCopy.saturation, 0f, 2f, T("모든 색상의 강도를 조절합니다.", "Adjusts the intensity of all colors."));
        workingCopy.vibrance = DrawSliderWithTooltip(T("생동감", "Vibrance"), workingCopy.vibrance, 0f, 2f, T("채도가 낮은 색상을 우선적으로 강조합니다.", "Boosts lower-saturated colors more than already saturated ones."));
        workingCopy.tint = DrawSliderWithTooltip(T("색조", "Hue Shift"), workingCopy.tint, -1f, 1f, T("전체 색상을 색상환 기준으로 회전시킵니다.", "Rotates all colors around the hue spectrum."));
        workingCopy.temperature = DrawSliderWithTooltip(T("온도", "Temperature"), workingCopy.temperature, -1f, 1f, T("푸른색(차가움)과 주황색(따뜻함) 사이의 균형을 맞춥니다.", "Shifts color balance between Blue (Cold) and Orange (Warm)."));
        workingCopy.brightness = DrawSliderWithTooltip(T("오프셋", "Offset"), workingCopy.brightness, -1f, 1f, T("최저 밝기(블랙 포인트) 레벨을 조절합니다.", "Adjusts the brightness level of the black point."));
        workingCopy.exposure = DrawSliderWithTooltip(T("노출", "Exposure"), workingCopy.exposure, 0f, 5f, T("전체적인 빛의 세기를 곱연산합니다.", "Multiplies overall light intensity."));
        EditorGUILayout.EndVertical();

        DrawHeaderWithTooltip(T("토널 휠", "TONAL WHEELS"), T("특정 밝기 영역에 대한 색상 치우침을 조절합니다.", "Directional color tinting for specific luminosity ranges."));
        EditorGUILayout.BeginVertical(sectionBoxStyle);

        string liftTooltip = T("Shadows 영역의 색조와 밝기를 조절합니다.", "Shadows tint and brightness.");
        string gammaTooltip = T("Midtones 영역의 색조와 밝기를 조절합니다.", "Midtones tint and brightness.");
        string gainTooltip = T("Highlights 영역의 색조와 밝기를 조절합니다.", "Highlights tint and brightness.");

        selectedTonalRange = GUILayout.Toolbar(selectedTonalRange, new GUIContent[] {
            new GUIContent(T("리프트", "Lift"), helpIcon, liftTooltip),
            new GUIContent(T("감마", "Gamma"), helpIcon, gammaTooltip),
            new GUIContent(T("게인", "Gain"), helpIcon, gainTooltip)
        }, GUILayout.Height(25));

        DrawSelectedTonalWheel();
        EditorGUILayout.EndVertical();

        DrawHeaderWithTooltip(T("고급 필름 S-커브", "ADVANCED FILMIC S-CURVE"), T("명암의 부드러운 전환과 다이나믹 레인지를 제어합니다.", "Fine-control over high-dynamic range roll-off."));
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        workingCopy.toeStrength = DrawSliderWithTooltip(T("섀도우 토", "Shadow Toe"), workingCopy.toeStrength, 0f, 1f, T("어두운 영역의 디테일을 압착하여 묵직함을 더합니다.", "Compresses shadow detail."));
        workingCopy.shoulderStrength = DrawSliderWithTooltip(T("하이라이트 숄더", "Highlight Shoulder"), workingCopy.shoulderStrength, 0f, 1f, T("밝은 영역이 하얗게 날아가는 전환을 부드럽게 합니다.", "Softens white roll-off."));
        workingCopy.curveSlope = DrawSliderWithTooltip(T("기울기", "Dynamic Slope"), workingCopy.curveSlope, 0.5f, 2.0f, T("중간 톤의 대비 기울기를 조절합니다.", "Mid-tone contrast steepness."));
        EditorGUILayout.EndVertical();

        DrawAnalysisSection();
        if (EditorGUI.EndChangeCheck()) { isDirty = true; MarkNeedsUpdate(); }
    }

    private void DrawAssetMetaPanel()
    {
        EditorGUILayout.BeginVertical(metaBoxStyle);
        EditorGUILayout.LabelField(new GUIContent(T("에셋 및 설정 정보", "ASSET & SETTINGS INFO"), helpIcon, T("현재 활성화된 에셋의 상세 정보와 주요 파라미터 요약입니다.", "Details and summarized parameters.")), EditorStyles.miniBoldLabel);
        GUILayout.Space(5);

        EditorGUI.BeginChangeCheck();
        activePreset = (LUTPreset)EditorGUILayout.ObjectField(T("활성 에셋", "Active Asset"), activePreset, typeof(LUTPreset), false);
        if (EditorGUI.EndChangeCheck() && activePreset != null)
        {
            workingCopy = Instantiate(activePreset);
            TriggerFullUpdate();
        }

        if (activePreset != null)
        {
            EditorGUILayout.LabelField($"{T("경로", "Path")}: {AssetDatabase.GetAssetPath(activePreset)}", infoTextStyle);
            string metaValues = $"Exp: {workingCopy.exposure:F1} | Ctr: {workingCopy.contrast:F1} | Sat: {workingCopy.saturation:F1} | Vib: {workingCopy.vibrance:F1}";
            EditorGUILayout.LabelField(metaValues, infoTextStyle);
            string balValues = $"Temp: {workingCopy.temperature:F1} | Tint: {workingCopy.tint:F1}";
            EditorGUILayout.LabelField(balValues, infoTextStyle);
            EditorGUILayout.LabelField($"{T("품질", "LUT Quality")}: {workingCopy.lutQuality}^3", infoTextStyle);
        }
        else
        {
            EditorGUILayout.HelpBox(T("편집할 에셋을 연결하세요.", "Connect an asset to edit."), MessageType.None);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawABGridTab()
    {
        DrawAssetMetaPanel();
        DrawHeaderWithTooltip(T("A/B 크로마티시티 그리드", "A/B CHROMATICITY GRID"), T("색상 평면을 워핑하여 독창적인 스타일을 만듭니다.", "Warp color chromaticity planes."));
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        EditorGUI.BeginChangeCheck();
        currentGridShape = (GridShape)EditorGUILayout.EnumPopup(new GUIContent(T("그리드 모드", "Grid Mode"), helpIcon, T("원형 또는 사각형 제어 맵 사이를 전환합니다.", "Circular or Square mapping.")), currentGridShape);
        if (EditorGUI.EndChangeCheck()) { InitializeABGrid(); CreateABBackgroundTexture(); MarkNeedsUpdate(); }

        Rect gridArea = GUILayoutUtility.GetRect(360, 360);
        float side = Mathf.Min(gridArea.width, gridArea.height);
        Rect squareArea = new Rect(gridArea.center.x - side / 2, gridArea.center.y - side / 2, side, side);

        if (abGridBackground != null) GUI.DrawTexture(squareArea, abGridBackground);
        DrawInteractiveABGrid(squareArea);

        GUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(T("모두 고정", "PIN ALL"), EditorStyles.miniButton)) SetAllPins(true);
        if (GUILayout.Button(T("모두 해제", "UNPIN ALL"), EditorStyles.miniButton)) SetAllPins(false);
        if (GUILayout.Button(T("그리드 초기화", "RESET GRID"), EditorStyles.miniButton)) InitializeABGrid();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        DrawAnalysisSection();
        if (isDraggingGrid) { isDirty = true; MarkNeedsUpdate(); }
    }

    private void DrawAnalysisSection()
    {
        DrawHeaderWithTooltip(T("3D 벡터 공간 (32^3)", "3D VECTOR SPACE (32^3)"), T("색상이 3D RGB 공간에서 어떻게 변환되는지 시각화합니다.", "Visualize 3D RGB transformation."));
        EditorGUILayout.BeginVertical(sectionBoxStyle);

        EditorGUILayout.BeginHorizontal();
        autoScaleCube = EditorGUILayout.ToggleLeft(T("자동 줌", "Auto Zoom"), autoScaleCube, GUILayout.Width(80));
        GUILayout.FlexibleSpace();

        EditorGUIUtility.labelWidth = 55;
        EditorGUI.BeginChangeCheck();
        currentDensity = (DensityOption)EditorGUILayout.EnumPopup(new GUIContent(T("밀도", "Density")), currentDensity, GUILayout.Width(130));
        currentPointSize = (PointSizeOption)EditorGUILayout.EnumPopup(new GUIContent(T("크기", "Size")), currentPointSize, GUILayout.Width(110));
        if (EditorGUI.EndChangeCheck()) Repaint();
        EditorGUIUtility.labelWidth = 0;
        EditorGUILayout.EndHorizontal();

        DrawHighRes3DCube(360, 260);
        DrawStatisticalDashboard();
        EditorGUILayout.EndVertical();

        DrawHeaderWithTooltip(T("LUT 스트립 비교", "LUT STRIP COMPARE"), T("표준 2D LUT 표현 방식입니다.", "Standard 2D LUT representation."));
        EditorGUILayout.BeginVertical(sectionBoxStyle);
        EditorGUILayout.LabelField(T("중립 레퍼런스", "Neutral Reference"), infoTextStyle);
        GUI.DrawTexture(GUILayoutUtility.GetRect(360, 15), neutralLutStrip, ScaleMode.StretchToFill);
        GUILayout.Space(5);
        EditorGUILayout.LabelField(T("활성 변환", "Active Transformation"), infoTextStyle);
        if (realtimeLutStrip != null) GUI.DrawTexture(GUILayoutUtility.GetRect(380, 15), realtimeLutStrip, ScaleMode.StretchToFill);
        else EditorGUILayout.LabelField(T("계산 중...", "Computing..."), infoTextStyle);
        EditorGUILayout.EndVertical();
    }

    // --- 핵심 헬퍼 메서드 ---

    private void MarkNeedsUpdate()
    {
        needsUpdate = true;
        lastInteractionTime = Time.realtimeSinceStartup;
        if (isProcessingAsync) needsUpdateAfterProcess = true;
    }

    private void DrawHeaderWithTooltip(string label, string tooltip)
    {
        Rect r = GUILayoutUtility.GetRect(0, 22);
        EditorGUI.DrawRect(r, colorHeader);
        GUIContent content = new GUIContent(label, helpIcon, tooltip);
        EditorGUI.LabelField(new Rect(r.x + 10, r.y, r.width - 20, r.height), content, EditorStyles.miniBoldLabel);
    }

    private float DrawSliderWithTooltip(string label, float val, float min, float max, string tooltip)
    {
        EditorGUILayout.BeginHorizontal();
        GUIContent content = new GUIContent(label, helpIcon, tooltip);
        EditorGUILayout.LabelField(content, labelWhiteStyle, GUILayout.Width(110));
        Rect r = GUILayoutUtility.GetRect(130, 18);
        float prog = Mathf.Clamp01((val - min) / (max - min));
        EditorGUI.DrawRect(new Rect(r.x, r.y + 7, r.width, 3), new Color(0.06f, 0.06f, 0.07f, 1f));
        EditorGUI.DrawRect(new Rect(r.x, r.y + 7, r.width * prog, 3), colorAccent);
        float nVal = GUI.HorizontalSlider(r, val, min, max, GUIStyle.none, GUIStyle.none);
        EditorGUI.DrawRect(new Rect(r.x + (r.width * prog) - 2, r.y + 2, 4, 14), Color.white);
        nVal = EditorGUILayout.FloatField(nVal, GUILayout.Width(45));
        EditorGUILayout.EndHorizontal();
        return nVal;
    }

    private void CaptureSceneView()
    {
        if (!SceneView.lastActiveSceneView) return;
        Camera cam = SceneView.lastActiveSceneView.camera;
        CaptureCamera(cam);
    }

    private void CaptureGameView()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = GameObject.FindObjectOfType<Camera>();
        if (cam == null) return;
        CaptureCamera(cam);
    }

    private void CaptureCamera(Camera cam)
    {
        RenderTexture rt = new RenderTexture(cam.pixelWidth, cam.pixelHeight, 24);
        cam.targetTexture = rt; cam.Render();
        if (previewImage) DestroyImmediate(previewImage);
        previewImage = new Texture2D(cam.pixelWidth, cam.pixelHeight, TextureFormat.RGB24, false);
        RenderTexture.active = rt; previewImage.ReadPixels(new Rect(0, 0, cam.pixelWidth, cam.pixelHeight), 0, 0);
        previewImage.Apply(); cam.targetTexture = null; RenderTexture.active = null; RenderTexture.ReleaseTemporary(rt);
        PrepareProcessedSource(); TriggerFullUpdate(); Repaint();
    }

    private void GenerateColorChecker()
    {
        int w = 600, h = 400;
        if (previewImage) DestroyImmediate(previewImage);
        previewImage = new Texture2D(w, h, TextureFormat.RGB24, false);

        Color[] patches = {
            new Color(0.45f, 0.32f, 0.24f), new Color(0.76f, 0.58f, 0.50f), new Color(0.36f, 0.47f, 0.58f), new Color(0.35f, 0.42f, 0.25f), new Color(0.50f, 0.48f, 0.69f), new Color(0.38f, 0.74f, 0.67f),
            new Color(0.85f, 0.49f, 0.17f), new Color(0.27f, 0.36f, 0.71f), new Color(0.75f, 0.33f, 0.38f), new Color(0.36f, 0.23f, 0.42f), new Color(0.62f, 0.74f, 0.24f), new Color(0.89f, 0.64f, 0.16f),
            new Color(0.11f, 0.24f, 0.59f), new Color(0.27f, 0.58f, 0.28f), new Color(0.68f, 0.20f, 0.24f), new Color(0.93f, 0.78f, 0.03f), new Color(0.73f, 0.32f, 0.62f), new Color(0.04f, 0.53f, 0.64f),
            new Color(0.95f, 0.95f, 0.94f), new Color(0.78f, 0.79f, 0.79f), new Color(0.63f, 0.63f, 0.64f), new Color(0.47f, 0.47f, 0.48f), new Color(0.33f, 0.34f, 0.34f), new Color(0.12f, 0.12f, 0.12f)
        };

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int px = x / (w / 6);
                int py = 3 - (y / (h / 4));
                int idx = Mathf.Clamp(py * 6 + px, 0, 23);
                previewImage.SetPixel(x, y, patches[idx]);
            }
        }
        previewImage.Apply();
        PrepareProcessedSource(); TriggerFullUpdate(); Repaint();
    }

    private void ApplyToAsset()
    {
        if (!activePreset || workingCopy == null) return;
        Undo.RecordObject(activePreset, "Apply");
        EditorUtility.CopySerialized(workingCopy, activePreset);
        EditorUtility.SetDirty(activePreset); AssetDatabase.SaveAssets(); isDirty = false;
    }

    private void InitializeABGrid()
    {
        abGridPoints = new Vector2[AB_GRID_RES_S + 1, AB_GRID_RES_H];
        abOriginalPoints = new Vector2[AB_GRID_RES_S + 1, AB_GRID_RES_H];
        abGridPinned = new bool[AB_GRID_RES_S + 1, AB_GRID_RES_H];
        for (int s = 0; s <= AB_GRID_RES_S; s++)
        {
            for (int h = 0; h < AB_GRID_RES_H; h++)
            {
                Vector2 pos = (currentGridShape == GridShape.Circle) ? new Vector2(Mathf.Cos((float)h / AB_GRID_RES_H * Mathf.PI * 2) * ((float)s / AB_GRID_RES_S), Mathf.Sin((float)h / AB_GRID_RES_H * Mathf.PI * 2) * ((float)s / AB_GRID_RES_S))
                    : new Vector2(((float)h / (AB_GRID_RES_H - 1)) * 2f - 1f, ((float)s / AB_GRID_RES_S) * 2f - 1f);
                abGridPoints[s, h] = abOriginalPoints[s, h] = pos;
            }
        }
        selectedGridPoints.Clear(); needsCacheUpdate = true; MarkNeedsUpdate();
    }

    private void CreateABBackgroundTexture()
    {
        int size = 256;
        if (abGridBackground != null) DestroyImmediate(abGridBackground);
        abGridBackground = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = ((float)x / size * 2f - 1f) / 0.98f;
                float v = ((float)y / size * 2f - 1f) / 0.98f;
                float angle = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
                if (angle < 0) angle += 360f;
                float mag = Mathf.Sqrt(u * u + v * v);
                Color col = Color.HSVToRGB(angle / 360f, Mathf.Clamp01(mag), 0.65f);
                if (currentGridShape == GridShape.Circle && mag > 1.0f) col = new Color(0, 0, 0, 0);
                abGridBackground.SetPixel(x, y, col);
            }
        }
        abGridBackground.Apply();
    }

    private void CreateColorRingTexture()
    {
        int size = 256;
        colorWheelTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size * 2f - 1f;
                float v = (float)y / size * 2f - 1f;
                float dist = Mathf.Sqrt(u * u + v * v);
                if (dist <= 1.0f)
                {
                    float angle = Mathf.Atan2(v, u) * Mathf.Rad2Deg;
                    if (angle < 0) angle += 360f;
                    Color col = Color.HSVToRGB(angle / 360f, dist, 1f);
                    colorWheelTex.SetPixel(x, y, col);
                }
                else colorWheelTex.SetPixel(x, y, Color.clear);
            }
        }
        colorWheelTex.Apply();
    }

    private void CreateNeutralStrip()
    {
        int q = 32;
        neutralLutStrip = new Texture2D(q * q, q, TextureFormat.RGB24, false);
        Color[] pixels = new Color[q * q * q];
        for (int z = 0; z < q; z++) for (int y = 0; y < q; y++) for (int x = 0; x < q; x++) pixels[y * (q * q) + (z * q + x)] = new Color((float)x / (q - 1), (float)y / (q - 1), (float)z / (q - 1));
        neutralLutStrip.SetPixels(pixels); neutralLutStrip.Apply();
    }

    private void DrawHighRes3DCube(float width, float height)
    {
        Rect r = GUILayoutUtility.GetRect(width, height);
        EditorGUI.DrawRect(r, new Color(0.04f, 0.04f, 0.05f, 1f));
        Event e = Event.current;
        if (r.Contains(e.mousePosition))
        {
            if (e.type == EventType.ScrollWheel) { cubeScale = Mathf.Clamp(cubeScale - e.delta.y * 5f, 20f, 3000f); autoScaleCube = false; Repaint(); e.Use(); }
            if (e.type == EventType.MouseDrag) { if (e.button == 0) { cubeRotation.y += e.delta.x * 0.5f; cubeRotation.x += e.delta.y * 0.5f; } else if (e.button == 2) { cubePanOffset += e.delta; autoScaleCube = false; } Repaint(); e.Use(); }
        }
        GUI.BeginGroup(r);
        Vector2 localCenter = new Vector2(width / 2f, height / 2f) + cubePanOffset;
        DrawCubeFrame(localCenter, new Color(1, 1, 1, 0.02f), 1.0f);
        DrawAxisGuides(localCenter);
        if (cachedCubePositions != null && !needsCubeRecalc)
        {
            int step = (int)currentDensity;
            float pSize = pointSizeValues[(int)currentPointSize];
            for (int i = 0; i < cachedCubePositions.Length; i += step)
            {
                Vector3 p3D = Quaternion.Euler(cubeRotation.x, cubeRotation.y, 0) * cachedCubePositions[i];
                float dSize = (0.5f + (p3D.z + 0.5f) * 1.0f) * pSize;
                EditorGUI.DrawRect(new Rect(localCenter.x + p3D.x * cubeScale - dSize * 0.5f, localCenter.y - p3D.y * cubeScale - dSize * 0.5f, dSize, dSize), cachedCubeColors[i]);
            }
        }
        GUI.EndGroup();
    }

    private void SetAllPins(bool state)
    {
        if (abGridPinned == null) return;
        for (int s = 0; s <= AB_GRID_RES_S; s++)
            for (int h = 0; h < AB_GRID_RES_H; h++) abGridPinned[s, h] = state;
    }

    private void DrawInteractiveABGrid(Rect area)
    {
        Vector2 center = area.center; float radius = area.width * 0.45f;
        Event e = Event.current;
        if (e.type == EventType.MouseUp && isDraggingGrid) { isDraggingGrid = false; needsCacheUpdate = true; MarkNeedsUpdate(); }

        Handles.BeginGUI(); Handles.color = new Color(1, 1, 1, 0.15f);
        for (int s = 0; s <= AB_GRID_RES_S; s++)
        {
            for (int h = 0; h < AB_GRID_RES_H; h++)
            {
                Vector3 p1 = new Vector3(center.x + abGridPoints[s, h].x * radius, center.y + abGridPoints[s, h].y * radius, 0);
                if (s < AB_GRID_RES_S) Handles.DrawLine(p1, new Vector3(center.x + abGridPoints[s + 1, h].x * radius, center.y + abGridPoints[s + 1, h].y * radius, 0));
                if (currentGridShape == GridShape.Circle || h < AB_GRID_RES_H - 1) Handles.DrawLine(p1, new Vector3(center.x + abGridPoints[s, (h + 1) % AB_GRID_RES_H].x * radius, center.y + abGridPoints[s, (h + 1) % AB_GRID_RES_H].y * radius, 0));
            }
        }
        Handles.EndGUI();

        for (int s = 0; s <= AB_GRID_RES_S; s++)
        {
            for (int h = 0; h < AB_GRID_RES_H; h++)
            {
                Vector2 pos = center + abGridPoints[s, h] * radius;
                Rect ptRect = new Rect(pos.x - 5, pos.y - 5, 10, 10);
                bool isSel = selectedGridPoints.Contains(new Vector2Int(s, h));
                bool isHover = hoveredGridPoint.x == s && hoveredGridPoint.y == h;

                if (isHover) EditorGUI.DrawRect(new Rect(pos.x - 7, pos.y - 7, 14, 14), new Color(0.2f, 1f, 0.3f, 0.8f));
                EditorGUI.DrawRect(ptRect, isSel ? Color.yellow : (abGridPinned[s, h] ? Color.red : Color.white));

                if (ptRect.Contains(e.mousePosition) && e.type == EventType.MouseDown)
                {
                    if (e.control)
                    {
                        Vector2Int idx = new Vector2Int(s, h);
                        if (isSel) selectedGridPoints.Remove(idx);
                        else selectedGridPoints.Add(idx);
                    }
                    else
                    {
                        if (!isSel)
                        {
                            selectedGridPoints.Clear();
                            selectedGridPoints.Add(new Vector2Int(s, h));
                        }
                    }
                    isDraggingGrid = true; lastMousePos = e.mousePosition; e.Use();
                }
            }
        }

        if (isDraggingGrid && e.type == EventType.MouseDrag && selectedGridPoints.Count > 0)
        {
            Vector2 delta = (e.mousePosition - lastMousePos) / radius; lastMousePos = e.mousePosition;
            foreach (var idx in selectedGridPoints)
            {
                Vector2 nPos = abGridPoints[idx.x, idx.y] + delta;
                if (currentGridShape == GridShape.Circle) nPos = Vector2.ClampMagnitude(nPos, 1.0f);
                else nPos = new Vector2(Mathf.Clamp(nPos.x, -1f, 1f), Mathf.Clamp(nPos.y, -1f, 1f));
                abGridPoints[idx.x, idx.y] = nPos;
            }
            for (int s = 0; s <= AB_GRID_RES_S; s++)
            {
                for (int h = 0; h < AB_GRID_RES_H; h++)
                {
                    Vector2Int cur = new Vector2Int(s, h); if (selectedGridPoints.Contains(cur) || abGridPinned[s, h]) continue;
                    float maxW = 0; foreach (var sel in selectedGridPoints) maxW = Mathf.Max(maxW, Mathf.Exp(-Vector2.SqrMagnitude(abOriginalPoints[s, h] - abOriginalPoints[sel.x, sel.y]) * 15.0f));
                    Vector2 wPos = abGridPoints[s, h] + delta * maxW;
                    if (currentGridShape == GridShape.Circle) wPos = Vector2.ClampMagnitude(wPos, 1.0f);
                    else wPos = new Vector2(Mathf.Clamp(wPos.x, -1f, 1f), Mathf.Clamp(wPos.y, -1f, 1f));
                    abGridPoints[s, h] = wPos;
                }
            }
            if (Time.realtimeSinceStartup - lastDragUpdateTime > 1.0f) { needsCacheUpdate = true; MarkNeedsUpdate(); lastDragUpdateTime = Time.realtimeSinceStartup; }
            Repaint(); e.Use();
        }
    }

    private void DrawAxisGuides(Vector2 center)
    {
        Handles.BeginGUI(); Vector3[] axes = { new Vector3(0.65f, 0, 0), new Vector3(0, 0.65f, 0), new Vector3(0, 0, 0.65f) }; Color[] cols = { Color.red, Color.green, Color.blue };
        Vector3 originTrans = Quaternion.Euler(cubeRotation.x, cubeRotation.y, 0) * -new Vector3(0.5f, 0.5f, 0.5f);
        Vector3 pOrigin = new Vector3(center.x + originTrans.x * cubeScale, center.y - originTrans.y * cubeScale, 0);
        for (int i = 0; i < 3; i++)
        {
            Vector3 endPos3D = axes[i] - new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 endTrans = Quaternion.Euler(cubeRotation.x, cubeRotation.y, 0) * endPos3D;
            Vector3 pEnd = new Vector3(center.x + endTrans.x * cubeScale, center.y - endTrans.y * cubeScale, 0);
            Handles.color = cols[i] * 0.6f; Handles.DrawLine(pOrigin, pEnd);
            Vector3 labelDir = (endTrans - originTrans).normalized;
            Vector2 labelPos = new Vector2(pEnd.x + labelDir.x * 20f, pEnd.y - labelDir.y * 20f);
            GUI.color = cols[i]; GUI.Label(new Rect(labelPos.x - 10, labelPos.y - 10, 20, 20), (i == 0 ? "R" : (i == 1 ? "G" : "B")), EditorStyles.miniBoldLabel);
        }
        GUI.color = Color.white; Handles.EndGUI();
    }

    private void DrawStatisticalDashboard()
    {
        DrawStatBar(T("글로벌 대비", "Global Contrast"), workingCopy.contrast / 2f, new Color(0.9f, 0.5f, 0.2f));
        DrawStatBar(T("색상 채도", "Color Saturation"), workingCopy.saturation / 2f, new Color(0.2f, 0.7f, 0.9f));
    }

    private void DrawStatBar(string label, float fill, Color col)
    {
        Rect r = GUILayoutUtility.GetRect(0, 16); EditorGUI.LabelField(new Rect(r.x + 5, r.y, 80, r.height), label, infoTextStyle);
        EditorGUI.DrawRect(new Rect(r.x + 90, r.y + 5, r.width - 100, 5), new Color(0.06f, 0.06f, 0.07f, 1f));
        EditorGUI.DrawRect(new Rect(r.x + 90, r.y + 5, (r.width - 100) * Mathf.Clamp01(fill), 5), col);
    }

    private void DrawCubeFrame(Vector2 center, Color col, float mult)
    {
        Vector3[] corners = { new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.5f, -.5f), new Vector3(.5f, .5f, -.5f), new Vector3(-.5f, .5f, -.5f), new Vector3(-.5f, -.5f, .5f), new Vector3(.5f, -.5f, .5f), new Vector3(.5f, .5f, .5f), new Vector3(-.5f, .5f, -.5f) };
        Vector2[] proj = new Vector2[8]; for (int i = 0; i < 8; i++)
        {
            Vector3 v = Quaternion.Euler(cubeRotation.x, cubeRotation.y, 0) * (corners[i] * mult);
            proj[i] = new Vector2(center.x + v.x * cubeScale, center.y - v.y * cubeScale);
        }
        Handles.BeginGUI(); Handles.color = col; for (int i = 0; i < 4; i++)
        {
            Handles.DrawLine(new Vector3(proj[i].x, proj[i].y, 0), new Vector3(proj[(i + 1) % 4].x, proj[(i + 1) % 4].y, 0));
            Handles.DrawLine(new Vector3(proj[i + 4].x, proj[i + 4].y, 0), new Vector3(proj[((i + 1) % 4) + 4].x, proj[((i + 1) % 4) + 4].y, 0));
            Handles.DrawLine(new Vector3(proj[i].x, proj[i].y, 0), new Vector3(proj[i + 4].x, proj[i + 4].y, 0));
        }
        Handles.EndGUI();
    }

    private void DrawSelectedTonalWheel()
    {
        float wheelSide = 180f; EditorGUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); Rect wheelRect = GUILayoutUtility.GetRect(wheelSide, wheelSide); GUILayout.FlexibleSpace(); EditorGUILayout.EndHorizontal();
        GUI.DrawTexture(wheelRect, colorWheelTex); Vector2 center = wheelRect.center;
        Vector2 currentWheelPos = workingCopy.wheelPositions[selectedTonalRange];
        Vector2 handlePos = center + currentWheelPos * (wheelSide * 0.5f);
        Event e = Event.current;
        if (wheelRect.Contains(e.mousePosition) && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            Vector2 mouseDir = e.mousePosition - center;
            float rawMag = mouseDir.magnitude / (wheelSide * 0.5f);
            workingCopy.wheelPositions[selectedTonalRange] = mouseDir.normalized * Mathf.Clamp01(rawMag);
            isDirty = true; MarkNeedsUpdate(); e.Use();
        }
        EditorGUI.DrawRect(new Rect(handlePos.x - 6, handlePos.y - 2, 12, 4), Color.black);
        EditorGUI.DrawRect(new Rect(handlePos.x - 2, handlePos.y - 6, 4, 12), Color.black);
        EditorGUI.DrawRect(new Rect(handlePos.x - 5, handlePos.y - 1, 10, 2), Color.white);
        EditorGUI.DrawRect(new Rect(handlePos.x - 1, handlePos.y - 5, 2, 10), Color.white);
        EditorGUILayout.BeginHorizontal();
        workingCopy.lumaValues[selectedTonalRange] = DrawSliderWithTooltip(T("루마", "Luma"), workingCopy.lumaValues[selectedTonalRange], -1f, 1f, T("선택한 톤 영역의 상대적인 밝기를 조절합니다.", "Adjusts the relative brightness."));
        if (GUILayout.Button(new GUIContent(T("영점", "Zero")), EditorStyles.miniButton, GUILayout.Width(40)))
        {
            workingCopy.wheelPositions[selectedTonalRange] = Vector2.zero;
            workingCopy.lumaValues[selectedTonalRange] = 0f;
            isDirty = true; MarkNeedsUpdate();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawMainLivePreview()
    {
        if (processedSource == null) return;
        float availW = position.width - 450f;
        float aspect = (float)processedSource.width / processedSource.height;
        float vH = Mathf.Min(availW / aspect, (position.height - 100) / 2f);
        float vW = vH * aspect;

        EditorGUILayout.BeginVertical(); GUILayout.Space(10);
        Rect refRect = DrawFrame(T("원본 참조", "ORIGINAL REFERENCE"), processedSource, vW, vH, true);

        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        if (activeTab == 1 && refRect.Contains(mousePos))
        {
            Vector2 localPos = mousePos - new Vector2(refRect.x + 5, refRect.y + 5);
            float u = Mathf.Clamp01(localPos.x / vW);
            float v = Mathf.Clamp01(1.0f - localPos.y / vH);

            if (e.type == EventType.MouseDown)
            {
                isSamplingFromImage = true;
                samplingStartPos = mousePos;
                e.Use();
            }

            if (isSamplingFromImage)
            {
                Rect dragVisualRect = new Rect(Mathf.Min(samplingStartPos.x, mousePos.x), Mathf.Min(samplingStartPos.y, mousePos.y), Mathf.Max(2f, Mathf.Abs(samplingStartPos.x - mousePos.x)), Mathf.Max(2f, Mathf.Abs(samplingStartPos.y - mousePos.y)));
                EditorGUI.DrawRect(dragVisualRect, new Color(1, 1, 1, 0.2f));
                Handles.BeginGUI(); Handles.color = Color.white;
                Handles.DrawPolyLine(new Vector3(dragVisualRect.xMin, dragVisualRect.yMin), new Vector3(dragVisualRect.xMax, dragVisualRect.yMin), new Vector3(dragVisualRect.xMax, dragVisualRect.yMax), new Vector3(dragVisualRect.xMin, dragVisualRect.yMax), new Vector3(dragVisualRect.xMin, dragVisualRect.yMin));
                Handles.EndGUI();
                if (e.type == EventType.MouseUp) { SampleImageArea(samplingStartPos, mousePos, refRect, vW, vH, e.control); isSamplingFromImage = false; e.Use(); }
            }
            else
            {
                Handles.BeginGUI(); Handles.color = new Color(0.2f, 1f, 0.3f, 0.6f);
                Handles.DrawWireDisc(mousePos, Vector3.forward, 10f);
                Handles.EndGUI();
                Color sampled = processedSource.GetPixelBilinear(u, v);
                hoveredGridPoint = GetNearestGridIndex(sampled);
            }
        }
        else
        {
            hoveredGridPoint = new Vector2Int(-1, -1);
            if (e.type == EventType.MouseUp) isSamplingFromImage = false;
        }

        GUILayout.Space(15);
        DrawFrame(T("조정된 출력 (라이브)", "ADJUSTED OUTPUT"), modifiedPreview ?? processedSource, vW, vH, false);
        EditorGUILayout.EndVertical();
    }

    private void SampleImageArea(Vector2 start, Vector2 end, Rect frame, float vW, float vH, bool additive)
    {
        if (!additive) selectedGridPoints.Clear();
        Rect sampleRect = new Rect(Mathf.Min(start.x, end.x), Mathf.Min(start.y, end.y), Mathf.Max(2f, Mathf.Abs(start.x - end.x)), Mathf.Max(2f, Mathf.Abs(start.y - end.y)));
        int steps = 5;
        for (int i = 0; i <= steps; i++)
        {
            for (int j = 0; j <= steps; j++)
            {
                float px = Mathf.Lerp(sampleRect.xMin, sampleRect.xMax, (float)i / steps);
                float py = Mathf.Lerp(sampleRect.yMin, sampleRect.yMax, (float)j / steps);
                Vector2 local = new Vector2(px - (frame.x + 5), py - (frame.y + 5));
                float u = Mathf.Clamp01(local.x / vW);
                float v = Mathf.Clamp01(1.0f - local.y / vH);
                Color col = processedSource.GetPixelBilinear(u, v);
                Vector2Int idx = GetNearestGridIndex(col);
                if (!selectedGridPoints.Contains(idx)) selectedGridPoints.Add(idx);
            }
        }
    }

    private Vector2Int GetNearestGridIndex(Color col)
    {
        float h, s, v;
        Color.RGBToHSV(col, out h, out s, out v);
        Vector2 target = (currentGridShape == GridShape.Circle) ? new Vector2(Mathf.Cos(h * Mathf.PI * 2) * s, Mathf.Sin(h * Mathf.PI * 2) * s) : new Vector2(col.r * 2f - 1f, col.g * 2f - 1f);
        float minDist = float.MaxValue;
        Vector2Int best = new Vector2Int(0, 0);
        for (int i = 0; i <= AB_GRID_RES_S; i++)
        {
            for (int j = 0; j < AB_GRID_RES_H; j++)
            {
                float d = Vector2.SqrMagnitude(target - abOriginalPoints[i, j]);
                if (d < minDist) { minDist = d; best = new Vector2Int(i, j); }
            }
        }
        return best;
    }

    private Rect DrawFrame(string label, Texture2D tex, float w, float h, bool isRef)
    {
        EditorGUILayout.BeginVertical();
        Rect hR = GUILayoutUtility.GetRect(w + 10, 20); EditorGUI.DrawRect(hR, isRef ? new Color(0.2f, 0.2f, 0.22f, 1f) : colorAccent * 0.6f);
        EditorGUI.LabelField(new Rect(hR.x + 5, hR.y, hR.width, 20), label, EditorStyles.miniBoldLabel);
        Rect r = GUILayoutUtility.GetRect(w + 10, h + 10); EditorGUI.DrawRect(r, new Color(0.05f, 0.05f, 0.06f, 1f));
        if (tex) GUI.DrawTexture(new Rect(r.x + 5, r.y + 5, w, h), tex, ScaleMode.ScaleToFit);
        EditorGUILayout.EndVertical();
        return r;
    }

    private void ResetToStandard()
    {
        if (workingCopy == null) return;
        Undo.RecordObject(workingCopy, "Reset");
        workingCopy.contrast = 1f; workingCopy.saturation = 1f; workingCopy.vibrance = 1f;
        workingCopy.tint = 0f; workingCopy.temperature = 0f; workingCopy.brightness = 0f; workingCopy.exposure = 1f;
        workingCopy.toeStrength = 0.2f; workingCopy.shoulderStrength = 0.2f; workingCopy.curveSlope = 1.0f;
        for (int i = 0; i < 3; i++) { workingCopy.wheelPositions[i] = Vector2.zero; workingCopy.lumaValues[i] = 0f; }
        InitializeABGrid(); isDirty = true; MarkNeedsUpdate();
    }

    private void CreateNewPreset()
    {
        string p = EditorUtility.SaveFilePanelInProject("New Preset", "NewLUT", "asset", "");
        if (string.IsNullOrEmpty(p)) return;
        LUTPreset n = CreateInstance<LUTPreset>(); AssetDatabase.CreateAsset(n, p);
        AssetDatabase.SaveAssets(); activePreset = n; workingCopy = Instantiate(n); Repaint();
    }

    private void PrepareProcessedSource()
    {
        if (!previewImage) return;
        int tW = useLowResPreview ? 640 : previewImage.width;
        int tH = Mathf.RoundToInt(tW * ((float)previewImage.height / previewImage.width));
        if (!smallPreview || smallPreview.width != tW) { if (smallPreview) DestroyImmediate(smallPreview); smallPreview = new Texture2D(tW, tH, TextureFormat.RGB24, false); }
        RenderTexture rt = RenderTexture.GetTemporary(tW, tH);
        Graphics.Blit(previewImage, rt); RenderTexture.active = rt;
        smallPreview.ReadPixels(new Rect(0, 0, tW, tH), 0, 0); smallPreview.Apply(); RenderTexture.active = null; RenderTexture.ReleaseTemporary(rt);
        processedSource = smallPreview;
    }

    private void InitStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, margin = new RectOffset(0, 0, 10, 5) };
        headerStyle.normal.textColor = Color.white;
        subHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
        subHeaderStyle.normal.textColor = colorTextDim;
        sectionBoxStyle = new GUIStyle("helpBox") { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(5, 5, 5, 5) };
        infoTextStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, normal = { textColor = colorTextDim } };
        labelWhiteStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, normal = { textColor = Color.white } };
        reportTextStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, richText = true, padding = new RectOffset(10, 10, 10, 10), wordWrap = true, normal = { textColor = new Color(0.8f, 0.85f, 0.9f) } };
        metaBoxStyle = new GUIStyle("box") { padding = new RectOffset(8, 8, 8, 8), margin = new RectOffset(5, 5, 5, 10) };
    }
}