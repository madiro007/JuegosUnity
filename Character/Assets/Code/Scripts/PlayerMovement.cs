using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovementInputSystem : MonoBehaviour
{
    [Header("Referencias")]
    public Transform cameraTransform; // Asigna la cámara del jugador. Si es null, usa transform

    [Header("Movimiento")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 7.5f;
    public float acceleration = 12f;
    public float deceleration = 14f;
    public float rotationSmoothTime = 0.08f;

    [Header("Salto y Gravedad")]
    public float jumpHeight = 1.2f;
    public float gravity = -20f;        // Negativo
    public float groundedGravity = -2f; // Para “pegar” al suelo
    public float coyoteTime = 0.12f;
    public int extraAirJumps = 0;

    [Header("Ground Check")]
    public LayerMask groundMask = ~0;
    public float groundCheckRadius = 0.25f;
    public float groundCheckDistance = 0.4f;
    public Vector3 groundCheckOffset = new Vector3(0, 0.1f, 0);
    public float maxSlopeAngle = 50f;

    [Header("Pendientes")]
    public bool slideOnSteepSlopes = true;
    public float slopeSlideSpeed = 6f;

    [Header("Sensibilidad Look")]
    public float mouseSensitivity = 0.15f;     // multiplicador para Mouse delta
    public float gamepadLookSensitivity = 120f; // grados/segundo para stick derecho
    public bool clampPitch = true;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    private CharacterController controller;

    // Estado movimiento
    private Vector3 velocity;
    private Vector3 planarVelocity;
    private float currentSpeed;
    private float targetSpeed;
    private float rotationVelocity;
    private bool isGrounded;
    private float lastGroundedTime;
    private int airJumpsUsed;
    private Vector3 groundNormal = Vector3.up;

    // Estado look
    private float yaw;   // rotación Y del cuerpo/jugador
    private float pitch; // rotación X de la cámara

    // Input System
    private InputSystem_Actions inputActions;
    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool jumpPressed;
    private bool sprintHeld;
    
    private AudioSource audioSource;
    private CharacterController characterController;

    // Conjunto de contactos del frame actual y del anterior
    private readonly HashSet<Collider> currentHits = new HashSet<Collider>();
    private readonly HashSet<Collider> previousHits = new HashSet<Collider>();

    // Eventos simples por código 
    public System.Action<Collider> OnEnterContact;
    public System.Action<Collider> OnStayContact;
    public System.Action<Collider> OnExitContact;


    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (cameraTransform == null) cameraTransform = transform;

        // Inicializar Input Actions
        inputActions = new InputSystem_Actions();
        // Suscripción a callbacks (opcional, aquí usamos polling en Update para claridad)
        inputActions.Player.Enable();

        // Inicializar yaw/pitch desde transform/cámara
        yaw = transform.eulerAngles.y;
        if (cameraTransform != null && cameraTransform != transform)
        {
            Vector3 camAngles = cameraTransform.localEulerAngles;
            // Convertir a rango -180..180 para evitar saltos
            pitch = NormalizeAngle(camAngles.x);
        }

        // Ocultar y bloquear cursor si es FPS
        // Cursor.lockState = CursorLockMode.Locked;
        // Cursor.visible = false;

     //   audioSource = GetComponent<AudioSource>();
        characterController = GetComponent<CharacterController>();

    }

    private void OnEnable()
    {
        if (inputActions == null)
            inputActions = new InputSystem_Actions();

        inputActions.Player.Enable();
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();
    }

    private void Update()
    {
        ReadInputs();
        HandleLook();        // Procesar rotación de cámara/cuerpo
        HandleGroundCheck();
        HandleMovement();
        HandleJump();
        ApplyGravity();
        ApplySlopeSlideIfNeeded();

        Vector3 move = planarVelocity + Vector3.up * velocity.y;
        controller.Move(move * Time.deltaTime);

        foreach (var col in previousHits)
        {
            if (!currentHits.Contains(col))
            {
                OnExitContact?.Invoke(col);
                Debug.Log($"Exit contacto con: {col.name}");
            }
        }

        previousHits.Clear();
        foreach (var col in currentHits) previousHits.Add(col);
        currentHits.Clear();


    }

    private void ReadInputs()
    {
        moveInput = inputActions.Player.Move.ReadValue<Vector2>();
        lookInput = inputActions.Player.Look.ReadValue<Vector2>();

        // Jump: usar triggered en el frame
        // Aquí usamos WasPressedThisFrame para comportamiento de botón momento
        jumpPressed = inputActions.Player.Jump.WasPressedThisFrame();

        // Sprint sostenido
        sprintHeld = inputActions.Player.Sprint.IsPressed();
    }

    private void HandleLook()
    {
        // Diferenciar mouse vs gamepad:
        // Mouse delta suele venir con altas magnitudes proporcionales a DPI.
        // Stick derecho suele ser Vector2 [-1..1].
        Vector2 delta = lookInput;

        if (Mouse.current != null && Mouse.current.delta.IsActuated())
        {
            // Mouse: escalar por sensibilidad, independiente de Time.deltaTime
            yaw += delta.x * mouseSensitivity;
            pitch -= delta.y * mouseSensitivity;
        }
        else
        {
            // Gamepad: tratar como grados/segundo
            yaw += delta.x * gamepadLookSensitivity * Time.deltaTime;
            pitch -= delta.y * gamepadLookSensitivity * Time.deltaTime;
        }

        if (clampPitch)
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Aplicar rotación: cuerpo rota en yaw, cámara rota en pitch
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        if (cameraTransform != null)
        {
            // Si la cámara es hija del jugador, ajusta su localRotation en pitch
            if (cameraTransform.IsChildOf(transform))
            {
                cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
            else
            {
                // Si la cámara no es hija, alinear yaw y pitch absolutos
                cameraTransform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }
    }

    private void HandleGroundCheck()
    {
        Vector3 origin = transform.position + groundCheckOffset;
        float sphereRadius = groundCheckRadius;
        float distance = groundCheckDistance + controller.skinWidth;

        isGrounded = false;
        groundNormal = Vector3.up;

        if (Physics.SphereCast(origin, sphereRadius, Vector3.down, out RaycastHit hit, distance, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundNormal = hit.normal;
            float angle = Vector3.Angle(groundNormal, Vector3.up);
            if (angle <= maxSlopeAngle + 0.5f && controller.isGrounded)
            {
                isGrounded = true;
                lastGroundedTime = Time.time;
                airJumpsUsed = 0;
            }
        }
        else
        {
            if (controller.isGrounded)
            {
                isGrounded = true;
                lastGroundedTime = Time.time;
                airJumpsUsed = 0;
            }
        }

        if (isGrounded && velocity.y < 0f)
        {
            velocity.y = groundedGravity;
        }
    }

    private void HandleMovement()
    {
        // moveInput es Vector2 [-1..1]
        Vector2 input = moveInput;
        input = input.sqrMagnitude > 1f ? input.normalized : input;

        // Dirección basada en cámara
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;
        camForward.y = 0f; camRight.y = 0f;
        camForward.Normalize(); camRight.Normalize();

        Vector3 inputDir = camForward * input.y + camRight * input.x;

        // Velocidad target con sprint
        float desiredSpeed = sprintHeld ? sprintSpeed : walkSpeed;
        targetSpeed = input.magnitude > 0.1f ? desiredSpeed : 0f;

        float accel = (targetSpeed > currentSpeed) ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * Time.deltaTime);

        // Rotación del cuerpo hacia el movimiento si hay input (útil en tercera persona)
        if (inputDir.sqrMagnitude > 0.001f)
        {
            float targetAngle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref rotationVelocity, rotationSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
            yaw = angle; // mantener sincronizado con look yaw
        }

        Vector3 moveDir = inputDir.normalized;
        if (isGrounded)
        {
            moveDir = Vector3.ProjectOnPlane(moveDir, groundNormal).normalized;
        }

        planarVelocity = moveDir * currentSpeed;
    }

    private void HandleJump()
    {
        bool canCoyote = Time.time - lastGroundedTime <= coyoteTime;

        if (jumpPressed)
        {
            if (isGrounded || canCoyote)
            {
                velocity.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
                isGrounded = false;
            }
            else if (airJumpsUsed < extraAirJumps)
            {
                velocity.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
                airJumpsUsed++;
            }
        }
    }

    private void ApplyGravity()
    {
        velocity.y += gravity * Time.deltaTime;
    }

    private void ApplySlopeSlideIfNeeded()
    {
        if (!isGrounded || !slideOnSteepSlopes) return;

        float slopeAngle = Vector3.Angle(groundNormal, Vector3.up);
        if (slopeAngle > maxSlopeAngle)
        {
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
            Vector3 slide = slopeDir * slopeSlideSpeed;
            planarVelocity += slide * Time.deltaTime;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 origin = transform.position + groundCheckOffset;
        Gizmos.DrawWireSphere(origin + Vector3.down * (groundCheckDistance), groundCheckRadius);
    }

    // Utilidad para normalizar ángulos a -180..180
    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {

        var col = hit.collider;

        // Añadimos al set del frame actual
        bool wasAlready = previousHits.Contains(col);
        currentHits.Add(col);

        if (!wasAlready)
        {
            OnEnterContact?.Invoke(col);
            // Debug.Log($"Enter contacto con: {col.name}");
        }
        else
        {
            OnStayContact?.Invoke(col);
           // Debug.Log("Chocando");
          //  audioSource.Play();
            // Debug.Log($"Stay contacto con: {col.name}");
        }
    }


   
    







}