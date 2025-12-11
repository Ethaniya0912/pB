using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterLocomotionManager : MonoBehaviour
{
    CharacterManager character;

    [Header("Ground Check & Jumping")]
    [SerializeField] protected float gravityForce = -5.55f;

    [Header("Flags")]
    public bool isRolling = false;
    protected virtual void Awake()
    {

    }

    protected virtual void Update()
    {

    }
}
