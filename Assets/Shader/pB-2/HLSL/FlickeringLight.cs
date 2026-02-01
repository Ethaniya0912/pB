using UnityEngine;
using System.Collections.Generic;

public class FlickeringLight : MonoBehaviour
{
    [Header("Light Settings")]
    public Light torchLight;
    public float minIntensity = 0.8f;
    public float maxIntensity = 1.5f;
    public float flickerSpeed = 0.07f; // 불빛 강도가 변하는 주기(초)

    [Header("Smoothing")]
    [Range(1, 50)]
    public int smoothing = 5;

    [Header("Movement Settings")]
    public Vector3 posA; // 이동 시작 지점 (로컬 좌표 권장)
    public Vector3 posB; // 이동 종료 지점 (로컬 좌표 권장)
    public float moveSpeed = 0.5f; // 이동 속도

    private Queue<float> smoothQueue;
    private float lastSum = 0;
    private float timer = 0;

    void Start()
    {
        smoothQueue = new Queue<float>(smoothing);
        if (torchLight == null) torchLight = GetComponent<Light>();

        // 초기 큐 채우기
        for (int i = 0; i < smoothing; i++)
        {
            float startVal = Random.Range(minIntensity, maxIntensity);
            smoothQueue.Enqueue(startVal);
            lastSum += startVal;
        }

        // 시작 시 현재 위치를 posA로 설정하고 싶다면 인스펙터에서 조절하거나 아래 주석을 해제하세요.
        posA = transform.localPosition;
    }

    void Update()
    {
        // 1. 위치 이동 로직 (PingPong을 이용한 왕복 이동)
        float t = Mathf.PingPong(Time.time * moveSpeed, 1.0f);
        transform.localPosition = Vector3.Lerp(posA, posB, t);

        // 2. 불빛 일렁임 로직
        if (torchLight == null) return;

        timer += Time.deltaTime;

        // flickerSpeed 시간이 지날 때마다 새로운 강도 계산
        if (timer >= flickerSpeed)
        {
            timer = 0;

            // 기존 값을 제거
            if (smoothQueue.Count >= smoothing)
            {
                lastSum -= smoothQueue.Dequeue();
            }

            // 새로운 무작위 값을 추가
            float newVal = Random.Range(minIntensity, maxIntensity);
            smoothQueue.Enqueue(newVal);
            lastSum += newVal;
        }

        // 매 프레임 평균값으로 강도 적용 (부드러운 전환을 위해)
        if (smoothQueue.Count > 0)
        {
            torchLight.intensity = lastSum / smoothQueue.Count;
        }
    }
}