using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SailboatPhysics : MonoBehaviour
{
    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Tiefer Schwerpunkt = Ballastwirkung von Kiel + Bulb
        rb.centerOfMass = new Vector3(0f, -2.5f, -0.3f);
    }

    void OnDrawGizmos()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.TransformPoint(rb.centerOfMass), 0.25f);
    }
}