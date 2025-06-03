using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;


#region Authoring

[RequireComponent(typeof(CharacterAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    public int AttackDamage;
    public float CooldownTime;
    public GameObject GemPrefab;
    public class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnemyTag());
            AddComponent(entity, new EnemyAttackData
            {
                HitPoints = authoring.AttackDamage,
                CooldownTime = authoring.CooldownTime
            });
            AddComponent(entity, new EnemyCooldownExpirationTimeStamp());
            SetComponentEnabled<EnemyCooldownExpirationTimeStamp>(entity, false);
            AddComponent(entity, new GemPrefab
            {
                Value = GetEntity(authoring.GemPrefab, TransformUsageFlags.Dynamic),
            });
        }
    }
}


#endregion



#region Components

public struct EnemyTag : IComponentData
{
}

public struct EnemyAttackData : IComponentData
{
    public int HitPoints;
    public float CooldownTime;
}

public struct EnemyCooldownExpirationTimeStamp : IComponentData, IEnableableComponent
{
    public double Value;
}

public struct GemPrefab : IComponentData
{
    public Entity Value;
}
#endregion



#region Systems

public partial struct EnemyMoveToPlayerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // We can use this when we only have one instance of this entity
        Entity playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position.xy;
        
        var moveToPlayerJob = new EnemyMoveToPlayerJob
        {
            PlayerPosition = playerPosition,
        };
        
        // moveToPlayerJob.ScheduleParallel();  // This is the same as below. Just it makes same things implicitly via partial
        state.Dependency = moveToPlayerJob.ScheduleParallel(state.Dependency);
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct EnemyAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;
        
        foreach (var (expirationTimeStamp, cooldownEnabled)
                 in SystemAPI.Query<RefRO<EnemyCooldownExpirationTimeStamp>, EnabledRefRW<EnemyCooldownExpirationTimeStamp>>())
        {
            if(expirationTimeStamp.ValueRO.Value > elapsedTime) continue;
            cooldownEnabled.ValueRW = false;
        }

        var attackJob = new EnemyAttackJob
        {
            PlayerLookUp = SystemAPI.GetComponentLookup<PlayerTag>(),
            AttackDataLookUp = SystemAPI.GetComponentLookup<EnemyAttackData>(),
            CooldownLookUp = SystemAPI.GetComponentLookup<EnemyCooldownExpirationTimeStamp>(),
            DamageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
            ElapsedTime =  SystemAPI.Time.ElapsedTime
        };

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = attackJob.Schedule(simulationSingleton, state.Dependency);
    }
}

#region Jobs

[BurstCompile]
[WithAll(typeof(EnemyTag))]
public partial struct EnemyMoveToPlayerJob : IJobEntity
{
    public float2 PlayerPosition;
    
    private void Execute(ref CharacterMoveDirection direction, in LocalTransform transform)
    {
        var vectorToPlayer = PlayerPosition - transform.Position.xy;
        direction.characterMoveDirection =  math.normalize(vectorToPlayer);
    }
}

[BurstCompile]
public struct EnemyAttackJob : ICollisionEventsJob
{
    [ReadOnly] public ComponentLookup<PlayerTag> PlayerLookUp;
    [ReadOnly] public ComponentLookup<EnemyAttackData> AttackDataLookUp;
    public ComponentLookup<EnemyCooldownExpirationTimeStamp> CooldownLookUp;
    public BufferLookup<DamageThisFrame> DamageBufferLookup;
    
    public double ElapsedTime;
    public void Execute(CollisionEvent collisionEvent)
    {
        Entity playerEntity;
        Entity enemyEntity;

        if (PlayerLookUp.HasComponent(collisionEvent.EntityA) && AttackDataLookUp.HasComponent(collisionEvent.EntityB))
        {
            playerEntity = collisionEvent.EntityA;
            enemyEntity = collisionEvent.EntityB;
        }
        else if (PlayerLookUp.HasComponent(collisionEvent.EntityB) && AttackDataLookUp.HasComponent(collisionEvent.EntityA))
        {
            playerEntity = collisionEvent.EntityB;
            enemyEntity = collisionEvent.EntityA;
        }
        else
        {
            return;
        }
        
        if(CooldownLookUp.IsComponentEnabled(enemyEntity)) return;

        var attackData = AttackDataLookUp[enemyEntity];
        CooldownLookUp[enemyEntity] = new EnemyCooldownExpirationTimeStamp
            { Value = ElapsedTime + attackData.CooldownTime };
        CooldownLookUp.SetComponentEnabled(enemyEntity, true);

        var playerDamageBuffer = DamageBufferLookup[playerEntity];
        playerDamageBuffer.Add(new DamageThisFrame
        {
            Value = attackData.HitPoints
        });

    }
}
#endregion

#endregion