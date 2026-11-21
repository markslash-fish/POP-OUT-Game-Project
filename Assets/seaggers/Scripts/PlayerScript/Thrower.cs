using System.Collections;
using UnityEngine;
using System.Collections.Generic;

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
        if (held != null && !releasing && ownerHealth != null && ownerHealth.IsDead) CancelThrow();

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

        var mat = new Material(Shader.Find("Sprites/Default"));
        arc = MakeLine("TrajectoryArc", mat, 0.08f, false);
        ring = MakeLine("LandingRing", mat, 0.06f, true);
        ring.positionCount = ringPoints;
        rangeRing = MakeLine("RangeRing", mat, 0.05f, true);
        rangeRing.positionCount = rangePoints;
        SetIndicator(false);
    }

    LineRenderer MakeLine(string lineName, Material mat, float width, bool loop)
    {
        var go = new GameObject(lineName);
        go.transform.SetParent(transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = mat;
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
        arc.enabled = on;
        ring.enabled = on;
        rangeRing.enabled = on;
    }

    void UpdateIndicator()
    {
        Vector3 pos = (handL.position + handR.position) / 2f;
        Vector3 dir = GetLaunchDir(aimAngle);
        Vector3 vel = dir * aimSpeed;

        var points = new List<Vector3> { pos };
        Vector3 landing = pos;

        for (int i = 0; i < maxPoints; i++)
        {
            Vector3 next = pos + vel * stepTime + Physics.gravity * (0.5f * stepTime * stepTime);
            vel += Physics.gravity * stepTime;

            if (Physics.Linecast(pos, next, out RaycastHit hit, hitMask, QueryTriggerInteraction.Ignore))
            {
                points.Add(hit.point);
                landing = hit.point;
                break;
            }

            points.Add(next);
            pos = next;
            landing = next;
        }

        arc.positionCount = points.Count;
        arc.SetPositions(points.ToArray());

        for (int i = 0; i < ringPoints; i++)
        {
            float a = i / (float)ringPoints * Mathf.PI * 2f;
            ring.SetPosition(i, landing + new Vector3(Mathf.Cos(a) * landingRadius, 0.03f, Mathf.Sin(a) * landingRadius));
        }

        Vector3 center = (handL.position + handR.position) / 2f;
        for (int i = 0; i < rangePoints; i++)
        {
            float a = i / (float)rangePoints * Mathf.PI * 2f;
            Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * maxRange;
            Vector3 origin = p + Vector3.up * 2f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit h, 30f, hitMask, QueryTriggerInteraction.Ignore))
                p.y = h.point.y + 0.03f;
            rangeRing.SetPosition(i, p);
        }
    }

    void ClampRange()
    {
        Vector3 start = (handL.position + handR.position) / 2f;
        if (Reach(start, aimSpeed) <= maxRange) return;

        float lo = 0f, hi = aimSpeed;
        for (int i = 0; i < 12; i++)
        {
            float mid = (lo + hi) / 2f;
            if (Reach(start, mid) <= maxRange) lo = mid; else hi = mid;
        }
        aimSpeed = lo;
    }

    float Reach(Vector3 start, float speed)
    {
        Vector3 dir = GetLaunchDir(aimAngle);
        Vector3 vel = dir * speed;
        Vector3 pos = start;

        for (int i = 0; i < maxPoints; i++)
        {
            Vector3 next = pos + vel * stepTime + Physics.gravity * (0.5f * stepTime * stepTime);
            vel += Physics.gravity * stepTime;

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

        obj.transform.SetParent(null);
        var rb = obj.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        Vector3 dir = stickThrow ? lockedDir : Quaternion.AngleAxis(-angle, aimRoot.right) * aimRoot.forward;
        rb.linearVelocity = dir * speed;

        yield return new WaitForSeconds(0.2f);   // let it clear the arm before it collides
        if (obj != null) obj.GetComponent<Collider>().enabled = true;

        releasing = false;
    }
}