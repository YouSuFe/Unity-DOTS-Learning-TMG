using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine;

#region  Character Authoring

public enum PlayerAnimationIndex : byte
{
    Movement = 0,
    Idle = 1,
    
    None = Byte.MaxValue, 
}
public class CharacterAuthoring : MonoBehaviour
{
    public float moveSpeed; 
    public int hitPoints; 
    
    public class Baker : Baker<CharacterAuthoring>
    {
        public override void Bake(CharacterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new InitializeCharacterFlag());
            AddComponent(entity, new CharacterMoveDirection());
            AddComponent(entity, new CharacterMoveSpeed
            {
                characterMoveSpeed = authoring.moveSpeed
            });
            AddComponent(entity, new FacingDirectionOverride
            {
                Value =  1,
            });
            AddComponent(entity, new AnimationIndexOverride());
            AddComponent(entity, new CharacterMaxHitPoints
            {
                Value = authoring.hitPoints
            });
            AddComponent(entity, new CharacterCurrentHitPoints
            {
                Value = authoring.hitPoints
            });

            AddBuffer<DamageThisFrame>(entity);
            
            AddComponent(entity, new DestroyEntityFlag());
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }

}

#endregion


#region Character Component Datas

public struct InitializeCharacterFlag : IComponentData, IEnableableComponent
{
}


public struct CharacterMoveDirection : IComponentData
{
    public float2 characterMoveDirection;
}

public struct CharacterMoveSpeed : IComponentData
{
    public float characterMoveSpeed;
}

// Component for changing the direction of where player looks
[MaterialProperty("_FacingDirection")]
public struct FacingDirectionOverride : IComponentData
{
    public float Value;
}

[MaterialProperty("_AnimationIndex")]
public struct AnimationIndexOverride : IComponentData
{
    public float Value;
}

public struct CharacterMaxHitPoints : IComponentData
{
    public int Value;
}

public struct CharacterCurrentHitPoints : IComponentData
{
    public int Value;
}

#region Buffers
public struct DamageThisFrame : IBufferElementData
{
    public int Value;
}

#endregion

#endregion


#region Character Systems

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CharacterInitializationSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (mass, shouldInitialize) 
                 in SystemAPI.Query<RefRW<PhysicsMass>, EnabledRefRW<InitializeCharacterFlag>>())
        {
            mass.ValueRW.InverseInertia = float3.zero;
            shouldInitialize.ValueRW = false;
        }
    }
}
public partial struct CharacterMoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (velocity,facingDirection, direction,characterMoveSpeed, entity) 
                 in SystemAPI.Query<RefRW<PhysicsVelocity>, RefRW<FacingDirectionOverride>, RefRO<CharacterMoveDirection>, RefRO<CharacterMoveSpeed>>().WithEntityAccess())
        {
            float2 moveStep2d = direction.ValueRO.characterMoveDirection * characterMoveSpeed.ValueRO.characterMoveSpeed ;
            velocity.ValueRW.Linear = new float3(moveStep2d, 0f);

            if (math.abs(moveStep2d.x) > 0.15f)
            {
                // If moveStep is a negative number, this returns -1; if it is a positive number, then it returns 1.
                facingDirection.ValueRW.Value = math.sign(moveStep2d.x);
            }

            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                var animationOverride = SystemAPI.GetComponentRW<AnimationIndexOverride>(entity);
                var animationType = math.lengthsq(moveStep2d) > float.Epsilon ? PlayerAnimationIndex.Movement : PlayerAnimationIndex.Idle;
                animationOverride.ValueRW.Value = (float) animationType;
            }
        }
    }
}

public partial struct GlobalTimeUpdateSystem : ISystem
{
    private static int globalTimeShaderPropertyID;

    public void OnCreate(ref SystemState state)
    {
        globalTimeShaderPropertyID = Shader.PropertyToID("_GlobalTime");
    }

    public void OnUpdate(ref SystemState state)
    {
        Shader.SetGlobalFloat(globalTimeShaderPropertyID, (float) SystemAPI.Time.ElapsedTime);
    }
}

public partial struct ProcessDamageThisFrameSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (hitPoints, damageThisFrame, entity)
                 in SystemAPI.Query<RefRW<CharacterCurrentHitPoints>, DynamicBuffer<DamageThisFrame>>().WithPresent<DestroyEntityFlag>().WithEntityAccess())
        {
            if(damageThisFrame.IsEmpty) continue;

            foreach (var damage in damageThisFrame)
            {
                hitPoints.ValueRW.Value -= damage.Value;
            }
            damageThisFrame.Clear();

            if (hitPoints.ValueRO.Value <= 0)
            {
                SystemAPI.SetComponentEnabled<DestroyEntityFlag>(entity, true);
            }
        }
    }
}
#endregion