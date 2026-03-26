using UnityEngine;

/// <summary>
/// [Phase 1 — AvatarAutoWeightBaker]
///
/// ■ 이 파일이 하는 일
///   Unity Humanoid Avatar의 본 계층 깊이를 기준으로
///   버텍스별 블러 가중치를 자동 계산하여 GPU 버퍼에 업로드합니다.
///   ObjectMotionBlurController에 버퍼 레퍼런스를 직접 주입하여
///   매 프레임 MPB가 재설정될 때 버퍼가 유실되지 않도록 합니다.
///
/// ■ 부착 위치
///   SkinnedMeshRenderer가 있는 오브젝트 (Skeleton 자식 등)에 부착.
///   ObjectMotionBlurController는 루트 오브젝트에 부착.
///   이 스크립트가 Awake에서 부모 계층을 순회하여 Controller를 찾습니다.
///
/// ■ 요구 조건
///   - Animator 컴포넌트 (같은 오브젝트 또는 부모에 존재)
///   - Animator.isHuman == true
///   - SkinnedMeshRenderer + BoneWeight 데이터
/// </summary>
// RequireComponent 없음 — 루트 오브젝트에 부착해도 자식에서 SMR 자동 탐색
public class AvatarAutoWeightBaker : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // Inspector
    // ─────────────────────────────────────────────────────────────
    [Header("Blur Weight Tuning")]
    [Tooltip("전체 가중치 스케일. 0=블러 없음, 1=계산값 그대로.")]
    [Range(0f, 1f)] public float globalIntensity = 1f;

    [Tooltip("팔 계열 말단 강조 지수. 값이 작을수록 손끝 가중치가 높아집니다.")]
    [Range(0.3f, 1.5f)] public float armBias = 0.65f;

    [Tooltip("다리 계열 말단 강조 지수.")]
    [Range(0.3f, 1.5f)] public float legBias = 0.5f;  // 다리도 더 강하게

    [Tooltip("척추/흉부 최대 가중치 상한. 0.35 이하 권장.")]
    [Range(0f, 0.5f)] public float spineCap = 0.35f;

    [Tooltip("머리 가중치 배율. 0이면 머리 블러 없음.")]
    [Range(0f, 1.0f)] public float headScale     = 0.6f;  // 머리/목도 가시적으로 블러
    [Tooltip("어깨/쇄골 가중치 스케일.")]
    [Range(0f, 1.0f)] public float shoulderScale = 0.7f;  // 어깨 파라미터화

    [Header("Debug")]
    [Tooltip("true: 베이킹 완료 시 가중치 통계를 콘솔에 출력합니다.")]
    public bool debugLog = false;

    // ─────────────────────────────────────────────────────────────
    // 내부 변수
    // ─────────────────────────────────────────────────────────────
    private SkinnedMeshRenderer     _smr;
    private Animator                _anim;
    private ComputeBuffer           _weightBuffer;
    private float[]                 _cachedWeights;   // P6_AimWeightOverride용 캐시
    private ObjectMotionBlurController _controller; // 루트의 Controller 캐시

    private static readonly int PropBlurWeightBuffer = Shader.PropertyToID("_BlurWeightBuffer");

    // ─────────────────────────────────────────────────────────────
    // Unity 생명주기
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // 자신 포함 자식에서 SkinnedMeshRenderer 탐색
        // RequireComponent 없으므로 루트에 부착해도 자식 SMR을 찾을 수 있음
        _smr = GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (_smr == null)
        {
            Debug.LogWarning(
                $"[AvatarAutoWeightBaker] '{name}': SkinnedMeshRenderer를 찾을 수 없습니다. " +
                "이 컴포넌트를 SkinnedMeshRenderer가 있는 오브젝트 또는 그 부모에 부착하세요.", this);
            enabled = false;
            return;
        }

        // Animator는 자신 또는 부모에서 탐색
        _anim = GetComponentInParent<Animator>(true);

        if (_anim == null)
        {
            Debug.LogWarning(
                $"[AvatarAutoWeightBaker] '{name}': Animator를 찾을 수 없습니다.", this);
            enabled = false;
            return;
        }

        if (!_anim.isHuman)
        {
            Debug.LogWarning(
                $"[AvatarAutoWeightBaker] '{name}': Humanoid Avatar가 설정되지 않았습니다." +
                " 컴포넌트를 비활성화합니다.", this);
            enabled = false;
            return;
        }

        // ObjectMotionBlurController 탐색 — 부모 계층 전체 순회
        // GetComponentInParent 대신 직접 순회하여 비활성 오브젝트도 탐색
        Transform t = transform;
        while (t != null)
        {
            _controller = t.GetComponent<ObjectMotionBlurController>();
            if (_controller != null) break;
            t = t.parent;
        }

        BakeWeights();
    }

    private void OnDestroy()
    {
        _weightBuffer?.Release();
        _weightBuffer = null;
    }

    // ─────────────────────────────────────────────────────────────
    // 베이킹
    // ─────────────────────────────────────────────────────────────
    private void BakeWeights()
    {
        Mesh        mesh        = _smr.sharedMesh;
        if (mesh == null)
        {
            Debug.LogWarning($"[AvatarAutoWeightBaker] '{name}': sharedMesh가 null입니다.", this);
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

        // Step 1: 각 본의 Hips로부터 깊이 계산
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
            if (boneDepth[i] > maxDepth) maxDepth = boneDepth[i];
        }

        // Step 2: 버텍스별 가중치 계산
        float[] vertWeights = new float[mesh.vertexCount];
        for (int v = 0; v < mesh.vertexCount; v++)
        {
            BoneWeight bw = boneWeights[v];
            float w0 = CalcBoneBlurWeight(bones, boneDepth, maxDepth, bw.boneIndex0) * bw.weight0;
            float w1 = CalcBoneBlurWeight(bones, boneDepth, maxDepth, bw.boneIndex1) * bw.weight1;
            vertWeights[v] = Mathf.Clamp01((w0 + w1) * globalIntensity);
        }

        // Step 3: GPU 버퍼 생성
        _weightBuffer = new ComputeBuffer(vertWeights.Length, sizeof(float));
        _weightBuffer.SetData(vertWeights);
        _cachedWeights = vertWeights; // P6_AimWeightOverride용 캐시

        // Step 4: 버퍼 전달
        // ─────────────────────────────────────────────────────────
        // ObjectMotionBlurController가 있으면 직접 주입
        // Controller가 매 LateUpdate에서 _weightBuffer를 MPB에 포함시키므로
        // GetPropertyBlock/SetPropertyBlock 덮어쓰기 문제가 사라집니다.
        // ─────────────────────────────────────────────────────────
        if (_controller != null)
        {
            _controller.InjectWeightBuffer(_weightBuffer);
            // 주입 완료는 debugLog 관계없이 항상 출력 — 연결 확인용
            Debug.Log(
                $"[AvatarAutoWeightBaker] '{name}': WeightBuffer → " +
                $"'{_controller.name}' 주입 완료 (버텍스 {vertWeights.Length}개)");
        }
        else
        {
            // Controller 없음 — 직접 MPB에 설정 (폴백)
            var mpb = new MaterialPropertyBlock();
            _smr.GetPropertyBlock(mpb);
            mpb.SetBuffer(PropBlurWeightBuffer, _weightBuffer);
            _smr.SetPropertyBlock(mpb);
            Debug.LogWarning(
                $"[AvatarAutoWeightBaker] '{name}': ObjectMotionBlurController를 찾을 수 없어 " +
                "직접 MPB 설정. 루트에 ObjectMotionBlurController가 부착되어 있는지 확인하세요.");
        }

        if (debugLog) PrintDebugStats(vertWeights);
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────
    private float CalcBoneBlurWeight(Transform[] bones, int[] depths, int maxDepth, int boneIdx)
    {
        if (boneIdx < 0 || boneIdx >= bones.Length) return 0f;
        float t = (float)depths[boneIdx] / Mathf.Max(maxDepth, 1);
        string n = bones[boneIdx] != null ? bones[boneIdx].name.ToLower() : "";

        if (n.Contains("arm") || n.Contains("hand") || n.Contains("finger") || n.Contains("thumb"))
            t = Mathf.Pow(t, armBias);
        else if (n.Contains("leg") || n.Contains("foot") || n.Contains("toe") || n.Contains("ankle"))
            t = Mathf.Pow(t, legBias);
        else if (n.Contains("spine") || n.Contains("chest"))
            t = Mathf.Min(t, spineCap);
        else if (n.Contains("head") || n.Contains("neck"))
            t *= headScale;
        else if (n.Contains("shoulder") || n.Contains("clavicle"))
            t *= shoulderScale;

        return Mathf.Clamp01(t);
    }

    /// <summary>
    /// P6_AimWeightOverride에서 호출합니다.
    /// BakeWeights() 완료 후 캐시된 가중치 배열을 반환합니다.
    /// 베이킹 전에는 null을 반환합니다.
    /// </summary>
    public float[] GetCachedWeights() => _cachedWeights;

    private int CalcDepthFromRoot(Transform bone, Transform root)
    {
        if (bone == null) return 0;
        int depth = 0;
        Transform cur = bone;
        while (cur != null && cur != root) { cur = cur.parent; depth++; if (depth > 30) break; }
        return depth;
    }

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
            $"  가중치=0 버텍스 수: {zeroCount} ({(float)zeroCount / weights.Length * 100:F1}%)", this);
    }
}
