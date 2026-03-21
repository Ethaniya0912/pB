using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class WorldCharacterEffectsManager : MonoBehaviour
{
    public static WorldCharacterEffectsManager Instance{get; private set;}

    [Header("VFX")]
    public GameObject bloodSplatterVFX;
    public GameObject hitsparkVFX; // 인스펙터에서 앞서 만든 hitspark 프리팹 연결

    [Header("Damage")]
    public TakeDamageEffect takeDamageEffect;

    [SerializeField] List<InstantCharacterEffect> instantEffects;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        DontDestroyOnLoad(gameObject);
        GenerateEffectIDs();
    }

    private void GenerateEffectIDs()
    {
        for (int i = 0; i < instantEffects.Count; i++)
        {
            instantEffects[i].instantEffectID = i;
        }
    }
}
