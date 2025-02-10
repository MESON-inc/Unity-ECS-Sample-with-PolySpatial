using Unity.Entities;
using Unity.Mathematics;

public struct MeshInstanceData : IComponentData
{
    public int OriginIndex;
    public float3 Position;
    public float3 Scale;
    public float Amplitude;
    public float Frequency;
    public float RotateSpeed;
}