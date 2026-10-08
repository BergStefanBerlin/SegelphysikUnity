using UnityEngine;

/// <summary>
/// Kraftpfeile zur Laufzeit (Stufe 2):
///  - Schwerkraft: rot, Start am CoG, Richtung -Y, Laenge ~ m·g
///  - Auftrieb:   blau, Start am CoB, Richtung +Y, Laenge ~ Summe F
///  - Hebelarm CoG<->CoB (= GZ): gelb, gestrichelt (optional)
///
/// Pfeile werden zur Laufzeit erzeugt und sind SZENEN-Objekte (keine Boot-Kinder),
/// damit sie nicht die Boot-Rotation erben. Skalierung ueber newtonPerMeter:
/// m·g ~ 115 kN bei 11,7 t -> bei 4000 N/m ein ~29-m-Pfeil.
/// </summary>
public class ForceArrowVisualizer : MonoBehaviour
{
    [Header("Referenzen")]
    public Rigidbody targetRigidbody;
    public BuoyancyMesh buoyancy;

    [Header("Skalierung")]
    [Tooltip("Newton pro Meter Pfeillaenge")]
    public float newtonPerMeter = 4000f;

    [Header("Darstellung")]
    public Color gravityColor = new Color(1f, 0.28f, 0.22f);
    public Color buoyancyColor = new Color(0.22f, 0.55f, 1f);
    public Color leverColor = new Color(1f, 0.86f, 0.2f);
    [Tooltip("Hebelarm-Pfeil CoG<->CoB (GZ) anzeigen")]
    public bool showLeverArm = true;
    [Tooltip("Pfeilspitzen-Groesse in Metern")]
    public float tipSize = 1.5f;
    [Tooltip("Schaft-Staerke in Metern")]
    public float shaftWidth = 0.15f;

    LineRenderer gravityLine, buoyancyLine, leverLine;
    Transform gravityTip, buoyancyTip;
    readonly Vector3[] dashBuffer = new Vector3[256];

    void Start()
    {
        if (targetRigidbody == null) targetRigidbody = GetComponent<Rigidbody>();
        if (buoyancy == null) buoyancy = GetComponent<BuoyancyMesh>();
        if (targetRigidbody == null || buoyancy == null)
        {
            Debug.LogError("[ForceArrowVisualizer] targetRigidbody und buoyancy werden benoetigt.", this);
            enabled = false;
            return;
        }

        // Eigener Root in der Szene (nicht Kind des Boots)
        var root = new GameObject("ForceArrows (Runtime)");

        gravityLine = CreateArrow("Gravity (rot, CoG)", root.transform, gravityColor, out gravityTip);
        buoyancyLine = CreateArrow("Buoyancy (blau, CoB)", root.transform, buoyancyColor, out buoyancyTip);
        if (showLeverArm)
            leverLine = CreateLine("LeverArm GZ (gelb, gestrichelt)", root.transform, leverColor, 0.08f);
    }

    void FixedUpdate()
    {
        if (targetRigidbody == null || buoyancy == null) return;

        // Schwerkraft: rot, ab CoG, Richtung -Y
        Vector3 cog = targetRigidbody.worldCenterOfMass;
        float gravityForce = targetRigidbody.mass * Physics.gravity.magnitude;
        SetArrow(gravityLine, gravityTip, cog, cog + Vector3.down * (gravityForce / newtonPerMeter));

        // Auftrieb: blau, ab CoB, Richtung +Y
        Vector3 cob = buoyancy.CenterOfBuoyancy;
        Vector3 bForce = buoyancy.BuoyancyForce;
        SetArrow(buoyancyLine, buoyancyTip, cob, cob + Vector3.up * (bForce.magnitude / newtonPerMeter));

        // Hebelarm CoG<->CoB (GZ)
        if (leverLine != null)
            SetDashedLine(leverLine, cog, cob, dashBuffer);
    }

    // ---------- Helfer ----------

    LineRenderer CreateArrow(string name, Transform parent, Color color, out Transform tip)
    {
        LineRenderer lr = CreateLine(name, parent, color, shaftWidth);
        var tipGo = new GameObject("Tip");
        tipGo.transform.SetParent(lr.transform, false);
        var mf = tipGo.AddComponent<MeshFilter>();
        mf.sharedMesh = CreateConeMesh(12, tipSize, tipSize * 2.2f);
        var mr = tipGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = lr.material;
        tip = tipGo.transform;
        return lr;
    }

    LineRenderer CreateLine(string name, Transform parent, Color color, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = width;
        lr.useWorldSpace = true;
        lr.startColor = lr.endColor = color;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null)
        {
            Debug.LogWarning("[ForceArrowVisualizer] Kein Shader gefunden - Pfeile evtl. unsichtbar.", this);
            sh = Shader.Find("Standard");
        }
        if (sh != null)
        {
            var mat = new Material(sh);
            mat.color = color;
            lr.material = mat;
        }
        return lr;
    }

    static void SetArrow(LineRenderer lr, Transform tip, Vector3 start, Vector3 end)
    {
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        Vector3 dir = end - start;
        if (dir.sqrMagnitude < 1e-6f)
        {
            tip.gameObject.SetActive(false);
            return;
        }
        tip.gameObject.SetActive(true);
        tip.position = end;
        tip.rotation = Quaternion.LookRotation(dir.normalized);
    }

    static void SetDashedLine(LineRenderer lr, Vector3 a, Vector3 b, Vector3[] buffer)
    {
        Vector3 dir = b - a;
        float len = dir.magnitude;
        if (len < 1e-4f)
        {
            lr.positionCount = 0;
            return;
        }
        dir /= len;
        const float dash = 0.8f, gap = 0.45f;
        int count = 0;
        float t = 0f;
        while (t < len && count < buffer.Length - 1)
        {
            float endT = Mathf.Min(t + dash, len);
            buffer[count++] = a + dir * t;
            buffer[count++] = a + dir * endT;
            t = endT + gap;
        }
        lr.positionCount = count;
        lr.SetPositions(buffer);
    }

    /// <summary>Kegel mit Basis bei z = 0, Spitze bei +Z (LookRotation zeigt Spitze in Pfeilrichtung).</summary>
    static Mesh CreateConeMesh(int segments, float radius, float height)
    {
        var mesh = new Mesh();
        var verts = new Vector3[segments + 2];
        var tris = new int[segments * 6];
        verts[0] = Vector3.zero;                       // Basis-Mittelpunkt
        for (int i = 0; i < segments; i++)
        {
            float ang = Mathf.PI * 2f * i / segments;
            verts[i + 1] = new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
        }
        verts[segments + 1] = new Vector3(0f, 0f, height);   // Spitze
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int ti = i * 6;
            tris[ti + 0] = i + 1;                     // Mantel (nach aussen)
            tris[ti + 1] = next + 1;
            tris[ti + 2] = segments + 1;
            tris[ti + 3] = 0;                         // Basis (nach hinten)
            tris[ti + 4] = next + 1;
            tris[ti + 5] = i + 1;
        }
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        return mesh;
    }
}
