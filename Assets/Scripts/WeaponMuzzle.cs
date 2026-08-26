using System.Collections;
using UnityEngine;

// How the visible beam is drawn between the barrel and the point the shot landed on.
public enum MuzzleBeamMode
{
    // A line laid directly over the raycast: start at the muzzle, end at the impact point. Exact by
    // construction -- no particle sizing to calibrate.
    Line,
    // The old route: stretch the beam particle system by driving its Start Size Y.
    ParticleStretch
}

// A muzzle points itself at the crosshair so its attached visual effects point the right way; the
// gun (WeaponBase/its subclasses) still owns everything else -- fire rate, damage, hit detection,
// and so on.
//
// If the muzzle sits under a Turret rig, the turret does the pointing instead, and the muzzle just
// rides along on the pitch joint. That is deliberate: the turret is the thing that knows which
// orientations would bury the barrel in the hull, and a muzzle free-gimballing on top of it would
// aim straight back out of the arc the turret just clamped to.
public class WeaponMuzzle : MonoBehaviour
{
    [Header("Mechanical Settings")]
    [Tooltip("How smoothly the barrel tracks the crosshair. Ignored when a Turret above this muzzle is doing the aiming.")]
    public float aimSmoothSpeed = 15f;

    [Header("Line of Fire")]
    [Tooltip("Only used when this muzzle has no Turret above it. How far past the pivot the bore clears its own housing -- the self-check starts here, so the gun is not blocked by the wing it is embedded in.")]
    public float boreClearance = 1.5f;

    [Tooltip("Only used when this muzzle has no Turret above it. How far ahead to look for our own hull.")]
    public float selfCheckRange = 80f;

    [Tooltip("Only used when this muzzle has no Turret above it. Widens the check so grazing shots along the hull count as blocked too.")]
    public float selfCheckRadius = 0.3f;

    [Header("Visual Effects")]
    public ParticleSystem mainVisualParticles;
    public ParticleSystem muzzleFlashParticles;

    [Header("Beam")]
    [Tooltip("Line lays a beam straight over the raycast, so it always ends exactly where the shot landed. ParticleStretch instead resizes the beam particle system, which depends on its scale and Length Scale being set up to match.")]
    public MuzzleBeamMode beamMode = MuzzleBeamMode.Line;

    [Tooltip("How long the beam stays on screen, in seconds. It fades out over this time.")]
    public float beamDuration = 0.08f;

    [Tooltip("World-space thickness of the beam core. 0 copies the beam particle system's Start Size X (scaled into world units).")]
    public float beamWidth = 0f;

    [Tooltip("Thickness along the beam, sampled from muzzle (0) to impact (1) and multiplied by the widths above. A flat curve at 1 keeps an even beam; raise the right end to flare it out at the impact.")]
    public AnimationCurve beamWidthProfile = AnimationCurve.Constant(0f, 1f, 1f);

    [Tooltip("Material for the beam. Leave empty to reuse the beam particle system's own material, so the beam looks like the effect it replaces.")]
    public Material beamMaterial;

    [Tooltip("Core colour. Values above 1 (use the HDR intensity slider) push it past the scene's bloom threshold, which is what makes it read as white-hot.")]
    [ColorUsage(true, true)]
    public Color beamColor = new Color(3f, 3f, 3f, 1f);

    [Header("Glow Outline")]
    [Tooltip("Draws a second, wider, softer line behind the core so the beam has a glowing outline.")]
    public bool drawGlow = true;

    [Tooltip("Outline thickness as a multiple of the core beam width. This is the knob to turn for a thicker or thinner glow.")]
    public float glowWidthMultiplier = 3.5f;

    [Tooltip("Outline colour. Keep it dimmer and more saturated than the core -- the core reads as the hot centre, this as the halo around it.")]
    [ColorUsage(true, true)]
    public Color glowColor = new Color(0.05f, 1.2f, 2.4f, 0.55f);

    [Tooltip("Material for the outline. Leave empty to use the same material as the core.")]
    public Material glowMaterial;

    [Header("Beam Light")]
    [Tooltip("Puts real-time lights on the shot, so the beam actually lights up the ship, asteroids and anything it hits.")]
    public bool emitLight = true;

    [Tooltip("Colour of the lights the shot casts.")]
    public Color lightColor = new Color(0.2f, 0.75f, 1f, 1f);

    // Point lights fall off with the square of distance, so intensity has to be read together with
    // how far away the thing being lit is. Lighting a surface d units away as brightly as the
    // scene's sun (a directional at 2.45) takes roughly 2.45 * d*d: about 250 at 10 units, 6000 at
    // 50, 25000 at 100. That is why these numbers look enormous next to the muzzle light, which
    // only ever lights the ship's own hull a few units away.
    [Tooltip("Intensity of the light at the barrel. It only lights the ship's own hull a few units away, so it stays small.")]
    public float muzzleLightIntensity = 20f;

    [Tooltip("Intensity of the light where the shot lands. Asteroids are tens to hundreds of units across and point lights fall off with distance squared, so this needs thousands to read as a flash on the rock rather than a pinprick.")]
    public float impactLightIntensity = 4000f;

    [Tooltip("How many lights the shot can drop along its length to light up things it passes near. They are placed at the points on the beam closest to whatever it flew by, so a miss still lights the rock it skimmed. Each one costs a real-time light for the length of the shot.")]
    [Range(0, 8)]
    public int beamLightCount = 3;

    [Tooltip("How far from the beam something can be and still get lit by the shot passing it, in world units.")]
    public float beamLightSearchRadius = 250f;

    [Tooltip("How bright a passed-by surface should end up, roughly in the same units as the scene's sun (a directional at 2.45). The light's actual intensity is worked out from this and how far the rock is, so a distant near-miss lights up as clearly as a close one.")]
    public float beamLightBrightness = 2.5f;

    [Tooltip("What the shot's passing lights are allowed to notice. Leave as Everything unless something is lighting up that shouldn't.")]
    public LayerMask beamLightMask = ~0;

    [Tooltip("Cone angle of the passing lights, in degrees. They point sideways off the beam at whatever they are lighting, so the firing ship -- which sits back along the beam axis -- stays outside the cone. Widen this and the ship starts catching them.")]
    [Range(20f, 170f)]
    public float beamLightSpotAngle = 100f;

    [Tooltip("The most brightness the shot's own impact light is allowed to put back on the ship that fired it, in the same units as the scene's sun (2.45). Stops a hit on something close from washing out the hull.")]
    public float selfLightLimit = 0.6f;

    [Tooltip("Range of the barrel light, in world units. Kept short so the flash stays on the ship.")]
    public float muzzleLightRange = 15f;

    [Tooltip("Range of the impact light, in world units. This has to cover a decent slice of an asteroid to be noticeable at combat distances.")]
    public float impactLightRange = 250f;

    [Header("Particle Stretch Mode")]
    [Tooltip("Residual tuning for the computed beam length; 1 uses it as-is. Only used in ParticleStretch mode.")]
    public float beamLengthCalibration = 1f;

    // Colour is pushed through a property block rather than the line's vertex colours, because
    // vertex colours are packed into 8 bits per channel and clip at 1 -- an HDR beam set that way
    // would never get past the scene's bloom threshold. Which property to write depends on the
    // shader: the pack's particle materials are legacy additive ones driven by _TintColor.
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private ParticleSystemRenderer mainVisualRenderer;
    private Transform beamRoot;
    private LineRenderer coreLine;
    private LineRenderer glowLine;
    private MaterialPropertyBlock coreColorBlock;
    private MaterialPropertyBlock glowColorBlock;
    private int coreColorId = -1;
    private int glowColorId = -1;
    private Light muzzleLight;
    private Light impactLight;
    private Light[] travelLights;
    private Vector3[] travelLightPositions;
    private Vector3[] travelLightDirections;
    private float[] travelLightIntensities;
    private float[] travelLightRanges;
    private int activeTravelLights;

    // Reused so a shot that flies past half an asteroid field does not allocate.
    private Collider[] passedColliders;
    private const int MaxPassedColliders = 64;

    // A light sitting right on the surface it lights would need a physically silly intensity, and
    // it looks like a blown-out dot. Treating everything as at least this far away keeps it sane.
    private const float MinLitDistance = 8f;
    private float resolvedBeamWidth;
    private Coroutine beamRoutine;

    // The turret this muzzle is mounted on, if any. Null for a fixed gun bolted straight to the hull.
    private Turret turret;
    private readonly RaycastHit[] selfHits = new RaycastHit[8];

    private void Awake()
    {
        turret = GetComponentInParent<Turret>();

        if (mainVisualParticles != null)
        {
            mainVisualRenderer = mainVisualParticles.GetComponent<ParticleSystemRenderer>();
        }

        if (beamMode == MuzzleBeamMode.Line)
        {
            BuildBeamVisuals();
        }
    }

    public void AimAt(Vector3 targetPoint)
    {
        // Turreted: the rig swivels and this muzzle inherits it, arc limits and all.
        if (turret != null)
        {
            turret.Track(targetPoint);
            return;
        }

        Vector3 direction = (targetPoint - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            transform.up = Vector3.Slerp(transform.up, direction, Time.deltaTime * aimSmoothSpeed);
        }
    }

    // Whether this muzzle is allowed to shoot right now: pointed close enough to the target, and not
    // pointed through our own ship. The weapon asks before it commits to a shot, so a masked muzzle
    // costs no cooldown, no sound and no beam -- it just keeps tracking until the shot opens up.
    public bool HasClearShot()
    {
        if (turret != null) return turret.HasFiringSolution();

        // Fixed gun: no arc to check, but it can still have a wingtip in front of it. The check
        // starts past boreClearance so the housing this gun is embedded in never blocks it.
        Vector3 origin = transform.position + transform.up * boreClearance;
        int count = Physics.SphereCastNonAlloc(origin, selfCheckRadius, transform.up, selfHits,
            selfCheckRange, ~0, QueryTriggerInteraction.Ignore);

        Transform shipRoot = transform.root;
        for (int i = 0; i < count; i++)
        {
            Collider hit = selfHits[i].collider;
            if (hit != null && hit.transform.root == shipRoot) return false;
        }

        return true;
    }

    // Called once per shot with the world point the shot landed on (the hit, or the end of the
    // weapon's range when it hit nothing). The weapon raycasts from this transform, so drawing from
    // here to that point puts the visual exactly on top of the shot.
    public void FireVisuals(Vector3 impactPoint)
    {
        if (beamMode == MuzzleBeamMode.Line)
        {
            ShowLineBeam(impactPoint);
        }
        else
        {
            PlayStretchedParticleBeam(impactPoint);
        }

        // The flash is skipped when it is the same system as the beam: in Line mode that would
        // redraw the beam we just replaced, and in ParticleStretch mode restarting it would cut the
        // beam short. Give a muzzle a separate flash system if you want one.
        if (muzzleFlashParticles != null && muzzleFlashParticles != mainVisualParticles)
        {
            muzzleFlashParticles.Stop();
            muzzleFlashParticles.Play();
        }
    }

    // ---------------------------------------------------------------- line beam

    // Everything the shot draws hangs off one child object. The lines are drawn in world space (so
    // the beam stays where it was fired while the ship keeps flying) and the lights are moved by
    // world position, so this transform is only a tidy place to park them.
    private void BuildBeamVisuals()
    {
        beamRoot = new GameObject($"{gameObject.name}_Beam").transform;
        beamRoot.SetParent(transform, false);

        Material coreMaterial = ResolveBeamMaterial();
        resolvedBeamWidth = ResolveBeamWidth();

        // Sorting order keeps the wide halo behind the hot core rather than washing it out.
        if (drawGlow)
        {
            Material haloMaterial = glowMaterial != null ? glowMaterial : coreMaterial;
            glowLine = CreateLine("Glow", haloMaterial, 0);
            glowColorId = FindColorProperty(haloMaterial);
            glowColorBlock = new MaterialPropertyBlock();
        }

        coreLine = CreateLine("Core", coreMaterial, 1);
        coreColorId = FindColorProperty(coreMaterial);
        coreColorBlock = new MaterialPropertyBlock();

        if (emitLight)
        {
            muzzleLight = CreateLight("MuzzleLight", LightType.Point);
            impactLight = CreateLight("ImpactLight", LightType.Point);

            travelLights = new Light[beamLightCount];
            travelLightPositions = new Vector3[beamLightCount];
            travelLightDirections = new Vector3[beamLightCount];
            travelLightIntensities = new float[beamLightCount];
            travelLightRanges = new float[beamLightCount];
            passedColliders = new Collider[MaxPassedColliders];

            for (int i = 0; i < travelLights.Length; i++)
            {
                // Spots, not points: aiming them off the beam at what they light is what keeps the
                // firing ship out of them.
                travelLights[i] = CreateLight($"BeamLight{i}", LightType.Spot);
            }
        }
    }

    private LineRenderer CreateLine(string lineName, Material material, int sortingOrder)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(beamRoot, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.sharedMaterial = material;
        line.sortingOrder = sortingOrder;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        line.enabled = false;

        return line;
    }

    private Light CreateLight(string lightName, LightType type)
    {
        GameObject lightObject = new GameObject(lightName);
        lightObject.transform.SetParent(beamRoot, false);

        Light light = lightObject.AddComponent<Light>();
        light.type = type;
        light.color = lightColor;

        if (type == LightType.Spot)
        {
            light.spotAngle = beamLightSpotAngle;
            light.innerSpotAngle = beamLightSpotAngle * 0.7f;
        }
        // Shadows on a light that lives for 80ms buy nothing and cost a shadow map each.
        light.shadows = LightShadows.None;
        light.enabled = false;

        return light;
    }

    // Returns the colour property this shader actually has, or -1 if it has none and the beam has
    // to fall back to (clamped) vertex colours.
    private int FindColorProperty(Material material)
    {
        if (material == null) return -1;
        if (material.HasProperty(TintColorId)) return TintColorId;
        if (material.HasProperty(BaseColorId)) return BaseColorId;
        if (material.HasProperty(ColorId)) return ColorId;
        return -1;
    }

    private Material ResolveBeamMaterial()
    {
        if (beamMaterial != null) return beamMaterial;

        // Borrowing the particle system's material keeps the muzzle looking like it always did.
        if (mainVisualRenderer != null && mainVisualRenderer.sharedMaterial != null)
        {
            return mainVisualRenderer.sharedMaterial;
        }

        Shader fallback = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (fallback == null) fallback = Shader.Find("Sprites/Default");

        Debug.LogWarning($"<color=red>[Muzzle Warning]</color> Muzzle '{gameObject.name}' has no beam material and no beam particle system to borrow one from; falling back to '{(fallback != null ? fallback.name : "none")}'.");
        return fallback != null ? new Material(fallback) : null;
    }

    private float ResolveBeamWidth()
    {
        if (beamWidth > 0f) return beamWidth;

        // Match the thickness the particle beam was authored with. Start Size is not world units --
        // Hierarchy/Local scaling multiplies it by the emitter's transform scale -- so undo that.
        if (mainVisualParticles != null)
        {
            ParticleSystem.MainModule main = mainVisualParticles.main;
            float width = main.startSizeXMultiplier;

            switch (main.scalingMode)
            {
                case ParticleSystemScalingMode.Hierarchy:
                    width *= Mathf.Abs(mainVisualParticles.transform.lossyScale.x);
                    break;
                case ParticleSystemScalingMode.Local:
                    width *= Mathf.Abs(mainVisualParticles.transform.localScale.x);
                    break;
            }

            if (width > 0f) return width;
        }

        return 0.25f;
    }

    private void ShowLineBeam(Vector3 impactPoint)
    {
        if (beamRoot == null) BuildBeamVisuals();
        if (coreLine == null) return;

        // Worked out once per shot rather than per frame: one physics query, and the beam only
        // lives for a few frames anyway.
        FindPassedSurfaces(transform.position, impactPoint);

        if (beamRoutine != null) StopCoroutine(beamRoutine);
        beamRoutine = StartCoroutine(BeamRoutine(impactPoint));
    }

    private IEnumerator BeamRoutine(Vector3 impactPoint)
    {
        float duration = Mathf.Max(beamDuration, 0.01f);
        float elapsed = 0f;

        SetBeamEnabled(true);

        while (elapsed < duration)
        {
            float fade = 1f - (elapsed / duration);

            // The tail is re-read every frame so the beam stays welded to the barrel while the ship
            // moves; the head stays nailed to the point the shot actually hit.
            Vector3 start = transform.position;
            UpdateLine(coreLine, coreColorBlock, coreColorId, start, impactPoint, beamColor, resolvedBeamWidth, fade);
            UpdateLine(glowLine, glowColorBlock, glowColorId, start, impactPoint, glowColor, resolvedBeamWidth * glowWidthMultiplier, fade);
            UpdateLights(start, impactPoint, fade);

            elapsed += Time.deltaTime;
            yield return null;
        }

        SetBeamEnabled(false);
        beamRoutine = null;
    }

    private void UpdateLine(LineRenderer line, MaterialPropertyBlock colorBlock, int colorId, Vector3 start, Vector3 end, Color color, float width, float fade)
    {
        if (line == null) return;

        line.SetPosition(0, start);
        line.SetPosition(1, end);

        // Fading dims RGB as well as alpha: the beam materials are additive, and additive blending
        // ignores alpha entirely, so dropping alpha alone would leave the beam at full brightness
        // until it popped out of existence.
        Color tint = color;
        tint.r *= fade;
        tint.g *= fade;
        tint.b *= fade;
        tint.a *= fade;

        if (colorId != -1 && colorBlock != null)
        {
            // Keeps the HDR values intact all the way to the shader, which is what bloom needs.
            colorBlock.SetColor(colorId, tint);
            line.SetPropertyBlock(colorBlock);
            line.startColor = Color.white;
            line.endColor = Color.white;
        }
        else
        {
            // No colour property to write to; vertex colours it is, clamped and all.
            line.startColor = tint;
            line.endColor = tint;
        }

        // widthCurve shapes the thickness along the beam; widthMultiplier scales the whole profile.
        line.widthCurve = beamWidthProfile;
        line.widthMultiplier = width * fade;
    }

    private void UpdateLights(Vector3 start, Vector3 end, float fade)
    {
        if (!emitLight || muzzleLight == null) return;

        PlaceLight(muzzleLight, start, muzzleLightIntensity * fade, muzzleLightRange);

        // The impact light shines in every direction, so a hit on something close would flood the
        // hull. Capping it by the square of the distance to the muzzle bounds what the firing ship
        // can receive to selfLightLimit, whatever the shot hit and however close it was.
        float shotDistance = Mathf.Max(Vector3.Distance(start, end), 1f);
        float cappedImpact = Mathf.Min(impactLightIntensity, selfLightLimit * shotDistance * shotDistance);
        PlaceLight(impactLight, end, cappedImpact * fade, impactLightRange);

        // Positions and intensities were chosen when the shot was fired; only the fade moves here.
        for (int i = 0; i < activeTravelLights; i++)
        {
            Light travelLight = travelLights[i];
            PlaceLight(travelLight, travelLightPositions[i], travelLightIntensities[i] * fade, travelLightRanges[i]);
            travelLight.transform.rotation = Quaternion.LookRotation(travelLightDirections[i]);
            travelLight.spotAngle = beamLightSpotAngle;
            travelLight.innerSpotAngle = beamLightSpotAngle * 0.7f;
        }
    }

    // Picks where the shot's passing lights go. Spacing them evenly along the beam was the obvious
    // thing to do and the wrong one: on a miss the beam runs the weapon's whole range through empty
    // space, so evenly spaced lights mostly sit nowhere near anything. Instead the beam is swept for
    // what it flew close to, and a light is dropped on the beam at the closest point to each of the
    // nearest few -- so a shot that skims an asteroid lights the part it skimmed.
    private void FindPassedSurfaces(Vector3 start, Vector3 end)
    {
        activeTravelLights = 0;
        if (!emitLight || travelLights == null || travelLights.Length == 0) return;

        Vector3 beam = end - start;
        float beamLength = beam.magnitude;
        if (beamLength < 0.001f) return;

        Vector3 direction = beam / beamLength;

        int found = Physics.OverlapCapsuleNonAlloc(start, end, beamLightSearchRadius, passedColliders, beamLightMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < found; i++)
        {
            Collider candidate = passedColliders[i];
            if (candidate == null) continue;
            if (candidate.transform.root == transform.root) continue; // our own ship

            // Where along the shot it came closest, and how far off it was.
            float along = Mathf.Clamp(Vector3.Dot(candidate.bounds.center - start, direction), 0f, beamLength);
            Vector3 pointOnBeam = start + direction * along;

            Vector3 surface = SurfacePoint(candidate, pointOnBeam);
            Vector3 toSurface = surface - pointOnBeam;

            // Degenerate only if the beam is inside the collider, which means it hit and the impact
            // light already covers it.
            if (toSurface.sqrMagnitude < 0.0001f) continue;

            InsertPassedSurface(pointOnBeam, toSurface.normalized, Mathf.Max(toSurface.magnitude, MinLitDistance));
        }
    }

    // The point on a collider's surface nearest a point on the beam. The asteroids are convex mesh
    // colliders, so ClosestPoint answers properly for them; anything non-convex hands the point
    // straight back, and for those we fall back to the bounding box. Bounds alone would not do: on a
    // 300-unit rock the box is nowhere near the surface, and the light would come out too dim and
    // too short-ranged to reach it.
    private Vector3 SurfacePoint(Collider collider, Vector3 point)
    {
        Vector3 surface = collider.ClosestPoint(point);
        if ((surface - point).sqrMagnitude > 0.0001f) return surface;

        return collider.bounds.ClosestPoint(point);
    }

    // Keeps the nearest few misses, closest first, in the fixed pool of lights we have.
    private void InsertPassedSurface(Vector3 pointOnBeam, Vector3 towardSurface, float missDistance)
    {
        // Intensity is solved from the distance, because point lights fall off with distance
        // squared: a rock 200 units off the beam needs ~64x the intensity of one 25 units off to
        // look equally lit. Solving it per shot is what makes far near-misses visible at all.
        float intensity = beamLightBrightness * missDistance * missDistance;
        float range = missDistance * 3f;

        int slot = activeTravelLights;
        if (slot >= travelLights.Length)
        {
            // Pool is full: only worth keeping this one if it beats the dimmest we already have.
            int weakest = 0;
            for (int i = 1; i < activeTravelLights; i++)
            {
                if (travelLightRanges[i] > travelLightRanges[weakest]) weakest = i;
            }

            if (range >= travelLightRanges[weakest]) return;
            slot = weakest;
        }
        else
        {
            activeTravelLights++;
        }

        travelLightPositions[slot] = pointOnBeam;
        travelLightDirections[slot] = towardSurface;
        travelLightIntensities[slot] = intensity;
        travelLightRanges[slot] = range;
    }

    private void PlaceLight(Light light, Vector3 position, float intensity, float range)
    {
        if (light == null) return;

        light.transform.position = position;
        light.color = lightColor;
        light.range = range;
        light.intensity = intensity;
    }

    private void SetBeamEnabled(bool enabled)
    {
        if (coreLine != null) coreLine.enabled = enabled;
        if (glowLine != null) glowLine.enabled = enabled;
        if (muzzleLight != null) muzzleLight.enabled = enabled;
        if (impactLight != null) impactLight.enabled = enabled;

        if (travelLights != null)
        {
            // Only the ones that found something to light: a shot through empty space turns none on.
            for (int i = 0; i < travelLights.Length; i++)
            {
                if (travelLights[i] != null) travelLights[i].enabled = enabled && i < activeTravelLights;
            }
        }
    }

    private void OnDisable()
    {
        // Coroutines die with the component, so make sure a half-faded beam does not stay on screen.
        SetBeamEnabled(false);
        beamRoutine = null;
    }

    // ---------------------------------------------------------- particle stretch

    private void PlayStretchedParticleBeam(Vector3 impactPoint)
    {
        if (mainVisualParticles == null) return;

        mainVisualParticles.Stop();

        float beamLength = Vector3.Distance(mainVisualParticles.transform.position, impactPoint);
        ParticleSystem.MainModule main = mainVisualParticles.main;
        main.startSize3D = true;
        main.startSizeY = StartSizeYForWorldLength(beamLength);

        mainVisualParticles.Play();
    }

    // Converts a world-space length into the Start Size Y the particle system needs. Start Size is
    // not world units: Unity multiplies it on the way to the screen, and those multipliers are why
    // a Start Size Y set straight from the hit distance drew a beam that stopped short.
    //   * Hierarchy/Local scaling multiplies every particle's size by the emitter's transform
    //     scale. The muzzles hang off scaled ship parts (0.1 on the muzzle times ~0.63 on the gun).
    //   * A stretched billboard's length is its size along the stretch axis times the renderer's
    //     Length Scale.
    // Whether that lands exactly still depends on where the particle's pivot sits, which is why
    // Line mode exists and is the default.
    private float StartSizeYForWorldLength(float worldLength)
    {
        float worldUnitsPerSize = beamLengthCalibration;

        switch (mainVisualParticles.main.scalingMode)
        {
            case ParticleSystemScalingMode.Hierarchy:
                worldUnitsPerSize *= Mathf.Abs(mainVisualParticles.transform.lossyScale.y);
                break;
            case ParticleSystemScalingMode.Local:
                worldUnitsPerSize *= Mathf.Abs(mainVisualParticles.transform.localScale.y);
                break;
            // Shape scaling leaves particle sizes alone, so there is nothing to undo.
        }

        if (mainVisualRenderer != null && mainVisualRenderer.renderMode == ParticleSystemRenderMode.Stretch)
        {
            worldUnitsPerSize *= mainVisualRenderer.lengthScale;
        }

        if (worldUnitsPerSize <= 0.0001f)
        {
            Debug.LogWarning($"<color=red>[Muzzle Error]</color> Muzzle '{gameObject.name}' cannot size its beam: scale and Length Scale multiply out to {worldUnitsPerSize}. Falling back to an unscaled length.");
            return worldLength;
        }

        return worldLength / worldUnitsPerSize;
    }
}
