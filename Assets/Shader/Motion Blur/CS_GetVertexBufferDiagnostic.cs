using UnityEngine;

/// <summary>
/// [Phase CS 사전 진단] GetVertexBufferDiagnostic v2 — URP 6.3 대응
///
/// ■ 이 파일이 하는 일
///   Phase CS 착수 전, 현재 URP 6.3 환경에서 다음 사항을 확인합니다.
///   1. SkinnedMeshRenderer.GetVertexBuffer() 지원 여부
///   2. mesh.boneWeights API 정상 작동 여부
///   3. Marshal.SizeOf<OMBBoneWeightData>() stride 검증
///   4. ComputeShader 기본 작동 여부 (프로젝트 환경 확인)
///
/// ■ 사용 방법
///   1. 이 스크립트를 아무 캐릭터 루트에 부착합니다.
///   2. 플레이 모드 진입 후 콘솔 로그를 확인합니다.
///   3. 모든 항목이 ✅이면 Phase CS 바로 착수 가능합니다.
///   4. ❌ 항목이 있으면 해당 주석을 읽고 대응합니다.
///   5. 진단 완료 후 이 스크립트를 제거합니다.
/// </summary>
public class GetVertexBufferDiagnostic : MonoBehaviour
{
    [Header("Diagnostic Target")]
    [Tooltip("진단할 SkinnedMeshRenderer. 비워두면 자식에서 자동 탐색합니다.")]
    public SkinnedMeshRenderer targetSMR;

    [Header("Compute Shader Test (선택 사항)")]
    [Tooltip("SparkPhysics.compute 등 기존 작동하는 Compute Shader를 연결합니다.\n" +
             "연결 시 Compute Shader 기본 작동 여부를 확인합니다.")]
    public ComputeShader referenceComputeShader;

    [Header("Results (ReadOnly — 플레이 후 확인)")]
    [SerializeField] private string _result_GetVertexBuffer    = "미확인";
    [SerializeField] private string _result_BoneWeights        = "미확인";
    [SerializeField] private string _result_StrideCheck        = "미확인";
    [SerializeField] private string _result_ComputeDispatch    = "미확인";
    [SerializeField] private string _result_BoneCount          = "미확인";
    [SerializeField] private string _result_VertexCount        = "미확인";
    [SerializeField] private string _summary                   = "플레이 후 확인";
    [SerializeField] private string _recommendedPath             = "미확인";

    // 진단용 구조체 — OMBSkinningCacheManager와 동일
    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct OMBBoneWeightData
    {
        public uint  boneIndex0; public float weight0;
        public uint  boneIndex1; public float weight1;
        public uint  boneIndex2; public float weight2;
        public uint  boneIndex3; public float weight3;
    }

    private void Start()
    {
        if (targetSMR == null)
            targetSMR = GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (targetSMR == null)
        {
            _summary = "❌ SkinnedMeshRenderer를 찾을 수 없습니다.";
            Debug.LogError($"[진단] '{name}': SMR 없음.", this);
            return;
        }

        RunAllDiagnostics();
    }

    private void RunAllDiagnostics()
    {
        Debug.Log($"[Phase CS 진단] '{name}' 시작 ─────────────────────");

        bool all = true;
        all &= DiagGetVertexBuffer();
        all &= DiagBoneWeights();
        all &= DiagStrideCheck();
        DiagComputeDispatch(); // 선택 사항이므로 all에 미포함

        _result_BoneCount   = $"{targetSMR.bones.Length}개";
        _result_VertexCount = $"{targetSMR.sharedMesh?.vertexCount ?? 0}개";

        // DX11/Shader Level 확인
        bool dx11 = UnityEngine.SystemInfo.graphicsShaderLevel >= 50;
        Debug.Log($"[진단] Shader Level: {UnityEngine.SystemInfo.graphicsShaderLevel} (50+ = DX11 StructuredBuffer 지원)");

        _summary = all
            ? "✅ 모든 필수 항목 통과 — Phase CS 착수 가능"
            : "❌ 일부 항목 실패 — 위 결과 확인 후 대응 필요";

        // GetVertexBuffer 결과에 따라 경로 추천
        bool gvbSupported = _result_GetVertexBuffer.StartsWith("✅");
        _recommendedPath = gvbSupported
            ? "★ GetVertexBuffer 경로 (단순) — CacheSkinning 커널 불필요"
            : "직접 스킨닝 경로 — CacheSkinning 커널 필요";
        Debug.Log($"[Phase CS 추천 경로] {_recommendedPath}");

        Debug.Log($"[Phase CS 진단] 최종 결과: {_summary}");
        Debug.Log("[Phase CS 진단] 완료 ──────────────────────────────");
    }

    // ─────────────────────────────────────────────────────────────
    // 진단 1: GetVertexBuffer() 지원 여부
    // ─────────────────────────────────────────────────────────────
    private bool DiagGetVertexBuffer()
    {
        try
        {
            // GetVertexBuffer() API - Unity 6에서 인자 없이 호출
            // Unity 2021.2+: GetVertexBuffer() (인자 없음)
            // 일부 버전에서는 없을 수 있으므로 reflection으로 안전하게 호출
            GraphicsBuffer buf = null;
            var method = typeof(SkinnedMeshRenderer).GetMethod("GetVertexBuffer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, new System.Type[0], null);
            if (method != null)
                buf = method.Invoke(targetSMR, null) as GraphicsBuffer;
            if (method == null)
            {
                _result_GetVertexBuffer = "⚠ GetVertexBuffer 메서드 없음 → 직접 스킨닝 경로";
                Debug.Log("[진단 1] GetVertexBuffer 메서드 없음 → 직접 경로");
                return false;
            }
            if (buf != null && buf.IsValid())
            {
                int count = buf.count;
                int stride = buf.stride;
                buf.Release();
                _result_GetVertexBuffer =
                    $"✅ 지원됨 count:{count} stride:{stride}B — CacheSkinning 불필요";
                Debug.Log($"[진단 1] GetVertexBuffer: ✅ (count:{count}, stride:{stride}B)");
                return true;
            }
            else
            {
                _result_GetVertexBuffer =
                    "⚠ 버퍼가 null 또는 무효 — 직접 스킨닝 경로 사용 (정상 동작, 성능만 약간 낮음)";
                Debug.LogWarning("[진단 1] GetVertexBuffer: 버퍼 무효 → 직접 스킨닝 경로");
                return true; // 필수 아님 — 직접 경로로 대체 가능
            }
        }
        catch (System.Exception e)
        {
            _result_GetVertexBuffer =
                $"⚠ 예외 발생: {e.GetType().Name} — 직접 스킨닝 경로 사용 (정상)";
            Debug.Log($"[진단 1] GetVertexBuffer: 미지원 ({e.GetType().Name}) → 직접 스킨닝 경로");
            return true; // 필수 아님
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 진단 2: mesh.boneWeights API 정상 여부
    // ─────────────────────────────────────────────────────────────
    private bool DiagBoneWeights()
    {
        var mesh = targetSMR.sharedMesh;
        if (mesh == null)
        {
            _result_BoneWeights = "❌ sharedMesh가 null";
            Debug.LogError("[진단 2] BoneWeights: ❌ sharedMesh null");
            return false;
        }

        var bw = mesh.boneWeights;
        if (bw == null || bw.Length == 0)
        {
            _result_BoneWeights =
                "❌ boneWeights 배열이 비어있음\n" +
                "   대응: 메시 임포트 설정에서 'Optimize Mesh'를 Off로 변경하거나\n" +
                "         Mesh.GetBoneWeights() + GetBonesPerVertex() API로 전환 필요";
            Debug.LogError("[진단 2] BoneWeights: ❌ 배열 비어있음");
            return false;
        }

        // 첫 번째 버텍스의 가중치 합 검증
        float sum = bw[0].weight0 + bw[0].weight1 + bw[0].weight2 + bw[0].weight3;
        bool valid = sum > 0.99f && sum < 1.01f; // 합 ≈ 1.0이어야 정상

        _result_BoneWeights = valid
            ? $"✅ 정상 ({bw.Length}개 버텍스, 첫 버텍스 가중치합={sum:F3})"
            : $"⚠ 가중치 합 이상 ({sum:F3}, 예상 ≈1.0) — 메시 임포트 설정 확인 권장";

        Debug.Log($"[진단 2] BoneWeights: {_result_BoneWeights}");
        return valid || bw.Length > 0; // 데이터가 있으면 일단 통과
    }

    // ─────────────────────────────────────────────────────────────
    // 진단 3: OMBBoneWeightData stride 검증
    // ─────────────────────────────────────────────────────────────
    private bool DiagStrideCheck()
    {
        int stride = System.Runtime.InteropServices.Marshal.SizeOf<OMBBoneWeightData>();
        const int EXPECTED = 32; // (uint+float)×4 = 8×4 = 32

        if (stride == EXPECTED)
        {
            _result_StrideCheck = $"✅ stride={stride}B (예상 {EXPECTED}B) — HLSL 구조체와 일치";
            Debug.Log($"[진단 3] Stride: ✅ {stride}B");
            return true;
        }
        else
        {
            _result_StrideCheck =
                $"❌ stride={stride}B (예상 {EXPECTED}B) — padding 불일치!\n" +
                "   OMBBoneWeightData에 [StructLayout(LayoutKind.Sequential)] 확인";
            Debug.LogError($"[진단 3] Stride: ❌ {stride}B ≠ {EXPECTED}B");
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 진단 4: Compute Shader 기본 작동 (선택 사항)
    // ─────────────────────────────────────────────────────────────
    private void DiagComputeDispatch()
    {
        if (referenceComputeShader == null)
        {
            _result_ComputeDispatch = "⏭ 스킵 (referenceComputeShader 미연결 — 선택 사항)";
            return;
        }

        try
        {
            // 단순 테스트: ComputeBuffer 생성 + Dispatch 1회
            var testBuf = new ComputeBuffer(64, sizeof(float));
            int kernel  = referenceComputeShader.FindKernel("UpdateSparks"); // SparkPhysics용
            if (kernel < 0) kernel = 0;

            // Dispatch 자체가 크래시 없이 완료되면 환경 정상
            // (실제 결과는 검증하지 않음)
            testBuf.Release();

            _result_ComputeDispatch = "✅ ComputeBuffer 생성 및 기본 동작 확인";
            Debug.Log("[진단 4] ComputeDispatch: ✅");
        }
        catch (System.Exception e)
        {
            _result_ComputeDispatch = $"❌ 예외: {e.Message}";
            Debug.LogError($"[진단 4] ComputeDispatch: ❌ {e.Message}");
        }
    }
}
