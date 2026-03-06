using System;
using UnityEngine;
using TDA.Character.Player; // PlayerCamera 참조를 위해 필요


namespace TDA.World
{
    /// <summary>
    /// [수정사항] 상위 계층(WorldGameStateManager)에서 전달할 정책 데이터 구조체(Preset) 정의
    /// 개별 매개변수로 전달하던 방식을 통합하여 추후 필터나 노이즈 강도 등이 추가되어도
    /// 함수의 파라미터를 수정할 필요 없이 확장성을 보장합니다.
    /// </summary>
    [Serializable]
    public struct CameraStancePreset
    {
        public float fov;
        public float lerpSpeed;
        public Vector3 customOffset;
    }


    /// <summary>
    /// [P2] 카메라 중재 및 정책 라우터 (Mediator / Router)
    ///
    /// [아키텍처 설계 철학]
    /// 1. NGO 참조 고립: 멀티플레이 환경에서 PlayerCamera가 싱글턴일 경우 발생하는 참조 충돌을 막기 위해,
    ///    본 매니저가 씬 내 유일한 '관제탑(Singleton)' 역할을 수행하며 로컬 플레이어의 카메라만 식별하여 쥐고 있습니다.
    /// 2. 순수 중재자(Mediator): 이 클래스는 물리 연산이나 렌더링 로직을 직접 수행하지 않습니다.
    ///    외부 시스템(전투, 연출)의 명령을 받아 적절한 카메라 프리셋으로 포장하여 실무 집행자(PlayerCamera)에게 전달만 합니다.
    /// </summary>
    public class WorldCameraManager : MonoBehaviour
    {
        // 씬 내 유일한 인스턴스 (관제탑)
        public static WorldCameraManager Instance { get; private set; }


        [Header("Active Local Player Camera")]
        // [NGO 참조 고립] 현재 이 PC를 실제로 조작하고 있는 '나(Local Owner)'의 카메라 참조
        // 인스펙터에서 할당하지 않고 런타임에 동적으로 주입받습니다.
        [SerializeField] private PlayerCamera localPlayerCamera;


        private void Awake()
        {
            // [안전한 싱글턴 패턴 구현]
            if (Instance == null)
            {
                Instance = this;
                // 매니저는 씬 전환 시에도 유지되어야 함
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                // 중복 생성된 매니저 파괴 (메모리 관리 및 참조 혼선 방지)
                Destroy(gameObject);
            }
        }


        /// <summary>
        /// [NGO Dependency Injection]
        /// 로컬 오너(Owner) 플레이어가 씬에 스폰될 때 호출되어 자신의 카메라를 등록합니다.
        /// 이 메서드를 통해 멀티플레이어 환경에서의 카메라 참조 충돌을 완벽하게 회피합니다.
        /// </summary>
        /// <param name="cam">로컬 플레이어 프리팹 하위에 부착된 PlayerCamera 컴포넌트</param>
        public void SetLocalCamera(PlayerCamera cam)
        {
            localPlayerCamera = cam;

#if UNITY_EDITOR
            Debug.Log("<color=green>[WorldCameraManager]</color> 로컬 플레이어 카메라가 성공적으로 등록되었습니다.");
#endif
        }


        // =========================================================================================
        // External API (Relay Functions - 물리 연산 없이 명령만 전달)
        // =========================================================================================


        /// <summary>
        /// 외부 전투 시스템(PlayerCombatManager)에서 락온 대상을 변경할 때 호출되는 라우터 함수입니다.
        /// </summary>
        public void TriggerLockOn(Transform target)
        {
            if (localPlayerCamera != null)
            {
                localPlayerCamera.SetLockOnTarget(target);
            }
        }


        /// <summary>
        /// [P1] 애니메이션 이벤트 통신망(CameraShake_Heavy 등)으로부터 호출되는 카메라 진동 라우터입니다.
        /// </summary>
        /// <param name="amplitude">진동의 세기</param>
        /// <param name="freq">진동의 주파수(속도)</param>
        public void ApplyCameraShake(float amplitude, float freq = 2f)
        {
            if (localPlayerCamera != null)
            {
                // PlayerCamera 내부의 절차적 노이즈(Procedural Noise) 시스템에 명령 전달
                localPlayerCamera.Shake(amplitude, freq);
            }
        }


        /// <summary>
        /// 특정 오브젝트 포커싱 정책 실행 (예: 인벤토리 오픈, 상호작용 연출)
        /// </summary>
        public void StartFocus(Transform target, CameraStancePreset preset)
        {
            if (localPlayerCamera != null)
            {
                localPlayerCamera.SetContextualFocus(target, preset);
            }
        }


        /// <summary>
        /// 현재 진행 중인 모든 포커싱 정책을 중단하고 기본 시점으로 복귀합니다.
        /// </summary>
        public void StopFocus()
        {
            if (localPlayerCamera != null)
            {
                localPlayerCamera.ClearContextualFocus();
            }
        }


        /// <summary>
        /// 바디캠 특유의 거친 흔들림 가중치를 조절합니다. (추격전, 공포 연출 등에서 활용)
        /// </summary>
        /// <param name="weight">0.0(안정적) ~ 1.0(극도로 흔들림)</param>
        public void SetBodycamWeight(float weight)
        {
            if (localPlayerCamera != null)
            {
                localPlayerCamera.SetBodycamWeight(weight);
            }
        }


        // =========================================================================================
        // Legacy Support (기존 기능 유지용 래퍼)
        // =========================================================================================


        /// <summary>
        /// 기존 코드와의 호환성을 위한 쉐이크 함수입니다.
        /// </summary>
        public void ShakeCamera(float intensity, float duration)
        {
            // 새 아키텍처에 맞게 변환하여 전달
            ApplyCameraShake(intensity, 2f);
        }
    }
}
