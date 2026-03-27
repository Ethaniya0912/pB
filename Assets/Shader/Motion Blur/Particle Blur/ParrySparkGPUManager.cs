// ══════════════════════════════════════════════════════════════════════════════
//  ParrySparkGPUManager.cs
//  계층  : [L4 Event/Coordination] — GPU 파티클 실행기
//
//  역할  : HitSparkVFXData(SO) 를 유일한 입력으로 받아
//          ComputeShader(SparkPhysics) + Material(SparkRender_Procedural) 로
//          GPU 파티클을 시뮬레이션하고 렌더링합니다.
//
//  변경 요약 (기존 하드코딩 → SO 드리븐):
//  ┌──────────────────────────────────────────────────────────────────────┐
//  │  이전 (하드코딩)           →  이후 (SO 드리븐)                       │
//  │  public int maxSparks      →  data.maxSparks                         │
//  │  public SparkLayerConfig   →  data.primaryLayer / secondaryLayer /   │
//  │    primaryLayer/secondaryLayer/tertiaryLayer                tertiaryLayer│
//  │  public bool enableImpactLight  →  data.enableImpactLight            │
//  │  public Color impactLightColor  →  data.impactLightColor             │
//  │  public float autoDestroyTime   →  data.autoDestroyTime              │
//  └──────────────────────────────────────────────────────────────────────┘
//
//  공개 API:
//    EmitSparks(HitSparkVFXData data, Vector3 hitPosition, Vector3 baseDirection)
//    → CharacterEffectsManager 에서만 호출
//
//  아키텍처 규약:
//  • 도메인(CombatManager) 직접 참조 금지
//  • 네트워크 동기화 대상 아님 — 로컬 GPU 연산 전용
//  • physicsCompute / renderMaterial 은 HitSparkVFXData 에서 가져옴
// ══════════════════════════════════════════════════════════════════════════════
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

[DisallowMultipleComponent]
public class ParrySparkGPUManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    //  GPU 버퍼 구조체 — SparkRender_Procedural.shader 의 SparkData 와 완전 동일
    //  positionAge.w = 나이(age), velocityLife.w = 수명(lifetime)
    //  baseSize.w    = flickerSeed / colorSeed (0~1)
    // ─────────────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct SparkData
    {
        public Vector4 positionAge;   // xyz=위치, w=나이
        public Vector4 velocityLife;  // xyz=속도, w=수명
        public Vector4 baseSize;      // x=폭, y=높이, z=타입(0/1/2), w=seed
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Diagnostics (에디터 전용 시각화는 유지)
    // ─────────────────────────────────────────────────────────────────────────
    [Header("── Diagnostics ───────────────────────────────────────")]
    public bool enableDebugLog = false;

    // ─────────────────────────────────────────────────────────────────────────
    //  Private — 런타임 상태
    // ─────────────────────────────────────────────────────────────────────────
    private HitSparkVFXData _activeData;

    private ComputeShader _computeShader;
    private Material _renderMaterial;   // 인스턴스 복제본
    private ComputeBuffer _sparkBuffer;
    private SparkData[] _sparkDataArray;
    private int _kernelUpdateSparks = -1;
    private bool _isBurstActive;

    // ══════════════════════════════════════════════════════════════════════════
    //  Public API — 단일 진입점
    //  CharacterEffectsManager.PlayHitSparkVFX() 에서만 호출합니다.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// SO 데이터를 기반으로 GPU 스파크를 방출합니다.
    /// 데이터 에셋이 바뀌면 내부 버퍼를 자동으로 재구성합니다.
    /// </summary>
    /// <param name="data">HitSparkVFXRegistry 에서 조회한 SO 데이터</param>
    /// <param name="hitPosition">충돌 지점 (World Space)</param>
    /// <param name="baseDirection">편향 방향 (World Space, 정규화 필요)</param>
    public void EmitSparks(HitSparkVFXData data, Vector3 hitPosition, Vector3 baseDirection)
    {
        Debug.Log($"[GPU-DEBUG 1] EmitSparks 진입 — data={data?.name ?? "NULL"}");

        if (data == null)
        {
            Debug.LogWarning("[ParrySparkGPUManager] EmitSparks: data 가 null 입니다.", this);
            return;
        }

        Debug.Log($"[GPU-DEBUG 2] shader={data.physicsComputeShader != null} mat={data.sparkMaterial != null} maxSparks={data.maxSparks}");
        Debug.Log($"[GPU-DEBUG 3] primaryLayer.count={data.primaryLayer.count} secondaryLayer.count={data.secondaryLayer.count}");

        if (data.physicsComputeShader == null || data.sparkMaterial == null)
        {
            Debug.LogError(
                $"[ParrySparkGPUManager] SO '{data.name}' 에 " +
                $"physicsComputeShader 또는 sparkMaterial 이 할당되지 않았습니다.", this);
            return;
        }

        // 데이터 에셋이 바뀌었으면 GPU 버퍼 재구성
        if (_activeData != data)
        {
            CleanupBuffers();
            InitializeBuffers(data);
            _activeData = data;
        }

        Debug.Log($"[GPU-DEBUG 4] BurstAll 호출 — hitPos={hitPosition} dir={baseDirection}");
        BurstAll(data, hitPosition, baseDirection);
        Destroy(gameObject, data.autoDestroyTime);
        Debug.Log($"[GPU-DEBUG 5] EmitSparks 완료 — autoDestroy={data.autoDestroyTime}s 후 소멸");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Unity 생명주기
    // ══════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // 불필요한 기본 컴포넌트 제거 (렌더링은 Graphics.RenderPrimitives 로 처리)
        if (TryGetComponent<MeshRenderer>(out var mr)) Destroy(mr);
        if (TryGetComponent<MeshFilter>(out var mf)) Destroy(mf);
        if (TryGetComponent<ParticleSystem>(out var ps)) Destroy(ps);
    }

    private void Update()
    {
        if (_sparkBuffer == null || _kernelUpdateSparks < 0) return;
        if (_computeShader == null || _renderMaterial == null) return;

        float safeDt = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);

        // ── Compute Dispatch ─────────────────────────────────────────────────
        _computeShader.SetFloat("deltaTime", safeDt);
        _computeShader.SetInt("maxSparks", _activeData.maxSparks);
        _computeShader.SetBuffer(_kernelUpdateSparks, "_SparkBuffer", _sparkBuffer);
        _computeShader.Dispatch(
            _kernelUpdateSparks,
            Mathf.CeilToInt(_activeData.maxSparks / 64f), 1, 1);

        // ── 진단 로그 (에디터/개발 빌드 전용) ─────────────────────────────
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (enableDebugLog && _isBurstActive && Time.frameCount % 30 == 0)
        {
            _sparkBuffer.GetData(_sparkDataArray);
            var p0 = _sparkDataArray[0].positionAge;
            var v0 = _sparkDataArray[0].velocityLife;
            Debug.Log(
                $"[GPU Spark] P0 ({p0.x:F2},{p0.y:F2},{p0.z:F2}) " +
                $"Age:{p0.w:F2}/{v0.w:F2}");
            if (p0.w >= v0.w) _isBurstActive = false;
        }
#endif

        // ── Render ───────────────────────────────────────────────────────────
        RenderParams rp = new RenderParams(_renderMaterial)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 1_000_000f)
        };
        Graphics.RenderPrimitives(
            rp, MeshTopology.Triangles, _activeData.maxSparks * 6, 1);
    }

    private void OnDestroy() => CleanupBuffers();

    private void OnDisable()
    {
        // 씬 전환 등 강제 비활성화 시에도 버퍼 정리
        CleanupBuffers();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  버퍼 초기화 / 정리
    // ══════════════════════════════════════════════════════════════════════════

    private void InitializeBuffers(HitSparkVFXData data)
    {
        // SO 에서 런타임 에셋 가져오기
        _computeShader = data.physicsComputeShader;
        _renderMaterial = new Material(data.sparkMaterial); // 인스턴스 복제

        int stride = Marshal.SizeOf(typeof(SparkData));
        _sparkBuffer = new ComputeBuffer(data.maxSparks, stride);
        _sparkDataArray = new SparkData[data.maxSparks];

        // 전체 파티클을 '비활성(expired)' 상태로 초기화
        for (int i = 0; i < data.maxSparks; i++)
        {
            _sparkDataArray[i] = new SparkData
            {
                positionAge = new Vector4(0f, 0f, 0f, 100f), // age > lifetime → 비활성
                velocityLife = new Vector4(0f, 0f, 0f, 1f),
                baseSize = Vector4.zero
            };
        }

        _sparkBuffer.SetData(_sparkDataArray);
        _renderMaterial.SetBuffer("_SparkBuffer", _sparkBuffer);

        _kernelUpdateSparks = _computeShader.FindKernel("UpdateSparks");

        if (_kernelUpdateSparks < 0)
        {
            Debug.LogError(
                "[ParrySparkGPUManager] ComputeShader 에서 'UpdateSparks' 커널을 찾지 못했습니다.");
        }
    }

    private void CleanupBuffers()
    {
        if (_sparkBuffer != null)
        {
            _sparkBuffer.Release();
            _sparkBuffer = null;
        }

        if (_renderMaterial != null)
        {
            Destroy(_renderMaterial);
            _renderMaterial = null;
        }

        _kernelUpdateSparks = -1;
        _activeData = null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Burst 실행 — SO 의 레이어 설정을 읽어 파티클 생성
    // ══════════════════════════════════════════════════════════════════════════

    private void BurstAll(HitSparkVFXData data, Vector3 hitPosition, Vector3 baseDirection)
    {
        if (_sparkBuffer == null) return;

        _sparkBuffer.GetData(_sparkDataArray);

        if (baseDirection == Vector3.zero) baseDirection = Vector3.up;

        // 카메라 기저 벡터 (카메라 없으면 월드 기저 사용)
        Camera cam = Camera.main;
        Vector3 camRight = cam ? cam.transform.right : Vector3.right;
        Vector3 camUp = cam ? cam.transform.up : Vector3.up;

        int cursor = 0;

        cursor = SpawnLayer(
            _sparkDataArray, cursor, hitPosition, baseDirection,
            camRight, camUp, data.primaryLayer, sparkType: 0f);

        cursor = SpawnLayer(
            _sparkDataArray, cursor, hitPosition, baseDirection,
            camRight, camUp, data.secondaryLayer, sparkType: 1f);

        if (data.enableTertiaryLayer)
        {
            cursor = SpawnLayer(
                _sparkDataArray, cursor, hitPosition, baseDirection,
                camRight, camUp, data.tertiaryLayer, sparkType: 2f);
        }

        _sparkBuffer.SetData(_sparkDataArray);
        _isBurstActive = true;

        // Impact Light — SO 플래그 기준
        if (data.enableImpactLight)
            SpawnImpactLight(data, hitPosition);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (enableDebugLog)
        {
            Debug.Log(
                $"[GPU Spark] BurstAll — SO:{data.name} " +
                $"cursor:{cursor}/{data.maxSparks}");
        }
#endif
    }

    // ── 레이어 공통 파티클 생성 ─────────────────────────────────────────────
    private int SpawnLayer(
        SparkData[] buf, int startSlot,
        Vector3 hitPos, Vector3 baseDir,
        Vector3 camRight, Vector3 camUp,
        HitSparkLayerConfig cfg, float sparkType)
    {
        int slot = startSlot;
        int spawned = 0;
        bool isTertiary = sparkType > 1.5f;

        while (slot < buf.Length && spawned < cfg.count)
        {
            // 이미 살아있는 슬롯은 건너뜀
            if (buf[slot].positionAge.w < buf[slot].velocityLife.w)
            {
                slot++;
                continue;
            }

            // ── 방향 결정 ────────────────────────────────────────────────────
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

            // ── 속도 ─────────────────────────────────────────────────────────
            bool isSlow = !isTertiary && (Random.value < cfg.slowFraction);
            float speed = isSlow
                ? Random.Range(0.2f, 1.2f)
                : Random.Range(cfg.speedMin, cfg.speedMax);

            float life = Random.Range(cfg.lifeMin, cfg.lifeMax);

            // ── 크기 편차 ────────────────────────────────────────────────────
            float wVar = 1f + Random.Range(-cfg.sizeVariance, cfg.sizeVariance);
            float hVar = 1f + Random.Range(-cfg.sizeVariance, cfg.sizeVariance);
            float w = Mathf.Max(0.001f, cfg.sizeWidth * wVar);
            float h = Mathf.Max(0.001f, cfg.sizeHeight * hVar);

            // ── seed — 셰이더의 flicker 위상 + 색조 편차로 사용 ─────────────
            float sparkSeed = Random.value;

            Vector3 jitter = hitPos + Random.insideUnitSphere * (isTertiary ? 0f : 0.03f);

            buf[slot] = new SparkData
            {
                positionAge = new Vector4(jitter.x, jitter.y, jitter.z, 0f),
                velocityLife = new Vector4(dir.x * speed, dir.y * speed, dir.z * speed, life),
                baseSize = new Vector4(w, h, sparkType, sparkSeed)
            };

            slot++;
            spawned++;
        }

        return slot;
    }

    // ── Impact Light ─────────────────────────────────────────────────────────

    /// <summary>
    /// 타격 지점에 순간 Point Light 를 생성합니다.
    /// 모든 수치는 HitSparkVFXData(SO) 에서 읽습니다.
    /// </summary>
    private void SpawnImpactLight(HitSparkVFXData data, Vector3 hitPosition)
    {
        GameObject lightGO = new GameObject("ImpactPointLight");
        lightGO.transform.SetParent(transform);
        lightGO.transform.position = hitPosition;

        Light lt = lightGO.AddComponent<Light>();
        lt.type = LightType.Point;
        lt.color = data.impactLightColor;       // SO 에서 읽음
        lt.range = data.impactLightRange;       // SO 에서 읽음
        lt.intensity = data.impactLightIntensity;   // SO 에서 읽음
        lt.shadows = LightShadows.Soft;
        lt.shadowStrength = 1f;
        lt.renderMode = LightRenderMode.ForcePixel;

        StartCoroutine(FadeLightOut(lt, data.impactLightFadeTime, data.impactLightFadeExponent));
    }

    private IEnumerator FadeLightOut(Light lt, float duration, float exponent)
    {
        float startIntensity = lt.intensity;
        float elapsed = 0f;

        while (elapsed < duration && lt != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float fadeT = 1f - Mathf.Pow(t, exponent); // SO 의 exponent 로 곡선 제어
            lt.intensity = startIntensity * fadeT;
            yield return null;
        }

        if (lt != null) lt.intensity = 0f;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  에디터 기즈모 — 데이터가 로드된 경우에만 표시
    // ══════════════════════════════════════════════════════════════════════════
#if UNITY_EDITOR
    [Header("── Gizmo Preview ───────────────────────────────────────")]
    [Tooltip("씬 뷰 기즈모 표시 여부")]
    public bool showGizmos = true;
    [Range(4, 32)]
    public int gizmoLineCount = 12;
    public float gizmoLength = 0.8f;

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos || _activeData == null) return;

        Vector3 origin = transform.position;
        Vector3 baseDir;

        Camera cam = Camera.main;
        baseDir = cam != null
            ? (cam.transform.forward + Vector3.up * _activeData.upwardBias).normalized
            : (transform.forward + Vector3.up * _activeData.upwardBias).normalized;

        DrawLayerGizmo(origin, baseDir, _activeData.primaryLayer,
            new Color(1f, 0.9f, 0.4f, 0.9f), "L1 Primary");
        DrawLayerGizmo(origin, baseDir, _activeData.secondaryLayer,
            new Color(1f, 0.4f, 0.1f, 0.7f), "L2 Secondary");
        if (_activeData.enableTertiaryLayer)
            DrawLayerGizmo(origin, baseDir, _activeData.tertiaryLayer,
                new Color(0.2f, 1f, 0.8f, 1f), "L3 Beam");

        if (_activeData.enableImpactLight)
        {
            Gizmos.color = new Color(
                _activeData.impactLightColor.r,
                _activeData.impactLightColor.g,
                _activeData.impactLightColor.b, 0.25f);
            Gizmos.DrawWireSphere(origin, _activeData.impactLightRange);
        }

        Gizmos.color = Color.white;
        Gizmos.DrawRay(origin, baseDir * gizmoLength * 1.3f);
    }

    private void DrawLayerGizmo(
        Vector3 origin, Vector3 baseDir,
        HitSparkLayerConfig cfg, Color col, string label)
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
                Vector3 pt = center
                    + Quaternion.AngleAxis(s * (360f / seg), baseDir) * perpAxis * radius;
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
            $"{label}\nspread:{cfg.spreadAmount:F2}  count:{cfg.count}");
    }
#endif
}