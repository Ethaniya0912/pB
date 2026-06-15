using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] 자가 부트스트랩 — 씬·프리팹 수정 없이 계측 컴포넌트를 가동한다.
    /// 실행계획 v1.1 §0.A.2 "게임 로직 무침습" 원칙의 구현:
    /// 어떤 씬에서 시작하든 런타임에 숨은 GameObject 하나를 만들어
    /// NetEventLogger / StateChecksumV0 / BoundaryEchoHarness / SoakHarness /
    /// RnsmHud(+RuntimeNetStatsMonitor 자동) / NetSimController 를 올린다.
    ///
    /// 비활성화 방법: NETDIAG_DISABLED 스크립팅 정의 추가 (릴리즈 출하 시).
    /// 핫키: F6 = RNSM HUD 토글 · F8 = PROF 순환 · F9 = M2 경계값 스윕(클라 전용) · F10 = soak 시작/종료.
    /// </summary>
    public static class NetDiagnosticsBootstrap
    {
#if !NETDIAG_DISABLED
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
#if UNITY_EDITOR
            // DontSave hideFlags 오브젝트는 플레이 종료 시 자동 파괴되지 않고 에디터에
            // 누적된다 — 이전 세션 잔존(좀비) GO 를 먼저 회수해야 Find/진단이 오염되지 않는다.
            foreach (var stale in Resources.FindObjectsOfTypeAll<GameObject>())
                if (stale.name == "[NetDiagnostics]" && !UnityEditor.EditorUtility.IsPersistent(stale))
                    Object.DestroyImmediate(stale);
#endif

            var go = new GameObject("[NetDiagnostics]");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            go.AddComponent<NetEventLogger>();
            go.AddComponent<StateChecksumV0>();
            go.AddComponent<BoundaryEchoHarness>();
            go.AddComponent<SoakHarness>();
            go.AddComponent<RnsmHud>();          // [Step 0 잔여] RNSM HUD (RTT/대역폭 가시화, M1·M6보조)
            go.AddComponent<NetSimController>();  // [Step 0 잔여] PROF-G/A/B 토글(F8) — 지연/지터 주입

#if UNITY_EDITOR
            // 플레이 종료 시 자체 정리. 플레이 중 도메인 리로드가 끼면 이 구독은 소실되지만,
            // 그 경우에도 위 잔존 스윕이 다음 플레이에서 회수한다.
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode && go != null)
                    Object.DestroyImmediate(go);
            };
#endif

            NetDiagnostics.Event("SYS", $"bootstrap complete (unity {Application.unityVersion}, build {Application.version})");
        }
#endif
    }
}
