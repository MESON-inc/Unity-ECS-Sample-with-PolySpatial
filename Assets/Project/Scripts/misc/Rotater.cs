using UnityEngine;

public class Rotater : MonoBehaviour
{
    [SerializeField] private float _speed = 3f;

    private void Update()
    {
        transform.Rotate(Vector3.up, Time.deltaTime * _speed);
    }
}