using System.Collections;
using UnityEngine;
using TDA.Core.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.Character.Player
{
    /// <summary>
    /// 이전에 누락되었던 플레이어 전용 호흡 사운드 시스템을 100% 복구 및 연동했습니다.
    /// 차징(Wind-up) 중 호흡 사운드 페이드 인/아웃 기능이 추가되었습니다.
    /// </summary>
    public class PlayerSoundFxManager : CharacterSoundFxManager
    {
        [Header("State Audio")]
        [SerializeField] private AudioSource breathingAudioSource;
        [SerializeField] private AudioClip heavyBreathingClip;
        private Coroutine breathingFadeCoroutine;

        public override void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            base.OnAnimationEventReceived(eventType);

            // 스태미나 고갈 또는 강공격 차징 시작 시 숨소리 페이드 인 (재생)
            if (eventType == global::AnimationEventType.PlaySFX_Stamina_Exhausted ||
                eventType == global::AnimationEventType.Charge_Started)
            {
                HandleBreathingAudio(true);
            }
            // 스태미나 회복, 차징 종료(강공격 발동), 또는 액션 캔슬(구르기/피격) 시 페이드 아웃 (정지)
            else if (eventType == global::AnimationEventType.PlaySFX_Stamina_Recovered ||
                     eventType == global::AnimationEventType.Charge_Ended ||
                     eventType == global::AnimationEventType.Action_Ended)
            {
                HandleBreathingAudio(false);
            }
        }

        private void HandleBreathingAudio(bool isExhausted)
        {
            if (breathingAudioSource == null || heavyBreathingClip == null) return;

            // 꺼야 하는데 이미 안 재생 중이거나 볼륨이 0이면 무시 (Action_Ended 스팸 방어)
            if (!isExhausted && (!breathingAudioSource.isPlaying || breathingAudioSource.volume <= 0.01f)) return;

            if (breathingFadeCoroutine != null) StopCoroutine(breathingFadeCoroutine);

            if (gameObject.activeInHierarchy)
            {
                breathingFadeCoroutine = StartCoroutine(FadeBreathingAudioRoutine(isExhausted));
            }
        }

        private IEnumerator FadeBreathingAudioRoutine(bool fadeIn)
        {
            float duration = 0.8f;
            float timer = 0f;
            float startVolume = breathingAudioSource.volume;
            float targetVolume = fadeIn ? 1f : 0f;

            if (fadeIn && !breathingAudioSource.isPlaying)
            {
                breathingAudioSource.clip = heavyBreathingClip;
                breathingAudioSource.loop = true;
                breathingAudioSource.Play();
            }

            while (timer < duration)
            {
                timer += Time.deltaTime;
                breathingAudioSource.volume = Mathf.Lerp(startVolume, targetVolume, timer / duration);
                yield return null;
            }

            breathingAudioSource.volume = targetVolume;
            if (!fadeIn) breathingAudioSource.Stop();
        }
    }

#if UNITY_EDITOR
    // =========================================================================================
    // [인스펙터 커스텀] 자식 클래스(PlayerSoundFxManager) 전용 버튼 UI 생성기
    // 네임스페이스 분리로 인해 부모의 에디터 상속이 끊기는 유니티 버그를 방지합니다.
    // =========================================================================================
    [CustomEditor(typeof(PlayerSoundFxManager))]
    public class PlayerSoundFxManagerEditor : Editor
    {
        // 일괄 수정(Bulk Edit)을 위한 임시 변수들
        private float bulkVolume = 1.0f;
        private bool bulkRandomizePitch = true;

        public override void OnInspectorGUI()
        {
            // 기존 인스펙터 그리기
            DrawDefaultInspector();

            EditorGUILayout.Space(15);

            PlayerSoundFxManager manager = (PlayerSoundFxManager)target;

            // 파란색 예쁜 버튼 추가 (누락본 자동 추가)
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("🔊 오디오 이벤트(300~599) 누락본 자동 추가", GUILayout.Height(30)))
            {
                Undo.RecordObject(manager, "Auto Populate Audio Events");
                manager.AutoPopulateAudioEvents();
            }

            EditorGUILayout.Space(10);

            // =================================================================================
            // [신규 기능] 볼륨 및 피치 일괄 수정 도구 (Bulk Edit Tools)
            // =================================================================================
            EditorGUILayout.LabelField("🛠️ 리스트 일괄 수정 도구 (Bulk Edit)", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");

            // 1. 볼륨 일괄 수정
            EditorGUILayout.BeginHorizontal();
            bulkVolume = EditorGUILayout.Slider("전체 볼륨 (Volume)", bulkVolume, 0f, 1f);
            GUI.backgroundColor = new Color(1f, 0.8f, 0.4f); // 주황색
            if (GUILayout.Button("일괄 적용", GUILayout.Width(80)))
            {
                Undo.RecordObject(manager, "Bulk Apply Volume");
                for (int i = 0; i < manager.soundMappings.Count; i++)
                {
                    // struct는 값 복사이므로 임시 변수에 담아서 수정 후 다시 할당해야 합니다.
                    var mapping = manager.soundMappings[i];
                    mapping.volume = bulkVolume;
                    manager.soundMappings[i] = mapping;
                }
                EditorUtility.SetDirty(manager); // 변경 사항 저장 플래그
                Debug.Log($"<color=orange>[SoundManager]</color> 리스트의 모든 사운드 볼륨이 {bulkVolume}으로 일괄 변경되었습니다.");
            }
            EditorGUILayout.EndHorizontal();

            // 2. 랜덤 피치 일괄 수정
            EditorGUILayout.BeginHorizontal();
            bulkRandomizePitch = EditorGUILayout.Toggle("랜덤 피치 (Random Pitch)", bulkRandomizePitch);
            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f); // 연두색
            if (GUILayout.Button("일괄 적용", GUILayout.Width(80)))
            {
                Undo.RecordObject(manager, "Bulk Apply Randomize Pitch");
                for (int i = 0; i < manager.soundMappings.Count; i++)
                {
                    var mapping = manager.soundMappings[i];
                    mapping.randomizePitch = bulkRandomizePitch;
                    manager.soundMappings[i] = mapping;
                }
                EditorUtility.SetDirty(manager);
                Debug.Log($"<color=lime>[SoundManager]</color> 리스트의 모든 사운드 랜덤 피치 여부가 {bulkRandomizePitch}(으)로 일괄 변경되었습니다.");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            GUI.backgroundColor = Color.white; // 색상 초기화
        }
    }
#endif
}