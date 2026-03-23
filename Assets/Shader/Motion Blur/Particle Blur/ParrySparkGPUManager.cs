using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

// ────────────────────────────────────────────────────────────────────────────
// enum 클래스 외부 선언 (CS0592 방지)
// ────────────────────────────────────────────────────────────────────────────
public enum BurstDirectionMode { CameraFacingHemisphere, UseTransformForward }

// ────────────────────────────────────────────────────────────────────────────
// 레이어별 설정 구조체
// ────────────────────────────────────────────────────────────────────────────
[System.Serializable]
public struct SparkLayerConfig
{
    [Header("Count & Life")]
    public int count;
    public float lifeMin;
    public float lifeMax;

    [Header("Speed")]
    [Range(0f, 1f)]
    public float slowFraction;
    public float speedMin;
    public float speedMax;

    [Header("Particle Size")]
    public float sizeWidth;
    public float sizeHeight;
    [Range(0f, 1f)]
    public float sizeVariance;

    [Header("Spread Shape")]
    [Range(0f, 1f)]
    public float spreadAmount;
    public bool useCameraPlaneSpread;

    public static SparkLayerConfig DefaultPrimary => new SparkLayerConfig
    {
        count = 50,
        lifeMin = 0.4f,
        lifeMax = 0.9f,
        slowFraction = 0.70f,
        speedMin = 4f,
        speedMax = 12f,
        sizeWidth = 0.012f,
        sizeHeight = 0.06f,
        sizeVariance = 0.6f,
        spreadAmount = 0.35f,
        useCameraPlaneSpread = true,
    };
    public static SparkLayerConfig DefaultSecondary => new SparkLayerConfig
    {
        count = 350,
        lifeMin = 0.15f,
        lifeMax = 0.55f,
        slowFraction = 0.70f,
        speedMin = 1.5f,
        speedMax = 8f,
        sizeWidth = 0.018f,
        sizeHeight = 0.018f,
        sizeVariance = 0.5f,
        spreadAmount = 0.85f,
        useCameraPlaneSpread = true,
    };
    public static SparkLayerConfig DefaultTertiary => new SparkLayerConfig
    {
        count = 1,
        lifeMin = 0.5f,
        lifeMax = 0.8f,
        slowFraction = 0f,
        speedMin = 6f,
        speedMax = 6f,
        sizeWidth = 0.008f,
        sizeHeight = 0.30f,
        sizeVariance = 0f,
        spreadAmount = 0f,
        useCameraPlaneSpread = false,
    };
}

public class ParrySparkGPUManager : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────────
    // GPU 버퍼 구조체
    //   baseSize.w = flickerSeed / colorSeed (0~1, 파티클 생성 시 Random.value)
    // ────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    struct SparkData
    {
        public Vector4 positionAge;   // xyz=위치, w=나이
        public Vector4 velocityLife;  // xyz=속도, w=수명
        public Vector4 baseSize;      // x=폭, y=높이, z=타입(0/1/2), w=seed
    }

    // ─── Dependencies ────────────────────────────────────────────────────────
    [Header("── Dependencies ──────────────────────────")]
    public ComputeShader physicsCompute;
    public Material renderMaterial;

    // ─── Buffer ───────────────────────────────────────────────────────────────
    [Header("── Buffer ──────────────────────────────────")]
    public int maxSparks = 2000;

    // ─── Burst Direction ──────────────────────────────────────────────────────
    [Header("── Burst Direction ─────────────────────────")]
    public BurstDirectionMode directionMode = BurstDirectionMode.CameraFacingHemisphere;
    [Range(-0.5f, 1f)]
    public float upwardBias = 0.15f;

    // ─── Spark Layers ─────────────────────────────────────────────────────────
    [Header("── Layer 1 : Primary Streaks ───────────────")]
    public SparkLayerConfig primaryLayer = SparkLayerConfig.DefaultPrimary;

    [Header("── Layer 2 : Secondary Sparks ──────────────")]
    public SparkLayerConfig secondaryLayer = SparkLayerConfig.DefaultSecondary;

    [Header("── Layer 3 : Single Beam ───────────────────")]
    public bool enableTertiaryLayer = true;
    public SparkLayerConfig tertiaryLayer = SparkLayerConfig.DefaultTertiary;

    // ─── [개선 ③] Point Light ─────────────────────────────────────────────────
    [Header("── Impact Light (개선 ③) ──────────────────")]
    [Tooltip("타격 지점에 Point Light 를 잠깐 발생시켜 주변 환경에 오렌지 빛 반사")]
    public bool enableImpactLight = true;
    [Tooltip("광원 색상 — 주황-빨강 계열 권장")]
    public Color impactLightColor = new Color(1f, 0.35f, 0.05f);
    [Tooltip("광원 범위 (m)")]
    public float impactLightRange = 4f;
    [Tooltip("최대 광원 세기")]
    public float impactLightIntensity = 12f;
    [Tooltip("광원이 0으로 fade out 되는 시간 (초)")]
    public float impactLightFadeTime = 0.25f;
    [Tooltip("Fade 곡선 지수\n1.0 = 선형(linear)\n2~3 = 완만한 지수 감쇠\n5~8 = 급격한 지수 감쇠 (기본값 4 권장)")]
    [Range(1f, 10f)]
    public float impactLightFadeExponent = 4f;

    // ─── Auto Play ────────────────────────────────────────────────────────────
    [Header("── Auto Play & Destroy ───────────────────────")]
    public bool playOnEnable = true;
    public float autoDestroyTime = 2.5f;

    // ─── Gizmo ────────────────────────────────────────────────────────────────
    [Header("── Gizmo Preview ─────────────────────────────")]
    public bool showGizmos = true;
    [Range(4, 32)]
    public int gizmoLineCount = 12;
    public float gizmoLength = 0.8f;

    // ─── Diagnostics ──────────────────────────────────────────────────────────
    [Header("── Diagnostics ───────────────────────────────")]
    public bool enableDebugLog = false;

    // ─── Private ──────────────────────────────────────────────────────────────
    private int kernelUpdateSparks = -1;
    private ComputeBuffer sparkBuffer;
    private SparkData[] sparkDataArray;
    private bool isBurstActive = false;

    // ════════════════════════════════════════════════════════════════════════
    void Awake()
    {
        if (TryGetComponent<MeshRenderer>(out var mr)) Destroy(mr);
        if (TryGetComponent<MeshFilter>(out var mf)) Destroy(mf);
        if (TryGetComponent<ParticleSystem>(out var ps)) Destroy(ps);

        if (physicsCompute == null || renderMaterial == null)
        {
            Debug.LogError("[GPU Spark] ComputeShader 또는 Material 없음!");
            return;
        }

        renderMaterial = new Material(renderMaterial);

        int stride = Marshal.SizeOf(typeof(SparkData));
        sparkBuffer = new ComputeBuffer(maxSparks, stride);
        sparkDataArray = new SparkData[maxSparks];

        for (int i = 0; i < maxSparks; i++)
            sparkDataArray[i] = new SparkData
            {
                positionAge = new Vector4(0, 0, 0, 100f),
                velocityLife = new Vector4(0, 0, 0, 1f),
                baseSize = Vector4.zero
            };

        sparkBuffer.SetData(sparkDataArray);
        renderMaterial.SetBuffer("_SparkBuffer", sparkBuffer);
        kernelUpdateSparks = physicsCompute.FindKernel("UpdateSparks");
    }

    void OnEnable()
    {
        if (playOnEnable)
        {
            Vector3 dir = GetBurstDirection();
            BurstAll(transform.position, dir);
            Destroy(gameObject, autoDestroyTime);
        }
    }

    void Update()
    {
        if (sparkBuffer == null || kernelUpdateSparks < 0) return;

        float safeDt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);

        physicsCompute.SetFloat("deltaTime", safeDt);
        physicsCompute.SetInt("maxSparks", maxSparks);
        physicsCompute.SetBuffer(kernelUpdateSparks, "_SparkBuffer", sparkBuffer);
        physicsCompute.Dispatch(kernelUpdateSparks, Mathf.CeilToInt(maxSparks / 64f), 1, 1);

        if (enableDebugLog && isBurstActive && Time.frameCount % 30 == 0)
        {
            sparkBuffer.GetData(sparkDataArray);
            var p0 = sparkDataArray[0].positionAge;
            var v0 = sparkDataArray[0].velocityLife;
            Debug.Log($"[GPU Spark] P0 ({p0.x:F2},{p0.y:F2},{p0.z:F2}) Age:{p0.w:F2}/{v0.w:F2}");
            if (p0.w >= v0.w) isBurstActive = false;
        }

        RenderParams rp = new RenderParams(renderMaterial);
        rp.worldBounds = new Bounds(Vector3.zero, Vector3.one * 1000000f);
        Graphics.RenderPrimitives(rp, MeshTopology.Triangles, maxSparks * 6, 1);
    }

    void OnDestroy()
    {
        if (sparkBuffer != null) { sparkBuffer.Release(); sparkBuffer = null; }
    }

    // ════════════════════════════════════════════════════════════════════════
    // Public API
    // ════════════════════════════════════════════════════════════════════════
    public void BurstAll(Vector3 hitPosition, Vector3 baseDirection)
    {
        if (sparkBuffer == null) return;
        sparkBuffer.GetData(sparkDataArray);
        if (baseDirection == Vector3.zero) baseDirection = Vector3.up;

        Camera cam = Camera.main;
        Vector3 camRight = cam ? cam.transform.right : Vector3.right;
        Vector3 camUp = cam ? cam.transform.up : Vector3.up;

        int cursor = 0;
        cursor = SpawnLayer(sparkDataArray, cursor, hitPosition, baseDirection,
                            camRight, camUp, primaryLayer, sparkType: 0f);
        cursor = SpawnLayer(sparkDataArray, cursor, hitPosition, baseDirection,
                            camRight, camUp, secondaryLayer, sparkType: 1f);
        if (enableTertiaryLayer)
            cursor = SpawnLayer(sparkDataArray, cursor, hitPosition, baseDirection,
                                camRight, camUp, tertiaryLayer, sparkType: 2f);

        sparkBuffer.SetData(sparkDataArray);
        isBurstActive = true;

        // ── [개선 ③] Point Light 생성 ────────────────────────────────────
        if (enableImpactLight)
            SpawnImpactLight(hitPosition);

        if (enableDebugLog)
            Debug.Log($"[GPU Spark] BurstAll cursor:{cursor}/{maxSparks}");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 레이어 공통 생성
    // ════════════════════════════════════════════════════════════════════════
    int SpawnLayer(SparkData[] buf, int startSlot,
                   Vector3 hitPos, Vector3 baseDir,
                   Vector3 camRight, Vector3 camUp,
                   SparkLayerConfig cfg, float sparkType)
    {
        int slot = startSlot;
        int spawned = 0;
        bool isTertiary = (sparkType > 1.5f);

        while (slot < maxSparks && spawned < cfg.count)
        {
            if (buf[slot].positionAge.w < buf[slot].velocityLife.w)
            {
                slot++;
                continue;
            }

            // 방향 결정
            Vector3 dir;
            if (isTertiary)
            {
                dir = baseDir;
            }
            else
            {
                Vector3 sphereRandom;
                if (cfg.useCameraPlaneSpread)
                {
                    float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float r = Random.Range(0f, 1f);
                    sphereRandom = (camRight * Mathf.Cos(ang) + camUp * Mathf.Sin(ang)) * r;
                }
                else
                {
                    sphereRandom = Random.insideUnitSphere;
                }
                dir = Vector3.Lerp(baseDir, sphereRandom, cfg.spreadAmount).normalized;
                if (dir == Vector3.zero) dir = baseDir;
            }

            // 속도
            bool isSlow = !isTertiary && (Random.value < cfg.slowFraction);
            float speed = isSlow
                ? Random.Range(0.2f, 1.2f)
                : Random.Range(cfg.speedMin, cfg.speedMax);

            float life = Random.Range(cfg.lifeMin, cfg.lifeMax);

            // 크기 (랜덤 편차)
            float wVar = 1f + Random.Range(-cfg.sizeVariance, cfg.sizeVariance);
            float hVar = 1f + Random.Range(-cfg.sizeVariance, cfg.sizeVariance);
            float w = Mathf.Max(0.001f, cfg.sizeWidth * wVar);
            float h = Mathf.Max(0.001f, cfg.sizeHeight * hVar);

            // ── [개선 ④⑥] flickerSeed / colorSeed — w 채널에 저장 ──────
            // 파티클마다 다른 Random.value → 셰이더에서 flicker 위상 + 색조 편차로 사용
            float sparkSeed = Random.value;

            Vector3 jitter = hitPos + Random.insideUnitSphere * (isTertiary ? 0f : 0.03f);

            buf[slot] = new SparkData
            {
                positionAge = new Vector4(jitter.x, jitter.y, jitter.z, 0f),
                velocityLife = new Vector4(dir.x * speed, dir.y * speed, dir.z * speed, life),
                baseSize = new Vector4(w, h, sparkType, sparkSeed) // w=seed
            };

            slot++;
            spawned++;
        }

        return slot;
    }

    // ════════════════════════════════════════════════════════════════════════
    // [개선 ③] Point Light — 타격 지점 순간 광원
    // ════════════════════════════════════════════════════════════════════════
    void SpawnImpactLight(Vector3 hitPosition)
    {
        // 이 컴포넌트의 자식으로 Light 오브젝트 생성
        // → 부모(HitSpark)가 Destroy될 때 함께 제거됨
        GameObject lightGO = new GameObject("ImpactPointLight");
        lightGO.transform.SetParent(transform);
        lightGO.transform.position = hitPosition;

        Light lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = impactLightColor;
        lt.range = impactLightRange;
        lt.intensity = impactLightIntensity;
        lt.shadows = LightShadows.Soft;
        lt.shadowStrength = 1f;
        lt.renderMode = LightRenderMode.ForcePixel;

        StartCoroutine(FadeLightOut(lt, impactLightFadeTime));
    }

    IEnumerator FadeLightOut(Light lt, float duration)
    {
        float startIntensity = lt.intensity;
        float elapsed = 0f;

        while (elapsed < duration && lt != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // impactLightFadeExponent 로 fade 곡선 제어
            //   t^1  = 선형(linear)
            //   t^4  = 급격한 지수 감쇠 — 초반에 빠르게 꺼지고 후반은 천천히
            //   Mathf.Pow(t, exp) 는 t=0 에서 0, t=1 에서 1 이므로
            //   1 - pow(t, exp) 로 반전하면 시작=1, 끝=0 의 감쇠 곡선이 됨
            float fadeT = 1f - Mathf.Pow(t, impactLightFadeExponent);
            lt.intensity = startIntensity * fadeT;
            yield return null;
        }

        if (lt != null)
            lt.intensity = 0f;
    }

    // ════════════════════════════════════════════════════════════════════════
    // 방향 결정
    // ════════════════════════════════════════════════════════════════════════
    Vector3 GetBurstDirection()
    {
        if (directionMode == BurstDirectionMode.CameraFacingHemisphere)
        {
            Camera cam = Camera.main;
            if (cam != null)
                return (cam.transform.forward + Vector3.up * upwardBias).normalized;
        }
        return transform.forward;
    }

    // ════════════════════════════════════════════════════════════════════════════
    // 기즈모 (에디터 전용)
    // ════════════════════════════════════════════════════════════════════════════
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        Vector3 origin = transform.position;
        Vector3 baseDir;

        if (directionMode == BurstDirectionMode.CameraFacingHemisphere)
        {
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            baseDir = sv != null
                ? (sv.camera.transform.forward + Vector3.up * upwardBias).normalized
                : (transform.forward + Vector3.up * upwardBias).normalized;
        }
        else
        {
            baseDir = transform.forward;
        }

        DrawLayerGizmo(origin, baseDir, primaryLayer, new Color(1f, 0.9f, 0.4f, 0.9f), "L1 Primary");
        DrawLayerGizmo(origin, baseDir, secondaryLayer, new Color(1f, 0.4f, 0.1f, 0.7f), "L2 Secondary");
        if (enableTertiaryLayer)
            DrawLayerGizmo(origin, baseDir, tertiaryLayer, new Color(0.2f, 1f, 0.8f, 1f), "L3 Beam");

        // Impact Light 범위 표시
        if (enableImpactLight)
        {
            Gizmos.color = new Color(impactLightColor.r, impactLightColor.g, impactLightColor.b, 0.25f);
            Gizmos.DrawWireSphere(origin, impactLightRange);
        }

        Gizmos.color = Color.white;
        Gizmos.DrawRay(origin, baseDir * gizmoLength * 1.3f);
    }

    void DrawLayerGizmo(Vector3 origin, Vector3 baseDir,
                        SparkLayerConfig cfg, Color col, string label)
    {
        Gizmos.color = col;
        float halfAngle = cfg.spreadAmount * 90f;

        Vector3 perpAxis = Vector3.Cross(baseDir, Vector3.up);
        if (perpAxis.sqrMagnitude < 0.001f)
            perpAxis = Vector3.Cross(baseDir, Vector3.right);
        perpAxis.Normalize();

        for (int k = 0; k < gizmoLineCount; k++)
        {
            float azimuth = (360f / gizmoLineCount) * k;
            Vector3 edgeDir = Quaternion.AngleAxis(azimuth, baseDir)
                            * Quaternion.AngleAxis(halfAngle, perpAxis)
                            * baseDir;
            Gizmos.DrawRay(origin, edgeDir.normalized * gizmoLength);
        }

        if (halfAngle > 1f && halfAngle < 89f)
        {
            float dist = gizmoLength * Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            float radius = gizmoLength * Mathf.Sin(halfAngle * Mathf.Deg2Rad);
            Vector3 center = origin + baseDir * dist;
            int seg = 32;
            Vector3 prev = center + perpAxis * radius;
            for (int s = 1; s <= seg; s++)
            {
                Vector3 pt = center + Quaternion.AngleAxis(s * (360f / seg), baseDir) * perpAxis * radius;
                Gizmos.DrawLine(prev, pt);
                prev = pt;
            }
        }
        else if (halfAngle >= 89f)
        {
            Gizmos.DrawWireSphere(origin, gizmoLength * 0.5f);
        }

        UnityEditor.Handles.color = col;
        UnityEditor.Handles.Label(
            origin + baseDir * gizmoLength + Vector3.up * 0.05f,
            $"{label}\nspread:{cfg.spreadAmount:F2}  count:{cfg.count}"
        );
    }
#endif
}