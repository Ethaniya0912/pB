using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TDA.Items.WeaponItemActions; // WeaponItemAction 참조를 위해 추가
public class WorldActionManager : MonoBehaviour
{
    public static WorldActionManager Instance { get; private set; }

    [Header("Weapon Item Action")]
    public WeaponItemAction[] weaponItemActions;

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
    }

    private void Start()
    {
        // 모든 웨폰 아이템 액션에 포문을 돌려 id를 줌.
        for (int i = 0; i < weaponItemActions.Length; i++)
        {
            weaponItemActions[i].actionID = i;
        }
    }

    public WeaponItemAction GetWeaponItemActioByID(int ID)
    {
        // 전체 배열을 뒤져 전달받은 ID에 매치된 액션ID를 찾음.
        return weaponItemActions.FirstOrDefault(action => action.actionID == ID);
    }
}
