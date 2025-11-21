using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class TitleScreenManager : MonoBehaviour
{
    public static TitleScreenManager Instance;

    [Header("Menus")]
    [SerializeField] GameObject titleScreenMainMenu;
    [SerializeField] GameObject titleScreenLoadMenu;

    [Header("Buttons")]
    [SerializeField] Button mainMenuNewGameButton;
    [SerializeField] Button loadMenuReturnButton;
    [SerializeField] Button mainMenuLoadGameButton;

    [Header("Pop Ups")]
    [SerializeField] GameObject noCharacterSlotsPopup;
    [SerializeField] Button noCharacterSlotsOkayButton;

    [Header("Save Slots")]
    public CharacterSlots currentSelectedSlot = CharacterSlots.No_Slot;

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
    }
    public void StartNetworkAsHost()
    {
        NetworkManager.Singleton.StartHost();
    }
    
    public void StartNewGame()
    {
        WorldSaveGameManager.Instance.AttemptToCreateNewGame();
    }

    public void OpenLoadGameMenu()
    {
        // ���θ޴� �ݱ�
        titleScreenLoadMenu.SetActive(false);

        // �ε� �޴� ����
        titleScreenLoadMenu.SetActive(true); 

        // ���� ������ ã�� �ڵ� ����Ʈ�ϱ�.
        loadMenuReturnButton.Select();
    }

    public void CloseLoadGameMenu()
    {
        // �ε� �޴� �ݱ�.
        titleScreenLoadMenu.SetActive(false);

        // ���� �޴� ����.
        titleScreenMainMenu.SetActive(true);

        // �ε� ��ư ������.
        mainMenuLoadGameButton.Select();
    }

    public void DisplayNofreeCharacterSlotPopUp()
    {
        noCharacterSlotsPopup.SetActive(true);
        noCharacterSlotsOkayButton.Select();
    }

    public void CloseNoFreeCharacterSlotsPopUp()
    {
        noCharacterSlotsPopup.SetActive(false);
        mainMenuNewGameButton.Select();
    }

    public void SelectCharacterSlot(CharacterSlots characterSlots)
    {
        currentSelectedSlot = characterSlots;
    }

    public void SelectNoSlot()
    {
        currentSelectedSlot = CharacterSlots.No_Slot;
    }
}