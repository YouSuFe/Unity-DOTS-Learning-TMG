using TMG.Survivors;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;

#region Authoring

public class PlayerAuthoring : MonoBehaviour
{
    public GameObject AttackPrefab;
    public float CooldownTime;
    public float DetectionSize;
    public GameObject WorldUIPrefab;
    
    public class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerTag());
            AddComponent(entity, new InitializeCameraTargetTag());
            AddComponent(entity, new CameraTarget());

            var enemyLayer = LayerMask.NameToLayer("Enemy");
            var enemyLayerMask = (uint)math.pow(2, enemyLayer);

            var attackCollisionFilter = new CollisionFilter
            {
                BelongsTo = uint.MaxValue,
                CollidesWith = enemyLayerMask
            };
            
            AddComponent(entity, new PlayerAttackData
            {
                AttackPrefab = GetEntity(authoring.AttackPrefab, TransformUsageFlags.Dynamic),
                CooldownTime = authoring.CooldownTime,
                DetectionSize = new float3(authoring.DetectionSize),
                CollisionFilter = attackCollisionFilter
            });
            AddComponent(entity, new PlayerCooldownExpirationTimeStamp());
            AddComponent(entity, new GemsCollectedCount());
            AddComponent(entity, new UpdateGameUIFlag());
            AddComponent(entity, new PlayerWorldUIPrefab
            {
                Value =  authoring.WorldUIPrefab,
            });
            
        }
    }
}

#endregion

#region Component Data

public struct PlayerTag : IComponentData
{
}

public struct InitializeCameraTargetTag : IComponentData
{
}

public struct CameraTarget : IComponentData
{
    public UnityObjectRef<Transform> CameraTransform;
}

public struct PlayerAttackData : IComponentData
{
    public Entity AttackPrefab;
    public float CooldownTime;
    public float3 DetectionSize;
    public CollisionFilter CollisionFilter;
}

public struct GemsCollectedCount : IComponentData
{
    public int Value;
}

// This is for game object world UI updates to be used in Systems or Jobs.
public struct UpdateGameUIFlag : IComponentData, IEnableableComponent
{
}

public struct PlayerCooldownExpirationTimeStamp : IComponentData
{
    public double Value;
}

// It adds additional clean-up logic when the main entity is destroyed (player).
// The thing is when an entity is destroyed, it remains as a ghost on the system.
// That is because it has some clean-up logic that will be cleaned after destroyed (I guess LocalTransform is one of them). 
public struct PlayerWorldUI : ICleanupComponentData
{
    public  UnityObjectRef<Transform> CanvasTransform;
    public  UnityObjectRef<Slider> HealthBarSlider;
}

public struct PlayerWorldUIPrefab : IComponentData
{
    public UnityObjectRef<GameObject> Value;
}
#endregion



#region Systems

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CameraInitializationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InitializeCameraTargetTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if(CameraTargetSingleton.Instance == null) return;
        var cameraTargetTransform = CameraTargetSingleton.Instance.transform;

        EntityCommandBuffer commandBuffer = new EntityCommandBuffer(state.WorldUpdateAllocator);
        
        foreach (var (cameraTarget, entity) in SystemAPI.Query<RefRW<CameraTarget>>().WithAll<InitializeCameraTargetTag, PlayerTag>().WithEntityAccess())
        {
            cameraTarget.ValueRW.CameraTransform =  cameraTargetTransform;
            commandBuffer.RemoveComponent<InitializeCameraTargetTag>(entity);
        }
        commandBuffer.Playback(state.EntityManager);
    }
}


[UpdateInGroup(typeof(TransformSystemGroup))]
public partial struct MoveCameraSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, cameraTarget) 
                 in SystemAPI.Query<RefRO<LocalToWorld>, RefRO<CameraTarget>>().WithAll<PlayerTag>().WithNone<InitializeCameraTargetTag>())
        {
            cameraTarget.ValueRO.CameraTransform.Value.position = transform.ValueRO.Position;
        }
    }
}
public partial class PlayerInputSystem : SystemBase
{
    private SurvivorsInput _input;

    protected override void OnCreate()
    {
        _input = new SurvivorsInput();
        _input.Enable();
    }

    protected override void OnUpdate()
    {
        var currentInput = (float2)_input.Player.Move.ReadValue<Vector2>();

        foreach (var direction in SystemAPI.Query<RefRW<CharacterMoveDirection>>().WithAll<PlayerTag>())
        {
            direction.ValueRW.characterMoveDirection = currentInput;
        }
    }
}


public partial struct PlayerAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;

        EntityCommandBuffer entityCommandBuffer = SystemAPI
            .GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);
        
        PhysicsWorldSingleton physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        
        foreach (var (expirationTimeStamp, attackData, localTransform)
                 in SystemAPI.Query<RefRW<PlayerCooldownExpirationTimeStamp>, RefRO<PlayerAttackData>, RefRO<LocalTransform>>())
        {
            if (expirationTimeStamp.ValueRO.Value > elapsedTime) continue;
            
            float3 spawnPosition = localTransform.ValueRO.Position;
            var minDetectPosition = spawnPosition - attackData.ValueRO.DetectionSize;
            var maxDetectPosition = spawnPosition + attackData.ValueRO.DetectionSize;

            // Let's say we have spawn position as 0. our min and max have 10 units difference from origin which means 20
            // So, at the end we have 20x20 area to look, I guess.  
            var aabbInput = new OverlapAabbInput
            {
                Aabb = new Aabb
                {
                    Min = minDetectPosition,
                    Max = maxDetectPosition,
                },
                
                Filter = attackData.ValueRO.CollisionFilter
            };

            var overlapHits = new NativeList<int>(state.WorldUpdateAllocator);
            if (!physicsWorldSingleton.OverlapAabb(aabbInput, ref overlapHits))
            {
                continue;
            }
            
            var maxDistanceSq = float.MaxValue;
            var closestEnemyPosition = float3.zero;
            foreach (var overlapHit in overlapHits)
            {
                var currentEnemyPosition = physicsWorldSingleton.Bodies[overlapHit].WorldFromBody.pos;
                var distanceToPlayerSq = math.distancesq(spawnPosition.xy, currentEnemyPosition.xy);

                if (distanceToPlayerSq < maxDistanceSq)
                {
                    maxDistanceSq = distanceToPlayerSq;
                    closestEnemyPosition = currentEnemyPosition;
                }
            }

            var vectorToClosestEnemy = closestEnemyPosition - spawnPosition;
            var angleToClosestEnemy = math.atan2(vectorToClosestEnemy.y, vectorToClosestEnemy.x);
            var spawnOrientation = quaternion.Euler(0f, 0f, angleToClosestEnemy);
            
            var newAttack = entityCommandBuffer.Instantiate(attackData.ValueRO.AttackPrefab);
            entityCommandBuffer.SetComponent(newAttack, LocalTransform.FromPositionRotation(spawnPosition, spawnOrientation));

            expirationTimeStamp.ValueRW.Value = elapsedTime + attackData.ValueRO.CooldownTime;
        }
    }
}

public partial struct UpdateGemUISystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (gemCount, shouldUpdateUI)
                 in SystemAPI.Query<RefRO<GemsCollectedCount>, EnabledRefRW<UpdateGameUIFlag>>())
        {
            GameUIController.Instance.UpdateGemsCollectedText(gemCount.ValueRO.Value);
            shouldUpdateUI.ValueRW = false;
        }
    }
}

public partial struct PlayerWorldUISystem : ISystem
{
    // We cannot use Burst here beacuse it has managed type components
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(state.WorldUpdateAllocator);
        
        foreach (var (uiPrefab, entity)
                 in SystemAPI.Query<RefRO<PlayerWorldUIPrefab>>().WithNone<PlayerWorldUI>().WithEntityAccess())
        {
            var newWorldUI = Object.Instantiate(uiPrefab.ValueRO.Value.Value);
            
            entityCommandBuffer.AddComponent(entity, new PlayerWorldUI
            {
                CanvasTransform =  newWorldUI.transform,
                HealthBarSlider = newWorldUI.GetComponentInChildren<Slider>()
            });
        }

        foreach (var (localTransform, worldUI,
                     currentHitPoints, maxHitPoints)
                 in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PlayerWorldUI>, RefRO<CharacterCurrentHitPoints>, RefRO<CharacterMaxHitPoints>>())
        {
            worldUI.ValueRO.CanvasTransform.Value.position = localTransform.ValueRO.Position;
            var healthValue = (float)currentHitPoints.ValueRO.Value / maxHitPoints.ValueRO.Value;
            worldUI.ValueRO.HealthBarSlider.Value.value = healthValue;
        }

        // With this query, we are looking for destroyed ghost objects. The object itself is destroyed, but there are remains Clean Up objects.
        foreach (var (worldUI, entity) in SystemAPI.Query<RefRO<PlayerWorldUI>>().WithNone<LocalToWorld>().WithEntityAccess())
        {
            if (worldUI.ValueRO.CanvasTransform.Value != null)
            {
                Object.Destroy(worldUI.ValueRO.CanvasTransform.Value.gameObject);
            }
            
            entityCommandBuffer.RemoveComponent<PlayerWorldUI>(entity);
        }
        
        entityCommandBuffer.Playback(state.EntityManager);
    }
}
#endregion