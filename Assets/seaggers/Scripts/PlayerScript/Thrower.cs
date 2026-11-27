using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Thrower : MonoBehaviour
{
    public GameObject throwablePrefab;
    public Transform handL;
    public Transform handR;
    public Transform aimRoot;        // Animated_Body, its forward is the facing direction
    public float throwDelay = 0.25f; // wait for the throw animation to swing before letting go
    public float throwSpeed = 10f;
    public float upSpeed = 3f;

    [Header("Owner")]
    public Health ownerHealth;       // the thrower's Health (auto-found if Thrower is under it)

    [Header("Aiming")]
    public float minAngle = 10f;        // flattest throw (degrees up)  [mouse mode]
    public float maxAngle = 70f;        // highest lob                  [mouse mode]
    public float minSpeed = 5f;         // weakest throw                [mouse mode]
    public float maxSpeed = 15f;        // strongest throw              [mouse mode]
    public float dragRange = 200f;      // pixels of drag for the full range [mouse mode]
    public float maxRange = 8f;         // farthest landing distance in meters (flat ground)

    [Header("Ammo (from the Game Design docx)")]
    public int maxAmmo = 3;              // balloons before a reload
    public float throwCooldown = 1f;     // seconds between throws
    public float reloadTime = 2f;        // INTERPRETATION: starts when ammo hits 0 and refills to full

    [Header("Mobile Skill Button (Wild Rift style)")]
    public VirtualJoystick aimStick;                 // the skill button (drag to aim, release to throw)
    public float mobileAimAngle = 45f;               // fixed launch angle (degrees up)
    public float minThrowDistance = 1.5f;            // landing distance when the drag is tiny
    public float quickCastDistance = 5f;             // landing distance for a quick tap
    public float tapTime = 0.2f;                     // press shorter than this = quick tap
    [Range(0f, 0.5f)] public float cancelThreshold = 0.15f;  // held + returned to center = cancel

    Vector2 dragStart;
    float aimAngle;
    float aimSpeed;

    [Header("Indicator")]
    public LayerMask hitMask = ~0;
    public Color indicatorColor = new Color(0.2f, 0.5f, 1f, 0.35f);
    public Color enemyImpactColor = new Color(1f, 0.73f, 0.18f);
    public int maxPoints = 40;
    public float stepTime = 0.05f;
    public float landingRadius = 0.4f;

    LineRenderer arc;
    LineRenderer ring;
    const int ringPoints = 32;
    LineRenderer rangeRing;
    const int rangePoints = 64;
    const float RangePreviewInterval = 0.05f;
    static readonly Vector3[] CircleDirections = BuildCircleDirections();
    Material indicatorMaterial;
    Vector3[] arcPositions;
    Vector3[] landingPositions;
    Vector3[] rangePositions;
    RaycastHit[] trajectoryHits;
    Collider[] splashHits;
    Collider[] ownerColliders;
    PopOnContact balloonContact;
    float balloonRadius;
    int terrainMask;
    int trajectoryMask;
    bool indicatorVisible;
    float nextRangePreview;

    GameObject held;
    bool releasing;
    int nextBalloonId;
    const float BalloonLifetime = 12f;

    // Ammo state (read by the HUD)
    int ammo;
    float nextThrowTime;
    float reloadEndTime;
    bool reloading;
    public int Ammo => ammo;
    public int MaxAmmo => maxAmmo;
    public bool IsReloading => reloading;
    public float ReloadRemaining => reloading ? Mathf.Max(0f, reloadEndTime - Time.time) : 0f;
    internal bool IsReleasing => releasing;
    internal event System.Action<int, Vector3, Vector3> BalloonReleased;
    internal event System.Action<int, Vector3, Health, IReadOnlyCollection<Health>> BalloonImpacted;

    // Mobile state
    AnimatedBodyMover mover;    // found automatically from aimRoot (used to convert stick -> world direction)
    bool usingStick;            // current aim started from the skill button (not the mouse)
    bool prevStickHeld;
    float lastStickMag;
    float pressTime;
    Vector3 stickFlatDir;       // world direction (flat) the stick points to

    bool CanStartAiming()
    {
        if (ownerHealth != null && ownerHealth.IsDead) return false;
        var st = ownerHealth != null ? ownerHealth.Status : null;
        if (st != null && !st.CanAttack) return false;   // stunned or disarmed
        return !reloading && ammo > 0 && Time.time >= nextThrowTime;
    }

    void Update()
    {
        // reload runs on its own, even during the countdown
        if (reloading && Time.time >= reloadEndTime)
        {
            reloading = false;
            ammo = maxAmmo;
        }

        // died while holding a balloon: drop it
        if (held != null && !releasing && ownerHealth != null &&
            (ownerHealth.IsDead || (ownerHealth.Status != null && !ownerHealth.Status.CanAttack))) CancelThrow();

        // Match not in "Playing" (countdown / ended): drop the balloon and ignore input
        if (!MatchManager.InputEnabled)
        {
            if (held != null && !releasing) CancelThrow();
            prevStickHeld = aimStick != null && aimStick.IsPressed;   // avoids a fake "press" when input returns
            SetIndicator(false);
            return;
        }

        bool stickHeld = aimStick != null && aimStick.IsPressed;
        bool stickDown = stickHeld && !prevStickHeld;
        bool stickUp = !stickHeld && prevStickHeld;
        prevStickHeld = stickHeld;
        Mouse mouse = Application.isMobilePlatform ? null : Mouse.current;

        // START aiming
        if ((stickDown || mouse?.rightButton.wasPressedThisFrame == true) &&
            held == null && !releasing && CanStartAiming())
        {
            Spawn();
            usingStick = stickDown;
            aimSpeed = Mathf.Lerp(minSpeed, maxSpeed, 0.5f);
            lastStickMag = 0f;
            pressTime = Time.time;

            if (usingStick)
            {
                aimAngle = mobileAimAngle;
                stickFlatDir = Flat(aimRoot.forward);      // default = where the character faces
                SetDistanceAim(minThrowDistance);
            }
            else
            {
                dragStart = mouse.position.ReadValue();
                aimAngle = Mathf.Lerp(minAngle, maxAngle, 0.5f);
            }
        }

        // WHILE aiming
        bool aiming = held != null && !releasing &&
                      (usingStick ? stickHeld : mouse?.rightButton.isPressed == true);

        if (aiming)
        {
            if (usingStick)
            {
                lastStickMag = aimStick.Value.magnitude;

                Vector3 w = mover != null ? mover.ToWorld(aimStick.Value) : Vector3.zero;
                w.y = 0f;
                if (w.sqrMagnitude > 0.0001f) stickFlatDir = w.normalized;

                // drag distance -> landing distance (like Wild Rift)
                aimAngle = mobileAimAngle;
                SetDistanceAim(Mathf.Lerp(minThrowDistance, maxRange, lastStickMag));
            }
            else
            {
                Vector2 drag = mouse.position.ReadValue() - dragStart;
                aimAngle = Mathf.Lerp(minAngle, maxAngle, Mathf.InverseLerp(-dragRange, dragRange, drag.y));
                aimSpeed = Mathf.Lerp(minSpeed, maxSpeed, Mathf.InverseLerp(0f, dragRange, Mathf.Abs(drag.x)));
                ClampRange();
            }
        }

        // RELEASE
        bool released = usingStick ? stickUp : mouse?.rightButton.wasReleasedThisFrame == true;
        if (released && held != null && !releasing)
        {
            if (usingStick && lastStickMag < cancelThreshold)
            {
                if (Time.time - pressTime <= tapTime)
                {
                    // quick tap = quick cast forward
                    aimAngle = mobileAimAngle;
                    SetDistanceAim(quickCastDistance);
                    StartCoroutine(Throw());
                }
                else
                {
                    CancelThrow();     // held, then returned to center = cancel
                }
            }
            else
            {
                StartCoroutine(Throw());
            }
        }

        SetIndicator(aiming);
        if (aiming) UpdateIndicator();
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    // Direction the balloon will be launched in, for a given angle (degrees up)
    Vector3 GetLaunchDir(float angle)
    {
        Vector3 f = usingStick ? stickFlatDir : aimRoot.forward;
        Vector3 r = usingStick ? Vector3.Cross(Vector3.up, f) : aimRoot.right;
        return Quaternion.AngleAxis(-angle, r) * f;
    }

    // Finds the launch speed so the balloon lands EXACTLY 'distance' meters away at the current angle
    void SetDistanceAim(float distance)
    {
        Vector3 origin = (handL.position + handR.position) / 2f;
        Vector3 target = origin + stickFlatDir * distance;

        // ground height at the target (fallback: height of the character root)
        float groundY = aimRoot.position.y;
        if (Physics.Raycast(target + Vector3.up * 5f, Vector3.down, out RaycastHit h, 30f, terrainMask, QueryTriggerInteraction.Ignore)
            && h.point.y < origin.y)
            groundY = h.point.y;

        float dy = groundY - origin.y;                       // negative = target is below the hand
        float g = Physics.gravity.magnitude;
        float th = aimAngle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(th);
        float denom = 2f * cos * cos * (distance * Mathf.Tan(th) - dy);

        aimSpeed = denom > 0.001f ? Mathf.Sqrt(g * distance * distance / denom) : maxSpeed;
    }

    void CancelThrow()
    {
        if (held != null) Destroy(held);
        held = null;
    }

    void Awake()
    {
        if (aimRoot != null) mover = aimRoot.GetComponent<AnimatedBodyMover>();
        if (ownerHealth == null) ownerHealth = GetComponentInParent<Health>();
        ammo = maxAmmo;
        trajectoryMask = hitMask;
        if (throwablePrefab != null)
        {
            balloonContact = throwablePrefab.GetComponent<PopOnContact>();
            if (balloonContact != null) trajectoryMask &= ~balloonContact.ignoreLayers.value;
            if (throwablePrefab.TryGetComponent(out SphereCollider sphere))
            {
                Vector3 scale = sphere.transform.lossyScale;
                balloonRadius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            }
        }
        terrainMask = trajectoryMask & ~LayerMask.GetMask("Ragdoll", "RagdollLegs");
        if (aimStick != null && mover == null)
            Debug.LogWarning("Thrower: AnimatedBodyMover not found on aimRoot. Mobile aiming needs it.");
    }

    void CreateIndicator()
    {
        indicatorMaterial = new Material(Shader.Find("Sprites/Default"));
        arc = MakeLine("TrajectoryArc", indicatorMaterial, 0.08f, false);
        ring = MakeLine("LandingRing", indicatorMaterial, 0.06f, true);
        ring.positionCount = ringPoints;
        rangeRing = MakeLine("RangeRing", indicatorMaterial, 0.05f, true);
        rangeRing.positionCount = rangePoints;
        arcPositions = new Vector3[Mathf.Max(0, maxPoints) + 1];
        landingPositions = new Vector3[ringPoints];
        rangePositions = new Vector3[rangePoints];
        splashHits = new Collider[32];
    }

    void OnDisable()
    {
        SetIndicator(false);
    }

    void OnDestroy()
    {
        if (indicatorMaterial != null) Destroy(indicatorMaterial);
    }

    static Vector3[] BuildCircleDirections()
    {
        var directions = new Vector3[rangePoints];
        for (int i = 0; i < directions.Length; i++)
        {
            float angle = i * (2f * Mathf.PI / rangePoints);
            directions[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }
        return directions;
    }

    LineRenderer MakeLine(string lineName, Material mat, float width, bool loop)
    {
        var go = new GameObject(lineName);
        go.transform.SetParent(transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.startColor = lr.endColor = indicatorColor;
        lr.startWidth = lr.endWidth = width;
        lr.loop = loop;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    void SetIndicator(bool on)
    {
        if (indicatorVisible == on) return;
        if (arc == null) CreateIndicator();
        indicatorVisible = on;
        if (on) nextRangePreview = 0f;

        arc.enabled = on;
        ring.enabled = false;
        rangeRing.enabled = on;
    }

    void UpdateIndicator()
    {
        Vector3 center = (handL.position + handR.position) / 2f;
        if (arcPositions.Length <= maxPoints) arcPositions = new Vector3[maxPoints + 1];
        Vector3 velocity = GetLaunchDir(aimAngle) * aimSpeed;
        Vector3 end = TraceTrajectory(center, velocity, arcPositions, out int pointCount, out RaycastHit impact);
        arc.positionCount = pointCount;
        arc.SetPositions(arcPositions);
        UpdateImpactMarker(impact, end);

        // The arc still follows the aim every frame. The ground range outline can update less often.
        float now = Time.unscaledTime;
        if (now < nextRangePreview) return;
        nextRangePreview = now + RangePreviewInterval;
        for (int i = 0; i < rangePoints; i++)
        {
            Vector3 p = center + CircleDirections[i] * maxRange;
            Vector3 origin = p + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit h, 30f, terrainMask, QueryTriggerInteraction.Ignore))
                p.y = h.point.y + 0.03f;
            rangePositions[i] = p;
        }
        rangeRing.SetPositions(rangePositions);
    }

    void UpdateImpactMarker(RaycastHit hit, Vector3 end)
    {
        ring.enabled = hit.collider != null;
        if (!ring.enabled) return;

        Vector3 normal = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : Vector3.up;
        Vector3 point = hit.distance > 0f ? hit.point : end - normal * balloonRadius;
        Quaternion surface = Quaternion.FromToRotation(Vector3.up, normal);
        for (int i = 0; i < ringPoints; i++)
        {
            Vector3 offset = CircleDirections[i * (rangePoints / ringPoints)] * landingRadius;
            landingPositions[i] = point + normal * 0.03f + surface * offset;
        }

        Color color = CanHitEnemy(hit.collider, point) ? enemyImpactColor : indicatorColor;
        color.a = 1f;
        ring.startColor = ring.endColor = color;
        ring.SetPositions(landingPositions);
    }

    bool CanHitEnemy(Collider directHit, Vector3 point)
    {
        if (IsEnemy(directHit)) return true;
        if (balloonContact == null || balloonContact.pushRadius <= 0f) return false;

        float radius = balloonContact.pushRadius;
        var contacts = splashHits;
        int count = Physics.OverlapSphereNonAlloc(point, radius, contacts, ~0, QueryTriggerInteraction.Ignore);
        if (count == contacts.Length)
        {
            contacts = Physics.OverlapSphere(point, radius, ~0, QueryTriggerInteraction.Ignore);
            count = contacts.Length;
        }
        for (int i = 0; i < count; i++)
            if (IsEnemy(contacts[i])) return true;
        return false;
    }

    bool IsEnemy(Collider collider)
    {
        Health target = collider.GetComponentInParent<Health>();
        return target != null && target != ownerHealth && !target.IsDead && !target.IsInvulnerable;
    }

    void ClampRange()
    {
        Vector3 start = (handL.position + handR.position) / 2f;
        Vector3 direction = GetLaunchDir(aimAngle);
        if (Reach(start, direction, aimSpeed) <= maxRange) return;

        float lo = 0f, hi = aimSpeed;
        for (int i = 0; i < 12; i++)
        {
            float mid = (lo + hi) / 2f;
            if (Reach(start, direction, mid) <= maxRange) lo = mid; else hi = mid;
        }
        aimSpeed = lo;
    }

    float Reach(Vector3 start, Vector3 direction, float speed)
    {
        Vector3 end = TraceTrajectory(start, direction * speed, null, out _, out _);
        return Vector3.ProjectOnPlane(end - start, Vector3.up).magnitude;
    }

    Vector3 TraceTrajectory(Vector3 start, Vector3 vel, Vector3[] points, out int count, out RaycastHit impact)
    {
        Vector3 gravityStep = Physics.gravity * stepTime;
        Vector3 halfGravityStep = gravityStep * (0.5f * stepTime);
        Vector3 pos = start;
        count = 1;
        impact = default;
        if (points != null) points[0] = pos;

        for (int i = 0; i < maxPoints; i++)
        {
            Vector3 step = vel * stepTime + halfGravityStep;
            vel += gravityStep;
            float distance = step.magnitude;
            if (distance > 0f && TryGetImpact(pos, step / distance, distance, out impact))
            {
                pos += step * (impact.distance / distance);
                if (points != null) points[count++] = pos;
                break;
            }
            pos += step;
            if (points != null) points[count++] = pos;
        }

        return pos;
    }

    bool TryGetImpact(Vector3 start, Vector3 direction, float distance, out RaycastHit closest)
    {
        trajectoryHits ??= new RaycastHit[16];
        var hits = trajectoryHits;
        int count = balloonRadius > 0f
            ? Physics.SphereCastNonAlloc(start, balloonRadius, direction, hits, distance, trajectoryMask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(start, direction, hits, distance, trajectoryMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length)
        {
            hits = balloonRadius > 0f
                ? Physics.SphereCastAll(start, balloonRadius, direction, distance, trajectoryMask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastAll(start, direction, distance, trajectoryMask, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }

        closest = default;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.distance >= nearest || (ownerHealth != null && hit.transform.IsChildOf(ownerHealth.transform))) continue;
            closest = hit;
            nearest = hit.distance;
        }
        return closest.collider != null;
    }

    void Spawn()
    {
        held = Instantiate(throwablePrefab, (handL.position + handR.position) / 2f, Quaternion.identity);

        var rb = held.GetComponent<Rigidbody>();
        rb.isKinematic = true;

        var col = held.GetComponent<Collider>();
        col.enabled = false;   // so it doesn't push the arm while held

        var pop = held.GetComponent<PopOnContact>();
        if (pop != null) pop.owner = ownerHealth;   // so the balloon knows who threw it
    }

    void LateUpdate()
    {
        if (held != null)
            held.transform.position = (handL.position + handR.position) / 2f;
    }

    IEnumerator Throw()
    {
        float angle = aimAngle;
        float speed = aimSpeed;

        // use up one balloon + start the throw cooldown (and the reload when empty)
        ammo--;
        nextThrowTime = Time.time + throwCooldown;
        if (ammo <= 0)
        {
            reloading = true;
            reloadEndTime = Time.time + reloadTime;
        }

        // Skill-button throws lock the direction at release (matches the indicator exactly)
        bool stickThrow = usingStick;
        Vector3 lockedDir = stickThrow ? GetLaunchDir(angle) : Vector3.zero;

        releasing = true;
        GameObject obj = held;
        mover?.PlayThrow();

        yield return new WaitForSeconds(throwDelay);
        held = null;

        Vector3 dir = stickThrow ? lockedDir : Quaternion.AngleAxis(-angle, aimRoot.right) * aimRoot.forward;
        Vector3 velocity = dir * speed;
        int shot = nextBalloonId++ % 256;
        if (obj.TryGetComponent(out PopOnContact pop) && BalloonImpacted != null)
            pop.ImpactReported = (point, directHit, targets) => BalloonImpacted?.Invoke(shot, point, directHit, targets);
        BalloonReleased?.Invoke(shot, obj.transform.position, velocity);
        Launch(obj, velocity);
        releasing = false;
    }

    public void ReplayThrow(Vector3 position, Vector3 velocity)
    {
        var balloon = Instantiate(throwablePrefab, position, Quaternion.identity);
        balloon.GetComponent<Collider>().enabled = false;
        var pop = balloon.GetComponent<PopOnContact>();
        if (pop != null) pop.owner = ownerHealth;
        Launch(balloon, velocity);
    }

    internal GameObject ShowRemoteBalloon(Vector3 position, Vector3 velocity)
    {
        var balloon = Instantiate(throwablePrefab, position, Quaternion.identity);
        balloon.GetComponent<Collider>().enabled = false;
        if (balloon.TryGetComponent(out PopOnContact pop)) pop.owner = ownerHealth;
        balloon.GetComponent<Rigidbody>().linearVelocity = velocity;
        Destroy(balloon, BalloonLifetime);
        return balloon;
    }

    internal bool ShowRemoteImpact(Vector3 point, Health victim, bool directHit)
    {
        if (balloonContact == null) return false;

        balloonContact.ShowImpact(point, ownerHealth);
        return victim != null && balloonContact.ApplyHit(victim, point, directHit, ownerHealth);
    }

    void Launch(GameObject balloon, Vector3 velocity)
    {
        balloon.transform.SetParent(null);
        Destroy(balloon, BalloonLifetime);
        var body = balloon.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = velocity;
        var collider = balloon.GetComponent<Collider>();
        collider.enabled = true;
        if (ownerHealth == null) return;

        ownerColliders ??= ownerHealth.GetComponentsInChildren<Collider>(true);
        foreach (Collider part in ownerColliders)
            if (part != null && part.enabled && part.gameObject.activeInHierarchy)
                Physics.IgnoreCollision(collider, part);
    }
}
