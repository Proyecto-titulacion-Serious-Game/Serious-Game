using UnityEngine;

public class FloatingSign : MonoBehaviour
{
    [Header("Animación")]
    public float rotateSpeed = 40f;
    public float floatHeight = 0.08f;
    public float floatSpeed = 2f;

    private Vector3 startPos;

    void Start()
    {
        startPos = transform.localPosition;
    }

    void Update()
    {
        // Girar
        transform.Rotate(Vector3.up * rotateSpeed * Time.deltaTime);

        // Flotar
        transform.localPosition = startPos +
            Vector3.up * Mathf.Sin(Time.time * floatSpeed) * floatHeight;
    }
}