using UnityEngine;
using Unity.Netcode;
using TDA.Items;
using TDA.Character.Player;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.DebugTools
{
    /// <summary>
    /// [디버깅 전용] 인스펙터 창에서 무기, 방패, 가방 등을 즉시 장착해볼 수 있는 커스텀 에디터 컴포넌트입니다.
    /// Player 최상위 오브젝트에 부착하여 사용하세요.
    /// </summary>
    public class PlayerEquipmentDebugger : MonoBehaviour
    {
        private PlayerManager player;

        [Header("Drag & Drop Items to Test")]
        [Tooltip("테스트할 오른손 무기 SO를 넣으세요.")]
        public WeaponItem rightHandTestWeapon;

        [Tooltip("테스트할 왼손 무기(쌍검 등) SO를 넣으세요.")]
        public WeaponItem leftHandTestWeapon;

        [Tooltip("테스트할 왼손 방패 SO를 넣으세요.")]
        public ShieldWeaponItemSO leftHandTestShield;

        [Tooltip("테스트할 가방(Backpack) SO를 넣으세요.")]
        public EquipmentItem testBackpack;

        private void Awake()
        {
            player = GetComponent<PlayerManager>();
        }

        // =========================================================================================
        // 디버그 장착 실행 로직 (NetworkVariable 값을 직접 변경하여 시스템을 태웁니다)
        // =========================================================================================
        public void EquipRightHand()
        {
            if (rightHandTestWeapon != null && player != null)
            {
                player.playerNetworkManager.currentRightHandWeaponID.Value = rightHandTestWeapon.itemID;
                Debug.Log($"<color=lime>[Debugger]</color> 오른손에 '{rightHandTestWeapon.itemName}' 장착 명령 전송!");
            }
        }

        public void EquipLeftHandWeapon()
        {
            if (leftHandTestWeapon != null && player != null)
            {
                player.playerNetworkManager.currentLeftHandWeaponID.Value = leftHandTestWeapon.itemID;
                Debug.Log($"<color=lime>[Debugger]</color> 왼손에 '{leftHandTestWeapon.itemName}' 무기 장착 명령 전송!");
            }
        }

        public void EquipLeftHandShield()
        {
            if (leftHandTestShield != null && player != null)
            {
                player.playerNetworkManager.currentLeftHandWeaponID.Value = leftHandTestShield.itemID;
                Debug.Log($"<color=cyan>[Debugger]</color> 왼손에 '{leftHandTestShield.itemName}' 방패 장착 명령 전송!");
            }
        }

        public void EquipBackpack()
        {
            if (testBackpack != null && player != null)
            {
                player.playerEquipmentManager.currentBackpackID.Value = testBackpack.itemID;
                Debug.Log($"<color=yellow>[Debugger]</color> 등에 '{testBackpack.itemName}' 가방 장착 명령 전송!");
            }
        }

        public void UnequipAll()
        {
            if (player != null)
            {
                int unarmedID = -1;
                // 언암드(맨손) ID가 데이터베이스에 있다면 해당 ID를 가져옵니다.
                if (WorldItemDatabase.Instance != null && WorldItemDatabase.Instance.unarmedWeapon != null)
                {
                    unarmedID = WorldItemDatabase.Instance.unarmedWeapon.itemID;
                }

                player.playerNetworkManager.currentRightHandWeaponID.Value = unarmedID;
                player.playerNetworkManager.currentLeftHandWeaponID.Value = unarmedID;
                player.playerEquipmentManager.currentBackpackID.Value = -1; // 가방 해제

                Debug.Log("<color=red>[Debugger]</color> 모든 장비 해제 완료!");
            }
        }
    }

    // =========================================================================================
    // [유니티 에디터 전용 확장] 인스펙터에 예쁜 버튼들을 그려주는 클래스입니다.
    // =========================================================================================
#if UNITY_EDITOR
    [CustomEditor(typeof(PlayerEquipmentDebugger))]
    public class PlayerEquipmentDebuggerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // 1. 기본 필드(SO 드래그 슬롯)들을 먼저 그립니다.
            DrawDefaultInspector();

            PlayerEquipmentDebugger debugger = (PlayerEquipmentDebugger)target;

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("🛠️ 런타임 장착 테스트 (클릭 시 즉각 반영)", EditorStyles.boldLabel);

            // 게임이 실행 중(Play)일 때만 버튼이 활성화되도록 제어
            GUI.enabled = Application.isPlaying;

            // 2. 무기 장착 버튼 가로 배치
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⚔️ 오른손 무기 장착", GUILayout.Height(30))) debugger.EquipRightHand();
            if (GUILayout.Button("🗡️ 왼손 무기 장착", GUILayout.Height(30))) debugger.EquipLeftHandWeapon();
            EditorGUILayout.EndHorizontal();

            // 3. 방패 & 가방 버튼 가로 배치 (색상 추가)
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f); // 파란색 톤
            if (GUILayout.Button("🛡️ 왼손 방패 장착", GUILayout.Height(30))) debugger.EquipLeftHandShield();

            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f); // 초록색 톤
            if (GUILayout.Button("🎒 가방(Backpack) 장착", GUILayout.Height(30))) debugger.EquipBackpack();
            EditorGUILayout.EndHorizontal();

            // 4. 모두 해제 버튼
            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f); // 빨간색 톤
            if (GUILayout.Button("🗑️ 모두 해제 (Unequip All)", GUILayout.Height(30))) debugger.UnequipAll();

            // 설정 초기화
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("버튼을 누르려면 게임을 실행(Play Mode)해야 합니다.", MessageType.Info);
            }
        }
    }
#endif
}