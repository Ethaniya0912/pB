using System;
using System.Collections.Generic;
using UnityEngine;
using TDA.Core.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TDA.Character
{
    [Serializable]
    public struct SoundTriggerMapping
    {
        public global::AnimationEventType triggerType;
        public AudioClip audioClip;
        [Range(0f, 1f)] public float volume;
        public bool randomizePitch;
    }

    /// <summary>
    /// [L4 Event] 캐릭터의 모든 청각 피드백을 총괄하는 자율 오디오 스테이션.
    /// 중복 재생 쿨타임 필터를 내장하여 사운드 찢어짐을 방지합니다.
    /// </summary>
    public class CharacterSoundFxManager : MonoBehaviour, IAnimationEventListener
    {
        protected AudioSource audioSource;
        protected CharacterEventManager eventManager;

        [Header("Event Triggers (P4)")]
        public List<SoundTriggerMapping> soundMappings = new List<SoundTriggerMapping>();

        [Header("Anti-Clipping Filter")]
        public float globalMinInterval = 0.05f;

        protected Dictionary<global::AnimationEventType, float> lastPlayedTimes = new Dictionary<global::AnimationEventType, float>();

        protected virtual void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            eventManager = GetComponent<CharacterEventManager>();
        }

        protected virtual void OnEnable()
        {
            if (eventManager != null) eventManager.OnAnimationEventTriggered += OnAnimationEventReceived;
        }

        protected virtual void OnDisable()
        {
            if (eventManager != null) eventManager.OnAnimationEventTriggered -= OnAnimationEventReceived;
        }

        public virtual void OnAnimationEventReceived(global::AnimationEventType eventType)
        {
            if (lastPlayedTimes.TryGetValue(eventType, out float lastTime))
            {
                if (Time.time < lastTime + globalMinInterval) return;
            }

            foreach (var mapping in soundMappings)
            {
                if (mapping.triggerType == eventType && mapping.audioClip != null)
                {
                    lastPlayedTimes[eventType] = Time.time;
                    PlaySoundFX(mapping.audioClip, mapping.volume > 0 ? mapping.volume : 1f, mapping.randomizePitch);
                    break;
                }
            }
        }

        public virtual void PlaySoundFX(AudioClip soundFX, float volume = 1, bool randomizePitch = true, float pitchRandom = 0.1f)
        {
            if (audioSource == null) return;
            audioSource.PlayOneShot(soundFX, volume);
            audioSource.pitch = 1;
            if (randomizePitch) audioSource.pitch += UnityEngine.Random.Range(-pitchRandom, pitchRandom);
        }

        public virtual void PlayRollSoundFX()
        {
            if (WorldSoundFXManager.Instance != null && WorldSoundFXManager.Instance.rollSFX != null)
            {
                if (audioSource != null) audioSource.PlayOneShot(WorldSoundFXManager.Instance.rollSFX);
            }
        }

#if UNITY_EDITOR
        // =========================================================================================
        // [에디터 전용] 누락된 사운드 이벤트 자동 매핑 도우미 함수
        // =========================================================================================
        public void AutoPopulateAudioEvents()
        {
            bool isModified = false;

            // Enum에 정의된 모든 값을 순회합니다.
            foreach (global::AnimationEventType eventType in Enum.GetValues(typeof(global::AnimationEventType)))
            {
                int enumValue = (int)eventType;

                // 기획 조건: [300 ~ 599] 오디오 및 사운드 피드백 영역인지 검사
                if (enumValue >= 300 && enumValue <= 599)
                {
                    bool exists = false;

                    // 현재 soundMappings 리스트에 이미 해당 이벤트가 존재하는지 체크
                    foreach (var mapping in soundMappings)
                    {
                        if (mapping.triggerType == eventType)
                        {
                            exists = true;
                            break;
                        }
                    }

                    // 누락된 이벤트라면 리스트에 추가
                    if (!exists)
                    {
                        soundMappings.Add(new SoundTriggerMapping
                        {
                            triggerType = eventType,
                            audioClip = null,
                            volume = 1.0f, // 기본 볼륨 100%
                            randomizePitch = true
                        });
                        isModified = true;
                    }
                }
            }

            if (isModified)
            {
                EditorUtility.SetDirty(this);
                Debug.Log("<color=lime>[CharacterSoundFxManager]</color> 오디오 이벤트(300~599) 누락본 자동 채우기 완료!");
            }
            else
            {
                Debug.Log("<color=gray>[CharacterSoundFxManager]</color> 이미 모든 오디오 이벤트가 등록되어 있습니다.");
            }
        }
#endif
    }

#if UNITY_EDITOR
    // =========================================================================================
    // [인스펙터 커스텀] 버튼 UI 생성기 (상속받은 클래스에서도 보이도록 true 설정)
    // =========================================================================================
    [CustomEditor(typeof(CharacterSoundFxManager), true)]
    public class CharacterSoundFxManagerEditor : Editor
    {
        // 일괄 수정(Bulk Edit)을 위한 임시 변수들
        private float bulkVolume = 1.0f;
        private bool bulkRandomizePitch = true;

        public override void OnInspectorGUI()
        {
            // 기존 인스펙터 그리기
            DrawDefaultInspector();

            EditorGUILayout.Space(15);

            CharacterSoundFxManager manager = (CharacterSoundFxManager)target;

            // 파란색 예쁜 버튼 추가
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("🔊 오디오 이벤트(300~599) 누락본 자동 추가", GUILayout.Height(30)))
            {
                // 실행 취소(Ctrl+Z)를 위한 레코드 남기기
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