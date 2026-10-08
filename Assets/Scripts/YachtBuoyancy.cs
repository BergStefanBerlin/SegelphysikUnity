using UnityEngine;

public class YachtBuoyancy : MonoBehaviour
{
    [Header("Referenzen")]
    public OceanWaves ocean;

    [Header("Physik")]
    public float waterDensity = 1025f;
    public float displacementVolume = 40f;

    [Header("Rumpf-Ausmasse (m)")]
    public float beamHalf = 2.25f;
    public float keelDepth = 2.6f;
    public float freeboard = 1.5f;
    public float lenHalf = 8.0f;

    [Header("Aufloesung")]
    public int nx = 5;
    public int ny = 7;
    public int nz = 13;

    [Header("Daempfung")]
    public float verticalDrag = 200000f;
    public float horizontalDrag = 6000f;
    public float rollDamping = 0.95f;
    public float pitchDamping = 0.90f;

    Rigidbody rb;
    Vector3[] probes;
    float[] weights;
    float sumW;
    const float g = 9.81f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        int n = nx * ny * nz;
        probes = new Vector3[n];
        weights = new float[n];
        sumW = 0f;

        // Gitter vom Kiel bis ueber das Deck – der Auftriebsmittelpunkt
        // muss beim Schwimmen UEBER dem Schwerpunkt liegen (Stabilitaet!)
        float yMin = -keelDepth;
        float yMax = freeboard;
        float yMid = 0.5f * (yMin + yMax);
        float yHalf = 0.5f * (yMax - yMin);

        int idx = 0;
        for (int i = 0; i < nx; i++)
        {
            float x = -beamHalf + 2f * beamHalf * i / (nx - 1);
            float wx = Mathf.Sqrt(Mathf.Clamp01(1f - (x / beamHalf) * (x / beamHalf)));

            for (int j = 0; j < ny; j++)
            {
                float y = yMin + (yMax - yMin) * j / (ny - 1);
                float ry = (y - yMid) / yHalf;
                float wy = Mathf.Sqrt(Mathf.Clamp01(1f - ry * ry));

                for (int k = 0; k < nz; k++)
                {
                    float z = -lenHalf + 2f * lenHalf * k / (nz - 1);
                    float wz = Mathf.Sqrt(Mathf.Clamp01(1f - (z / lenHalf) * (z / lenHalf)));

                    float w = wx * wy * wz + 0.02f;
                    probes[idx] = new Vector3(x, y, z);
                    weights[idx] = w;
                    sumW += w;
                    idx++;
                }
            }
        }
    }

    void FixedUpdate()
    {
        if (ocean == null) return;

        // ---- Roll- und Stampf-Daempfung ----
        Vector3 localAngVel = transform.InverseTransformDirection(rb.angularVelocity);
        localAngVel.x *= pitchDamping;
        localAngVel.z *= rollDamping;
        rb.angularVelocity = transform.TransformDirection(localAngVel);

        // ---- Auftrieb ueber alle Messpunkte ----
        float cellHeight = (keelDepth + freeboard) / (ny - 1);

        for (int idx = 0; idx < probes.Length; idx++)
        {
            Vector3 wp = transform.TransformPoint(probes[idx]);
            float waterY = ocean.WaveHeight(wp.x, wp.z, Time.time);
            float depth = waterY - wp.y;
            if (depth <= 0f) continue;

            float frac = Mathf.Clamp01(depth / cellHeight);
            float cellVol = displacementVolume * weights[idx] / sumW;
            float F = waterDensity * g * cellVol * frac;

            float wFrac = weights[idx] / sumW;
            Vector3 vPoint = rb.GetPointVelocity(wp);

            // Alle Daempfungsanteile hart klemmen: nie staerker als der
            // lokale Auftrieb -> reine Energiesenke, kein Aufschaukeln moeglich
            Vector3 damp = new Vector3(
                Mathf.Clamp(-vPoint.x * horizontalDrag * wFrac, -0.5f * F, 0.5f * F),
                Mathf.Clamp(-vPoint.y * verticalDrag   * wFrac, -F,        F),
                Mathf.Clamp(-vPoint.z * horizontalDrag * wFrac, -0.5f * F, 0.5f * F));

            rb.AddForceAtPosition(Vector3.up * F, wp);
            rb.AddForceAtPosition(damp, wp);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (probes == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < probes.Length; i++)
            Gizmos.DrawWireCube(transform.TransformPoint(probes[i]), Vector3.one * 0.15f);
    }
}