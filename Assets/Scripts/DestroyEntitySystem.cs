using TMG.Survivors;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
[UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
public partial struct DestroyEntitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);

        EntityCommandBuffer beginEntityCommandBuffer = SystemAPI
            .GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        foreach (var (_,entity) in SystemAPI.Query<RefRO<DestroyEntityFlag>>().WithEntityAccess())
        {
            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                GameUIController.Instance.ShowGameOverUI();
            }

            if (SystemAPI.HasComponent<GemPrefab>(entity))
            {
                var gemPrefab = SystemAPI.GetComponent<GemPrefab>(entity).Value;
                var newGem = beginEntityCommandBuffer.Instantiate(gemPrefab);
                
                var spawnPosition = SystemAPI.GetComponent<LocalToWorld>(entity).Position;
                beginEntityCommandBuffer.SetComponent(newGem, LocalTransform.FromPosition(spawnPosition));
            }
            entityCommandBuffer.DestroyEntity(entity);
        }
    }
}

public struct DestroyEntityFlag : IComponentData, IEnableableComponent
{
}