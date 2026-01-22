using UnityEngine;

/// <summary>
/// 방어구 및 가방의 속성을 정의하는 데이터 에셋입니다. (Dev A)
/// 가방의 시각적 배치를 위한 데이터가 추가되었습니다.
/// </summary>
[CreateAssetMenu(menuName = "Items/Equipment Item")]
public class EquipmentItem : Item
{
    [Header("Equipment Info")]
    public EquipmentSlot equipmentSlot;

    [Header("Stat Modifiers")]
    public int physicalDefense;
    public float weight;

    [Header("Backpack Settings")]
    public bool isBackpack;
    public int backpackWidth;   // 가방 장착 시 제공할 격자 가로 칸수
    public int backpackHeight;  // 가방 장착 시 제공할 격자 세로 칸수
    

    [Header("Backpack Visual Layout (Open State)")]


    public float backpackSlotPadding = 0.08f; // 가방별 격자 간격 조절

    [Tooltip("가방을 바닥에 내려놓았을 때 가방 모델 자체의 회전값입니다.")]
    public Vector3 backpackLyingRotation = new Vector3(90, 0, 0);

    [Tooltip("가방 내부에서 아이템 격자(InternalGridRoot)가 어느 면을 향할지 결정하는 로컬 회전값입니다.")]
    public Vector3 internalGridLocalRotation = Vector3.zero;

    [Tooltip("격자 판이 가방 메쉬에 파묻히지 않도록 띄우는 오프셋입니다.")]
    public Vector3 internalGridLocalOffset = new Vector3(0, 0.02f, 0);

    [Header("Scale Settings")]
    [Tooltip("장착했을 때 캐릭터 등에서 보여질 실제 스케일입니다. 프리팹의 의도된 크기를 입력하세요.")]
    public Vector3 backpackModelScale = new Vector3(1f, 1f, 1f); // 예: 0.15, 0.15, 0.15

    [Header("Visual Data")]
    public string armorPartID;  // 모델링 파츠 식별 ID
}