using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 8f;
    public float runSpeed = 12f;
    public float mouseSensitivity = 2f;
    public bool useRawMovementInput = true;

    [Header("Physics")]
    public float slopeForceDown = 0f;
    public float airControlMultiplier = 0.8f;
    public float groundedStickForce = 2f;

    [Header("Custom Gravity ;)")]
    public float gravity = -9.81f;
    public float maxFallSpeed = -53f;

    [Header("Jump Feel")]
    public float fallGravityMultiplier = 2.1f;
    public float lowJumpGravityMultiplier = 0.85f;
    public float apexThreshold = 0.2f;
    public float apexGravityMultiplier = 0.95f;
    public float jumpCutGravityMultiplier = 2.25f;

    [Header("Jump")]
    public bool enableJump = true;
    public float jumpForce = 5.6f;
    public float jumpHoldForce = 3f;
    public float maxJumpTime = 0.3f;
    public bool useJumpHold = false;
    public float jumpCutMultiplier = 0.65f;
    public KeyCode jumpKey = KeyCode.Space;

    [Header("Jump Smoothing")]
    public float coyoteTime = 0.15f;
    public float jumpBufferTime = 0.1f;
    public float jumpUngroundTime = 0.08f;

    [Header("Ground Check")]
    public float groundCheckRadius = 0.3f;
    public float groundCheckDistance = 0.45f;
    public float maxGroundAngle = 88f;
    public float maxWalkableGroundAngle = 60f;
    public LayerMask groundLayers = ~0;
    public float upwardUngroundVelocity = 0.15f;

    [Header("Wall Interaction")]
    public bool enableWallClimbing = true;
    public float wallClimbSpeed = 3f;
    public float wallSlideSpeed = 2f;
    public float wallJumpForce = 10f;
    public float wallJumpAwayForce = 5f;
    public float wallJumpUpForce = 6.5f;
    public float wallCheckDistance = 0.8f;

    [Header("Landing")]
    public bool enableLandingSmoothing = true;

    [Header("Crouching")]
    public bool enableCrouch = true;
    public float crouchHeight = 1f;
    public float standingHeight = 2f;
    public KeyCode crouchKey = KeyCode.LeftControl;

    [Header("Step Climbing")]
    public bool enableStepClimbing = true;
    public float stepHeight = 0.8f;
    public float stepSmooth = 0.15f;
    public float climbCheckDistance = 0.8f;

    [Header("Camera Smoothing")]
    public bool enableCameraSmoothing = true;
    public float cameraSmoothTimeGrounded = 0.08f;
    public float cameraSmoothTimeAir = 0.03f;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private Transform cameraTransform;
    private float verticalRotation = 0f;
    private bool isGrounded;
    private bool wasGrounded = false;
    private bool isCrouching = false;
    private bool isJumping = false;
    private float jumpTimeCounter = 0f;
    private float coyoteTimeCounter = 0f;
    private float jumpBufferCounter = 0f;
    private float jumpUngroundCounter = 0f;
    private bool isTouchingWall = false;
    private Vector3 wallNormal;
    private float verticalVelocity = 0f;
    private Rigidbody currentGroundRigidbody;
    private Vector3 desiredCameraLocalPosition;
    private Vector3 standingCameraLocalPosition;
    private Vector3 standingCapsuleCenter;
    private float standingCapsuleHeight;
    private Vector3 cameraLocalPositionVelocity;

    // This grabs the important parts and saves the normal standing setup.
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if(rb==null) return;
        capsule = GetComponent<CapsuleCollider>();

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.drag = 0f;
        rb.angularDrag = 0f;

        // We drive gravity manually so collision response is consistent across collider types.
        rb.useGravity = false;

        rb.constraints = RigidbodyConstraints.FreezeRotation;

        cameraTransform = GetComponentInChildren<Camera>()?.transform;

        if(cameraTransform != null)
        {
            desiredCameraLocalPosition = cameraTransform.localPosition;
            standingCameraLocalPosition = cameraTransform.localPosition;
        }

        if(capsule != null)
        {
            standingCapsuleCenter = capsule.center;
            standingCapsuleHeight = capsule.height;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // This handles look controls, jumping, crouching, and ground state every frame.
    void Update()
    {
        if(cameraTransform != null)
        {
            // Mouse X turns the player left and right.
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            // Mouse Y looks up and down.
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(0, mouseX, 0);

            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);
            cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
        }

        if(jumpUngroundCounter > 0f)
        {
            jumpUngroundCounter -= Time.deltaTime;
        }

        RaycastHit groundHit;
        // This checks if the player is actually touching the floor.
        bool detectedGround = CheckGrounded(out groundHit);
        // The jump timer can block grounding for a short moment after jumping.
        isGrounded = detectedGround && jumpUngroundCounter <= 0f;

            if(rb.velocity.y > upwardUngroundVelocity)
            {
                // If the player is still moving up, do not count that as grounded.
                isGrounded = false;
            }

        currentGroundRigidbody = isGrounded ? groundHit.rigidbody : null;
        verticalVelocity = rb.velocity.y;

        CheckForWalls();

        if(isGrounded)
        {
            coyoteTimeCounter = coyoteTime;
            if(!isJumping && verticalVelocity <0)
            {
                verticalVelocity = 0f;
            }
        } else
        {
            coyoteTimeCounter -= Time.deltaTime;
        }

        if(Input.GetKeyDown(jumpKey))
        {
            // Save the jump press for a short time so it still works if timing is close.
            jumpBufferCounter = jumpBufferTime;
        } else
        {
            // Let the stored jump press fade out.
            jumpBufferCounter = Mathf.Max(0f, jumpBufferCounter - Time.deltaTime);
        }

        if(enableCrouch)
        {
            HandlingCrouching();
        }

        if(enableJump)
        {
            if(jumpBufferCounter > 0f && coyoteTimeCounter > 0f && !isJumping)
            {
                // Jump only happens when the buffer and coyote time both say it is okay.
                Jump();
                jumpBufferCounter = 0f;
            }

            if(Input.GetKeyDown(jumpKey) && isTouchingWall && !isGrounded)
            {
                WallJump();
                jumpBufferCounter = 0f;
            }

            if(useJumpHold && Input.GetKey(jumpKey) && isJumping && jumpTimeCounter < maxJumpTime)
            {
                // Holding jump gives a little extra lift.
                verticalVelocity += jumpHoldForce * Time.deltaTime;
                jumpTimeCounter += Time.deltaTime;
            }

            if(Input.GetKeyUp(jumpKey))
            {
                // Reset jumping state when key is released so you can jump again.
                if(isJumping && verticalVelocity > 0)
                {
                    // Releasing jump cuts velocity smoothly while still maintaining some control.
                    verticalVelocity *= jumpCutMultiplier;
                }
                isJumping = false;
                jumpTimeCounter = 0f;
            }
        }

        if(enableLandingSmoothing)
        {
            if(!wasGrounded && isGrounded)
            {
                OnLand();
            }
        }

        wasGrounded = isGrounded;

        if(Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // This handles actual player movement with physics so it stays stable.
    void FixedUpdate()
    {
        if(rb == null) return;

        rb.constraints = RigidbodyConstraints.FreezeRotation;

        float horizontal = GetMovementAxis("Horizontal");
        float vertical = GetMovementAxis("Vertical");

        float deadZone = 0.15f;
        if(Mathf.Abs(horizontal) < deadZone) horizontal = 0f;
        if(Mathf.Abs(vertical) < deadZone) vertical = 0f;

        bool hasInput = Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f;

        Vector3 horizontalVelocity = Vector3.zero;

        if(hasInput) {
            Vector3 movement = (transform.forward * vertical + transform.right * horizontal).normalized;

            float currentSpeed = moveSpeed;

            if(isCrouching)
            {
                currentSpeed = moveSpeed * 0.5f;
            } else if (Input.GetKey(KeyCode.LeftShift))
            {
                currentSpeed = runSpeed;
            }

            if(!isGrounded)
            {
                currentSpeed *= airControlMultiplier;
            }

            horizontalVelocity = movement * currentSpeed;

            if(enableStepClimbing && isGrounded && !isCrouching)
            {
                HandleSteps();
            }
        } else if (isGrounded)
        {
            horizontalVelocity = Vector3.zero;
        } else
        {
            Vector3 currentVel = rb.velocity;
            horizontalVelocity = new Vector3(currentVel.x,0,currentVel.z);
        }

        horizontalVelocity = ResolveWallSlideVelocity(horizontalVelocity);
        
        if(!isGrounded)
        {
            if(isTouchingWall && enableWallClimbing)
            {
                verticalVelocity = ApplyAirGravity(verticalVelocity);

                // Keep wall sliding controlled but never freeze in place.
                verticalVelocity = Mathf.Max(verticalVelocity, -Mathf.Max(0.01f, wallSlideSpeed));
            } else
            {
                // Always continue accelerating downward while airborne.
                verticalVelocity = ApplyAirGravity(verticalVelocity);
            }
        } else
        {
            if(slopeForceDown > 0 && hasInput)
            {
                verticalVelocity = -slopeForceDown;
            } else if (!isJumping)
            {
                verticalVelocity = -groundedStickForce;
            }
            // If jumping while grounded, keep the jump velocity
        }

        Vector3 finalVelocity = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);

        if(isGrounded && !isJumping && currentGroundRigidbody != null)
        {
            finalVelocity += currentGroundRigidbody.velocity;
        }

        rb.velocity = finalVelocity;

        if(Time.frameCount %30 == 0)
        {
            //Debug.Log($"Velocity: {rb.velocity}, Grounded: {isGrounded}, Touching Wall: {isTouchingWall}");
        }
    }

    // This applies our custom gravity every physics tick when in the air.
    float ApplyAirGravity(float currentVerticalVelocity)
    {
        float gravityScale = 1f;

        // Ascending: apply reduced gravity if holding jump, otherwise apply higher gravity for jump cut
        if(currentVerticalVelocity > apexThreshold)
        {
            if(isJumping && Input.GetKey(jumpKey))
            {
                // Actively jumping and holding: reduced gravity for floaty feel
                gravityScale = Mathf.Max(0.01f, lowJumpGravityMultiplier);
            }
            else
            {
                // Jump released or apex reached: apply jump cut gravity for responsiveness
                gravityScale = Mathf.Max(0.01f, jumpCutGravityMultiplier);
            }
        }
        // At apex: minimal gravity for smooth peak
        else if(currentVerticalVelocity > -apexThreshold)
        {
            gravityScale = Mathf.Max(0.01f, apexGravityMultiplier);
        }
        // Descending: higher gravity for natural fall
        else
        {
            gravityScale = Mathf.Max(0.01f, fallGravityMultiplier);
        }

        currentVerticalVelocity += gravity * gravityScale * Time.fixedDeltaTime;
        return Mathf.Max(currentVerticalVelocity, maxFallSpeed);
    }

    // This keeps movement sliding along walls instead of getting stuck when pushing into them.
    Vector3 ResolveWallSlideVelocity(Vector3 desiredHorizontalVelocity)
    {
        if(desiredHorizontalVelocity.sqrMagnitude <= 0.0001f)
        {
            return desiredHorizontalVelocity;
        }

        float radius = capsule != null ? Mathf.Max(0.05f, capsule.radius * 0.9f) : 0.3f;
        Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.2f, radius + 0.05f);
        float castDistance = desiredHorizontalVelocity.magnitude * Time.fixedDeltaTime + 0.08f;

        Vector3 adjustedVelocity = desiredHorizontalVelocity;

        // Run a couple of passes so corners still slide cleanly.
        for(int i = 0; i < 2; i++)
        {
            Vector3 castDirection = adjustedVelocity.normalized;
            if(castDirection.sqrMagnitude <= 0.0001f)
            {
                break;
            }

            if(!Physics.SphereCast(origin, radius, castDirection, out RaycastHit hit, castDistance, groundLayers, QueryTriggerInteraction.Ignore))
            {
                break;
            }

            float walkableAngle = Mathf.Clamp(maxWalkableGroundAngle, 0f, maxGroundAngle);
            float hitAngle = Vector3.Angle(hit.normal, Vector3.up);
            if(hitAngle <= walkableAngle)
            {
                continue;
            }

            // Remove movement into the wall normal so the player slides along the surface.
            adjustedVelocity = Vector3.ProjectOnPlane(adjustedVelocity, hit.normal);
        }

        return adjustedVelocity;
    }

    // This cleans up tiny movement and smooths the camera after movement is done.
    void LateUpdate()
    {
        if(rb == null) return;

        float horizontal = GetMovementAxis("Horizontal");
        float vertical = GetMovementAxis("Vertical");

        if(Mathf.Abs(horizontal) < 0.1f && Mathf.Abs(vertical) < 0.1f && isGrounded)
        {
            Vector3 vel = rb.velocity;

            if(Mathf.Abs(vel.x) > 0.01f || Mathf.Abs(vel.z) > 0.01f)
            {
                vel.x = 0f;
                vel.z = 0f;
                vel.y = verticalVelocity;
                rb.velocity = vel;
            }
        }

        if(cameraTransform != null)
        {
            if(!enableCameraSmoothing)
            {
                cameraTransform.localPosition = desiredCameraLocalPosition;
            }
            else
            {
                float smoothTime = isGrounded ? cameraSmoothTimeGrounded : cameraSmoothTimeAir;
                smoothTime = Mathf.Max(0.0001f, smoothTime);
                cameraTransform.localPosition = Vector3.SmoothDamp(
                    cameraTransform.localPosition,
                    desiredCameraLocalPosition,
                    ref cameraLocalPositionVelocity,
                    smoothTime
                );
            }
        }
    }

    // This checks if the player is facing a wall and saves that information.
    void CheckForWalls()
    {
        RaycastHit hit;
        Vector3 origin = transform.position + Vector3.up * 0.5f;

        bool hitWall = false;

        if(Physics.Raycast(origin, transform.forward, out hit, wallCheckDistance))
        {
            if(Vector3.Angle(hit.normal, Vector3.up) > 45f)
            {
                hitWall = true;
                wallNormal = hit.normal;
            }
        }
        isTouchingWall = hitWall;
    }

    // This checks if the player is standing on something walkable.
    bool CheckGrounded(out RaycastHit groundHit)
    {
        float walkableAngle = Mathf.Clamp(maxWalkableGroundAngle, 0f, maxGroundAngle);
        float minGroundNormalY = Mathf.Cos(walkableAngle * Mathf.Deg2Rad);

        // Start the check close to the player's body.
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        // The sphere is a little smaller than the full body so it fits better on slopes.
        float sphereRadius = Mathf.Max(0.05f, groundCheckRadius);
        // This is how far down the check should look.
        float castDistance = groundCheckDistance;
        // This backup ray helps when the sphere check misses weird terrain.
        Vector3 fallbackRayOrigin = transform.position + Vector3.up * 0.3f;
        // Side probes help catch bumpy ground that sits a little off-center.
        float lateralProbeOffset = Mathf.Max(0.08f, sphereRadius * 0.8f);

        if(capsule != null)
        {
            // Fit the ground check to the player's collider instead of guessing.
            sphereRadius = Mathf.Max(0.05f, capsule.radius * 0.9f);
            Vector3 center = transform.TransformPoint(capsule.center);
            // This finds the bottom of the collider so the ground check starts near the feet.
            float footY = center.y - (capsule.height * 0.5f - capsule.radius);
            origin = new Vector3(center.x, footY + sphereRadius + 0.08f, center.z);
            fallbackRayOrigin = center + Vector3.up * 0.1f;
            castDistance = Mathf.Max(groundCheckDistance, 0.35f);
            lateralProbeOffset = Mathf.Max(0.08f, sphereRadius * 0.85f);
        }

        RaycastHit bestHit = default;
        bool foundGround = false;

        // Probe center and four side points so bumpy mesh terrain still counts as grounded.
        foundGround |= TryGroundProbe(origin, sphereRadius, castDistance, ref bestHit);
        foundGround |= TryGroundProbe(origin + transform.right * lateralProbeOffset, sphereRadius, castDistance, ref bestHit);
        foundGround |= TryGroundProbe(origin - transform.right * lateralProbeOffset, sphereRadius, castDistance, ref bestHit);
        foundGround |= TryGroundProbe(origin + transform.forward * lateralProbeOffset, sphereRadius, castDistance, ref bestHit);
        foundGround |= TryGroundProbe(origin - transform.forward * lateralProbeOffset, sphereRadius, castDistance, ref bestHit);

        if(foundGround)
        {
            groundHit = bestHit;
            return true;
        }

        if(Physics.Raycast(fallbackRayOrigin, Vector3.down, out groundHit, groundCheckDistance + 0.4f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            // Only count the surface if it is not too steep.
            float groundAngle = Vector3.Angle(groundHit.normal, Vector3.up);
            bool validSlope = groundAngle <= walkableAngle && groundHit.normal.y >= minGroundNormalY;
            bool belowFeet = groundHit.point.y <= fallbackRayOrigin.y - 0.02f;
            
            return validSlope && belowFeet;
        }

        return false;
    }

    // This does one ground check probe and keeps the best hit it finds.
    bool TryGroundProbe(Vector3 origin, float radius, float distance, ref RaycastHit bestHit)
    {
        float walkableAngle = Mathf.Clamp(maxWalkableGroundAngle, 0f, maxGroundAngle);
        float minGroundNormalY = Mathf.Cos(walkableAngle * Mathf.Deg2Rad);

        if(!Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, distance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        // Very steep ground should act like a wall, not a floor.
        float groundAngle = Vector3.Angle(hit.normal, Vector3.up);
        if(groundAngle > walkableAngle)
        {
            return false;
        }

        // Mesh collider triangle normals can be noisy; require a minimum upward normal.
        if(hit.normal.y < minGroundNormalY)
        {
            return false;
        }

        // Ground must be meaningfully below the probe origin; reject side/edge hits while airborne.
        if(hit.point.y > origin.y - 0.02f)
        {
            return false;
        }

        if(bestHit.collider == null || hit.distance < bestHit.distance)
        {
            bestHit = hit;
        }

        return true;
    }

    // This launches the player away from a wall.
    void WallJump()
    {
        coyoteTimeCounter = 0f;

        float wallLiftFactor = Mathf.Clamp01(wallNormal.y / 0.5f);
        float upwardForce = wallJumpUpForce * wallLiftFactor;

        verticalVelocity = Mathf.Max(jumpForce, upwardForce);
        jumpUngroundCounter = jumpUngroundTime;

        Vector3 awayDirection = Vector3.ProjectOnPlane(wallNormal, Vector3.up).normalized * wallJumpAwayForce;
        if(awayDirection.sqrMagnitude < 0.0001f)
        {
            awayDirection = -transform.forward * wallJumpAwayForce;
        }

        Vector3 launchVelocity = awayDirection + Vector3.up * upwardForce;

        rb.velocity = new Vector3(launchVelocity.x, verticalVelocity, launchVelocity.z);

        isJumping = true;
        jumpTimeCounter = 0f;
    }

    // This starts a normal jump straight upward.
    void Jump()
    {
        // Ensure we're marked as jumping and set the jump impulse
        isJumping = true;
        jumpTimeCounter = 0f;
        coyoteTimeCounter = 0f;
        jumpUngroundCounter = jumpUngroundTime;
        
        // Apply jump force directly with more immediate effect
        verticalVelocity = jumpForce;
        rb.velocity = new Vector3(rb.velocity.x, jumpForce, rb.velocity.z);
    }

    // This runs when the player lands so the jump state resets cleanly.
    void OnLand()
    {
        isJumping = false;

        jumpTimeCounter = 0f;

        if(verticalVelocity < -5f)
        {
            verticalVelocity = -1f;
        }
    }

    // This helps the player hop up small steps instead of getting stuck.
    void HandleSteps()
    {
        float vertical = GetMovementAxis("Vertical");
        if(Mathf.Abs(vertical)<0.1f) return;

        RaycastHit hitObstical; 
        Vector3 chestRayStart = transform.position;

        if(!Physics.Raycast(chestRayStart, transform.forward, out hitObstical, climbCheckDistance))
        {
            return;
        }

        float playerFeet = transform.position.y - 1f;
        float obsticalBottom = hitObstical.point.y;
        float obsticalHeight = obsticalBottom - playerFeet;

        if(obsticalHeight <= 0.05f || obsticalHeight > stepHeight)
        {
            return;
        }
        // This point checks the top of the step so the player can climb onto it.
        Vector3 topCheckPosition = new Vector3(hitObstical.point.x + transform.forward.x * 0.3f, hitObstical.point.y + 0.5f, hitObstical.point.z + transform.forward.z * 0.3f);

        RaycastHit hitTop;
        if(Physics.Raycast(topCheckPosition, Vector3.down, out hitTop, 1f))
        {
            float topSurfaceHeight = hitTop.point.y - playerFeet;

            if(topSurfaceHeight > 0.05f && topSurfaceHeight <= stepHeight)
            {
                RaycastHit ceilingCheck;
                if(!Physics.Raycast(transform.position, Vector3.up, out ceilingCheck, 1.5f))
                {
                    rb.position += Vector3.up * stepSmooth;
                    rb.position += transform.forward * 0.5f;
                }
            }
        }
    }

    // This decides whether the player should crouch or stand up.
    void HandlingCrouching()
    {
        if(Input.GetKey(crouchKey))
        {
            if(!isCrouching)
            {
                // Pressing and holding the key makes the player crouch.
                Crouch();
            }
        }
        else
        {
            if(isCrouching && CanStandUp())
            {
                // Let go of the key to stand back up if there is space.
                StandUp();
            }
        }
    }

    // This shrinks the player and lowers the camera for crouching.
    void Crouch()
    {
        isCrouching = true;

        if(capsule != null)
        {
            capsule.height = crouchHeight;
            // Move the collider down so the feet stay in the same spot.
            float crouchOffset = Mathf.Max(0f, (standingCapsuleHeight - crouchHeight) * 0.5f);
            capsule.center = standingCapsuleCenter + Vector3.down * crouchOffset;
        }

        if(cameraTransform != null)
        {
            // Drop the camera a little so crouch feels real.
            float cameraDrop = Mathf.Clamp((standingCapsuleHeight - crouchHeight) * 0.35f, 0.15f, 0.35f);
            desiredCameraLocalPosition = standingCameraLocalPosition + Vector3.down * cameraDrop;
            if(!enableCameraSmoothing)
            {
                cameraTransform.localPosition = desiredCameraLocalPosition;
            }
        }
    }

    // This puts the player back to normal standing size.
    void StandUp()
    {
        isCrouching = false;
        
        if (capsule != null)
        {
            capsule.height = standingCapsuleHeight;
            capsule.center = standingCapsuleCenter;
        }
        
        if (cameraTransform != null)
        {
            desiredCameraLocalPosition = standingCameraLocalPosition;
            if(!enableCameraSmoothing)
            {
                cameraTransform.localPosition = desiredCameraLocalPosition;
            }
        }
    }

    // This checks if there is enough head room to stand up safely.
    bool CanStandUp()
    {
        if(capsule == null)
        {
            return true;
        }

        // This checks only the extra space the player needs to stand up.
        float radius = Mathf.Max(0.05f, capsule.radius * 0.95f);
        Vector3 currentCenter = transform.TransformPoint(capsule.center);
        Vector3 standingCenter = transform.TransformPoint(standingCapsuleCenter);

        // Find the top of the crouched body.
        float currentTopY = currentCenter.y + (capsule.height * 0.5f - radius);
        // Find the top of the standing body.
        float standingTopY = standingCenter.y + (standingCapsuleHeight * 0.5f - radius);
        // The difference is the extra head room we need.
        float checkDistance = standingTopY - currentTopY;

        if(checkDistance <= 0.01f)
        {
            return true;
        }

        // If something hits this sphere cast, there is not enough room to stand.
        Vector3 castOrigin = new Vector3(currentCenter.x, currentTopY + 0.02f, currentCenter.z);
        return !Physics.SphereCast(castOrigin, radius, Vector3.up, out _, checkDistance, groundLayers, QueryTriggerInteraction.Ignore);
    }

    // This gets either raw input or normal input based on the setting.
    float GetMovementAxis(string axisName)
    {
        return useRawMovementInput ? Input.GetAxisRaw(axisName) : Input.GetAxis(axisName);
    }

    // This draws debug lines in the editor so the checks are easier to see.
    void OnDrawGizmos()
    {
        if(!Application.isPlaying) return;

        if(isTouchingWall)
        {
            Gizmos.color = Color.red;
        }
        else
        {
            Gizmos.color = Color.green;
        }

        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Gizmos.DrawRay(origin, transform.forward * wallCheckDistance);

        Gizmos.color = verticalVelocity > 0 ? Color.yellow : Color.gray;
        Gizmos.DrawRay(transform.position, Vector3.up * verticalVelocity * 0.2f);

        Gizmos.color = isGrounded ? Color.yellow : Color.gray;
        Vector3 groundOrigin = transform.position + Vector3.up * 0.2f;
        Gizmos.DrawWireSphere(groundOrigin, groundCheckRadius);
        Gizmos.DrawRay(groundOrigin, Vector3.down * groundCheckDistance);
    }

}
