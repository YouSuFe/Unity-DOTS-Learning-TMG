using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;


#region Authoring

public class PlasmaBlastAuthoring : MonoBehaviour
{
    public float MoveSpeed;
    public int AttackDamage;
    
    public class Baker : Baker<PlasmaBlastAuthoring>
    {
        public override void Bake(PlasmaBlastAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlasmaBlastData
            {
                MoveSpeed = authoring.MoveSpeed,
                AttackDamage = authoring.AttackDamage
            });
            AddComponent(entity, new DestroyEntityFlag());
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

#endregion

#region Components

public struct PlasmaBlastData : IComponentData
{
    public float MoveSpeed;
    public int AttackDamage;
}

#endregion

#region Systems

public partial struct MovePlasmaBlastSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        
        foreach (var (localTransform, plasmaData)
                 in SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlasmaBlastData>>())
        {
            localTransform.ValueRW.Position += localTransform.ValueRO.Right() * plasmaData.ValueRO.MoveSpeed *
                                              deltaTime;
        }
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct PlasmaBlastAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var attackJob = new PlasmaBlastAttackJob
        {
            PlasmaBlastDataLookUp = SystemAPI.GetComponentLookup<PlasmaBlastData>(true),
            EnemyLookUp = SystemAPI.GetComponentLookup<EnemyTag>(true),
            DamageBufferLookUp = SystemAPI.GetBufferLookup<DamageThisFrame>(),
            DestroyEntityLookUp =  SystemAPI.GetComponentLookup<DestroyEntityFlag>()
        };

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = attackJob.Schedule(simulationSingleton, state.Dependency);

    }
}

#region Jobs

public struct PlasmaBlastAttackJob : ITriggerEventsJob
{
    [ReadOnly] public ComponentLookup<PlasmaBlastData> PlasmaBlastDataLookUp;
    [ReadOnly] public ComponentLookup<EnemyTag> EnemyLookUp;
    public BufferLookup<DamageThisFrame> DamageBufferLookUp;
    public ComponentLookup<DestroyEntityFlag> DestroyEntityLookUp;
    
    public void Execute(TriggerEvent triggerEvent)
    {
        Entity plasmaBlastEntity;
        Entity enemyHitEntity;

        if (PlasmaBlastDataLookUp.HasComponent(triggerEvent.EntityA) && EnemyLookUp.HasComponent(triggerEvent.EntityB))
        {
            plasmaBlastEntity = triggerEvent.EntityA;
            enemyHitEntity = triggerEvent.EntityB;
        }
        else if (PlasmaBlastDataLookUp.HasComponent(triggerEvent.EntityB) && EnemyLookUp.HasComponent(triggerEvent.EntityA))
        {
            plasmaBlastEntity = triggerEvent.EntityB;
            enemyHitEntity = triggerEvent.EntityA;
        }
        else
        {
            return;
        }

        var attackDamage = PlasmaBlastDataLookUp[plasmaBlastEntity].AttackDamage;
        var enemyDamageBuffer = DamageBufferLookUp[enemyHitEntity];
        enemyDamageBuffer.Add(new DamageThisFrame
        {
            Value = attackDamage
        });
        
        DestroyEntityLookUp.SetComponentEnabled(plasmaBlastEntity, true);
    }
}

#endregion

#endregion