using UnityEngine;
using System;

// 자식의 트리거 신호를 부모에게 전달하는 범용 스크립트
public class TriggerProxy : MonoBehaviour
{
    public event Action<Collider> OnProxyTriggerEnter;
    public event Action<Collider> OnProxyTriggerExit;

    private void OnTriggerEnter(Collider other)
    {
        OnProxyTriggerEnter?.Invoke(other);
    }

    private void OnTriggerExit(Collider other)
    {
        OnProxyTriggerExit?.Invoke(other);
    }
}