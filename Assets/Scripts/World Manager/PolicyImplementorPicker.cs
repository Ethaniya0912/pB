// =============================================================================
// PolicyImplementorPicker.cs  |  pB-4 Wk3 Editor Drawer (★ v2.6 신규)
// Layer  : L4 Editor / Inspector Tool
// Namespace: TDA.PB4.AI
//
// 역할:
//   string 필드에 [PolicyImplementorPicker] attribute 적용 시,
//   인스펙터에 드롭다운 표시 — Reflection으로 IFactionGroupPolicy + MonoBehaviour
//   구현체를 자동 검색하여 옵션 제공.
//
// 사용:
//   [PolicyImplementorPicker]
//   public string policyImplementorTypeName;
//
// 위치: WorldAISpawnManager.cs와 같은 폴더 (Editor 폴더 분리 안 함).
// 어셈블리 단방향 의존 회피 위해 #if UNITY_EDITOR 보호.
// =============================================================================
using System;
using UnityEngine;

namespace TDA.PB4.AI
{
    /// <summary>
    /// string 필드에 적용 시 인스펙터에 IFactionGroupPolicy 구현체 드롭다운 표시.
    /// </summary>
    public class PolicyImplementorPickerAttribute : PropertyAttribute { }
}

#if UNITY_EDITOR
namespace TDA.PB4.AI
{
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;

    [CustomPropertyDrawer(typeof(PolicyImplementorPickerAttribute))]
    public class PolicyImplementorPickerDrawer : PropertyDrawer
    {
        // 캐시 — 첫 호출만 비용
        private static string[] _cachedOptions;
        private static double _lastCacheTime;
        private const double CACHE_TTL_SEC = 5.0; // 5초마다 재검색 (새 구현체 추가 즉시 반영)

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "(string 필드만 적용 가능)");
                return;
            }

            var options = GetCachedOptions();
            int currentIdx = 0;
            if (!string.IsNullOrEmpty(property.stringValue))
            {
                int found = Array.IndexOf(options, property.stringValue);
                if (found >= 0) currentIdx = found;
                else currentIdx = -1;  // 옵션에 없는 값 — 표시는 None으로 + 경고
            }

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUI.Popup(position, label.text, currentIdx == -1 ? 0 : currentIdx, options);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = newIdx == 0 ? "" : options[newIdx];
            }

            // 옵션에 없는 값이면 경고 라벨
            if (currentIdx == -1)
            {
                var warnRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight,
                                        position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.HelpBox(warnRect, $"'{property.stringValue}' 타입 없음 (재컴파일/오타 확인)", MessageType.Warning);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseH = EditorGUIUtility.singleLineHeight;
            if (!string.IsNullOrEmpty(property.stringValue))
            {
                var options = GetCachedOptions();
                if (Array.IndexOf(options, property.stringValue) < 0)
                    return baseH * 2 + 2;  // 경고 라벨 공간
            }
            return baseH;
        }

        private static string[] GetCachedOptions()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_cachedOptions != null && (now - _lastCacheTime) < CACHE_TTL_SEC)
                return _cachedOptions;

            var iface = typeof(TDA.PB4.Interfaces.Intelligence.IFactionGroupPolicy);
            var monoType = typeof(MonoBehaviour);
            var names = new List<string> { "(None)" };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract) continue;
                    if (!iface.IsAssignableFrom(t)) continue;
                    if (!monoType.IsAssignableFrom(t)) continue;
                    names.Add(t.Name);
                }
            }

            _cachedOptions = names.ToArray();
            _lastCacheTime = now;
            return _cachedOptions;
        }
    }
}
#endif
