using UnityEngine;

/// <summary>
/// 격자 인벤토리의 좌표 계산 및 드래그 가이드를 보조하는 UI 클래스입니다. (Dev B)
/// </summary>
public class UI_InventoryGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    public float slotSize = 100f; // UI 상의 슬롯 크기
    public int width;
    public int height;

    // 마우스 월드 좌표(hit.point)를 인벤토리 격자 인덱스로 변환
    public Vector2Int WorldPointToGrid(Vector3 worldPoint, Transform gridRoot)
    {
        Vector3 localPos = gridRoot.InverseTransformPoint(worldPoint);

        // 가로(X), 세로(Y) 인덱스 계산 (좌측 상단 기준)
        int x = Mathf.FloorToInt(localPos.x / 0.1f);
        int y = Mathf.FloorToInt(-localPos.y / 0.1f);

        return new Vector2Int(x, y);
    }

    // 아이템의 크기를 고려하여 격자 점유 영역을 반환 (충돌 체크용)
    public Rect GetItemGridRect(InventoryItem item, int itemW, int itemH)
    {
        return new Rect(item.gridPositionX, item.gridPositionY, itemW, itemH);
    }
}
