using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public bool autoFindTarget = true;

    [Header("Follow Settings")]
    public Vector3 offset = new Vector3(0,8,-12);
    public float followSpeed = 5f;
    public float rotationSpeed = 3f;

    [Header("Looks Settings")]
    public bool lookAtTarget = true;
    public Vector3 lookOffset = new Vector3(0,2,0);

    // Initializes the camera target on startup
    void Start()
    {
        FindTarget();
    }

    // Updates camera position and rotation to follow the target each frame
    void LateUpdate() {
        if (target == null && autoFindTarget) {
            FindTarget();
            return;
        }

        if (target == null) return;

        // The new position of the camera
        Vector3 desiredPosition = target.position + target.TransformDirection(offset);

        // Makes the follow look smooth
        transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);

        // Look at the target
        if(lookAtTarget) {
            Vector3 lookPosition = target.position + lookOffset;
            Quaternion targetRotation = Quaternion.LookRotation(lookPosition - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    // Searches for the player ship by name or tag to set as the camera target
    public void FindTarget() {
        // This tried to find the ship by the name. this may not work with other ships, since the name is different for each ship
        GameObject ship = GameObject.Find("PlayerShip");

        if (ship != null) {
            target = ship.transform;
        } else {
            //Player tag
            ship = GameObject.FindGameObjectWithTag("Player");
            if (ship!=null) {
                target = ship.transform;
            } else {
                // No Ship
                Debug.LogWarning("NO SHIP :( I wish this would work throughout the ships.");
            }
        }
    }

    // Manually sets a new target for the camera to follow
    public void SetTarget(Transform newTarget) {
        target = newTarget;
    }
}
