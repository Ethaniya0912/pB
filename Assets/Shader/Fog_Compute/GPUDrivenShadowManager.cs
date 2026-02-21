using UnityEngine;
using Unity.Collections;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement; // 씬 전환 이벤트를 위해 추가

/// <summary>
/// [Lead Architect] 
/// 2,200개 이상의 그림자 캐스터 데이터를 GPU에 상주시키고,
/// CPU 오버헤드 없이 간접 렌더링(Indirect Rendering)을 수행하는 제어 매니저입니다.
/// </summary>
public class GPUDrivenShadowManager : MonoBehaviour
{
    // [싱글톤 인스턴스] 전역 접근 및 씬 전환 유지 보장
    public static GPUDrivenShadowManager Instance { get; private set; }

    [Header("Dependencies")]
    public ComputeShader cullingCompute;
    public Material shadowMaterial;
    public Mesh instanceMesh;

    // [메모리 정렬: 16바이트 준수 (총 80 Bytes)]
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceData
    {
        public Matrix4x4 localToWorld; // 64 Bytes (16 x 4)
        public Vector3 boundsCenter;   // 12 Bytes
        public float boundsRadius;     // 4 Bytes -> 합계 16 Bytes 패딩 완료. Total: 80 Bytes
    }

    private GraphicsBuffer _instanceDataBuffer;
    private GraphicsBuffer _visibleIndexBuffer;
    private GraphicsBuffer _argsBuffer;

    private NativeArray<InstanceData> _instanceDataNative;
    private int _instanceCount;
    private int _kernelID;

    private static readonly int _InstanceBufferID = Shader.PropertyToID("_InstanceDataBuffer");
    private static readonly int _VisibleIndexBufferID = Shader.PropertyToID("_VisibleIndexBuffer");
    private static readonly int _FrustumPlanesID = Shader.PropertyToID("_FrustumPlanes");

    private uint[] _args = new uint[5] { 0, 0, 0, 0, 0 };
    private bool _isInitialized = false; // 중복 초기화 방지 플래그

    private void Awake()
    {
        // [씬 전환 로직 1단계: 싱글톤 유지]
        // 씬이 넘어가도 매니저 오브젝트가 파괴되지 않도록 DontDestroyOnLoad를 적용합니다.
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnEnable()
    {
        // [씬 전환 로직 2단계: 이벤트 구독]
        // 새로운 씬이 로드될 때마다 OnSceneLoaded 함수가 자동으로 호출되도록 등록합니다.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != this) return;

        // [씬 전환 로직 3단계: 버퍼 해제 및 재스캔]
        // 씬이 전환되면 이전 씬의 물체들은 파괴되므로, VRAM에 남아있는 쓸모없는 기존 버퍼를 안전하게 비웁니다.
        ReleaseBuffers();

        // 새로운 씬에 존재하는 MeshRenderer들을 다시 스캔하여 새로운 버퍼를 구축합니다.
        InitializeSystem();
    }

    private void Start()
    {
        if (!_isInitialized)
        {
            InitializeSystem();
        }
    }

    private void InitializeSystem()
    {
        // 1. 씬 내의 모든 렌더러 수집 (초기화 1회 한정, 가비지 컬렉션 허용 구역)
        MeshRenderer[] allRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        System.Collections.Generic.List<MeshRenderer> validRenderers = new System.Collections.Generic.List<MeshRenderer>();

        foreach (var r in allRenderers)
        {
            // [핵심 Fix 1] 그림자 수집 필터링!
            // 원래 그림자를 만들지 않는 물체(전구, 투명 트리거)나 비활성화된 물체는 가짜 그림자 생성 대상에서 완전히 제외합니다.
            if (!r.enabled || r.shadowCastingMode == ShadowCastingMode.Off)
                continue;

            validRenderers.Add(r);
        }

        _instanceCount = validRenderers.Count;

        if (_instanceCount == 0 || instanceMesh == null) return;

        // 2. NativeArray 할당 (비관리 메모리 사용으로 GC 차단)
        _instanceDataNative = new NativeArray<InstanceData>(_instanceCount, Allocator.Persistent);

        for (int i = 0; i < _instanceCount; i++)
        {
            var r = validRenderers[i];

            // [핵심 Fix 2] 본래 물체의 렌더링은 살려두고, 기존 CPU 그림자 투사만 끕니다.
            // (이전에 있던 r.enabled = false; 악성 코드를 제거하여 본래 물체가 사라지는 현상을 막습니다.)
            r.shadowCastingMode = ShadowCastingMode.Off;

            _instanceDataNative[i] = new InstanceData
            {
                localToWorld = r.transform.localToWorldMatrix,
                boundsCenter = r.bounds.center,
                boundsRadius = r.bounds.extents.magnitude // 외접구 반지름 계산
            };
        }

        // 3. GPU Buffer 할당
        int stride = Marshal.SizeOf<InstanceData>(); // 80 bytes

        // [누락 복구] 실제 VRAM 버퍼 할당 및 데이터 전송 로직
        _instanceDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _instanceCount, stride);
        _instanceDataBuffer.SetData(_instanceDataNative);

        _visibleIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, _instanceCount, sizeof(uint));

        _argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint) * 5);
        _args[0] = instanceMesh.GetIndexCount(0);
        _argsBuffer.SetData(_args);

        // 4. Compute Shader 및 Material 바인딩
        _kernelID = cullingCompute.FindKernel("CSMain");
        cullingCompute.SetBuffer(_kernelID, _InstanceBufferID, _instanceDataBuffer);
        cullingCompute.SetBuffer(_kernelID, _VisibleIndexBufferID, _visibleIndexBuffer);

        shadowMaterial.SetBuffer(_InstanceBufferID, _instanceDataBuffer);
        shadowMaterial.SetBuffer(_VisibleIndexBufferID, _visibleIndexBuffer);

        // 초기화 완료 플래그 설정
        _isInitialized = true;
    }

    private void Update()
    {
        if (_instanceCount == 0) return;

        // [핵심 Fix] 런타임 NullReferenceException 완벽 차단 (방어 코드 복구)
        // 1. 에디터 할당 및 GPU 버퍼 생성 여부 체크
        if (cullingCompute == null || shadowMaterial == null || instanceMesh == null) return;
        if (_visibleIndexBuffer == null || _argsBuffer == null || _instanceDataBuffer == null) return;

        // 2. 씬에 메인 카메라가 존재하는지 체크 (가장 유력한 에러 원인)
        Camera cam = Camera.main;
        if (cam == null) return;

        // 카메라 프러스텀 평면 계산 (6개 평면)
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        Vector4[] planeVectors = new Vector4[6];
        for (int i = 0; i < 6; i++)
        {
            planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
        }
        cullingCompute.SetVectorArray(_FrustumPlanesID, planeVectors);
        cullingCompute.SetInt("_InstanceCount", _instanceCount);

        _visibleIndexBuffer.SetCounterValue(0);

        int threadGroups = Mathf.CeilToInt(_instanceCount / 64.0f);
        cullingCompute.Dispatch(_kernelID, threadGroups, 1, 1);

        GraphicsBuffer.CopyCount(_visibleIndexBuffer, _argsBuffer, sizeof(uint));

        Graphics.DrawMeshInstancedIndirect(
            instanceMesh,
            0,
            shadowMaterial,
            new Bounds(Vector3.zero, Vector3.one * 10000f),
            _argsBuffer
        );
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            ReleaseBuffers();
        }
    }

    /// <summary>
    /// VRAM 및 비관리 메모리 누수를 철저히 방지하기 위해 버퍼를 명시적으로 해제합니다.
    /// </summary>
    private void ReleaseBuffers()
    {
        // [씬 전환 로직 4단계: 안전한 자원 해제]
        // Dispose와 Release를 호출하여 이전 씬의 메모리를 완벽히 정리합니다.
        if (_instanceDataNative.IsCreated) _instanceDataNative.Dispose();
        _instanceDataBuffer?.Release();
        _visibleIndexBuffer?.Release();
        _argsBuffer?.Release();

        _instanceCount = 0;
        _isInitialized = false;
    }
}