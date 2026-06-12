using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] 자가 부트스트랩 — 씬·프리팹 수정 없이 계측 컴포넌트를 가동한다.
    /// 실행계획 v1.1 §0.A.2 "게임 로직 무침습" 원칙의 구현:
    /// 어떤 씬에서 시작하든 런타임에 숨은 GameObject 하나를 만들어
    /// NetEventLogger / StateChecksumV0 / BoundaryEchoHarness / SoakHarness 를 올린다.
    ///
    /// 비활성화 방법: NETDIAG_DISABLED 스크립팅 정의 추가 (릴리즈 출하 시).
    /// 핫키: F9 = M2 경계값 스윕(클라 전용) · F10 = soak 시작/종료.
    /// </summary>
    public static class NetDiagnosticsBootstrap
    {
#if !NETDIAG_DISABLED
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("[NetDiagnostics]");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            go.AddComponent<NetEventLogger>();
            go.AddComponent<StateChecksumV0>();
            go.AddComponent<BoundaryEchoHarness>();
            go.AddComponent<SoakHarness>();

            NetDiagnostics.Event("SYS", $"bootstrap complete (unity {Application.unityVersion}, build {Application.version})");
        }
#endif
    }
}
