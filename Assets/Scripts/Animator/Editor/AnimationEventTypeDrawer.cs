#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

// 프로젝트 설정에 맞게 네임스페이스를 수정하세요.
// Enum이 전역(global)에 선언되어 있다면 아래 using 문은 필요 없을 수 있습니다.
// using TDA.Core.Events; 

namespace TDA.EditorTools
{
    /// <summary>
    /// 인스펙터에서 AnimationEventType Enum을 렌더링할 때,
    /// 접두사(Prefix)를 분석하여 폴더 형태의 드롭다운 메뉴로 예쁘게 분류해주는 커스텀 에디터 스크립트입니다.
    /// </summary>
    [CustomPropertyDrawer(typeof(global::AnimationEventType))]
    public class AnimationEventTypeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            string currentName = property.enumNames[property.enumValueIndex];

            // 기본 Enum 드롭다운 대신, 버튼을 눌렀을 때 GenericMenu(폴더 구조)가 뜨게 만듭니다.
            if (GUI.Button(position, new GUIContent(label.text + ": " + currentName), EditorStyles.popup))
            {
                GenericMenu menu = new GenericMenu();
                string[] names = property.enumNames;

                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    string path = GetCategoryPath(name);

                    int index = i;
                    menu.AddItem(new GUIContent(path), property.enumValueIndex == i, () =>
                    {
                        property.serializedObject.Update();
                        property.enumValueIndex = index;
                        property.serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.DropDown(position);
            }

            EditorGUI.EndProperty();
        }

        // 이름의 접두사(Prefix)를 분석하여 폴더 경로( / )를 만들어줍니다.
        private string GetCategoryPath(string name)
        {
            // 1. 사운드 (Audio)
            if (name.StartsWith("PlaySFX_") || name.StartsWith("PlayVoice_") || name.StartsWith("PlayFootstep"))
                return "🔊 Audio (사운드)/" + name;

            // 2. 시각 효과 (Visuals)
            if (name.StartsWith("Camera") || name.StartsWith("ScreenFX_") || name.StartsWith("VFX_") || name.StartsWith("Trail_") || name.StartsWith("Spawn_"))
                return "✨ Visuals (시각효과)/" + name;

            // 3. 전투 및 시스템 (Combat & System)
            if (name.StartsWith("AttackDir_"))
                return "⚔️ Combat (전투)/8방향 제스처/" + name;
            if (name.Contains("HitBox") || name.Contains("Combo") || name.Contains("Guard") || name.Contains("Parry") || name.Contains("Hitstop") || name.Contains("Recoil"))
                return "⚔️ Combat (전투)/" + name;

            // 4. 상호작용 및 파괴 (Interaction & Gore)
            if (name.StartsWith("Detach_") || name.StartsWith("Ragdoll_"))
                return "🩸 Gore (신체파괴)/" + name;
            if (name.StartsWith("Pair_") || name.StartsWith("ItemGrab_"))
                return "🤝 Interaction (상호작용)/" + name;

            // 5. 기본 시스템
            return "⚙️ System (시스템)/" + name;
        }
    }
}
#endif