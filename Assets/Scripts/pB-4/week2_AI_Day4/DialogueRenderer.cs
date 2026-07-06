// =============================================================================
// DialogueRenderer.cs  |  pB-4 Week 2 Day 4
// Layer  : L4 Event/Coordination (감각적 연출)
// Owner  : Person A
//
// 역할:
//   완성된 대사 텍스트와 Tone을 받아 월드스페이스 말풍선을 생성/페이드/제거.
//   Day 4 기초 구현은 OnGUI 기반으로 프리팹 없이도 작동하도록 설계.
//   프리팹/TMP 기반 확장은 추후 가능.
//
// 4계층 원칙:
//   - [Rule 1] Dispatcher/Assembler의 내부를 알 필요 없음. Render(line, tone)만 받음.
//   - [Rule 2] 색상/시간은 DialoguePresetSO에서.
//
// 구현 노트:
//   Day 4 기본 구현은 Unity 내장 OnGUI + Camera.WorldToScreenPoint로
//   최소 의존성으로 작동. TextMeshPro 패키지 유무와 무관.
//   추후 SpeechBubblePrefab 주입 시 자동으로 프리팹 모드로 전환.
// =============================================================================
using System.Collections;
using UnityEngine;

namespace TDA.PB4.AI.Speech
{
    /// <summary>월드스페이스 말풍선 렌더러. OnGUI 기반 (프리팹 선택적).</summary>
    [DisallowMultipleComponent]
    public class DialogueRenderer : MonoBehaviour
    {
        [Header("━━━ Preset 목록 (Tone별 스타일) ━━━")]
        [Tooltip("SpeechTone별 시각 스타일. 최소 Neutral 1개 필요.")]
        [SerializeField] private DialoguePresetSO[] presets;

        [Header("━━━ 앵커 (머리 위 지점) ━━━━━━━━━")]
        [Tooltip("말풍선이 표시될 위치. 비워두면 transform.position + offset 사용.")]
        [SerializeField] private Transform speechAnchor;

        [Tooltip("speechAnchor 미지정 시 적용할 월드 y 오프셋.")]
        [SerializeField] private float yOffset = 2.0f;

        [Header("━━━ (선택) 프리팹 기반 렌더 ━━━━━━━━")]
        [Tooltip("TextMeshPro 기반 프리팹. 미지정 시 OnGUI 기반 폴백 사용.")]
        [SerializeField] private GameObject bubblePrefab;

        [Header("━━━ 디버그 ━━━━━━━━━━━━━━━━━━━━━")]
        [SerializeField] private bool debugLog = true;

        // 활성 말풍선 상태 (OnGUI 기반)
        private bool isShowing = false;

        // [FIX] 진행 중인 Bubble 인스턴스 추적 — 강제 발화 시 이전 Bubble 즉시 제거용
        private GameObject currentBubble;
        private string currentText;
        private DialoguePresetSO currentPreset;
        private float currentAlpha;
        private Coroutine showCoroutine;
        /// <summary>NPC 비활성/씬 전환 시 Bubble 누수 방지.</summary>
        void OnDisable()
        {
            // BubbleFollower의 OnDisable과 별개 — 이건 DialogueRenderer가 비활성화될 때
            if (showCoroutine != null)
            {
                StopCoroutine(showCoroutine);
                showCoroutine = null;
            }
            if (currentBubble != null)
            {
                Destroy(currentBubble);
                currentBubble = null;
            }
            isShowing = false;
        }


        /// <summary>Dispatcher가 호출하는 공개 API.</summary>
        public void Render(string line, SpeechTone tone)
        {
            if (string.IsNullOrEmpty(line))
                return;

            var preset = FindPreset(tone);
            if (preset == null)
            {
                Debug.LogWarning($"[DialogueRenderer] {name}: Tone {tone}에 해당하는 Preset 없음.", this);
                return;
            }

            if (debugLog)
                Debug.Log(
                    $"<color=#FFD700>[DialogueRenderer]</color> {name}: " +
                    $"표시 — \"{line}\" (tone={tone})",
                    this);

            // [FIX] Coroutine race 방지 — 이전 coroutine 중단 + Bubble 인스턴스도 즉시 제거
            // (강제 발화 시 fadeOut 미완료 Bubble이 화면에 남는 버그 해결)
            if (showCoroutine != null)
            {
                StopCoroutine(showCoroutine);
                showCoroutine = null;
                isShowing = false;
                currentText = null;
                currentAlpha = 0f;
            }
            // 이전 Bubble 인스턴스 강제 제거
            if (currentBubble != null)
            {
                Destroy(currentBubble);
                currentBubble = null;
            }

            if (bubblePrefab != null)
            {
                // 프리팹 기반 (확장 경로 — 사용자가 프리팹 지정 시)
                showCoroutine = StartCoroutine(ShowPrefabBubble(line, preset));
            }
            else
            {
                // 기본 OnGUI 폴백
                showCoroutine = StartCoroutine(ShowOnGUIBubble(line, preset));
            }
        }

        /// <summary>
        /// TMPro reflection fallback — 패키지 설치 유무와 무관하게 안전하게 TMP_Text 값 설정.
        /// 패키지 미설치 또는 타입 미발견 시 조용히 실패 (경고만).
        /// </summary>
        private void TrySetTMPText(GameObject bubble, string line, DialoguePresetSO preset)
        {
            // TMPro 타입을 reflection으로 찾음 (컴파일 타임 의존 제거)
            var tmpType = System.Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro")
                       ?? System.Type.GetType("TMPro.TMP_Text, TextMeshPro")
                       ?? System.Type.GetType("TMPro.TMP_Text");
            if (tmpType == null)
            {
                if (debugLog)
                    Debug.LogWarning($"[DialogueRenderer] TMPro 타입 미발견. UI.Text 또는 OnGUI 폴백 사용 권장.", this);
                return;
            }

            var tmp = bubble.GetComponentInChildren(tmpType, true);
            if (tmp == null) return;

            var textProp = tmpType.GetProperty("text");
            var colorProp = tmpType.GetProperty("color");
            var fontSizeProp = tmpType.GetProperty("fontSize");

            textProp?.SetValue(tmp, line);
            colorProp?.SetValue(tmp, preset.fontColor);
            fontSizeProp?.SetValue(tmp, preset.fontSize);
        }

        private DialoguePresetSO FindPreset(SpeechTone tone)
        {
            if (presets == null || presets.Length == 0) return null;
            foreach (var p in presets)
            {
                if (p != null && p.tone == tone) return p;
            }
            // fallback: 첫 preset
            return presets[0];
        }

        // ─────────────────────────────────────────
        //   기본 구현: OnGUI 기반 말풍선
        // ─────────────────────────────────────────
        private IEnumerator ShowOnGUIBubble(string line, DialoguePresetSO preset)
        {
            currentText = line;
            currentPreset = preset;
            currentAlpha = 0f;
            isShowing = true;

            // Fade In
            yield return FadeTo(1f, preset.fadeInDuration);

            // Hold
            yield return new WaitForSeconds(preset.displayDuration);

            // Fade Out
            yield return FadeTo(0f, preset.fadeOutDuration);

            isShowing = false;
            currentText = null;
            showCoroutine = null;
        }

        private IEnumerator FadeTo(float target, float duration)
        {
            float start = currentAlpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                currentAlpha = Mathf.Lerp(start, target, duration <= 0f ? 1f : elapsed / duration);
                yield return null;
            }
            currentAlpha = target;
        }

        void OnGUI()
        {
            if (!isShowing || bubblePrefab != null || currentPreset == null) return;
            if (string.IsNullOrEmpty(currentText)) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            // 월드 → 스크린 좌표
            Vector3 worldPos = (speechAnchor != null ? speechAnchor.position
                                                     : transform.position + Vector3.up * yOffset);
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0f) return;  // 카메라 뒤

            float screenY = Screen.height - screenPos.y;  // GUI 좌표 flip

            // 스타일 구성
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = Mathf.RoundToInt(currentPreset.fontSize),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            var fontCol = currentPreset.fontColor;
            fontCol.a = currentAlpha;
            style.normal.textColor = fontCol;

            // 백그라운드 (GUI.color로 전체 알파 적용)
            Color prevColor = GUI.color;
            var bgCol = currentPreset.backgroundColor;
            bgCol.a *= currentAlpha;
            GUI.color = bgCol;

            // 박스 크기 계산
            float maxWidth = 280f;
            Vector2 size = style.CalcSize(new GUIContent(currentText));
            float w = Mathf.Min(maxWidth, size.x + 24f);
            float textH = style.CalcHeight(new GUIContent(currentText), w - 16f);
            float h = textH + 16f;

            Rect rect = new Rect(screenPos.x - w / 2f, screenY - h - 10f, w, h);

            // (선택) Shake 효과
            if (currentPreset.shakeAmplitude > 0f)
            {
                float shake = currentPreset.shakeAmplitude;
                rect.x += (Random.value - 0.5f) * shake;
                rect.y += (Random.value - 0.5f) * shake;
            }

            GUI.Box(rect, GUIContent.none);
            GUI.color = prevColor;

            // 텍스트
            Rect textRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f);
            GUI.Label(textRect, currentText, style);
        }

        // ─────────────────────────────────────────
        //   확장 구현: Prefab 기반 (bubblePrefab 지정 시)
        // ─────────────────────────────────────────
        private IEnumerator ShowPrefabBubble(string line, DialoguePresetSO preset)
        {
            // [FIX] 부모의 비균등 Scale(예: Armature 100x) 영향 회피 — 월드 루트에 생성
            Transform anchor = (speechAnchor != null) ? speechAnchor : transform;
            Vector3 spawnPos = anchor.position + (speechAnchor == null ? Vector3.up * yOffset : Vector3.zero);
            // [FIX] 인스턴스 필드에 저장 — 강제 발화 시 즉시 Destroy 가능
            currentBubble = Instantiate(bubblePrefab, spawnPos, Quaternion.identity, null);
            GameObject bubble = currentBubble;  // 로컬 alias (기존 코드 호환)
            // 매 프레임 NPC 머리 위 추적 (parent-less follow)
            var tracker = bubble.AddComponent<BubbleFollower>();
            tracker.target = anchor;
            tracker.offset = (speechAnchor == null ? Vector3.up * yOffset : Vector3.zero);

            // [FIX] TMPro 패키지 미설치 환경에서도 컴파일 되도록 reflection 경유.
            //       UI.Text는 항상 존재하므로 우선 시도, 없으면 TMP_Text reflection.
            var legacyText = bubble.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (legacyText != null)
            {
                legacyText.text = line;
                legacyText.color = preset.fontColor;
                legacyText.fontSize = Mathf.RoundToInt(preset.fontSize);
            }
            else
            {
                // TMPro reflection fallback — 패키지 유무와 무관하게 작동
                TrySetTMPText(bubble, line, preset);
            }

            // 캔버스 그룹 페이드
            var cg = bubble.GetComponent<CanvasGroup>();
            if (cg == null) cg = bubble.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            float elapsed = 0f;
            while (elapsed < preset.fadeInDuration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Clamp01(elapsed / preset.fadeInDuration);
                yield return null;
            }

            // 표시 시간 — preset.autoCalcDuration이 true면 텍스트 길이 기반 자동 계산
            float displayTime = ComputeDisplayDuration(line, preset);
            yield return new WaitForSeconds(displayTime);

            elapsed = 0f;
            while (elapsed < preset.fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                cg.alpha = Mathf.Clamp01(1f - elapsed / preset.fadeOutDuration);
                yield return null;
            }

            Destroy(bubble);
            currentBubble = null;
            showCoroutine = null;
        }

        /// <summary>
        /// 표시 시간 계산. autoCalcDuration이 true이면 텍스트 길이 기반.
        /// false이면 preset.displayDuration 고정값 사용.
        /// 우선순위: variant.durationOverride > preset.autoCalcDuration > preset.displayDuration
        /// </summary>
        private float ComputeDisplayDuration(string line, DialoguePresetSO preset)
        {
            if (!preset.autoCalcDuration)
                return preset.displayDuration;

            // 텍스트 길이 기반: charCount * secondsPerChar * durationScale
            int charCount = string.IsNullOrEmpty(line) ? 0 : line.Length;
            float raw = charCount * preset.secondsPerChar * preset.durationScale;

            // min/max 범위로 클램프
            return Mathf.Clamp(raw, preset.minDuration, preset.maxDuration);
        }

        // === Test Lab / 수동 트리거 ===
        [ContextMenu("TEST: '안녕하세요' 출력 (Warm)")]
        void DebugRenderWarm()
        {
            Render("안녕하세요! 만나서 반가워요.", SpeechTone.Warm);
        }

        [ContextMenu("TEST: '꺼져' 출력 (Aggressive)")]
        void DebugRenderAggressive()
        {
            Render("꺼져. 다가오지 마.", SpeechTone.Aggressive);
        }
    }
}