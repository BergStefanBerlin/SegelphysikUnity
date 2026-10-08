using UnityEngine;

/// <summary>
/// Statisches, "unendliches" Wasser (Stufe 1).
/// Halbraum y < surfaceHeight, Oberflaeche fix bei y = surfaceHeight (Default 0).
/// Reines Medium: liefert Dichte + Wasserebene. KEIN Collider, KEINE Wellen.
/// Daempfungs-Skalierung mit rho/1025 kommt spaeter (Wellen-Phase).
/// </summary>
public class WaterBody : MonoBehaviour
{
    [Header("Medium")]
    [Tooltip("Dichte in kg/m³. Presets: Suesswasser 1000 · Seewasser 1025 · Totes Meer ~1240 · Quecksilber 13546")]
    public float density = 1025f;

    [Tooltip("Hoehe der Wasseroberflaeche in Welt-Y")]
    public float surfaceHeight = 0f;

    [Header("Debug")]
    [Tooltip("Ausdehnung des Wasserlinien-Gizmos im Scene-View")]
    public float gizmoSize = 20f;

    public float Density => density;
    public float SurfaceHeight => surfaceHeight;

    /// <summary>Liegt der Weltpunkt unter Wasser?</summary>
    public bool IsUnderwater(Vector3 worldPoint) => worldPoint.y < surfaceHeight;

    /// <summary>Wasserebene in Weltkoordinaten: n·p + d = 0 (n = (0,1,0), d = -surfaceHeight).</summary>
    public void GetWorldPlane(out Vector3 normal, out float distance)
    {
        normal = Vector3.up;
        distance = -surfaceHeight;
    }

    private void OnDrawGizmos()
    {
        // Wasserlinie im Scene-View (Gitter auf Oberflaechenhoehe)
        Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.95f);
        Vector3 center = new Vector3(transform.position.x, surfaceHeight, transform.position.z);
        float half = gizmoSize * 0.5f;
        const int lines = 5;
        for (int i = 0; i <= lines; i++)
        {
            float t = -half + gizmoSize * i / lines;
            Gizmos.DrawLine(center + new Vector3(t, 0f, -half), center + new Vector3(t, 0f, half));
            Gizmos.DrawLine(center + new Vector3(-half, 0f, t), center + new Vector3(half, 0f, t));
        }
        Gizmos.DrawSphere(center, 0.2f);
    }
}
