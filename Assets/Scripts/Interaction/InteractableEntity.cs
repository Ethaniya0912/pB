using System.Collections.Generic;
using UnityEngine;


namespace SG
{
    /// <summary>
    /// 상호작용 오브젝트들의 데이터 관리를 담당하는 제네릭 미들웨어 클래스입니다.
    /// T 타입별로 별도의 Static Dictionary를 생성하여 타입을 안전하게 관리합니다.
    /// (Curiously Recurring Template Pattern 활용)
    /// </summary>
    /// <typeparam name="T">구체적인 구현 클래스 타입 (예: Door, Chest)</typeparam>
    public abstract class InteractableEntity<T> : InteractableObject where T : InteractableEntity<T>
    {
        // [핵심] T 타입 전용 딕셔너리가 메모리에 별도로 생성됩니다.
        // 예: InteractableEntity<Door>.Instances 와 InteractableEntity<Chest>.Instances는 다릅니다.
        public static Dictionary<int, T> Instances = new Dictionary<int, T>();


        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            RegisterObject();
        }


        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            UnregisterObject();
        }


        private void RegisterObject()
        {
            // ID가 -1인 경우(런타임 생성 등) 등록하지 않거나 별도 처리가 필요할 수 있음
            if (interactableID < 0) return;


            if (Instances.ContainsKey(interactableID))
            {
                // 중복 ID 경고 (개발 단계에서 ID 충돌 확인용)
                Debug.LogWarning($"[SG/{typeof(T).Name}] 중복된 ID 감지: {interactableID}. 인스턴스를 덮어씁니다.");
                Instances[interactableID] = (T)this;
            }
            else
            {
                Instances.Add(interactableID, (T)this);
            }
        }


        private void UnregisterObject()
        {
            if (Instances.ContainsKey(interactableID))
            {
                Instances.Remove(interactableID);
            }
        }


        /// <summary>
        /// 해당 타입(T)의 특정 ID를 가진 오브젝트를 가져옵니다.
        /// </summary>
        public static T Get(int id)
        {
            if (Instances.TryGetValue(id, out T instance))
            {
                return instance;
            }
            return null;
        }
    }
}
