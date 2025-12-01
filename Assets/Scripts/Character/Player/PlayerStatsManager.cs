using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStatsManager : CharacterStatsManager
{
    PlayerManager player;

    protected override void Awake()
    {
        base.Awake();

        player = GetComponent<PlayerManager>();
    }

    protected override void Start()
    {
        base.Start();

        // 캐릭터 크리에이션 메뉴를 만들고 스탯이 클래스에 따르면, 거기서 계산.
        // 그 전까진 계산이 안되서 여기서 임시 계산. 세이브 파일 존재시 로딩시 오버라이드.
        CalculateHealthBasedOnVitalityLevel(player.playerNetworkManager.vitality.Value);
        CalculateStaminaBasedOnEnduranceLevel(player.playerNetworkManager.endurance.Value);
    }

}
