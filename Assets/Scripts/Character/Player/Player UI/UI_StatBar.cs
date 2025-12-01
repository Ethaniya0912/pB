using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_StatBar : MonoBehaviour
{
    private Slider slider;
    private RectTransform rectTransform;

    // 스탯에 따라 바 사이즈가 스케일되는 변수 추가 ( 높은 스탯 = 긴  바)
    [Header("Bar Options")]
    [SerializeField] protected bool scaleBarLengthWithStats = true;
    [SerializeField] protected float widthScaleMultiplier = 1;
    // 세컨더리 이펙트를 위한 세컨더리 바.

    protected virtual void Awake()
    { 
        slider  = GetComponent<Slider>();
        rectTransform = GetComponent<RectTransform>();
    }

    public virtual void SetStat(float newValue)
    {
        slider.value = newValue;
    }

    public virtual void SetMaxStat(int maxValue)
    {
        slider.maxValue = maxValue;
        slider.value = maxValue;

        if (scaleBarLengthWithStats)
        {
            rectTransform.sizeDelta = new Vector2
            (maxValue * widthScaleMultiplier, rectTransform.sizeDelta.y);
            
            // 그룹 레이아웃세팅에 맞춰 바의 포지션을 재조정.
            PlayerUIManager.Instance.playerUIHUDManager.RefreshHUD();
        }
    }
}
