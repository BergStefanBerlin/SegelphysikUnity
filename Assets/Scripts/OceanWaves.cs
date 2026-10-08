 using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class OceanWaves : MonoBehaviour
{
    [Header("Flächengröße")]
    [SerializeField] private float size = 1000f;      // Kantenlänge in Metern
    [SerializeField] private int segments = 200;      // Unterteilung (mehr = feiner, aber teurer)

    [Header("Wellen-Parameter")]
    [SerializeField] private bool wavesEnabled = false; // Wellen an/aus
    [SerializeField] private float amplitude = 0.5f;  // Wellenhöhe (m)
    [SerializeField] private float wavelength = 30f;  // Wellenlänge (m)
    [SerializeField] private float speed = 1.5f;      // Geschwindigkeit (m/s)

    private Vector3[] baseVertices;
    private Mesh mesh;

    void Start()
    {
        mesh = new Mesh { name = "Ocean" };
        BuildGrid();
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    void Update()
    {
        float t = Time.time;
        var verts = mesh.vertices;

        for (int i = 0; i < verts.Length; i++)
        {
            // Ausgangsposition des Punktes holen
            float x = baseVertices[i].x;
            float z = baseVertices[i].z;
            // Wellenhöhe an dieser Stelle berechnen
            verts[i].y = WaveHeight(x, z, t);
        }

        mesh.vertices = verts;
        mesh.RecalculateNormals(); // für schattiertes Licht auf den Wellen
    }

    // Die eine Wellenfunktion – später fragt auch das Boot diese ab!
    public float WaveHeight(float x, float z, float time)
    {
        if (!wavesEnabled) return 0f; // NEU: Flaches Wasser, wenn ausgeschaltet
        float k = 2f * Mathf.PI / wavelength;
        float w = k * speed;
        return amplitude * Mathf.Sin(k * x + w * time)
            + amplitude * 0.5f * Mathf.Sin(k * (x + z) * 0.7f + w * 1.3f * time);
    }

    private void BuildGrid()
    {
        int n = segments + 1;
        baseVertices = new Vector3[n * n];
        int[] triangles = new int[segments * segments * 6];
        float half = size * 0.5f;

        for (int iz = 0; iz < n; iz++)
        {
            for (int ix = 0; ix < n; ix++)
            {
                int i = iz * n + ix;
                baseVertices[i] = new Vector3(
                    -half + size * ix / segments,
                    0f,
                    -half + size * iz / segments);
            }
        }

        int tri = 0;
        for (int iz = 0; iz < segments; iz++)
        {
            for (int ix = 0; ix < segments; ix++)
            {
                int a = iz * n + ix;
                int b = a + 1;
                int c = a + n;
                int d = c + 1;
                triangles[tri++] = a; triangles[tri++] = c; triangles[tri++] = b;
                triangles[tri++] = b; triangles[tri++] = c; triangles[tri++] = d;
            }
        }

        mesh.vertices = baseVertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
    }
}