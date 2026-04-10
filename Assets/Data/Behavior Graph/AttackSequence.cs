using System;
using Unity.Behavior;
using UnityEngine;
using Composite = Unity.Behavior.Composite;
using Unity.Properties;

[Serializable, GeneratePropertyBag]
[NodeDescription(name: "Attack", story: "Attack", category: "pB-4/Attack", id: "27174f870e53736ed5c1d281e1d266af")]
public partial class AttackSequence : Composite
{
    [SerializeReference] public Node StalkAction;
    [SerializeReference] public Node CircleStrafeAction;
    [SerializeReference] public Node StrikeAction;

    protected override Status OnStart()
    {
        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        return Status.Success;
    }

    protected override void OnEnd()
    {
    }
}

