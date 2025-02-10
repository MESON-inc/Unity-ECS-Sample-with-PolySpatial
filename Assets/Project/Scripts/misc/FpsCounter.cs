using TMPro;
using Unity.Assertions;
using UnityEngine;

public class FpsCounter : MonoBehaviour
{
    [SerializeField] private TMP_Text _fpsText;
    [SerializeField] private float _updateInterval = 0.5f;

    private float _accume = 0f;
    private int _frames = 0;
    private float _timeLeft;

    private void Start()
    {
        Assert.IsNotNull(_fpsText, "FpsCounter must have a TMP_Text component.");

        _timeLeft = _updateInterval;
    }

    private void Update()
    {
        _timeLeft -= Time.deltaTime;
        _accume += Time.timeScale / Time.deltaTime;
        ++_frames;

        if (_timeLeft <= 0.0f)
        {
            float fps = _accume / _frames;
            _fpsText.text = $"FPS: {fps:F2}";

            _timeLeft = _updateInterval;
            _accume = 0f;
            _frames = 0;
        }
    }
}