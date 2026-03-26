using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// [Phase CS — OMBSkinningCacheManager v3 Final]
/// GetVertexBuffer 경로 전용 (진단 결과: URP 6.3에서 지원됨)
///
/// ■ 동작 원리
///   GetVertexBuffer(0) → GPU 스킨닝 결과 GraphicsBuffer 직접 획득
///   매 프레임: CurrBuffer = GetVertexBuffer() 결과
///             vel = Curr - Prev (ComputeVelocityDirect 커널)
///             더블버퍼 스왑
///   CacheSkinning 커널 불필요 — 구현 단순화
///
/// ■ 이 파일이 해결하는 문제
///   Phase 3에서 _OMBVelocityWS = 루트 Transform 델타.
///   Strafe 같은 루트가 안 움직이는 애니메이션에서 vel ≈ 0 → 블러 없음 → 검정.
///   Phase CS: 버텍스별 실제 스킨닝 속도 → 루트 이동 없어도 정확한 블러.
///
/// ■ Script Execution Order: +200 으로 설정 (필수)
///   Project Settings → Script Execution Order → OMBSkinningCacheManager: 200
/// </summary>
[RequireComponent(typeof(ObjectMotionBlurController))]
public class OMBSkinningCacheManager : MonoBehaviour
{
    [Header("Compute Shader")]
    [Tooltip("CS_OMBSkinningCache.compute 파일을 연결하세요.")]
    public ComputeShader skinningCacheShader;

    [Header("Target")]
    [Tooltip("스킨닝할 SMR. 비워두면 자동 탐색.")]
    public SkinnedMeshRenderer targetSMR;

    [Header("Debug (ReadOnly)")]
    [SerializeField] private string _dbgStatus   = "Initializing";
    [SerializeField] private int    _dbgVertexCount;
    [SerializeField] private bool   _dbgReady;

    // ── 내부 버퍼 ────────────────────────────────────────────────
    private ComputeBuffer  _velocityBuffer;   // 출력: 버텍스별 속도 (P2에 주입)

    // GetVertexBuffer 더블버퍼
    private GraphicsBuffer _gvbA;
    private GraphicsBuffer _gvbB;
    private bool           _isEven = true;
    private GraphicsBuffer _currGVB => _isEven ? _gvbA : _gvbB;
    private GraphicsBuffer _prevGVB => _isEven ? _gvbB : _gvbA;

    private ObjectMotionBlurController _controller;
    private int  _vertexCount;
    private int  _kernelVelocityDirect;
    private bool _ready;
    private static readonly int VelocityBufID = Shader.PropertyToID("_OMBVelocityBuffer");
    private const int THREADS = 64;

    // ── 생명주기 ─────────────────────────────────────────────────
    private void Awake()
    {
        _controller = GetComponent<ObjectMotionBlurController>();
        if (targetSMR == null)
            targetSMR = GetComponentInChildren<SkinnedMeshRenderer>(true);
    }

    private void Start()
    {
        if (skinningCacheShader == null)
        {
            Debug.LogError("[OMBSkinning] skinningCacheShader 미연결", this);
            enabled = false; return;
        }
        if (targetSMR == null)
        {
            Debug.LogError("[OMBSkinning] SMR 없음", this);
            enabled = false; return;
        }

        if (!InitBuffers()) return;

        // InjectVelocityBuffer가 없을 경우 MPB 직접 주입으로 폴백
        var injectMethod = _controller.GetType().GetMethod("InjectVelocityBuffer");
        if (injectMethod != null)
        {
            injectMethod.Invoke(_controller, new object[] { _velocityBuffer });
        }
        else
        {
            // InjectVelocityBuffer가 없는 구버전 P2 — 경고만 출력
            // MPB 직접 주입은 weight 버퍼 덮어쓰기 문제를 유발하므로 제거됨
            Debug.LogWarning("[OMBSkinning] P2에 InjectVelocityBuffer 없음. " +
                "P2_ObjectMotionBlurController를 최신 버전으로 교체하세요.", this);
        }
        _dbgReady  = true;
        _dbgStatus = $"Running (GVB) — {_vertexCount} verts";
        Debug.Log($"[OMBSkinning] '{name}' 초기화 완료 — {_vertexCount} 버텍스", this);
    }

    private bool InitBuffers()
    {
        _vertexCount     = targetSMR.sharedMesh.vertexCount;
        _dbgVertexCount  = _vertexCount;

        _kernelVelocityDirect = skinningCacheShader.FindKernel("ComputeVelocityDirect");
        if (_kernelVelocityDirect < 0)
        {
            Debug.LogError("[OMBSkinning] ComputeVelocityDirect 커널 없음", this);
            enabled = false; return false;
        }

        // 속도 출력 버퍼
        _velocityBuffer = new ComputeBuffer(_vertexCount, 3 * sizeof(float));

        // 더블버퍼 초기화 — GetVertexBuffer() 첫 프레임 결과로 채워짐
        // null로 두면 Start 직후 LateUpdate에서 채워짐
        skinningCacheShader.SetInt("_VertexCount", _vertexCount);
        return true;
    }

    // Script Execution Order +200 필수 (Animator Job 완료 후 실행)
    private void LateUpdate()
    {
        if (!_ready && _dbgReady) { _ready = true; }
        if (!_dbgReady) return;

        // GetVertexBuffer: 이 프레임의 GPU 스킨닝 결과
        GraphicsBuffer curr;
        try
        {
            // GetVertexBuffer() - Unity 버전별 인자 없는 API 사용
            // Reflection으로 호환성 보장 (진단 스크립트와 동일)
            var gvbMethod = typeof(SkinnedMeshRenderer).GetMethod(
                "GetVertexBuffer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, new System.Type[0], null);
            curr = gvbMethod != null
                ? gvbMethod.Invoke(targetSMR, null) as GraphicsBuffer
                : null;
        }
        catch
        {
            Debug.LogError("[OMBSkinning] GetVertexBuffer 실패. SCM이 직접 스킨닝 경로로 전환됩니다.", this);
            enabled = false;
            var disableMethod = _controller.GetType().GetMethod("DisableComputeVelocity");
            disableMethod?.Invoke(_controller, null);
            return;
        }

        if (curr == null || !curr.IsValid())
        {
            curr?.Release();
            _dbgStatus = "Error: GVB null/invalid";
            return;
        }

        int gvbStride = curr.stride;
        int gvbCount  = curr.count;

        // 더블버퍼에 저장 (Release 후 교체)
        if (_isEven) { _gvbA?.Release(); _gvbA = curr; }
        else         { _gvbB?.Release(); _gvbB = curr; }

        // 이전 프레임 버퍼가 없으면 스킵 (첫 프레임)
        if (_prevGVB == null || !_prevGVB.IsValid())
        {
            _isEven = !_isEven;
            _dbgStatus = $"Warming up (stride:{gvbStride} count:{gvbCount})";
            return;
        }
        _dbgStatus = $"Running (GVB) stride:{gvbStride}";

        // ComputeVelocityDirect: vel = curr - prev
        int groups = Mathf.CeilToInt((float)_vertexCount / THREADS);
        using var cmd = new CommandBuffer { name = "OMB_Velocity" };

        cmd.SetComputeIntParam(skinningCacheShader, "_VertexCount", _vertexCount);
        // stride: GVB의 버텍스당 바이트 수. 위치는 항상 첫 12바이트(float3)
        // GVB stride를 float 단위로 변환
        // GetVertexBuffer stride = 버텍스당 바이트 수
        // 최소값 보장: 위치(12B) + 노멀(12B) = 최소 24B
        int strideInFloats = Mathf.Max(_currGVB.stride / 4, 3);
        cmd.SetComputeIntParam(skinningCacheShader, "_VertexStride", strideInFloats);
        cmd.SetComputeBufferParam(skinningCacheShader, _kernelVelocityDirect,
            "_CurrSkinBuffer",   _currGVB);
        cmd.SetComputeBufferParam(skinningCacheShader, _kernelVelocityDirect,
            "_PrevSkinBuffer",   _prevGVB);
        cmd.SetComputeBufferParam(skinningCacheShader, _kernelVelocityDirect,
            "_OMBVelocityBuffer", _velocityBuffer);
        cmd.DispatchCompute(skinningCacheShader, _kernelVelocityDirect, groups, 1, 1);

        Graphics.ExecuteCommandBuffer(cmd);
        _isEven = !_isEven;

        // velocity 버퍼는 InjectVelocityBuffer를 통해 P2_ObjectMotionBlurController가
        // 통합 관리합니다. MPB 직접 주입 모드는 weight 버퍼 덮어쓰기 문제를 유발하므로
        // 제거되었습니다.
    }

    private void OnDestroy()
    {
        _velocityBuffer?.Release();
        _gvbA?.Release();
        _gvbB?.Release();
    }
}
