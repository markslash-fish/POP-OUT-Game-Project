using System.Collections;
using UnityEngine;

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
    public LayerMask hitMask = ~0;      // layers the arc treats as ground
    public Color indicatorColor = new Color(0.2f, 0.5f, 1f, 0.35f);
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
    bool indicatorVisible;
    float nextRangePreview;

    GameObject held;
    bool releasing;

    // Ammo state (read by the HUD)
    int ammo;
    float nextThrowTime;
    float reloadEndTime;
    bool reloading;
    public int Ammo => ammo;
    public int MaxAmmo => maxAmmo;
    public bool IsReloading => reloading;
    public float ReloadRemaining => reloading ? Mathf.Max(0f, reloadEndTime - Time.time) : 0f;
    public event System.Action<Vector3, Vector3, float> BalloonReleased;

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

        // START aiming
        if ((Input.GetMouseButtonDown(1) || stickDown) && held == null && !releasing && CanStartAiming())
        {
            Spawn();
            usingStick = stickDown;
            dragStart = Input.mousePosition;
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
                aimAngle = Mathf.Lerp(minAngle, maxAngle, 0.5f);
            }
        }

        // WHILE aiming
        bool aiming = held != null && !releasing &&
                      (usingStick ? stickHeld : Input.GetMouseButton(1));

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
                Vector2 drag = (Vector2)Input.mousePosition - dragStart;
                aimAngle = Mathf.Lerp(minAngle, maxAngle, Mathf.InverseLerp(-dragRange, dragRange, drag.y));
                aimSpeed = Mathf.Lerp(minSpeed, maxSpeed, Mathf.InverseLerp(0f, dragRange, Mathf.Abs(drag.x)));
                ClampRange();
            }
        }

        // RELEASE
        bool released = usingStick ? stickUp : Input.GetMouseButtonUp(1);
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
        if (Physics.Raycast(target + Vector3.up * 5f, Vector3.down, out RaycastHit h, 30f, hitMask, QueryTriggerInteraction.Ignore)
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
        ring.enabled = on;
        rangeRing.enabled = on;
    }

    void UpdateIndicator()
    {
        Vector3 center = (handL.position + handR.position) / 2f;
        Vector3 pos = center;
        Vector3 vel = GetLaunchDir(aimAngle) * aimSpeed;
        Vector3 gravityStep = Physics.gravity * stepTime;
        Vector3 halfGravityStep = gravityStep * (0.5f * stepTime);

        if (arcPositions.Length <= maxPoints) arcPositions = new Vector3[maxPoints + 1];
        int pointCount = 1;
        arcPositions[0] = pos;
        Vector3 landing = pos;

        for (int i = 0; i < maxPoints; i++)
        {
            Vector3 next = pos + vel * stepTime + halfGravityStep;
            vel += gravityStep;

            if (Physics.Linecast(pos, next, out RaycastHit hit, hitMask, QueryTriggerInteraction.Ignore))
            {
                arcPositions[pointCount++] = hit.point;
                landing = hit.point;
                break;
            }

            arcPositions[pointCount++] = next;
            pos = next;
            landing = next;
        }

        arc.positionCount = pointCount;
        arc.SetPositions(arcPositions);

        landing.y += 0.03f;
        for (int i = 0; i < ringPoints; i++)
            landingPositions[i] = landing + CircleDirections[i * (rangePoints / ringPoints)] * landingRadius;
        ring.SetPositions(landingPositions);

        // The arc still follows the aim every frame. The ground range outline can update less often.
        float now = Time.unscaledTime;
        if (now < nextRangePreview) return;
        nextRangePreview = now + RangePreviewInterval;
        for (int i = 0; i < rangePoints; i++)
        {
            Vector3 p = center + CircleDirections[i] * maxRange;
            Vector3 origin = p + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit h, 30f, hitMask, QueryTriggerInteraction.Ignore))
                p.y = h.point.y + 0.03f;
            rangePositions[i] = p;
        }
        rangeRing.SetPositions(rangePositions);
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
        Vector3 vel = direction * speed;
        Vector3 gravityStep = Physics.gravity * stepTime;
        Vector3 halfGravityStep = gravityStep * (0.5f * stepTime);
        Vector3 pos = start;

        for (int i = 0; i < maxPoints; i++)
        {
            Vector3 next = pos + vel * stepTime + halfGravityStep;
            vel += gravityStep;

            if (Physics.Linecast(pos, next, out RaycastHit hit, hitMask, QueryTriggerInteraction.Ignore))
            {
                pos = hit.point;
                break;
            }
            pos = next;
        }

        Vector3 flat = pos - start;
        flat.y = 0f;
        return flat.magnitude;
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

        yield return new WaitForSeconds(throwDelay);
        held = null;

        Vector3 dir = stickThrow ? lockedDir : Quaternion.AngleAxis(-angle, aimRoot.right) * aimRoot.forward;
        Vector3 velocity = dir * speed;
        BalloonReleased?.Invoke(obj.transform.position, velocity, aimRoot.eulerAngles.y);
        yield return Launch(obj, velocity);
        releasing = false;
    }

    public void ReplayThrow(Vector3 position, Vector3 velocity)
    {
        var balloon = Instantiate(throwablePrefab, position, Quaternion.identity);
        balloon.GetComponent<Collider>().enabled = false;
        var pop = balloon.GetComponent<PopOnContact>();
        if (pop != null) pop.owner = ownerHealth;
        StartCoroutine(Launch(balloon, velocity));
    }

    IEnumerator Launch(GameObject balloon, Vector3 velocity)
    {
        balloon.transform.SetParent(null);
        var body = balloon.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.linearVelocity = velocity;

        yield return new WaitForSeconds(0.2f);   // let it clear the arm before it collides
        if (balloon != null) balloon.GetComponent<Collider>().enabled = true;
    }
}
