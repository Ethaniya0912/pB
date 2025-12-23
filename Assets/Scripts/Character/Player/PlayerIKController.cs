using UnityEngine;

namespace SG
{
    public class PlayerIKController : CharacterIKController
    {
        PlayerManager player;

        protected override void Awake()
        {
            base.Awake();
            player = GetComponent<PlayerManager>();
        }

        protected override void Update()
        {
            base.Update();
            
            // 로컬 플레이어라면, 카메라가 보는 방향을 살짝 쳐다보게 하는 로직 추가 가능
            if (player.IsOwner)
            {
                HandleHeadLookAtCamera();
            }
        }

        private void HandleHeadLookAtCamera()
        {
            // 여기에 카메라 방향으로 HeadTarget을 이동시키는 로직 구현 (Dev B 연출 영역)
            // 예: headTarget.position = player.playerCamera.transform.position + player.playerCamera.transform.forward * 10f;
        }
    }
}

