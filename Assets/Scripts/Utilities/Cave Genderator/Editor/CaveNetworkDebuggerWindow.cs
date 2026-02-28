#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using System.Collections.Generic;
using System.Reflection;
using CaveSystem.Multiplayer;
using System.Linq;

// 스팀 웍스 연동 (닉네임 표시용)
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace CaveSystem.Editor
{
    /// <summary>
    /// 동굴 시스템의 멀티플레이어 동기화 상태, 핑(Ping), 클라이언트별 진행 상태를 
    /// 실시간으로 모니터링하고 제어할 수 있는 커스텀 에디터 윈도우입니다.
    /// </summary>
    public class CaveNetworkDebuggerWindow : EditorWindow
    {
        private int manualSeed = 123456;
        private int selectedSceneIndex = 0;
        private string[] availableScenes;

        private Vector2 scrollPosition;
        private Vector3 targetTeleportPos = Vector3.zero;

        [MenuItem("Cave System/Network Sync Debugger")]
        public static void ShowWindow()
        {
            CaveNetworkDebuggerWindow window = GetWindow<CaveNetworkDebuggerWindow>("Network Sync Debugger");
            window.minSize = new Vector2(450, 650);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
            UpdateSceneList();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void UpdateSceneList()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToList();
            availableScenes = new string[scenes.Count];
            for (int i = 0; i < scenes.Count; i++)
            {
                availableScenes[i] = System.IO.Path.GetFileNameWithoutExtension(scenes[i].path);
            }
        }

        private void OnGUI()
        {
            GUILayout.Space(10);
            GUILayout.Label("🌐 Terrain Network Sync Debugger", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
            GUILayout.Space(10);

            if (!Application.isPlaying || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                EditorGUILayout.HelpBox("게임 플레이 모드에서 호스트 또는 클라이언트로 접속해야 활성화됩니다.", MessageType.Info);
                return;
            }

            DrawServerStatusSection();
            GUILayout.Space(15);
            DrawClientListSection();
            GUILayout.Space(15);
            DrawPlayerTeleportSection();
            GUILayout.Space(15);
            DrawAdminControlsSection();
        }

        private void DrawServerStatusSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("📊 서버 상태 (Server Info)", EditorStyles.boldLabel);

            string role = NetworkManager.Singleton.IsHost ? "Host (Server + Client)" :
                          NetworkManager.Singleton.IsServer ? "Dedicated Server" : "Client";

            EditorGUILayout.LabelField("현재 역할 (Role)", role);
            EditorGUILayout.LabelField("로컬 Client ID", NetworkManager.Singleton.LocalClientId.ToString());

            TerrainSyncNetworkManager syncManager = FindFirstObjectByType<TerrainSyncNetworkManager>();

            if (syncManager != null)
            {
                int currentSeed = syncManager.SyncedWorldSeed.Value;
                int readyCount = syncManager.ReadyClientCount.Value;
                int totalCount = NetworkManager.Singleton.ConnectedClientsIds.Count;

                EditorGUILayout.LabelField("동기화된 월드 시드", currentSeed == 0 ? "발급 대기 중..." : currentSeed.ToString());

                GUI.color = readyCount == totalCount && totalCount > 0 ? Color.green : Color.yellow;
                EditorGUILayout.LabelField("파티 지형 준비 상태", $"{readyCount} / {totalCount} 명 완료");
                GUI.color = Color.white;
            }
            else
            {
                EditorGUILayout.HelpBox("현재 씬에서 TerrainSyncNetworkManager를 찾을 수 없습니다.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawClientListSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("👥 접속 클라이언트 상태 (Client Status)", EditorStyles.boldLabel);

            if (!NetworkManager.Singleton.IsServer)
            {
                EditorGUILayout.HelpBox("클라이언트 상태 목록은 서버(호스트)에서만 볼 수 있습니다.", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            // [핵심 수정] 로비 UI가 파괴되는 게임 씬에서도 상태를 알기 위해 서버 동기화 데이터를 직접 조회합니다.
            HashSet<ulong> serverReadyMap = GetServerReadyMap();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Client", EditorStyles.boldLabel, GUILayout.Width(150));
            GUILayout.Label("지형 생성 상태", EditorStyles.boldLabel, GUILayout.Width(130));
            GUILayout.Label("Ping", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, Color.gray);

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                EditorGUILayout.BeginHorizontal();

                string playerName = $"Client {clientId}";

                if (clientId == NetworkManager.ServerClientId)
                {
                    playerName = "Host (나)";
#if !DISABLESTEAMWORKS
                    if (Steamworks.SteamClient.IsValid) playerName = $"{Steamworks.SteamClient.Name} (Host)";
#endif
                }
                else
                {
                    var playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
                    if (playerObj != null) playerName = playerObj.gameObject.name;
                }

                GUILayout.Label(playerName, GUILayout.Width(150));

                // 서버 메모리에 저장된 Ready 상태를 판별 (씬과 무관하게 유지됨)
                bool isReady = serverReadyMap != null && serverReadyMap.Contains(clientId);

                GUIStyle statusStyle = new GUIStyle(EditorStyles.label);
                statusStyle.normal.textColor = isReady ? Color.green : new Color(1f, 0.6f, 0f);
                statusStyle.fontStyle = isReady ? FontStyle.Bold : FontStyle.Normal;

                string statusText = isReady ? "준비 완료 (100%)" : "굽는 중 (Baking...)";
                GUILayout.Label(statusText, statusStyle, GUILayout.Width(130));

                ulong ping = 0;
                if (NetworkManager.Singleton.NetworkConfig.NetworkTransport != null)
                {
                    ping = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId);
                }

                GUI.color = ping < 50 ? Color.green : (ping < 150 ? Color.yellow : Color.red);
                GUILayout.Label($"{ping} ms", GUILayout.Width(80));
                GUI.color = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPlayerTeleportSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("🏃 플레이어 좌표 강제 이동 (Teleport)", EditorStyles.boldLabel);

            GUILayout.Space(5);
            targetTeleportPos = EditorGUILayout.Vector3Field("이동할 목표 좌표", targetTeleportPos);

            if (GUILayout.Button("내 플레이어 순간이동", GUILayout.Height(25)))
            {
                Transform playerTransform = null;

                var localClient = NetworkManager.Singleton.LocalClient;
                if (localClient != null && localClient.PlayerObject != null)
                {
                    playerTransform = localClient.PlayerObject.transform;
                }
                else
                {
                    GameObject fallbackPlayer = GameObject.FindWithTag("Player");
                    if (fallbackPlayer != null) playerTransform = fallbackPlayer.transform;
                }

                if (playerTransform != null)
                {
                    CharacterController cc = playerTransform.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;

                    playerTransform.position = targetTeleportPos;

                    if (cc != null) cc.enabled = true;
                    Debug.Log($"<color=cyan>[Debugger]</color> 플레이어를 {targetTeleportPos}로 이동시켰습니다.");
                }
                else
                {
                    Debug.LogWarning("플레이어 오브젝트를 찾을 수 없습니다. (Tag가 'Player'인지 확인하세요)");
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAdminControlsSection()
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("⚙️ 서버 어드민 제어 (Server Admin Controls)", EditorStyles.boldLabel);

            if (!NetworkManager.Singleton.IsServer)
            {
                EditorGUILayout.HelpBox("호스트(서버) 권한이 필요합니다.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            manualSeed = EditorGUILayout.IntField("수동 변경 시드값", manualSeed);

            if (GUILayout.Button("전체 플레이어 재전송 (Regen)", GUILayout.Width(180)))
            {
                TerrainSyncNetworkManager syncManager = FindFirstObjectByType<TerrainSyncNetworkManager>();
                if (syncManager != null)
                {
                    if (syncManager.SyncedWorldSeed.Value == manualSeed) manualSeed++;
                    syncManager.SyncedWorldSeed.Value = manualSeed;
                    Debug.Log($"<color=cyan>[Admin]</color> 월드 시드가 {manualSeed}(으)로 강제 변경되었습니다.");

                    // 상태 초기화
                    HashSet<ulong> readyMap = GetServerReadyMap();
                    if (readyMap != null) readyMap.Clear();
                    syncManager.ReadyClientCount.Value = 0;
                    ResetLobbyReadyMap();

                    // [핵심 수정] 로비/게임 씬 여부에 따른 강제 재생성 분기 처리
                    if (CaveManager.Instance != null)
                    {
                        if (CaveManager.Instance.isHeadlessPregenMode)
                        {
                            // 로비 씬: 비가시적 베이킹 재시작
                            CaveManager.Instance.StartLobbyPregeneration(manualSeed);
                        }
                        else
                        {
                            // 게임 씬: 동굴이 안 보일 때 이 로직이 작동하여 강제로 지형을 만듭니다.
                            Debug.Log("<color=yellow>[Admin]</color> 게임 씬에서 강제 지형 생성을 시작합니다.");
                            if (CaveManager.Instance.graphBuilder != null)
                            {
                                CaveManager.Instance.graphBuilder.GenerateGraph(manualSeed);
                                CaveManager.Instance.computeDispatcher.SetupGraphBuffers(CaveManager.Instance.graphBuilder.nodesData, CaveManager.Instance.graphBuilder.edgesData);
                            }
                            if (CaveManager.Instance.chunkManager != null)
                            {
                                CaveManager.Instance.chunkManager.ForceGenerateChunksAroundPlayer();
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();

            if (availableScenes != null && availableScenes.Length > 0)
            {
                selectedSceneIndex = EditorGUILayout.Popup("목표 씬(Scene)", selectedSceneIndex, availableScenes);
            }
            else
            {
                EditorGUILayout.LabelField("목표 씬(Scene)", "Build Settings에 씬이 없습니다!");
            }

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("강제 씬 전환 (Force Start)", GUILayout.Width(180)))
            {
                if (availableScenes != null && availableScenes.Length > 0)
                {
                    string targetScene = availableScenes[selectedSceneIndex];
                    if (EditorUtility.DisplayDialog("경고", $"지형 생성이 끝나지 않은 유저가 있을 수 있습니다. 강제로 '{targetScene}' 씬으로 전환하시겠습니까?", "강제 이동", "취소"))
                    {
                        Debug.Log($"<color=red>[Admin]</color> {targetScene} 씬으로 강제 동기화 이동을 시작합니다.");
                        NetworkManager.Singleton.SceneManager.LoadScene(targetScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
                    }
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // [신규] 게임 씬에서도 상태를 유지하기 위해 TerrainSyncNetworkManager의 내부 해시셋을 조회합니다.
        private HashSet<ulong> GetServerReadyMap()
        {
            TerrainSyncNetworkManager syncManager = FindFirstObjectByType<TerrainSyncNetworkManager>();
            if (syncManager == null) return null;

            FieldInfo field = typeof(TerrainSyncNetworkManager).GetField("readyClients", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                return field.GetValue(syncManager) as HashSet<ulong>;
            }
            return null;
        }

        private void ResetLobbyReadyMap()
        {
            LobbyUIManager lobbyUI = FindFirstObjectByType<LobbyUIManager>();
            if (lobbyUI == null) return;

            FieldInfo field = typeof(LobbyUIManager).GetField("clientBakingReadyMap", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                Dictionary<ulong, bool> map = field.GetValue(lobbyUI) as Dictionary<ulong, bool>;
                if (map != null)
                {
                    List<ulong> keys = new List<ulong>(map.Keys);
                    foreach (ulong key in keys) map[key] = false;
                }
            }
        }
    }
}
#endif