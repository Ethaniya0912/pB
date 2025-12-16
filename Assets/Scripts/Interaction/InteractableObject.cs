using UnityEngine;
using Unity.Netcode;


namespace SG
{
    /// <summary>
    /// 모든 상호작용 가능한 오브젝트의 최상위 부모 클래스입니다.
    /// 구체적인 기능 구현 없이 "식별자"와 "행동 정의"만 담당합니다. (SRP 준수)
    /// </summary>
    public abstract class InteractableObject : NetworkBehaviour
    {
        [Header("Instance Identity (Scene ID)")]
        [Tooltip("월드에 배치된 오브젝트의 고유 식별자입니다. 세이브/로드 및 상태 동기화에 사용됩니다.")]
        [SerializeField] public int interactableID;


        [Header("Base Settings")]
        [SerializeField] public string interactableName;


        /// <summary>
        /// 캐릭터가 이 오브젝트와 상호작용할 때 호출되는 진입점입니다.
        /// </summary>
        /// <param name="character">상호작용을 시도한 캐릭터</param>
        public abstract void Interact(CharacterManager character);
    }
}
