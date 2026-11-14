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

    [Header("Aiming")]
    public float minAngle = 10f;        // flattest throw (degrees up)
    public float maxAngle = 70f;        // highest lob
    public float minSpeed = 5f;         // weakest throw
    public float maxSpeed = 15f;        // strongest throw
    public float dragRange = 200f;      // pixels of drag for the full range
    public float maxRange = 8f;   // farthest landing distance in meters (flat ground)

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

    void Update()
    {
        if (Input.GetMouseButtonDown(1) && held == null && !releasing)
        {
            Spawn();
            dragStart = Input.mousePosition;
            aimAngle = Mathf.Lerp(minAngle, maxAngle, 0.5f);
            aimSpeed = Mathf.Lerp(minSpeed, maxSpeed, 0.5f);
        }

        if (Input.GetMouseButton(1) && held != null && !releasing)
        {
            Vector2 drag = (Vector2)Input.mousePosition - dragStart;
            aimAngle = Mathf.Lerp(minAngle, maxAngle, Mathf.InverseLerp(-dragRange, dragRange, drag.y));
            aimSpeed = Mathf.Lerp(minSpeed, maxSpeed, Mathf.InverseLerp(0f, dragRange, Mathf.Abs(drag.x)));
            ClampRange();
        }

        if (Input.GetMouseButtonUp(1) && held != null && !releasing)
            StartCoroutine(Throw());

        bool aiming = Input.GetMouseButton(1) && held != null && !releasing;
        SetIndicator(aiming);
        if (aiming) UpdateIndicator();
    }

    void Awake()
    {
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
        Vector3 dir = Quaternion.AngleAxis(-aimAngle, aimRoot.right) * aimRoot.forward;
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
        Vector3 dir = Quaternion.AngleAxis(-aimAngle, aimRoot.right) * aimRoot.forward;
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

        releasing = true;
        GameObject obj = held;
     
        yield return new WaitForSeconds(throwDelay);
        held = null;

        obj.transform.SetParent(null);
        var rb = obj.GetComponent<Rigidbody>();
        rb.isKinematic = false;
        Vector3 dir = Quaternion.AngleAxis(-angle, aimRoot.right) * aimRoot.forward;
        rb.linearVelocity = dir * speed;

        yield return new WaitForSeconds(0.2f);   // let it clear the arm before it collides
        obj.GetComponent<Collider>().enabled = true;

        releasing = false;
    }
}