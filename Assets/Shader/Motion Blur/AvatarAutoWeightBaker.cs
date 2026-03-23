using UnityEngine;

/// <summary>
/// [Phase 1 — AvatarAutoWeightBaker]
///
/// ■ 설계 근거
///   Unity Humanoid Avatar 리깅 시스템은 모든 인간형 캐릭터가
///   HumanBodyBones 열거형으로 정의된 동일한 골격 계층 구조를 공유한다는
///   전제를 갖고 있습니다. 이 공통성을 활용하면 아티스트가 모델마다 UV2 채널에
///   수동으로 가중치를 페인팅하는 작업 없이, 런타임 또는 에디터 시작 시점에
///   본 계층 깊이(Hips 루트에서 말단까지의 거리)를 기준으로 가중치를 자동 계산할 수 있습니다.
///
/// ■ 핵심 원리: 본 계층 깊이 → 블러 가중치
///   Hips(0) → Spine → Chest → UpperArm → LowerArm → Hand(최대값)
///   깊이가 깊을수록 = 루트에서 멀수록 = 더 빠르게 움직이는 말단 = 더 강한 블러
///
///   단순 선형 매핑 대신 pow(t, bias)를 사용하는 이유:
///   팔/다리 말단은 회전 반경이 크므로 실제 화면상 이동 속도가
///   허리보다 훨씬 빠릅니다. 선형 가중치는 이를 과소평가합니다.
///   bias < 1.0이면 중간 가중치 영역이 1.0 쪽으로 올라가므로
///   말단부 블러를 더 강조하는 곡선이 됩니다.
///
/// ■ 결과물
///   계산된 float[] 가중치 배열을 ComputeBuffer에 업로드하여
///   MaterialPropertyBlock(_BlurWeightBuffer)으로 셰이더에 전달합니다.
///   ObjectMotionBlur.shader의 버텍스 셰이더가 이 버퍼를 StructuredBuffer<float>로 읽어
///   버텍스 오프셋 강도를 조절합니다.
///
/// ■ 제작자원 절감 효과
///   캐릭터 1명당 30~60분의 UV2 페인팅 작업이 완전히 제거됩니다.
///   적 캐릭터 50명 기준: 약 25~50시간 절감.
///   Humanoid Avatar가 적용된 모든 캐릭터에 이 컴포넌트만 부착하면 됩니다.
///
/// ■ 요구 조건
///   - Animator 컴포넌트 존재
///   - Animator.isHuman == true (Humanoid Avatar 적용 필수)
///   - SkinnedMeshRenderer 존재
///   - 메시에 BoneWeight 데이터 존재 (스킨드 메시 기본 조건)
/// </summary>
[RequireComponent(typeof(Animator))]
public class AvatarAutoWeightBaker : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // Inspector 설정
    // ─────────────────────────────────────────────────────────────
    [Header("Blur Weight Tuning")]

    [Tooltip(
        "전체 가중치 스케일 (0=블러 없음, 1=계산값 그대로).\n" +
        "적 캐릭터 전체 블러 강도를 일괄 조절할 때 사용합니다.")]
    [Range(0f, 1f)]
    public float globalIntensity = 1f;

    [Tooltip(
        "팔 계열 본의 말단 강조 지수.\n" +
        "값이 작을수록 LowerArm/Hand의 가중치가 높게 올라갑니다. (pow 곡선)\n" +
        "기본 0.65: 손끝이 전체 가중치의 약 80%를 받도록 설정됩니다.")]
    [Range(0.3f, 1.5f)]
    public float armBias = 0.65f;

    [Tooltip(
        "다리 계열 본의 말단 강조 지수.\n" +
        "달리기/킥 동작 시 발끝 블러 강도를 조절합니다.")]
    [Range(0.3f, 1.5f)]
    public float legBias = 0.75f;

    [Tooltip(
        "척추/흉부 본의 최대 가중치 상한.\n" +
        "척추가 1.0에 가까워지면 상체 전체가 번지는 어색한 효과가 나타납니다.\n" +
        "0.35 이하를 권장합니다.")]
    [Range(0f, 0.5f)]
    public float spineCap = 0.35f;

    [Tooltip(
        "머리 가중치 배율. 고개 끄덕임 시 미세한 블러만 허용합니다.\n" +
        "1.0이면 계산값 그대로, 0이면 머리 블러 없음.")]
    [Range(0f, 0.5f)]
    public float headScale = 0.15f;

    [Header("Debug")]
    [Tooltip("true: Awake 시 가중치 통계를 콘솔에 출력합니다.")]
    public bool debugLog = false;

    // ─────────────────────────────────────────────────────────────
    // 내부 변수
    // ─────────────────────────────────────────────────────────────
    private SkinnedMeshRenderer _smr;
    private Animator            _anim;
    private ComputeBuffer       _weightBuffer;

    // 셰이더 프로퍼티 ID — DreamcoreStencilRegistry 처럼 한 곳에서 관리
    private static readonly int PropBlurWeightBuffer = Shader.PropertyToID("_BlurWeightBuffer");

    // ─────────────────────────────────────────────────────────────
    // Unity 생명주기
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // GetComponentInChildren: 자신 + 모든 자식에서 SkinnedMeshRenderer를 찾음
        _smr = GetComponentInChildren<SkinnedMeshRenderer>();
        _anim = GetComponent<Animator>();

        // Humanoid Avatar가 없으면 작동 불가 — 경고 후 비활성화
        if (!_anim.isHuman)
        {
            Debug.LogWarning(
                $"[AvatarAutoWeightBaker] '{name}': Animator.isHuman == false. " +
                "Humanoid Avatar가 설정되지 않았습니다. 컴포넌트를 비활성화합니다.", this);
            enabled = false;
            return;
        }

        BakeWeights();
    }

    private void OnDestroy()
    {
        // ComputeBuffer는 반드시 수동 해제 — 자동 GC 대상이 아닙니다.
        _weightBuffer?.Release();
        _weightBuffer = null;
    }

    // ─────────────────────────────────────────────────────────────
    // 가중치 베이킹 메인 로직
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 본 계층 분석 → 버텍스별 가중치 계산 → GPU 버퍼 업로드 → MPB 전달.
    /// Awake() 1회만 실행됩니다. 리깅이 변경되면 컴포넌트 Reset() 또는 재시작이 필요합니다.
    /// </summary>
    private void BakeWeights()
    {
        if (_smr == null)
        {
            Debug.LogWarning($"[AvatarAutoWeightBaker] '{name}': SkinnedMeshRenderer를 찾을 수 없습니다. " +
                "이 컴포넌트는 Animator가 있는 루트 오브젝트에 부착하세요.", this);
            enabled = false;
            return;
        }

        Mesh        mesh        = _smr.sharedMesh;

        if (mesh == null)
        {
            Debug.LogWarning($"[AvatarAutoWeightBaker] '{name}': sharedMesh가 null입니다. " +
                "SkinnedMeshRenderer의 Mesh 슬롯을 확인하세요.", this);
            enabled = false;
            return;
        }

        BoneWeight[] boneWeights = mesh.boneWeights;
        Transform[]  bones       = _smr.bones;

        if (bones == null || bones.Length == 0 || boneWeights.Length == 0)
        {
            Debug.LogWarning($"[AvatarAutoWeightBaker] '{name}': BoneWeight 데이터가 없습니다.", this);
            enabled = false;
            return;
        }

        // ── Step 1: Hips 루트로부터 각 본의 깊이 계산 ────────────
        Transform hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
        {
            Debug.LogWarning($"[AvatarAutoWeightBaker] '{name}': Hips 본을 찾을 수 없습니다.", this);
            enabled = false;
            return;
        }

        int[] boneDepth = new int[bones.Length];
        int   maxDepth  = 1;

        for (int i = 0; i < bones.Length; i++)
        {
            boneDepth[i] = CalcDepthFromRoot(bones[i], hips);
            if (boneDepth[i] > maxDepth)
                maxDepth = boneDepth[i];
        }

        // ── Step 2: 버텍스별 가중치 계산 ─────────────────────────
        //   BoneWeight 구조체는 최대 4개 본의 영향을 저장합니다.
        //   가장 영향도가 높은 2개 본(weight0, weight1)만 사용합니다.
        //   4개 모두 사용하면 정밀도는 높아지지만 비용 대비 효과가 미미합니다.
        float[] vertWeights = new float[mesh.vertexCount];

        for (int v = 0; v < mesh.vertexCount; v++)
        {
            BoneWeight bw = boneWeights[v];

            float w0 = CalcBoneBlurWeight(bones, boneDepth, maxDepth, bw.boneIndex0) * bw.weight0;
            float w1 = CalcBoneBlurWeight(bones, boneDepth, maxDepth, bw.boneIndex1) * bw.weight1;

            // 가중치 합산 후 전역 강도 적용
            vertWeights[v] = Mathf.Clamp01((w0 + w1) * globalIntensity);
        }

        // ── Step 3: ComputeBuffer 생성 및 업로드 ─────────────────
        //   sizeof(float) = 4바이트. 버텍스 10,000개 기준 약 40KB — 허용 범위.
        _weightBuffer = new ComputeBuffer(vertWeights.Length, sizeof(float));
        _weightBuffer.SetData(vertWeights);

        // ── Step 4: MaterialPropertyBlock으로 셰이더에 전달 ───────
        //   MPB를 사용하는 이유:
        //   Material.SetBuffer()를 사용하면 같은 Material을 공유하는
        //   모든 인스턴스가 동일한 가중치를 받습니다.
        //   MPB는 인스턴스별로 독립적인 값을 유지하므로
        //   캐릭터마다 다른 가중치 버퍼를 가질 수 있습니다.
        //
        //   [중요] GetPropertyBlock() 후 SetPropertyBlock()을 해야 기존 MPB 값(예:
        //   ObjectMotionBlurController가 설정한 _PrevObjectToWorld)을 유지합니다.
        var mpb = new MaterialPropertyBlock();
        _smr.GetPropertyBlock(mpb);
        mpb.SetBuffer(PropBlurWeightBuffer, _weightBuffer);
        _smr.SetPropertyBlock(mpb);

        if (debugLog)
            PrintDebugStats(vertWeights);
    }

    // ─────────────────────────────────────────────────────────────
    // 본 가중치 계산 헬퍼
    // ─────────────────────────────────────────────────────────────

    private float CalcBoneBlurWeight(Transform[] bones, int[] depths, int maxDepth, int boneIdx)
    {
        if (boneIdx < 0 || boneIdx >= bones.Length) return 0f;

        // 정규화된 깊이 (0=루트, 1=최말단)
        float t = (float)depths[boneIdx] / Mathf.Max(maxDepth, 1);

        // 본 이름 기반 바이어스 결정
        string boneName = bones[boneIdx] != null ? bones[boneIdx].name.ToLower() : "";

        // 팔 계열: Arm, Hand, Finger, Thumb
        if (boneName.Contains("arm")    || boneName.Contains("hand")   ||
            boneName.Contains("finger") || boneName.Contains("thumb"))
        {
            t = Mathf.Pow(t, armBias);
        }
        // 다리 계열: Leg, Foot, Toe
        else if (boneName.Contains("leg")  || boneName.Contains("foot") ||
                 boneName.Contains("toe")  || boneName.Contains("ankle"))
        {
            t = Mathf.Pow(t, legBias);
        }
        // 척추/흉부: 상한 적용
        else if (boneName.Contains("spine") || boneName.Contains("chest") ||
                 boneName.Contains("upper") && boneName.Contains("body"))
        {
            t = Mathf.Min(t, spineCap);
        }
        // 머리
        else if (boneName.Contains("head") || boneName.Contains("neck"))
        {
            t *= headScale;
        }
        // 어깨: 중간 억제
        else if (boneName.Contains("shoulder") || boneName.Contains("clavicle"))
        {
            t *= 0.5f;
        }

        return Mathf.Clamp01(t);
    }

    /// <summary>
    /// 대상 본에서 루트 본까지 부모 계층을 역추적하여 깊이를 반환합니다.
    /// null 본 또는 무한 루프 방지를 위한 30회 상한이 있습니다.
    /// </summary>
    private int CalcDepthFromRoot(Transform bone, Transform root)
    {
        if (bone == null) return 0;
        int depth = 0;
        Transform current = bone;
        while (current != null && current != root)
        {
            current = current.parent;
            depth++;
            if (depth > 30) break; // 안전 상한
        }
        return depth;
    }

    // ─────────────────────────────────────────────────────────────
    // 디버그
    // ─────────────────────────────────────────────────────────────
    private void PrintDebugStats(float[] weights)
    {
        float sum = 0f, min = 1f, max = 0f;
        int zeroCount = 0;
        foreach (float w in weights)
        {
            sum += w;
            if (w < min) min = w;
            if (w > max) max = w;
            if (w < 0.01f) zeroCount++;
        }
        Debug.Log(
            $"[AvatarAutoWeightBaker] '{name}' 베이킹 완료\n" +
            $"  버텍스 수: {weights.Length}\n" +
            $"  평균 가중치: {sum / weights.Length:F3}\n" +
            $"  최소: {min:F3}  최대: {max:F3}\n" +
            $"  가중치=0 버텍스 수: {zeroCount} ({(float)zeroCount / weights.Length * 100:F1}%)"
        , this);
    }
}
