using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using UnityEngine;


#region Authoring

public class GemAuthoring : MonoBehaviour
{
    public class Baker : Baker<GemAuthoring>
    {
        public override void Bake(GemAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new GemTag());
            AddComponent(entity, new DestroyEntityFlag());
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

#endregion

#region Components

public struct GemTag : IComponentData
{
}


#endregion


#region Systems

public partial struct CollectGemSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var collectJob = new CollectGemJob
        {
            GemLookup = SystemAPI.GetComponentLookup<GemTag>(),
            GemsCollectedLookup = SystemAPI.GetComponentLookup<GemsCollectedCount>(),
            DestroyEntityFlagLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>(),
            UpdateGameUIFlagLookup =  SystemAPI.GetComponentLookup<UpdateGameUIFlag>()
        };

        var simulationSystem = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = collectJob.Schedule(simulationSystem, state.Dependency);
    }
}

#region Jobs

[BurstCompile]
public struct CollectGemJob : ITriggerEventsJob
{
    [ReadOnly] public ComponentLookup<GemTag> GemLookup;
    public ComponentLookup<GemsCollectedCount> GemsCollectedLookup;
    public ComponentLookup<DestroyEntityFlag> DestroyEntityFlagLookup;
    public ComponentLookup<UpdateGameUIFlag>  UpdateGameUIFlagLookup;
    
    public void Execute(TriggerEvent triggerEvent)
    {
        Entity gemEntity;
        Entity playerEntity;

        if (GemLookup.HasComponent(triggerEvent.EntityA) && GemsCollectedLookup.HasComponent(triggerEvent.EntityB))
        {
            gemEntity = triggerEvent.EntityA;
            playerEntity = triggerEvent.EntityB;
        }
        else if (GemLookup.HasComponent(triggerEvent.EntityB) && GemsCollectedLookup.HasComponent(triggerEvent.EntityA))
        {
            gemEntity = triggerEvent.EntityB;
            playerEntity = triggerEvent.EntityA;
        }
        else
        {
            return;
        }

        var gemsCollected = GemsCollectedLookup[playerEntity];
        gemsCollected.Value += 1;
        GemsCollectedLookup[playerEntity] = gemsCollected;
        
        UpdateGameUIFlagLookup.SetComponentEnabled(playerEntity, true);
        
        DestroyEntityFlagLookup.SetComponentEnabled(gemEntity, true);
    }
}

#endregion

#endregion