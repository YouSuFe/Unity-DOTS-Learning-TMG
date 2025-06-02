using TMG.Survivors;
using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
public partial struct DestroyEntitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (_,entity) in SystemAPI.Query<RefRO<DestroyEntityFlag>>().WithEntityAccess())
        {
            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                GameUIController.Instance.ShowGameOverUI();
            }
            entityCommandBuffer.DestroyEntity(entity);
        }
    }
}

public struct DestroyEntityFlag : IComponentData, IEnableableComponent
{
}