using UnityEngine;

public class CameraLook : MonoBehaviour
{
    [Header("Look Settings")]
    public float mouseSensitivity = 2f;
    public Transform playerBody;
    
    private float xRotation = 0f;
    
    void Start()
    {
        // Lock cursor to center
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        
        if (playerBody == null)
        {
            playerBody = transform.parent;
        }
    }
    
    void Update()
    {
        // Get mouse input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        
        // Rotate camera up/down
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        
        // Rotate player left/right
        if (playerBody != null)
        {
            playerBody.Rotate(Vector3.up * mouseX);
        }
        
        // Unlock cursor with Escape
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
