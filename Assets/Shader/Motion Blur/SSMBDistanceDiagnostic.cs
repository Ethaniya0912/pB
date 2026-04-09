using UnityEngine;

/// <summary>
/// [SSMB+DepthBlur 진단 스크립트 V2]
/// 
/// V1 → V2 추가 사항:
///   DepthBlur 관련 글로벌값 (_NearDist, _GlobalSpeedFactor, _LockOnActive) 측정 추가.
///   캐릭터가 DepthBlur 적용 범위에 속하는지 실시간 판정.
/// </summary>
public class SSMBDistanceDiagnostic : MonoBehaviour
{
    [Header("측정 대상 (반드시 연결)")]
    public Camera mainCamera;
    public Transform characterRoot;

    [Header("기준값")]
    public float ssmbDepthNear = 6.0f;   // SSMB 보호 거리
    public float dbNearDist = 20.0f;  // DepthBlur Near 보호 거리 (mat 기준)

    // ── 글로벌 캐시 ──
    private static readonly int ID_SSBlurNear = Shader.PropertyToID("_SSBlurDepthNear");
    private static readonly int ID_SSBlurFade = Shader.PropertyToID("_SSBlurDepthFade");
    private static readonly int ID_IntScale = Shader.PropertyToID("_SSMBIntensityScale");
    private static readonly int ID_DistScale = Shader.PropertyToID("_SSMBDistanceScale");
    private static readonly int ID_BudgetScale = Shader.PropertyToID("_SSMBBudgetScale");
    private static readonly int ID_NearDist = Shader.PropertyToID("_NearDist");
    private static readonly int ID_MidDist = Shader.PropertyToID("_MidDist");
    private static readonly int ID_SpeedFactor = Shader.PropertyToID("_GlobalSpeedFactor");
    private static readonly int ID_LockOnActive = Shader.PropertyToID("_LockOnActive");
    private static readonly int ID_PlayerProtect = Shader.PropertyToID("_PlayerProtectRadius");

    // ── 실시간 측정값 ──
    private float _dist;
    private float _ssmb_near, _ssmb_fade, _ssmb_intScale;
    private float _db_nearDist, _db_midDist, _db_speedFactor, _db_lockOn, _db_playerProtect;

    void Update()
    {
        if (mainCamera == null || characterRoot == null) return;
        _dist = Vector3.Distance(mainCamera.transform.position, characterRoot.position);
        _ssmb_near = Shader.GetGlobalFloat(ID_SSBlurNear);
        _ssmb_fade = Shader.GetGlobalFloat(ID_SSBlurFade);
        _ssmb_intScale = Shader.GetGlobalFloat(ID_IntScale);
        _db_nearDist = Shader.GetGlobalFloat(ID_NearDist);
        _db_midDist = Shader.GetGlobalFloat(ID_MidDist);
        _db_speedFactor = Shader.GetGlobalFloat(ID_SpeedFactor);
        _db_lockOn = Shader.GetGlobalFloat(ID_LockOnActive);
        _db_playerProtect = Shader.GetGlobalFloat(ID_PlayerProtect);

        if (Input.GetKeyDown(KeyCode.Space)) PrintSnapshot();
    }

    void PrintSnapshot()
    {
        string camPos = mainCamera != null ? mainCamera.transform.position.ToString("F2") : "N/A";
        string charPos = characterRoot != null ? characterRoot.position.ToString("F2") : "N/A";

        // SSMB 판정
        bool ssmbOk = _ssmb_near > 0.5f && _dist < _ssmb_near;
        // DepthBlur 판정
        float effectiveNear = _db_nearDist; // 글로벌 override 값
        bool dbExposed = _dist >= effectiveNear;

        Debug.Log(
            "[SSMB+DepthBlur Diagnostic V2]\n" +
            "══════════════════════════════════════════\n" +
            $"  카메라: {camPos}  캐릭터: {charPos}\n" +
            $"  카메라-캐릭터 거리: {_dist:F2}m\n" +
            "──────────────── SSMB ────────────────────\n" +
            $"  [Global] _SSBlurDepthNear : {_ssmb_near:F4}  {(ssmbOk ? "✓ 보호됨" : "✗ 노출!")}\n" +
            $"  [Global] _SSBlurDepthFade : {_ssmb_fade:F4}\n" +
            $"  [Global] _SSMBIntensityScale: {_ssmb_intScale:F4}\n" +
            "──────────────── DepthBlur ───────────────\n" +
            $"  [Global] _NearDist  : {_db_nearDist:F4}" +
            (_db_nearDist < 1f ? "  ★★★ 미초기화(0) → 캐릭터 블러!" :
             _db_nearDist < _dist ? $"  ★★★ {_db_nearDist:F1}m < 캐릭터{_dist:F1}m → 블러 적용!" : "  ✓ 보호됨") + "\n" +
            $"  [Global] _MidDist   : {_db_midDist:F4}" + (_db_midDist < 1f ? "  ★ 미초기화" : "") + "\n" +
            $"  [Global] _GlobalSpeedFactor: {_db_speedFactor:F4}\n" +
            $"  [Global] _LockOnActive     : {_db_lockOn:F4}" + (_db_lockOn > 0.5f ? "  (락온 중)" : "") + "\n" +
            $"  [Global] _PlayerProtectRadius: {_db_playerProtect:F4}\n" +
            "──────────────── 판정 ────────────────────\n" +
            $"  SSMB 캐릭터: {(ssmbOk ? "PROTECTED ✓" : "EXPOSED ✗")}\n" +
            $"  DepthBlur 캐릭터: {(dbExposed ? $"EXPOSED ✗ (nearDist={_db_nearDist:F1} < dist={_dist:F1})" : "PROTECTED ✓")}\n" +
            "══════════════════════════════════════════"
        );
    }

    void OnGUI()
    {
        if (mainCamera == null || characterRoot == null)
        {
            GUI.Box(new Rect(10, 10, 400, 25), "⚠ Diagnostic: 오브젝트 미연결!"); return;
        }

        InitStyles();

        float x = 10, y = 10, w = 460, lh = 20;
        float totalH = lh * 14 + 36;
        GUI.Box(new Rect(x - 4, y - 4, w + 8, totalH), "");
        GUI.Label(new Rect(x, y, w, lh), "● SSMB + DepthBlur Distance Diagnostic V2", _header); y += lh + 4;

        // SSMB
        GUI.Label(new Rect(x, y, w, lh), "── SSMB ──────────────────────────────", _body); y += lh;
        bool ssmbOk = _ssmb_near > 0.5f && _dist < _ssmb_near;
        GUI.Label(new Rect(x, y, w, lh),
            $"카메라-캐릭터: {_dist:F2}m  |  SSBlurDepthNear(Global): {_ssmb_near:F1}m",
            ssmbOk ? _ok : _crit); y += lh;
        GUI.Label(new Rect(x, y, w, lh),
            ssmbOk ? "  → SSMB PROTECTED ✓" : "  → SSMB EXPOSED ✗", ssmbOk ? _ok : _crit); y += lh;

        // DepthBlur
        GUI.Label(new Rect(x, y, w, lh), "── DepthBlur ─────────────────────────", _body); y += lh;

        bool dbNearInit = _db_nearDist > 0.5f;
        bool dbCharExposed = _dist >= _db_nearDist;
        GUIStyle dbNearStyle = (!dbNearInit || dbCharExposed) ? _crit : _ok;
        GUI.Label(new Rect(x, y, w, lh),
            $"_NearDist(Global): {_db_nearDist:F4}  |  Mat 기준값: {dbNearDist:F1}m",
            dbNearStyle); y += lh;

        string dbMsg;
        if (!dbNearInit)
            dbMsg = "  → ★★★ _NearDist 미초기화(0) → 2.6m 캐릭터에 DepthBlur 완전 적용!";
        else if (dbCharExposed)
            dbMsg = $"  → ★★★ 캐릭터({_dist:F1}m) ≥ NearDist({_db_nearDist:F1}m) → DepthBlur 적용!";
        else
            dbMsg = $"  → DepthBlur PROTECTED ✓ (캐릭터 {_dist:F1}m < NearDist {_db_nearDist:F1}m)";
        GUI.Label(new Rect(x, y, w, lh), dbMsg, dbNearStyle); y += lh;

        GUI.Label(new Rect(x, y, w, lh),
            $"_MidDist: {_db_midDist:F1}m  |  _GlobalSpeedFactor: {_db_speedFactor:F3}" +
            (_db_lockOn > 0.5f ? "  [락온중]" : ""), _body); y += lh;

        if (_db_playerProtect > 0.01f)
        {
            GUI.Label(new Rect(x, y, w, lh),
                $"_PlayerProtectRadius: {_db_playerProtect:F2}m  → SCM이 NearDist을 이 값으로 주입했을 수 있음",
                _warn); y += lh;
        }

        y += 6;
        GUI.Label(new Rect(x, y, w, lh), "[Space] Console 전체 스냅샷", _body);
    }

    GUIStyle _header, _ok, _crit, _warn, _body;
    bool _init;
    void InitStyles()
    {
        if (_init) return;
        _header = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.3f, 0.85f, 1f) } };
        _ok = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.3f, 1f, 0.45f) } };
        _crit = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.3f, 0.3f) } };
        _warn = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(1f, 0.85f, 0.25f) } };
        _body = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = Color.white } };
        _init = true;
    }
}