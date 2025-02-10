using UnityEngine;

public class EmitterBase : MonoBehaviour, DotParticleSpawner.IParticleAnchor
{
    public Transform Transform => transform;
}
