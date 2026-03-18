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

        [Tooltip("소리가 발생할 물리적 위치 (Steam Audio 공간 음향 연동용)")]
        public AudioLocation audioLocation;

        [Range(0f, 1f)] public float volume;
        public bool randomizePitch;
    }

    /// <summary>
    /// [L4 Event] 캐릭터의 모든 청각 피드백을 총괄하는 자율 오디오 스테이션.
    /// 중복 재생 쿨타임 필터를 내장하며, 다중 발원지(Multi-Emitter) 라우팅을 통해 완벽한 공간 음향을 지원합니다.
    /// </summary>
    public class CharacterSoundFxManager : MonoBehaviour, IAnimationEventListener
    {
        [Header("Audio Sources (Emitters)")]
        [Tooltip("캐릭터 가슴/머리 위치의 기본 오디오 소스")]
        public AudioSource rootAudioSource;
        [Tooltip("왼쪽 발뼈에 부착된 오디오 소스")]
        public AudioSource leftFootAudioSource;
        [Tooltip("오른쪽 발뼈에 부착된 오디오 소스")]
        public AudioSource rightFootAudioSource;

        [Tooltip("현재 장착된 무기 끝에 부착된 오디오 소스 (장비 매니저가 동적 할당)")]
        [HideInInspector] public AudioSource currentWeaponAudioSource;

        protected CharacterEventManager eventManager;

        [Header("Event Triggers (P4)")]
        public List<SoundTriggerMapping> soundMappings = new List<SoundTriggerMapping>();

        [Header("Anti-Clipping Filter")]
        public float globalMinInterval = 0.05f;

        protected Dictionary<global::AnimationEventType, float> lastPlayedTimes = new Dictionary<global::AnimationEventType, float>();

        protected virtual void Awake()
        {
            // 하위 호환성을 위해 rootAudioSource가 비어있다면 본체에서 가져옵니다.
            if (rootAudioSource == null) rootAudioSource = GetComponent<AudioSource>();
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

                    // [Steam Audio 연동] 지정된 발원지(Location)의 오디오 소스를 선택하여 재생
                    AudioSource targetSource = GetAudioSourceByLocation(mapping.audioLocation);

                    if (targetSource != null)
                    {
                        PlaySoundFX(targetSource, mapping.audioClip, mapping.volume > 0 ? mapping.volume : 1f, mapping.randomizePitch);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 발원지 Enum에 맞는 AudioSource를 반환하는 라우팅 함수입니다.
        /// 해당 부위의 소스가 비어있다면 안전하게 Root 소스로 대체(Fallback)합니다.
        /// </summary>
        protected AudioSource GetAudioSourceByLocation(AudioLocation location)
        {
            switch (location)
            {
                case AudioLocation.LeftFoot:
                    return leftFootAudioSource != null ? leftFootAudioSource : rootAudioSource;
                case AudioLocation.RightFoot:
                    return rightFootAudioSource != null ? rightFootAudioSource : rootAudioSource;
                case AudioLocation.WeaponTip:
                    return currentWeaponAudioSource != null ? currentWeaponAudioSource : rootAudioSource;
                case AudioLocation.Root:
                default:
                    return rootAudioSource;
            }
        }

        /// <summary>
        /// 특정 오디오 소스(발, 무기 등)에서 사운드를 재생합니다.
        /// </summary>
        public virtual void PlaySoundFX(AudioSource source, AudioClip soundFX, float volume = 1f, bool randomizePitch = true, float pitchRandom = 0.1f)
        {
            if (source == null || soundFX == null) return;

            source.pitch = 1f;
            if (randomizePitch) source.pitch += UnityEngine.Random.Range(-pitchRandom, pitchRandom);

            source.PlayOneShot(soundFX, volume);
        }

        /// <summary>
        /// 레거시 지원용 오버로딩 (외부에서 발원지 지정 없이 호출할 때 무조건 Root에서 재생)
        /// </summary>
        public virtual void PlaySoundFX(AudioClip soundFX, float volume = 1f, bool randomizePitch = true, float pitchRandom = 0.1f)
        {
            PlaySoundFX(rootAudioSource, soundFX, volume, randomizePitch, pitchRandom);
        }

        public virtual void PlayRollSoundFX()
        {
            if (WorldSoundFXManager.Instance != null && WorldSoundFXManager.Instance.rollSFX != null)
            {
                if (rootAudioSource != null) rootAudioSource.PlayOneShot(WorldSoundFXManager.Instance.rollSFX);
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
                            audioLocation = AudioLocation.Root, // 신규 필드 기본값
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
        private AudioLocation bulkAudioLocation = AudioLocation.Root;

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

            // 1. 발원지 일괄 수정
            EditorGUILayout.BeginHorizontal();
            bulkAudioLocation = (AudioLocation)EditorGUILayout.EnumPopup("발원지 (Location)", bulkAudioLocation);
            GUI.backgroundColor = new Color(0.8f, 0.6f, 1f); // 보라색
            if (GUILayout.Button("일괄 적용", GUILayout.Width(80)))
            {
                Undo.RecordObject(manager, "Bulk Apply Audio Location");
                for (int i = 0; i < manager.soundMappings.Count; i++)
                {
                    var mapping = manager.soundMappings[i];
                    mapping.audioLocation = bulkAudioLocation;
                    manager.soundMappings[i] = mapping;
                }
                EditorUtility.SetDirty(manager);
                Debug.Log($"<color=magenta>[SoundManager]</color> 리스트의 모든 사운드 발원지가 {bulkAudioLocation}(으)로 일괄 변경되었습니다.");
            }
            EditorGUILayout.EndHorizontal();

            // 2. 볼륨 일괄 수정
            EditorGUILayout.BeginHorizontal();
            bulkVolume = EditorGUILayout.Slider("전체 볼륨 (Volume)", bulkVolume, 0f, 1f);
            GUI.backgroundColor = new Color(1f, 0.8f, 0.4f); // 주황색
            if (GUILayout.Button("일괄 적용", GUILayout.Width(80)))
            {
                Undo.RecordObject(manager, "Bulk Apply Volume");
                for (int i = 0; i < manager.soundMappings.Count; i++)
                {
                    var mapping = manager.soundMappings[i];
                    mapping.volume = bulkVolume;
                    manager.soundMappings[i] = mapping;
                }
                EditorUtility.SetDirty(manager);
                Debug.Log($"<color=orange>[SoundManager]</color> 리스트의 모든 사운드 볼륨이 {bulkVolume}으로 일괄 변경되었습니다.");
            }
            EditorGUILayout.EndHorizontal();

            // 3. 랜덤 피치 일괄 수정
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