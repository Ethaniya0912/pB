using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class UI_Match_Scroll_Wheel_To_Selected_Button : MonoBehaviour
{
    [SerializeField] GameObject currentSelected;
    [SerializeField] GameObject previouslySelected;
    [SerializeField] RectTransform currentSelectedTransform;

    [SerializeField] RectTransform contentPanel;
    [SerializeField] ScrollRect scrollRect;

    private void Update()
    {
        currentSelected = EventSystem.current.currentSelectedGameObject;

        if (currentSelected != null)
        {
            if (currentSelected != previouslySelected)
            {
                previouslySelected = currentSelected;
                currentSelectedTransform = currentSelected.GetComponent<RectTransform>();
                SnapTo(currentSelectedTransform);
            }
        }
    }

    private void SnapTo(RectTransform target)
    {
        Canvas.ForceUpdateCanvases();

        // 새롭게 선택된 트랜스폼의 포지션을 가져옴. 
        Vector2 newPosition = 
            (Vector2)scrollRect.transform.InverseTransformPoint(contentPanel.position) 
            - (Vector2)scrollRect.transform.InverseTransformPoint(target.position);

        // y포지션만 왓다갓다 할 수 있게 함.
        newPosition.x = 0;

        contentPanel.anchoredPosition = newPosition;
    }

}
