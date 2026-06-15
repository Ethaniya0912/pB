using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] F8 = PROF 순환 토글. 현재 프로파일을 화면 우상단에 표시.
    /// NetDiagnosticsBootstrap 이 부트스트랩 GO 에 부착(게임 로직 무침습).
    ///
    /// 이 클래스는 반드시 파일명=클래스명 단독 파일에 둔다. 과거 NetSimProfiles.cs 안에
    /// 정의돼 있던 동안에는 MonoScript(m_Script) 바인딩이 없어, 도메인 리로드(플레이 중
    /// refresh --compile 등) 시 직렬화 복원에 실패해 missing-script 로 파괴됐다
    /// (2026-06-12 소급 재검증에서 발견된 회귀의 근본 원인).
    /// </summary>
    public class NetSimController : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                var p = NetSimProfiles.Cycle();
                Debug.Log($"[NetSim] profile → {p.Name} (rtt {p.RttMs}ms, jitter {p.JitterMs}ms, loss 미주입)");
            }
        }

        void OnGUI()
        {
            var a = NetSimProfiles.Active;
            string label = a.Name == "OFF"
                ? "NetSim: OFF (F8)"
                : $"NetSim: {a.Name}  rtt {a.RttMs}ms  jitter ±{a.JitterMs / 2}ms (F8)";
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontSize = 13 };
            style.normal.textColor = a.Name == "OFF" ? Color.gray : Color.yellow;
            GUI.Label(new Rect(Screen.width - 360, 6, 354, 22), label, style);
        }
    }
}
