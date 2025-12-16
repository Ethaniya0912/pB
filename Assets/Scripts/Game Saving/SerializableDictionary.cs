using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 시리얼라이즈 : 인스펙터나 json에서 직렬화 가능 대상인식
[System.Serializable]
public class SerializableDictionary<Tkey, TValue> : Dictionary<Tkey, TValue>, ISerializationCallbackReceiver

{
    // 딕셔너리 직렬화 불가, 리스트 가능
    // 딕셔너리의 데이터를 임시로 담을 두개 리스트 선언
    // 시리얼라이즈필드를 붙여 유니티가 해당 리스트 저장.
    [SerializeField] private List<Tkey> keys = new List<Tkey>();
    [SerializeField] private List<TValue> values = new List<TValue>();

    // ISerializationCallbackReceiver 인터페이스 구현 : 직렬화(저장) 직전 호출
    // dictionary -> List 변환 과정
    public void OnBeforeSerialize()
    {
        // 기존 리스트 비워 중복 저장 방지.
        keys.Clear();
        values.Clear();

        // 현재 딕셔너리에 들어있는 모든 데이터(pair)순회
        foreach (KeyValuePair<Tkey, TValue> pair in this)
        {
            // 키는 키리스트, 값은 값 리스트에 순서대로
            keys.Add(pair.Key);
            values.Add(pair.Value);
        }
    }

    // ISerializationCallbackReceiver 인터페이스 구현 : 역직렬화(로딩) 직후 호출
    // List -> Dictionary 복구 과정
    public void OnAfterDeserialize()
    {
        // 딕셔너리 초기화, 리스트 데이터로 새로 채울 준비를 함.
        Clear();

        // 데이터 무결성 체크 : 키의 개수와 값의 개수는 동일해야함.
        if (keys.Count != values.Count)
        {
            Debug.LogError("Your key count does not match your value Count, something's wrong");
        }

        // 리스트 순회, 딕셔너리에 다시 데이터 체워넣기.
        for (int i = 0; i < keys.Count; i++)
        {
            Add(keys[i], values[i]);
        }
    }
}

