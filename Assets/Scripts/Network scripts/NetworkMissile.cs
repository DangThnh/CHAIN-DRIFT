using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class NetworkMissile : NetworkBehaviour
{
    [Header("Flight")]
    public float speed = 40f;               // m/s (trung bình)
    public float minFlightTime = 0.6f;
    public float arcHeightRatio = 0.3f;     // độ cao đỉnh cung = khoảng cách x tỉ lệ này
    public float maxArcHeight = 25f;
    public LayerMask impactMask;            // Drivable + TrackWall. Để 0 = chỉ nổ khi tới hồng tâm

    [Header("Explosion")]
    public float explosionRadius = 9f;
    public float carDamage = 60f;
    public float cargoDamage = 150f;
    [Range(0f, 1f)] public float edgeDamageRatio = 0.4f; // sát thương ở mép = tỉ lệ này
    public float pushVelocity = 10f;
    public float pushUpward = 0.4f;
    public float visualTime = 0.5f;

    [Header("Visuals (KHÔNG có Collider)")]
    public GameObject missileVisual;
    public GameObject explosionVisual;      // tắt sẵn

    [Networked] private Vector3 StartPos { get; set; }
    [Networked] private Vector3 TargetPos { get; set; }
    [Networked] private int StartTick { get; set; }
    [Networked] private int FlightTicks { get; set; }
    [Networked] private float ArcHeight { get; set; }
    [Networked] private NetworkCarController.PlayerTeam OwnerTeam { get; set; }
    [Networked] private NetworkBool Exploded { get; set; }
    [Networked] private TickTimer DespawnTimer { get; set; }

    private readonly Collider[] hits = new Collider[32];
    private readonly HashSet<Component> handled = new HashSet<Component>();

    // Gọi trong onBeforeSpawned (chỉ server)
    public void Init(Vector3 start, Vector3 target, NetworkCarController.PlayerTeam team, int startTick, float tickDelta)
    {
        StartPos = start;
        TargetPos = target;
        OwnerTeam = team;
        StartTick = startTick;
        float dist = Vector3.Distance(start, target);
        float flightTime = Mathf.Max(minFlightTime, dist / speed);
        FlightTicks = Mathf.Max(1, Mathf.CeilToInt(flightTime / tickDelta));
        ArcHeight = Mathf.Min(dist * arcHeightRatio, maxArcHeight);
    }

    public override void Spawned()
    {
        if (explosionVisual != null)
            explosionVisual.transform.localScale = Vector3.one * (explosionRadius * 2f);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;

        if (Exploded)
        {
            if (DespawnTimer.Expired(Runner)) Runner.Despawn(Object);
            return;
        }

        int elapsed = Runner.Tick.Raw - StartTick;
        float t = Mathf.Clamp01(elapsed / (float)FlightTicks);
        Vector3 next = Vector3.Lerp(StartPos, TargetPos, t);
        next.y += 4f * ArcHeight * t * (1f - t);

        if (impactMask.value != 0 && elapsed > 0 &&
            Physics.Linecast(transform.position, next, out RaycastHit hit, impactMask, QueryTriggerInteraction.Ignore))
        {
            Explode(hit.point);
            return;
        }

        Vector3 dir = next - transform.position;
        if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir);
        transform.position = next;

        if (t >= 1f) Explode(next);
    }

    void Explode(Vector3 center)
    {
        Exploded = true;
        DespawnTimer = TickTimer.CreateFromSeconds(Runner, visualTime);
        transform.position = center;

        int n = Physics.OverlapSphereNonAlloc(center, explosionRadius, hits, ~0, QueryTriggerInteraction.Ignore);
        handled.Clear();
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i];
            float f = Mathf.Clamp01(Vector3.Distance(center, c.bounds.ClosestPoint(center)) / explosionRadius);
            float dmgScale = Mathf.Lerp(1f, edgeDamageRatio, f);

            NetworkCarController car = c.GetComponentInParent<NetworkCarController>();
            if (car != null)
            {
                if (!handled.Add(car) || car.IsDead) continue;
                if (car.MyTeam != OwnerTeam) car.RPC_TakeDamage(carDamage * dmgScale); // cùng phe = 0 sát thương
                Rigidbody rb = car.GetComponent<Rigidbody>();
                if (rb != null) rb.AddExplosionForce(pushVelocity, center, explosionRadius, pushUpward, ForceMode.VelocityChange);
                continue;
            }

            CargoManager cargo = c.GetComponentInParent<CargoManager>();
            if (cargo != null && handled.Add(cargo) && OwnerTeam == NetworkCarController.PlayerTeam.Hunter)
                cargo.RPC_TakeDamage(cargoDamage * dmgScale);
        }
    }

    public override void Render()
    {
        if (missileVisual != null && missileVisual.activeSelf == Exploded) missileVisual.SetActive(!Exploded);
        if (explosionVisual != null && explosionVisual.activeSelf != Exploded) explosionVisual.SetActive(Exploded);
    }
}