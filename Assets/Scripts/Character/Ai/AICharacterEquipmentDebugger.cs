using UnityEngine;
using Unity.Netcode;
using TDA.Items;
using TDA.Character;
using TDA.Character.AI; // AI 장비 매니저 참조를 위해 추가

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.DebugTools
{
    /// <summary>
    /// [디버깅 전용] 인스펙터 창에서 AI(몬스터)에게 무기와 방패를 즉시 장착시켜볼 수 있는 에디터 툴입니다.
    /// 스켈레톤 등의 최상위 오브젝트에 부착하여 사용하세요.
    /// </summary>
    public class AICharacterEquipmentDebugger : MonoBehaviour
    {
        private CharacterManager aiCharacter;
        private AICharacterEquipmentManager aiEquipmentManager;

        [Header("Drag & Drop Items to Test")]
        [Tooltip("오른손에 쥐어줄 무기 SO를 넣으세요.")]
        public WeaponItem rightHandTestWeapon;

        [Tooltip("왼손에 쥐어줄 무기 SO를 넣으세요.")]
        public WeaponItem leftHandTestWeapon;

        [Tooltip("왼손에 쥐어줄 방패 SO를 넣으세요.")]
        public ShieldWeaponItemSO leftHandTestShield;

        private void Awake()
        {
            aiCharacter = GetComponent<CharacterManager>();
            aiEquipmentManager = GetComponent<AICharacterEquipmentManager>();
        }

        public void EquipRightHand()
        {
            // 네트워크 변수 대신 AI의 EquipmentManager를 직접 호출하여 장착합니다.
            if (rightHandTestWeapon != null && aiEquipmentManager != null)
            {
                aiEquipmentManager.LoadRightWeapon(rightHandTestWeapon.itemID);
                Debug.Log($"<color=lime>[AI Debugger]</color> 오른손에 '{rightHandTestWeapon.itemName}' 장착 명령!");
            }
        }

        public void EquipLeftHandWeapon()
        {
            if (leftHandTestWeapon != null && aiEquipmentManager != null)
            {
                aiEquipmentManager.LoadLeftWeapon(leftHandTestWeapon.itemID);
                Debug.Log($"<color=lime>[AI Debugger]</color> 왼손에 '{leftHandTestWeapon.itemName}' 무기 장착 명령!");
            }
        }

        public void EquipLeftHandShield()
        {
            if (leftHandTestShield != null && aiEquipmentManager != null)
            {
                aiEquipmentManager.LoadLeftWeapon(leftHandTestShield.itemID);
                Debug.Log($"<color=cyan>[AI Debugger]</color> 왼손에 '{leftHandTestShield.itemName}' 방패 장착 명령!");
            }
        }

        public void UnequipAll()
        {
            if (aiEquipmentManager != null)
            {
                int unarmedID = -1;
                if (WorldItemDatabase.Instance != null && WorldItemDatabase.Instance.unarmedWeapon != null)
                {
                    unarmedID = WorldItemDatabase.Instance.unarmedWeapon.itemID;
                }

                aiEquipmentManager.LoadRightWeapon(unarmedID);
                aiEquipmentManager.LoadLeftWeapon(unarmedID);

                Debug.Log("<color=red>[AI Debugger]</color> AI 무장 해제 완료!");
            }
        }
    }

#if UNITY_EDITOR
    // =========================================================================================
    // [유니티 에디터 전용 확장]
    // =========================================================================================
    [CustomEditor(typeof(AICharacterEquipmentDebugger))]
    public class AICharacterEquipmentDebuggerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            AICharacterEquipmentDebugger debugger = (AICharacterEquipmentDebugger)target;

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("🛠️ 런타임 AI 장착 테스트 (클릭 시 즉각 반영)", EditorStyles.boldLabel);

            GUI.enabled = Application.isPlaying;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⚔️ 오른손 무기", GUILayout.Height(30))) debugger.EquipRightHand();
            if (GUILayout.Button("🗡️ 왼손 무기", GUILayout.Height(30))) debugger.EquipLeftHandWeapon();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("🛡️ 왼손 방패", GUILayout.Height(30))) debugger.EquipLeftHandShield();

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("🗑️ 모두 해제", GUILayout.Height(30))) debugger.UnequipAll();
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("테스트를 진행하려면 게임을 실행(Play Mode)해야 합니다.", MessageType.Info);
            }
        }
    }
#endif
}