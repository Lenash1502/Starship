using System.Collections;
using UnityEngine;

// Added at runtime to a freshly-spawned death effect instance so it can time its own destruction
// off its ParticleSystem's actual finish time. Must live on the effect itself rather than on
// whatever died to spawn it -- that object is destroyed the same frame, which would cancel a
// coroutine hosted there before it ever got to run.
public class DeathEffectCleanup : MonoBehaviour
{
    public void Begin(ParticleSystem vfx, float fallbackLifetime)
    {
        if (vfx != null) StartCoroutine(DestroyWhenFinished(vfx));
        else Destroy(gameObject, fallbackLifetime);
    }

    private IEnumerator DestroyWhenFinished(ParticleSystem vfx)
    {
        yield return new WaitWhile(() => vfx != null && vfx.IsAlive(true));
        if (this != null) Destroy(gameObject);
    }
}
