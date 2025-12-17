using System.Collections.Generic;
using UnityEngine;

namespace SG
{
    /// <summary>
    /// 월드의 모든 가변 데이터(삭제됨, 상태 변경됨, 새로 생성됨)를 담는 데이터 클래스입니다.
    /// 파일로 직렬화(Json 등)되어 저장됩니다.
    /// </summary>
    [System.Serializable]
    public class WorldSaveData
    {
        [Header("Meta Data")]
        public string sceneName;

        [Header("1. Removed Objects")]
        // 씬에서 영구적으로 제거된 오브젝트들의 ID (예: 루팅된 아이템)
        public List<int> removedInteractableIDs = new List<int>();

        [Header("2. Modified Objects")]
        // 씬에 존재하지만 상태나 위치가 변경된 오브젝트들 (예: 열린 문, 이동된 상자)
        // Key: interactableID, Value: 상태 및 위치 정보
        public SerializableDictionary<int, WorldObjectState> objectStates = new SerializableDictionary<int, WorldObjectState>();

        [Header("3. Newly Spawned Items")]
        // 유저가 바닥에 버려서 런타임에 새로 생성되어야 하는 아이템들
        public List<WorldItemSaveData> droppedItems = new List<WorldItemSaveData>();
    }

    [System.Serializable]
    public struct WorldObjectState
    {
        public int interactableID;

        // 상태값 (문 열림/닫힘, 레버 ON/OFF 등)
        public bool boolValue;

        // 위치값 (이동 가능한 오브젝트용)
        public bool savePosition; // 위치 정보가 유효한지 체크
        public Vector3 position;
        public Quaternion rotation;
    }

    [System.Serializable]
    public struct WorldItemSaveData
    {
        public int itemID;       // 아이템 DB 식별자 (무엇을 생성할 것인가?)
        public int amount;       // 수량
        public Vector3 position; // 생성 위치
        public Quaternion rotation;
    }
}