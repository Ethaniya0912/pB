using UnityEngine;
using Unity.Netcode;
using TDA.Items;
using TDA.Character;
using TDA.Character.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.DebugTools
{
    /// <summary>
    /// [디버깅 전용] 인스펙터 창에서 AI(몬스터)에게 무기와 방패를 즉시 장착시켜볼 수 있는 에디터 툴입니다.
    /// 스켈레톤 등의 최상위 오브젝트에 부착하여 사용하세요.
    ///
    /// [스폰 시 자동 장착]
    /// Auto Equip On Spawn 플래그를 켜면, AI가 스폰(Start)될 때
    /// 지정된 무기/방패를 자동으로 장착합니다.
    /// AICharacterEquipmentManager.Start()의 기본 장착보다 늦게 실행되어
    /// 기본 무기를 원하는 무기로 덮어씁니다.
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

        // ─────────────────────────────────────────────────────────────────────
        // 스폰 시 자동 장착 플래그
        // ─────────────────────────────────────────────────────────────────────
        [Header("━━━ 스폰 시 자동 장착 (Auto Equip On Spawn) ━━━")]
        [Tooltip("체크하면 Play 시작(스폰) 시 오른손 무기를 자동 장착합니다.\n" +
                 "위의 Right Hand Test Weapon에 SO가 할당되어 있어야 합니다.")]
        public bool autoEquipRightHand = false;

        [Tooltip("체크하면 Play 시작(스폰) 시 왼손 무기를 자동 장착합니다.\n" +
                 "위의 Left Hand Test Weapon에 SO가 할당되어 있어야 합니다.")]
        public bool autoEquipLeftHandWeapon = false;

        [Tooltip("체크하면 Play 시작(스폰) 시 왼손 방패를 자동 장착합니다.\n" +
                 "위의 Left Hand Test Shield에 SO가 할당되어 있어야 합니다.\n" +
                 "왼손 무기와 동시에 체크하면 방패가 우선됩니다.")]
        public bool autoEquipLeftHandShield = false;

        private bool hasAutoEquipped = false;

        private void Awake()
        {
            aiCharacter = GetComponent<CharacterManager>();
            aiEquipmentManager = GetComponent<AICharacterEquipmentManager>();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Start — 스폰 시 자동 장착
        // AICharacterEquipmentManager.Start()가 먼저 실행되어 기본 무기를 장착한 후,
        // 이 Start()에서 원하는 무기로 덮어씁니다.
        // (Script Execution Order에 의존하지 않기 위해 1프레임 대기)
        // ─────────────────────────────────────────────────────────────────────
        private void Start()
        {
            if (autoEquipRightHand || autoEquipLeftHandWeapon || autoEquipLeftHandShield)
            {
                StartCoroutine(AutoEquipNextFrame());
            }
        }

        private System.Collections.IEnumerator AutoEquipNextFrame()
        {
            yield return null; // 1프레임 대기

            if (hasAutoEquipped) yield break;
            hasAutoEquipped = true;

            if (aiEquipmentManager == null)
            {
                Debug.LogError("<color=red>[AI Equip Auto]</color> AICharacterEquipmentManager가 없어 자동 장착 실패!");
                yield break;
            }

            Debug.Log($"<color=lime>[AI Equip Auto]</color> {name}: 스폰 시 자동 장착 시작...");

            // 오른손 무기
            if (autoEquipRightHand && rightHandTestWeapon != null)
            {
                Debug.Log($"<color=lime>[AI Equip Auto]</color> 오른손: '{rightHandTestWeapon.itemName}' (ID={rightHandTestWeapon.itemID})");
                DiagnoseWeaponItem(rightHandTestWeapon, "오른손 자동");
                aiEquipmentManager.LoadRightWeapon(rightHandTestWeapon.itemID);
                VerifyEquipResult("오른손", rightHandTestWeapon);
            }
            else if (autoEquipRightHand && rightHandTestWeapon == null)
            {
                Debug.LogWarning("<color=yellow>[AI Equip Auto]</color> autoEquipRightHand=true이지만 rightHandTestWeapon이 비어있습니다.");
            }

            // 왼손 방패 (무기보다 우선)
            if (autoEquipLeftHandShield && leftHandTestShield != null)
            {
                Debug.Log($"<color=cyan>[AI Equip Auto]</color> 왼손 방패: '{leftHandTestShield.itemName}' (ID={leftHandTestShield.itemID})");
                DiagnoseWeaponItem(leftHandTestShield, "왼손 방패 자동");
                aiEquipmentManager.LoadLeftWeapon(leftHandTestShield.itemID);
                VerifyEquipResult("왼손", leftHandTestShield);
            }
            else if (autoEquipLeftHandShield && leftHandTestShield == null)
            {
                Debug.LogWarning("<color=yellow>[AI Equip Auto]</color> autoEquipLeftHandShield=true이지만 leftHandTestShield가 비어있습니다.");
            }
            // 왼손 무기 (방패가 없을 때)
            else if (autoEquipLeftHandWeapon && leftHandTestWeapon != null)
            {
                Debug.Log($"<color=lime>[AI Equip Auto]</color> 왼손 무기: '{leftHandTestWeapon.itemName}' (ID={leftHandTestWeapon.itemID})");
                DiagnoseWeaponItem(leftHandTestWeapon, "왼손 무기 자동");
                aiEquipmentManager.LoadLeftWeapon(leftHandTestWeapon.itemID);
                VerifyEquipResult("왼손", leftHandTestWeapon);
            }
            else if (autoEquipLeftHandWeapon && leftHandTestWeapon == null)
            {
                Debug.LogWarning("<color=yellow>[AI Equip Auto]</color> autoEquipLeftHandWeapon=true이지만 leftHandTestWeapon이 비어있습니다.");
            }

            Debug.Log($"<color=lime>[AI Equip Auto]</color> {name}: 스폰 시 자동 장착 완료!");
        }

        // ─────────────────────────────────────────────────────────────────────
        // 수동 버튼: 오른손 무기 장착
        // ─────────────────────────────────────────────────────────────────────
        public void EquipRightHand()
        {
            if (rightHandTestWeapon != null && aiEquipmentManager != null)
            {
                Debug.Log($"<color=lime>[AI Equip]</color> 오른손 장착 시도: '{rightHandTestWeapon.itemName}' (ID={rightHandTestWeapon.itemID})");
                DiagnoseWeaponItem(rightHandTestWeapon, "오른손");
                aiEquipmentManager.LoadRightWeapon(rightHandTestWeapon.itemID);
                VerifyEquipResult("오른손", rightHandTestWeapon);
            }
            else
            {
                if (rightHandTestWeapon == null)
                    Debug.LogError("<color=red>[AI Equip]</color> rightHandTestWeapon이 비어있습니다. SO를 드래그하세요.");
                if (aiEquipmentManager == null)
                    Debug.LogError("<color=red>[AI Equip]</color> AICharacterEquipmentManager가 없습니다.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 수동 버튼: 왼손 무기 장착
        // ─────────────────────────────────────────────────────────────────────
        public void EquipLeftHandWeapon()
        {
            if (leftHandTestWeapon != null && aiEquipmentManager != null)
            {
                Debug.Log($"<color=lime>[AI Equip]</color> 왼손 무기 장착 시도: '{leftHandTestWeapon.itemName}' (ID={leftHandTestWeapon.itemID})");
                DiagnoseWeaponItem(leftHandTestWeapon, "왼손");
                aiEquipmentManager.LoadLeftWeapon(leftHandTestWeapon.itemID);
                VerifyEquipResult("왼손", leftHandTestWeapon);
            }
            else
            {
                if (leftHandTestWeapon == null)
                    Debug.LogError("<color=red>[AI Equip]</color> leftHandTestWeapon이 비어있습니다.");
                if (aiEquipmentManager == null)
                    Debug.LogError("<color=red>[AI Equip]</color> AICharacterEquipmentManager가 없습니다.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 수동 버튼: 왼손 방패 장착
        // ─────────────────────────────────────────────────────────────────────
        public void EquipLeftHandShield()
        {
            if (leftHandTestShield != null && aiEquipmentManager != null)
            {
                Debug.Log($"<color=cyan>[AI Equip]</color> 왼손 방패 장착 시도: '{leftHandTestShield.itemName}' (ID={leftHandTestShield.itemID})");
                DiagnoseWeaponItem(leftHandTestShield, "왼손 방패");
                aiEquipmentManager.LoadLeftWeapon(leftHandTestShield.itemID);
                VerifyEquipResult("왼손", leftHandTestShield);
            }
            else
            {
                if (leftHandTestShield == null)
                    Debug.LogError("<color=red>[AI Equip]</color> leftHandTestShield가 비어있습니다.");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // 수동 버튼: 전부 해제
        // ─────────────────────────────────────────────────────────────────────
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

                Debug.Log("<color=red>[AI Equip]</color> AI 무장 해제 완료!");
            }
        }

        // ==================================================================
        // 장착 전 진단: WeaponItem SO → Prefab → WeaponManager 체인 검증
        // ==================================================================
        private void DiagnoseWeaponItem(WeaponItem weapon, string slotName)
        {
            string prefix = $"<color=yellow>[AI Equip 진단:{slotName}]</color>";

            // 1. SO 기본 정보
            Debug.Log($"{prefix} SO: name='{weapon.itemName}' ID={weapon.itemID} type={weapon.GetType().Name}");

            // 2. weaponModel (프리팹 참조)
            if (weapon.weaponModel == null)
            {
                Debug.LogError($"{prefix} ❌ weapon.weaponModel = NULL! SO에 프리팹이 할당되지 않았습니다.\n" +
                    $"  → '{weapon.itemName}' SO의 Inspector에서 'Weapon Model' 필드에 프리팹을 드래그하세요.");
                return;
            }
            Debug.Log($"{prefix} ✅ weaponModel = '{weapon.weaponModel.name}'");

            // 3. WeaponManager 컴포넌트 확인
            var wm = weapon.weaponModel.GetComponent<WeaponManager>();
            if (wm == null)
            {
                Debug.LogError($"{prefix} ❌ 프리팹 '{weapon.weaponModel.name}'에 WeaponManager 컴포넌트가 없습니다!\n" +
                    $"  → 프리팹을 열고 루트 오브젝트에 Add Component → Weapon Manager\n" +
                    $"  → Damage Collider와 Audio Source를 할당하세요.\n" +
                    $"  (참고: Weapon_Straight_Sword 프리팹에 WeaponManager가 정상 부착되어 있으니 비교하세요)");
            }
            else
            {
                Debug.Log($"{prefix} ✅ WeaponManager 있음");

                // 3a. Damage Collider
                if (wm.meleeWeaponDamageCollider == null)
                    Debug.LogWarning($"{prefix} ⚠️ WeaponManager.meleeWeaponDamageCollider = NULL (Damage Collider 미할당)");
                else
                    Debug.Log($"{prefix} ✅ DamageCollider = '{wm.meleeWeaponDamageCollider.name}'");
            }

            // 4. WorldItemDatabase 등록 확인
            if (WorldItemDatabase.Instance == null)
            {
                Debug.LogError($"{prefix} ❌ WorldItemDatabase.Instance = NULL! 씬에 WorldItemDatabase가 없습니다.");
                return;
            }

            var dbWeapon = WorldItemDatabase.Instance.GetWeaponByID(weapon.itemID);
            if (dbWeapon == null)
            {
                Debug.LogError($"{prefix} ❌ WorldItemDatabase에서 ID={weapon.itemID}를 찾을 수 없습니다!\n" +
                    $"  → WorldItemDatabase의 Items 리스트에 '{weapon.itemName}'이 등록되어 있는지 확인하세요.");
            }
            else if (dbWeapon != weapon)
            {
                Debug.LogWarning($"{prefix} ⚠️ WorldItemDatabase에서 ID={weapon.itemID}로 찾은 아이템이 다른 SO입니다!\n" +
                    $"  → 드래그한 SO: '{weapon.itemName}'\n" +
                    $"  → DB에서 찾은 SO: '{dbWeapon.itemName}'\n" +
                    $"  → ID 중복이 있을 수 있습니다.");
            }
            else
            {
                Debug.Log($"{prefix} ✅ WorldItemDatabase 등록 확인 (ID={weapon.itemID})");

                // DB에서 가져온 weaponModel도 확인
                if (dbWeapon.weaponModel == null)
                    Debug.LogError($"{prefix} ❌ DB에서 가져온 SO의 weaponModel = NULL!");
                else if (dbWeapon.weaponModel != weapon.weaponModel)
                    Debug.LogWarning($"{prefix} ⚠️ DB SO와 드래그한 SO의 weaponModel이 다릅니다.\n" +
                        $"  → 드래그: '{weapon.weaponModel.name}'\n" +
                        $"  → DB: '{dbWeapon.weaponModel.name}'");
            }
        }

        // ==================================================================
        // 장착 후 검증: 실제로 오브젝트가 생성되었는지 확인
        // ==================================================================
        private void VerifyEquipResult(string slotName, WeaponItem weapon)
        {
            string prefix = $"<color=yellow>[AI Equip 검증:{slotName}]</color>";

            // 약간의 딜레이 후 확인 (같은 프레임에서는 Instantiate 결과가 반영 안 될 수 있음)
            // 동기 호출이므로 즉시 확인 가능
            Transform socketParent = null;
            string socketName = "";

            if (slotName == "오른손")
            {
                socketParent = FindDeep(transform, "WeaponSocket_Right");
                socketName = "WeaponSocket_Right";
            }
            else
            {
                socketParent = FindDeep(transform, "WeaponSocket_Left");
                socketName = "WeaponSocket_Left";
            }

            if (socketParent == null)
            {
                Debug.LogError($"{prefix} ❌ '{socketName}'을 찾을 수 없습니다!\n" +
                    $"  → 스켈레톤 본 구조에 '{socketName}' 오브젝트가 있는지 확인하세요.");
                return;
            }

            Debug.Log($"{prefix} 소켓 '{socketName}' 찾음. 자식 수: {socketParent.childCount}");

            bool found = false;
            for (int i = 0; i < socketParent.childCount; i++)
            {
                var child = socketParent.GetChild(i);
                Debug.Log($"{prefix}   자식[{i}]: '{child.name}' active={child.gameObject.activeSelf}");

                if (child.name.Contains(weapon.weaponModel != null ? weapon.weaponModel.name : "???"))
                {
                    found = true;
                    var childWM = child.GetComponent<WeaponManager>();
                    if (childWM != null)
                        Debug.Log($"{prefix} ✅ 무기 프리팹 생성 확인! WeaponManager 있음.");
                    else
                        Debug.LogWarning($"{prefix} ⚠️ 무기 프리팹 생성됨, 하지만 WeaponManager 없음!");
                }
            }

            if (!found)
            {
                Debug.LogError($"{prefix} ❌ '{socketName}'에 무기 프리팹이 생성되지 않았습니다!\n" +
                    $"  가능한 원인:\n" +
                    $"  1. weapon.weaponModel이 NULL → SO에 프리팹 미할당\n" +
                    $"  2. WorldItemDatabase에서 ID={weapon.itemID}를 못 찾음\n" +
                    $"  3. rightHandSlot/leftHandSlot이 NULL → WeaponModelInstantiationSlot 미연결\n" +
                    $"  4. Instantiate 후 LoadWeapon()에서 예외 발생\n" +
                    $"  → 위의 진단 로그를 확인하세요.");
            }
        }

        // ==================================================================
        // 유틸: 깊은 탐색으로 자식 Transform 찾기
        // ==================================================================
        private Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var result = FindDeep(parent.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
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