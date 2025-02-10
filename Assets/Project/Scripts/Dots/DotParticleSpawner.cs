using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>
/// DOTS を用いてパーティクルを表現するクラス
/// </summary>
public class DotParticleSpawner : MonoBehaviour
{
    #region ### ------------------------------ Relative Types ------------------------------ ###

    /// <summary>
    /// パーティクルの発生位置を示すインターフェース
    /// </summary>
    public interface IParticleAnchor
    {
        Transform Transform { get; }
    }

    public struct EmitParameter
    {
        public int Count;
        public float Radius;
        public float Scale;
        public Vector2 RotateSpeedRange;
        public Vector2 AmplitudeRange;
        public Vector2 FrequencyRange;
        public ColorTheme ColorTheme;
    }

    public class ColorTheme
    {
        private Color[] _colors;

        /// <summary>
        /// 引数なしコンストラクタを非公開
        /// </summary>
        private ColorTheme()
        {
        }

        private static readonly Color[] s_singleColor = { Color.white };

        public ColorTheme(Color[] colors)
        {
            _colors = (colors?.Length > 0)
                ? colors
                : s_singleColor;
        }

        public Color GetRandomNext()
        {
            int index = Random.Range(0, _colors.Length);
            return _colors[index];
        }
    }

    /// <summary>
    /// NOTE: この構造体自体がパーティクル Entity を持つのではなく、
    ///       ハンドル ID などを持って管理できるように変更したい
    /// </summary>
    public struct EmittedParticleInfo
    {
        public int Index;
        public IReadOnlyList<Entity> Particles;
    }

    private struct EmitterOriginInfo
    {
        public int Index;
        public IParticleAnchor Anchor;
    }

    private struct OriginIndexInfo : IEquatable<OriginIndexInfo>
    {
        public int Index;

        public static OriginIndexInfo Null => new OriginIndexInfo { Index = -1 };

        public override bool Equals(object obj)
        {
            return obj is OriginIndexInfo info && Equals(info);
        }

        public bool Equals(OriginIndexInfo other)
        {
            return Index == other.Index;
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        public static bool operator ==(OriginIndexInfo left, OriginIndexInfo right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(OriginIndexInfo left, OriginIndexInfo right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// パーティクルの発生源の管理を行う
    ///
    /// 今空いているインデックスの管理や、値の更新なども行う
    /// </summary>
    private class EmitterOriginManager
    {
        private EntityManager _entityManager;
        private Entity _emitterManagerEntity;
        private List<EmitterOriginInfo> _emitterOriginInfoList;

        private Queue<OriginIndexInfo> _availableIndices;
        private HashSet<OriginIndexInfo> _activeIndices;

        // 今回は 100 個もあれば問題ない
        // NOTE: もし必要なるようなら可変長に調整する
        private static readonly int s_capacity = 100;

        public EmitterOriginManager(EntityManager entityManager)
        {
            _entityManager = entityManager;

            _availableIndices = new Queue<OriginIndexInfo>(s_capacity);
            _activeIndices = new HashSet<OriginIndexInfo>(s_capacity);
            _emitterOriginInfoList = new List<EmitterOriginInfo>(s_capacity);

            CreateSingletonEntity();

            for (int i = 0; i < s_capacity; i++)
            {
                _availableIndices.Enqueue(new OriginIndexInfo { Index = i });
            }
        }

        /// <summary>
        /// 保持している Origin の位置を更新する
        /// </summary>
        public void UpdateOrigins()
        {
            // パーティクル発生源位置の更新
            DynamicBuffer<EmitterPositionElement> emitterPositions = _entityManager.GetBuffer<EmitterPositionElement>(_emitterManagerEntity);
            foreach (EmitterOriginInfo originInfo in _emitterOriginInfoList)
            {
                emitterPositions[originInfo.Index] = new EmitterPositionElement { Position = originInfo.Anchor.Transform.position };
            }
        }

        public OriginIndexInfo AddOrigin(IParticleAnchor anchor)
        {
            if (_availableIndices.Count == 0)
            {
                Debug.LogError($"<<<{nameof(EmitterOriginManager)}>>> No available index");
                return OriginIndexInfo.Null;
            }

            OriginIndexInfo indexInfo = _availableIndices.Dequeue();
            _activeIndices.Add(indexInfo);

            _emitterOriginInfoList.Add(new EmitterOriginInfo()
            {
                Index = indexInfo.Index,
                Anchor = anchor,
            });

            return indexInfo;
        }

        public void RemoveOrigin(OriginIndexInfo originIndexInfo)
        {
            int removeTarget = -1;
            for (int i = 0; i < _emitterOriginInfoList.Count; i++)
            {
                if (_emitterOriginInfoList[i].Index == originIndexInfo.Index)
                {
                    removeTarget = i;
                    break;
                }
            }

            if (removeTarget == -1)
            {
                Debug.LogWarning($"<<<{nameof(EmitterOriginManager)}>>> Target index is not found.");
                return;
            }

            _emitterOriginInfoList.RemoveAt(removeTarget);

            _activeIndices.Remove(originIndexInfo);
            _availableIndices.Enqueue(originIndexInfo);
        }

        /// <summary>
        /// パーティクル発生位置を保持するエンティティを作成する
        /// </summary>
        /// <remarks>
        /// シングルトンとして扱うため、複数生成しない
        /// </remarks>
        private void CreateSingletonEntity()
        {
            if (_emitterManagerEntity != Entity.Null)
            {
                return;
            }

            _emitterManagerEntity = _entityManager.CreateEntity();
            _entityManager.AddBuffer<EmitterPositionElement>(_emitterManagerEntity);

            DynamicBuffer<EmitterPositionElement> emitterPositions = _entityManager.GetBuffer<EmitterPositionElement>(_emitterManagerEntity);
            emitterPositions.Capacity = s_capacity;

            for (int i = 0; i < s_capacity; i++)
            {
                emitterPositions.Add(new EmitterPositionElement());
            }
        }
    }

    #endregion ### ------------------------------ Relative Types ------------------------------ ###

    [SerializeField] private int _poolSize = 1000;
    [SerializeField] private Material _material;
    [SerializeField] private Mesh _mesh;

    // Growth factor for pool expansion
    private const float k_PoolGrowthFactor = 1.5f;

    private Queue<Entity> _availableParticles;
    private HashSet<Entity> _activeParticles;

    private IReadOnlyList<Entity> _emptyList = new List<Entity>();

    private World _world;
    private EntityManager _entityManager;
    private EmitterOriginManager _emitterOriginManager;

    #region ### ------------------------------ MonoBehaviour ------------------------------ ###

    private void Awake()
    {
        Initialize();
    }

    private void Update()
    {
        _emitterOriginManager.UpdateOrigins();
    }

    #endregion ### ------------------------------ MonoBehaviour ------------------------------ ###

    private void Initialize()
    {
        Debug.Log($"<<<{nameof(ParticleSystem)}>>> Initialize...");

        _world = World.DefaultGameObjectInjectionWorld;
        _entityManager = _world.EntityManager;

        _availableParticles = new Queue<Entity>();
        _activeParticles = new HashSet<Entity>();

        _emitterOriginManager = new EmitterOriginManager(_entityManager);

        for (int i = 0; i < _poolSize; i++)
        {
            Entity entity = CreateEntity();
            _availableParticles.Enqueue(entity);
        }
    }

    /// <summary>
    /// EmitParameter を元にパーティクルを生成する
    /// </summary>
    /// <param name="emitParameter">生成するパーティクルのデータ</param>
    /// <returns>生成されたパーティクル情報</returns>
    public EmittedParticleInfo Emit(IParticleAnchor anchor, EmitParameter emitParameter)
    {
        if (_availableParticles.Count < emitParameter.Count)
        {
            int needed = emitParameter.Count - _availableParticles.Count;
            int expandAmount = Mathf.Max(needed, Mathf.CeilToInt(_availableParticles.Count * k_PoolGrowthFactor) - _availableParticles.Count);
            ExpandPool(expandAmount);
        }

        OriginIndexInfo originIndexInfo = _emitterOriginManager.AddOrigin(anchor);

        List<Entity> particles = new List<Entity>();

        for (int i = 0; i < emitParameter.Count; i++)
        {
            Entity entity = _availableParticles.Dequeue();
            EnableEntity(entity);
            UpdateEntity(entity, emitParameter, originIndexInfo);
            _activeParticles.Add(entity);
            particles.Add(entity);
        }

        return new EmittedParticleInfo()
        {
            Index = originIndexInfo.Index,
            Particles = particles,
        };
    }

    public void ReturnParticles(EmittedParticleInfo emittedParticleInfo)
    {
        if (emittedParticleInfo.Particles == null) return;

        foreach (Entity entity in emittedParticleInfo.Particles)
        {
            DisableEntity(entity);
            _availableParticles.Enqueue(entity);
            _activeParticles.Remove(entity);
        }

        _emitterOriginManager.RemoveOrigin(new OriginIndexInfo()
        {
            Index = emittedParticleInfo.Index,
        });
    }

    public void ShowParticles(EmittedParticleInfo emittedParticleInfo)
    {
        foreach (Entity entity in emittedParticleInfo.Particles)
        {
            EnableEntity(entity);
        }
    }

    public void HideParticles(EmittedParticleInfo emittedParticleInfo)
    {
        foreach (Entity entity in emittedParticleInfo.Particles)
        {
            DisableEntity(entity);
        }
    }

    private Entity CreateEntity()
    {
        Entity entity = _entityManager.CreateEntity();

        RenderFilterSettings filterSettings = RenderFilterSettings.Default;
        filterSettings.ShadowCastingMode = ShadowCastingMode.Off;
        filterSettings.ReceiveShadows = false;

        RenderMeshArray renderMeshArray = new RenderMeshArray(new[] { _material }, new[] { _mesh });
        RenderMeshDescription renderMeshDescription = new RenderMeshDescription()
        {
            FilterSettings = filterSettings,
            LightProbeUsage = LightProbeUsage.Off,
        };

        RenderMeshUtility.AddComponents(
            entity,
            _entityManager,
            renderMeshDescription,
            renderMeshArray,
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));

        _entityManager.AddComponentData(entity, new MeshInstanceData());
        _entityManager.AddComponentData(entity, new ColorData());

        DisableEntity(entity);

        return entity;
    }

    private void UpdateEntity(Entity entity, EmitParameter emitParameter, OriginIndexInfo originIndexInfo)
    {
        MeshInstanceData meshInstanceData = _entityManager.GetComponentData<MeshInstanceData>(entity);
        Vector3 p = Random.insideUnitSphere.normalized * emitParameter.Radius;
        float3 position = new float3(p.x, p.y, p.z);
        float3 scale = new float3(emitParameter.Scale);
        meshInstanceData.OriginIndex = originIndexInfo.Index;
        meshInstanceData.Position = position;
        meshInstanceData.Scale = scale;
        meshInstanceData.RotateSpeed = Random.Range(emitParameter.RotateSpeedRange.x, emitParameter.RotateSpeedRange.y);
        meshInstanceData.Amplitude = Random.Range(emitParameter.AmplitudeRange.x, emitParameter.AmplitudeRange.y);
        meshInstanceData.Frequency = Random.Range(emitParameter.FrequencyRange.x, emitParameter.FrequencyRange.y);
        _entityManager.SetComponentData(entity, meshInstanceData);

        ColorData colorData = _entityManager.GetComponentData<ColorData>(entity);
        Color c = emitParameter.ColorTheme.GetRandomNext();
        float4 value = new float4(c.r, c.g, c.b, 1.0f);
        colorData.Value = value;
        _entityManager.SetComponentData(entity, colorData);
    }

    private void DisableEntity(Entity entity)
    {
        if (!_entityManager.HasComponent<DisableRendering>(entity))
        {
            _entityManager.AddComponent<DisableRendering>(entity);
        }
    }

    private void EnableEntity(Entity entity)
    {
        if (_entityManager.HasComponent<DisableRendering>(entity))
        {
            _entityManager.RemoveComponent<DisableRendering>(entity);
        }
    }

    /// <summary>
    /// パーティクルプールを指定した数だけ拡張する
    /// </summary>
    /// <param name="additionalCount">追加するパーティクル数</param>
    private void ExpandPool(int additionalCount)
    {
        for (int i = 0; i < additionalCount; i++)
        {
            Entity entity = CreateEntity();
            _availableParticles.Enqueue(entity);
        }
    }
}