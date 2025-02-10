using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

public class ParticleTester : MonoBehaviour
{
    [SerializeField] private DotParticleSpawner _spawner;
    [SerializeField] private Button _emitButton;
    [SerializeField] private Button _deleteButton;
    [SerializeField] private GameObject _emitterBasePrefab;
    [SerializeField] private Color[] _colorTheme;
    [SerializeField] private int _particleEmitterCount = 500;
    [SerializeField] private float _particleScale = 0.01f;
    [SerializeField] private float _particleSpehereRadius = 1.0f;
    [SerializeField] private Vector2 _rotateSpeedRange = new Vector2(0.05f, 0.1f);
    [SerializeField] private Vector2 _amplitudeRange = new Vector2(0.05f, 0.08f);
    [SerializeField] private Vector2 _frequencyRange = new Vector2(0.5f, 1.5f);

    private Dictionary<GameObject, DotParticleSpawner.EmittedParticleInfo> _emitterBaseToInfoMap = new Dictionary<GameObject, DotParticleSpawner.EmittedParticleInfo>();

    private void Awake()
    {
        _emitButton.onClick.AddListener(HandleEmitButtonClicked);
        _deleteButton.onClick.AddListener(HandleDeleteButtonClicked);
    }

    private void HandleEmitButtonClicked()
    {
        Emit();
    }

    private void HandleDeleteButtonClicked()
    {
        DeleteTopEmitter();
    }

    private void Emit()
    {
        Vector3 randomPos = transform.position + Random.insideUnitSphere * 2;
        GameObject emitterBase = Instantiate(_emitterBasePrefab, randomPos, Quaternion.identity);
        DotParticleSpawner.IParticleAnchor anchor = emitterBase.GetComponent<DotParticleSpawner.IParticleAnchor>();
        Assert.IsNotNull(anchor, $"{_emitterBasePrefab.name} prefab must have a component that implements {nameof(DotParticleSpawner.IParticleAnchor)} interface");
        DotParticleSpawner.EmittedParticleInfo info = _spawner.Emit(anchor, new DotParticleSpawner.EmitParameter()
        {
            Count = _particleEmitterCount,
            Radius = _particleSpehereRadius,
            Scale = _particleScale,
            RotateSpeedRange = _rotateSpeedRange,
            AmplitudeRange = _amplitudeRange,
            FrequencyRange = _frequencyRange,
            ColorTheme = new DotParticleSpawner.ColorTheme(_colorTheme),
        });
        
        _emitterBaseToInfoMap.Add(emitterBase, info);
    }

    private void DeleteTopEmitter()
    {
        if (_emitterBaseToInfoMap.Count == 0) return;
        
        var item = _emitterBaseToInfoMap.First();
        
        _spawner.ReturnParticles(item.Value);
        Destroy(item.Key);
    }
}