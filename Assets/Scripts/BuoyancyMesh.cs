using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Exakte Auftriebsberechnung per Mesh-Clipping gegen die Wasserebene (Stufe 1).
/// Ersetzt das Probe-Gitter (YachtBuoyancy.cs).
///
/// FIX v3: Tetraeder-Apex liegt jetzt AUF der Wasserebene (vorher Ursprung).
///   Bei einem geclippten (offenen) Mesh haengt das Ergebnis vom Apex ab.
///   Apex auf der Ebene -> die Schnittflaeche traegt exakt 0 bei -> V_nass exakt.
///
/// Ablauf pro FixedUpdate:
///  1. Wasserebene 1x ins lokale Bootssystem transformieren
///  2. Schnelltests: Hoehen-Filter (Ebene ~horizontal) + Kugeltest (Bounding-Sphere)
///  3. Akkumulation ueber signierte Tetraeder (Apex auf der Ebene):
///     exaktes Unterwasservolumen V_nass + exakter Auftriebsmittelpunkt CoB
///  4. Kraft rho*g*V_nass am CoB (AddForceAtPosition)
///  5. Daempfung: 2 Regler (waterDamping linear, rotationDamping alle Achsen)
///
/// Verifikation: aufrecht schwimmend muss V_nass ~ m/rho gelten.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BuoyancyMesh : MonoBehaviour
{
    [Header("Referenzen")]
    [Tooltip("Wasserkörper. Wird automatisch gesucht, falls leer.")]
    public WaterBody waterBody;

    [Tooltip("Haupt-Mesh (Rumpf). Automatisch im eigenen Kind gesucht, falls leer.")]
    public MeshFilter meshFilter;

    [Tooltip("Weitere eintauchbare Teile (Kiel, Bulb, Ruder).")]
    public MeshFilter[] additionalMeshes;

    [Header("Physik")]
    [Tooltip("Erdbeschleunigung in m/s²")]
    public float gravity = 9.81f;

    [Header("Daempfung (2 Regler)")]
    [Tooltip("Lineare Wasserdämpfung, skaliert mit Eintauchanteil. 0 = aus")]
    public float waterDamping = 1.5f;

    [Tooltip("Rotationsdaempfung um alle Achsen, skaliert mit Eintauchanteil. 0 = aus")]
    public float rotationDamping = 2.5f;

    [Header("Debug / Verifikation")]
    [Tooltip("Unterwasservolumen V_nass in m³ (Anzeige)")]
    public float debugWetVolume;

    [Tooltip("Auftriebskraft in N (Anzeige)")]
    public float debugBuoyancyForce;

    [Tooltip("HUD-Anzeige (OnGUI) ein/aus")]
    public bool showHUD = true;

    // --- Laufzeit ---
    Rigidbody rb;
    Vector3[] vertices;
    int[] triangles;
    TriInfo[] triInfo;
    readonly Vector3[] clipBuffer = new Vector3[8];
    float meshVolumeTotal;      // Gesamtvolumen (lokal)
    float scaleFactor;          // |det(lossyScale)|
    float wetVolume;
    Vector3 worldCoB;
    Vector3 buoyancyForce;
    float totalVolumeWorld;     // V_gesamt in Weltkoordinaten
    float maxMass;              // rho * V_gesamt
    bool ready;

    struct TriInfo
    {
        public Vector3 mid;
        public float radius;
        public float minY;
        public float maxY;
    }

    public float WetVolume => wetVolume;
    public Vector3 CenterOfBuoyancy => worldCoB;
    public Vector3 BuoyancyForce => buoyancyForce;
    public float MeshVolumeTotal => meshVolumeTotal;
    public float MaxMass => maxMass;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (waterBody == null) waterBody = FindFirstObjectByType<WaterBody>();
    }

    void Start()
    {
        if (waterBody == null)
        {
            Debug.LogError("[BuoyancyMesh] Kein WaterBody in der Szene - Skript deaktiviert.", this);
            enabled = false;
            return;
        }
        if (meshFilter == null) meshFilter = GetComponentInChildren<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("[BuoyancyMesh] Kein Mesh zugewiesen - Skript deaktiviert.", this);
            enabled = false;
            return;
        }

        GatherCombinedMesh();
        PrecomputeTriangles();
        ComputeTotalVolume();

        // ---- DIAGNOSE v4: eine Zeile pro zugewiesenem Teil ----
        foreach (var d in diagSources)
            Debug.Log($"[Diag] '{d.goName}': Mesh='{d.meshName}', {d.verts} Verts, {d.tris} Tris, " +
                      $"V(signiert) = {d.signedVolume:F3} m3, Bounds = {d.boundsSize.ToString("F2")}", this);
        if (triangles.Length == 0)
            Debug.LogError("[Diag] GESAMT-Mesh hat 0 Dreiecke - die zugewiesenen MeshFilters enthalten keine Geometrie.", this);
        // ---- Ende Diagnose ----

        Vector3 s = transform.lossyScale;
        scaleFactor = Mathf.Abs(s.x * s.y * s.z);
        totalVolumeWorld = Mathf.Abs(meshVolumeTotal) * scaleFactor;
        maxMass = waterBody.Density * totalVolumeWorld;

        Debug.Log($"[BuoyancyMesh] {triInfo.Length} Dreiecke | V_gesamt = {totalVolumeWorld:F2} m³ | " +
                  $"max. tragfaehige Masse = {maxMass:F0} kg | Rigidbody-Masse = {rb.mass:F0} kg", this);

        if (maxMass < rb.mass)
        {
            Debug.LogError($"[BuoyancyMesh] UNMOEGLICH: Voll eingetaucht verdraengt das Mesh nur " +
                           $"{totalVolumeWorld:F2} m³ (= {maxMass:F0} kg), das Boot wiegt aber {rb.mass:F0} kg. " +
                           $"Es MUSS sinken. Masse auf unter {maxMass * 0.9f:F0} kg senken oder Modell skalieren.", this);
        }

        ready = true;
    }

    struct DiagSource
    {
        public string goName;
        public string meshName;
        public int verts;
        public int tris;
        public float signedVolume;
        public Vector3 boundsSize;
    }

    readonly List<DiagSource> diagSources = new List<DiagSource>();

    void GatherCombinedMesh()
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        int baseIndex = 0;

        diagSources.Clear();
        AddMeshDiag(meshFilter, verts, tris, ref baseIndex);
        if (additionalMeshes != null)
        {
            foreach (MeshFilter mf in additionalMeshes)
                if (mf != null && mf.sharedMesh != null)
                    AddMeshDiag(mf, verts, tris, ref baseIndex);
        }
        vertices = verts.ToArray();
        triangles = tris.ToArray();
    }

    void AddMeshDiag(MeshFilter mf, List<Vector3> verts, List<int> tris, ref int baseIndex)
    {
        Mesh mesh = mf.sharedMesh;
        var d = new DiagSource
        {
            goName = mf.transform.name + "' (unter '" + (mf.transform.parent != null ? mf.transform.parent.name : "-") + "')",
            meshName = mesh != null ? mesh.name : "<null>",
            boundsSize = mesh != null ? mesh.bounds.size : Vector3.zero
        };
        if (mesh == null) { diagSources.Add(d); return; }

        Matrix4x4 rel = mf.transform.localToWorldMatrix * transform.worldToLocalMatrix;
        Vector3[] src = mesh.vertices;
        int[] srcTris = mesh.triangles;
        int first = baseIndex;
        for (int i = 0; i < src.Length; i++)
            verts.Add(rel.MultiplyPoint3x4(src[i]));
        for (int i = 0; i < srcTris.Length; i++)
            tris.Add(srcTris[i] + baseIndex);
        baseIndex += src.Length;

        d.verts = src.Length;
        d.tris = srcTris.Length / 3;
        double v6 = 0.0;
        for (int t = 0; t < srcTris.Length; t += 3)
        {
            Vector3 a = verts[first + srcTris[t + 0]];
            Vector3 b = verts[first + srcTris[t + 1]];
            Vector3 c = verts[first + srcTris[t + 2]];
            v6 += (double)Vector3.Dot(Vector3.Cross(a, b), c);
        }
        d.signedVolume = (float)(v6 / 6.0);
        diagSources.Add(d);
    }

    void AddMesh(MeshFilter mf, List<Vector3> verts, List<int> tris, ref int baseIndex)
    {
        Mesh mesh = mf.sharedMesh;
        Matrix4x4 rel = mf.transform.localToWorldMatrix * transform.worldToLocalMatrix;
        Vector3[] src = mesh.vertices;
        for (int i = 0; i < src.Length; i++)
            verts.Add(rel.MultiplyPoint3x4(src[i]));
        int[] srcTris = mesh.triangles;
        for (int i = 0; i < srcTris.Length; i++)
            tris.Add(srcTris[i] + baseIndex);
        baseIndex += src.Length;
    }

    void PrecomputeTriangles()
    {
        triInfo = new TriInfo[triangles.Length / 3];
        for (int t = 0; t < triInfo.Length; t++)
        {
            Vector3 a = vertices[triangles[t * 3 + 0]];
            Vector3 b = vertices[triangles[t * 3 + 1]];
            Vector3 c = vertices[triangles[t * 3 + 2]];
            Vector3 mid = (a + b + c) / 3f;
            float r = Mathf.Max((mid - a).magnitude, Mathf.Max((mid - b).magnitude, (mid - c).magnitude));
            triInfo[t] = new TriInfo
            {
                mid = mid,
                radius = r,
                minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y)),
                maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y))
            };
        }
    }

    void ComputeTotalVolume()
    {
        meshVolumeTotal = 0f;
        for (int t = 0; t < triInfo.Length; t++)
        {
            Vector3 a = vertices[triangles[t * 3 + 0]];
            Vector3 b = vertices[triangles[t * 3 + 1]];
            Vector3 c = vertices[triangles[t * 3 + 2]];
            meshVolumeTotal += Vector3.Dot(Vector3.Cross(a, b), c) / 6f;
        }
        if (meshVolumeTotal < 0f)
            Debug.LogWarning($"[BuoyancyMesh] Winding invertiert (Volumen {meshVolumeTotal:F2} < 0) - wird automatisch korrigiert.", this);
    }

    void FixedUpdate()
    {
        if (!ready) return;

        // 1) Wasserebene ins lokale System: n_l·p + d_l = 0
        Vector3 nLocal = transform.InverseTransformDirection(Vector3.up);
        float dLocal = Vector3.Dot(Vector3.up, transform.position) - waterBody.SurfaceHeight;

        // FIX: Apex auf der Wasserebene (lokales System)
        Vector3 apex = -dLocal * nLocal;

        Vector3 s = transform.lossyScale;
        scaleFactor = Mathf.Abs(s.x * s.y * s.z);

        bool planeHorizontal = nLocal.y > 0.995f;
        float waterYLocal = -dLocal;

        double vol6 = 0.0;
        Vector3 weighted = Vector3.zero;

        for (int t = 0; t < triInfo.Length; t++)
        {
            int i0 = triangles[t * 3 + 0];
            int i1 = triangles[t * 3 + 1];
            int i2 = triangles[t * 3 + 2];

            if (planeHorizontal)
            {
                if (triInfo[t].minY > waterYLocal) continue;
                if (triInfo[t].maxY < waterYLocal)
                {
                    Accumulate(apex, vertices[i0], vertices[i1], vertices[i2], ref vol6, ref weighted);
                    continue;
                }
            }

            float dist = Vector3.Dot(nLocal, triInfo[t].mid) + dLocal;
            if (dist - triInfo[t].radius > 0f) continue;
            if (dist + triInfo[t].radius < 0f)
            {
                Accumulate(apex, vertices[i0], vertices[i1], vertices[i2], ref vol6, ref weighted);
                continue;
            }

            int k = ClipTriangle(vertices[i0], vertices[i1], vertices[i2], nLocal, dLocal, clipBuffer);
            if (k < 3) continue;
            for (int i = 1; i < k - 1; i++)
                Accumulate(apex, clipBuffer[0], clipBuffer[i], clipBuffer[i + 1], ref vol6, ref weighted);
        }

        float v6f = (float)vol6;
        if (meshVolumeTotal < 0f) { v6f = -v6f; weighted = -weighted; }

        float localVol = v6f / 6f;
        wetVolume = Mathf.Max(0f, localVol * scaleFactor);

        if (Mathf.Abs(localVol) > 1e-6f)
            worldCoB = transform.TransformPoint(weighted / localVol);
        else
            worldCoB = rb.worldCenterOfMass;

        buoyancyForce = Vector3.up * (waterBody.Density * gravity * wetVolume);
        if (wetVolume > 1e-6f)
            rb.AddForceAtPosition(buoyancyForce, worldCoB, ForceMode.Force);

        float submerged = Mathf.Clamp01(wetVolume / Mathf.Max(totalVolumeWorld, 1e-6f));
        if (waterDamping > 0f)
            rb.AddForce(-rb.linearVelocity * (waterDamping * submerged), ForceMode.Acceleration);
        if (rotationDamping > 0f)
            rb.AddTorque(-rb.angularVelocity * (rotationDamping * submerged), ForceMode.Acceleration);

        debugWetVolume = wetVolume;
        debugBuoyancyForce = buoyancyForce.magnitude;
    }

    /// <summary>Signiertes Tetraedervolumen (Apex auf der Wasserebene) + gewichteter Schwerpunkt.</summary>
    static void Accumulate(Vector3 apex, Vector3 a, Vector3 b, Vector3 c, ref double vol6, ref Vector3 weighted)
    {
        Vector3 A = a - apex, B = b - apex, C = c - apex;
        double v6 = Vector3.Dot(Vector3.Cross(A, B), C);
        vol6 += v6;
        weighted += (apex + a + b + c) * (float)(v6 / 24.0);
    }

    /// <summary>Clippt Dreieck gegen Ebene n·p + d &lt;= 0 (unter Wasser).</summary>
    static int ClipTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 n, float d, Vector3[] outVerts)
    {
        int count = 0;
        Vector3[] pts = { a, b, c };
        for (int i = 0; i < 3; i++)
        {
            Vector3 p0 = pts[i];
            Vector3 p1 = pts[(i + 1) % 3];
            float s0 = Vector3.Dot(n, p0) + d;
            float s1 = Vector3.Dot(n, p1) + d;
            bool in0 = s0 <= 0f;
            bool in1 = s1 <= 0f;
            if (in0) outVerts[count++] = p0;
            if (in0 != in1)
            {
                float t = s0 / (s0 - s1);
                outVerts[count++] = p0 + (p1 - p0) * t;
            }
        }
        return count;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || waterBody == null) return;
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(worldCoB, 0.18f);
    }

    void OnGUI()
    {
        if (!showHUD || !ready || waterBody == null) return;
        float target = rb != null ? rb.mass / Mathf.Max(waterBody.Density, 0.001f) : 0f;
        GUILayout.BeginArea(new Rect(10, 60, 460, 170));
        GUILayout.Label($"V_nass = {wetVolume:F2} m³   (Ziel ~ {target:F2} m³)");
        GUILayout.Label($"V_gesamt = {totalVolumeWorld:F2} m³   max. Masse = {maxMass:F0} kg   rb.mass = {rb.mass:F0} kg");
        GUILayout.Label($"Auftrieb = {buoyancyForce.magnitude / 1000f:F1} kN   Gewicht = {(rb.mass * gravity) / 1000f:F1} kN");
        GUILayout.Label($"CoB = {worldCoB.ToString("F2")}");
        GUILayout.EndArea();
    }
}
