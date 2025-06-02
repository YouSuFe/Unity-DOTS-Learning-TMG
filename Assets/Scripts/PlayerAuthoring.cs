using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

#region Authoring

public class PlayerAuthoring : MonoBehaviour
{
    public class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerTag());
            AddComponent(entity, new InitializeCameraTargetTag());
            AddComponent(entity, new CameraTarget());
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

#endregion