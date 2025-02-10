using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct DotParticleSystem : ISystem
{
    private EntityQuery _query;
    private BufferLookup<EmitterPositionElement> _emitterPositionBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        _emitterPositionBufferLookup = state.GetBufferLookup<EmitterPositionElement>(true);

        _query = state.GetEntityQuery(
            ComponentType.ReadOnly<MeshInstanceData>(),
            ComponentType.ReadOnly<LocalToWorld>(),
            ComponentType.Exclude<DisableRendering>());

        state.RequireForUpdate<EmitterPositionElement>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _emitterPositionBufferLookup.Update(ref state);

        Entity emitterManagerEntity = SystemAPI.GetSingletonEntity<EmitterPositionElement>();
        DynamicBuffer<EmitterPositionElement> emitterPositions = _emitterPositionBufferLookup[emitterManagerEntity];

        var job = new ParticleUpdateJob()
        {
            Time = SystemAPI.Time.ElapsedTime,
            EmitterPositions = emitterPositions,
        };
        JobHandle handle = job.ScheduleParallel(_query, state.Dependency);
        state.Dependency = handle;
    }
}

[BurstCompile]
partial struct ParticleUpdateJob : IJobEntity
{
    public double Time;
    [ReadOnly] public DynamicBuffer<EmitterPositionElement> EmitterPositions;

    private void Execute([EntityIndexInQuery] int index, ref MeshInstanceData meshData, ref LocalToWorld localToWorld)
    {
        float3 center = EmitterPositions[meshData.OriginIndex].Position;
        float3 basePosition = meshData.Position;
        float3 projectedPosition = basePosition;
        projectedPosition.y = 0;

        float rad = math.atan2(projectedPosition.z, projectedPosition.x);
        float radius = math.length(projectedPosition);
        float speed = meshData.RotateSpeed;
        float x = (float)math.cos(rad + math.PI * Time * speed) * radius;
        float z = (float)math.sin(rad + math.PI * Time * speed) * radius;
        float y = (float)math.sin(Time * meshData.Frequency) * meshData.Amplitude + basePosition.y;
        float3 newPosition = new float3(x, y, z);

        float3 position = center + newPosition;
        localToWorld.Value = float4x4.TRS(position, quaternion.identity, meshData.Scale);
    }
}