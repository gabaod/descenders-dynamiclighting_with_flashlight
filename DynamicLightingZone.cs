using UnityEngine;
using ModTool.Interface;

public class DynamicLightingZone : ModBehaviour
{
    [Header("References")]
    public GameObject startLine;
    public string trackedObjectName = "Player_Human"; // Name of the object to track
    public Light sunLight;

    [Header("Zone Settings")]
    public float darkZoneRadius = 100f;
    public float transitionWidth = 20f; // Smooth transition buffer

    [Header("Dark Zone Lighting")]
    public Color darkAmbientColor = new Color(0.1f, 0.1f, 0.15f);
    public float darkSunIntensity = 0.1f;
    public Color darkSunColor = new Color(0.3f, 0.3f, 0.4f);
    public Material darkSkybox;

    [Header("Normal Zone Lighting")]
    public Color normalAmbientColor = new Color(0.5f, 0.5f, 0.5f);
    public float normalSunIntensity = 1.0f;
    public Color normalSunColor = Color.white;
    public Material normalSkybox;

    [Header("Fog Settings")]
    public bool useFogInDarkZone = true;
    public Color darkFogColor = new Color(0.05f, 0.05f, 0.1f);
    public float darkFogDensity = 0.02f;

    public bool useFogInNormalZone = false;
    public Color normalFogColor = Color.gray;
    public float normalFogDensity = 0.005f;

    [Header("Flashlight Settings")]
    public bool useFlashlight = true;
    public GameObject flashlightObject; // Pre-created flashlight object with Light component
    public string flashlightParentBone = ""; // Optional: name of bone to attach to (e.g., "Handlebar")

    [Header("Flashlight Position")]
    public float flashlightOffsetX = 0f;
    public float flashlightOffsetY = 1f;
    public float flashlightOffsetZ = 1f;

    [Header("Flashlight Rotation")]
    public float flashlightRotationX = 0f; // Pitch (up/down)
    public float flashlightRotationY = 0f; // Yaw (left/right)
    public float flashlightRotationZ = 0f; // Roll

    [Header("Flashlight Properties")]
    public float flashlightIntensity = 2.0f;
    public float flashlightRange = 50f;
    public float flashlightSpotAngle = 60f;
    public Color flashlightColor = Color.white;
    public bool onlyInDarkZone = true; // Only enable flashlight in dark zone
    public bool flashlightCastsShadows = true;

    [Header("Debug")]
    public bool showDebugInfo = true;
    public bool showFlashlightDebug = true;
    public float debugLogInterval = 2.0f; // Log every N seconds instead of every frame

    private float currentBlend = 0f;
    private Transform startLineTransform;
    private Transform trackedTransform;
    private Light flashlight;
    private Transform flashlightParentTransform;
    private float storedNormalSunIntensity;
    private Color storedNormalAmbientColor;
    private Color storedNormalSunColor;
    private Material storedNormalSkybox;
    private bool hasStoredOriginals = false;
    private bool hasFoundTrackedObject = false;
    private bool hasSetupFlashlight = false;
    private float lastDebugLogTime = 0f;

    void Start()
    {
        Debug.Log("=== DynamicLightingZone Start ===");

        // Cache the startline transform reference
        if (startLine != null)
        {
            startLineTransform = startLine.transform;
            Debug.Log("StartLine found: " + startLine.name);
        }
        else
        {
            Debug.LogError("StartLine GameObject reference is missing!");
        }

        // Check flashlight object
        if (useFlashlight)
        {
            if (flashlightObject != null)
            {
                Debug.Log("Flashlight object assigned: " + flashlightObject.name);
                Debug.Log("Flashlight object position: " + flashlightObject.transform.position.ToString());
                Debug.Log("Flashlight object active: " + flashlightObject.activeInHierarchy);

                Light testLight = flashlightObject.GetComponent<Light>();
                if (testLight != null)
                {
                    Debug.Log("Light component found on flashlight!");
                    Debug.Log("Light type: " + testLight.type);
                    Debug.Log("Light enabled: " + testLight.enabled);
                    Debug.Log("Light intensity: " + testLight.intensity);
                    Debug.Log("Light range: " + testLight.range);
                }
                else
                {
                    Debug.LogError("NO Light component found on flashlight object!");
                }
            }
            else
            {
                Debug.LogError("Flashlight object is NULL - please assign it in the inspector!");
            }
        }

        // Try to find the tracked object by name
        FindTrackedObject();

        // Find sun light if not assigned
        if (sunLight == null)
        {
            Light[] lights = FindObjectsOfType<Light>();
            Debug.Log("Found " + lights.Length + " lights in scene");
            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    sunLight = light;
                    Debug.Log("Found directional light: " + light.gameObject.name);
                    break;
                }
            }
        }

        // Store original lighting settings if not manually set
        if (!hasStoredOriginals)
        {
            StoreOriginalLighting();
        }

        // Validate all required references
        if (darkSkybox == null)
        {
            Debug.LogWarning("Dark skybox material not assigned!");
        }

        // Setup flashlight if needed
        if (useFlashlight && flashlightObject != null)
        {
            SetupFlashlight();
        }

        Debug.Log("=== DynamicLightingZone Start Complete ===");
    }

    void FindTrackedObject()
    {
        if (string.IsNullOrEmpty(trackedObjectName))
        {
            Debug.LogError("Tracked object name is empty! Please specify the object name (e.g., 'Player_Human')");
            return;
        }

        GameObject foundObject = GameObject.Find(trackedObjectName);

        if (foundObject != null)
        {
            trackedTransform = foundObject.transform;
            hasFoundTrackedObject = true;
            Debug.Log("Successfully found and tracking: " + trackedObjectName);
            Debug.Log("Tracked object position: " + trackedTransform.position.ToString());

            // Try to setup flashlight if we have the tracked object now
            if (useFlashlight && flashlightObject != null && !hasSetupFlashlight)
            {
                SetupFlashlight();
            }
        }
        else
        {
            Debug.LogWarning("Could not find object named '" + trackedObjectName + "'. Will retry each frame until found.");
        }
    }

    void SetupFlashlight()
    {
        Debug.Log("=== Setting up Flashlight ===");

        if (trackedTransform == null || flashlightObject == null)
        {
            Debug.LogWarning("Cannot setup flashlight - missing tracked object or flashlight object");
            Debug.LogWarning("trackedTransform null: " + (trackedTransform == null));
            Debug.LogWarning("flashlightObject null: " + (flashlightObject == null));
            return;
        }

        // Get or add Light component to the flashlight object
        flashlight = flashlightObject.GetComponent<Light>();
        if (flashlight == null)
        {
            Debug.LogError("Flashlight object does not have a Light component!");
            return;
        }

        Debug.Log("Found Light component on flashlight");

        // Find parent bone if specified
        if (!string.IsNullOrEmpty(flashlightParentBone))
        {
            Transform[] allTransforms = trackedTransform.GetComponentsInChildren<Transform>();
            Debug.Log("Searching " + allTransforms.Length + " transforms for bone: " + flashlightParentBone);

            foreach (Transform t in allTransforms)
            {
                if (t.name == flashlightParentBone)
                {
                    flashlightParentTransform = t;
                    flashlightObject.transform.SetParent(flashlightParentTransform);
                    Debug.Log("Attached flashlight to bone: " + flashlightParentBone);
                    break;
                }
            }

            if (flashlightParentTransform == null)
            {
                Debug.LogWarning("Could not find bone named '" + flashlightParentBone + "', attaching to root instead");
                flashlightObject.transform.SetParent(trackedTransform);
            }
        }
        else
        {
            // Attach directly to tracked object
            Debug.Log("Attaching flashlight directly to tracked object");
            flashlightObject.transform.SetParent(trackedTransform);
        }

        // Apply position offset
        Vector3 offset = new Vector3(flashlightOffsetX, flashlightOffsetY, flashlightOffsetZ);
        flashlightObject.transform.localPosition = offset;

        // Apply rotation offset
        Quaternion rotation = Quaternion.Euler(flashlightRotationX, flashlightRotationY, flashlightRotationZ);
        flashlightObject.transform.localRotation = rotation;

        // Configure the light
        flashlight.type = LightType.Spot;
        flashlight.intensity = flashlightIntensity;
        flashlight.range = flashlightRange;
        flashlight.spotAngle = flashlightSpotAngle;
        flashlight.color = flashlightColor;
        flashlight.shadows = flashlightCastsShadows ? LightShadows.Soft : LightShadows.None;

        // Make sure it's enabled for testing
        flashlight.enabled = true;

        Debug.Log("Flashlight configuration:");
        Debug.Log("  Type: " + flashlight.type);
        Debug.Log("  Intensity: " + flashlight.intensity);
        Debug.Log("  Range: " + flashlight.range);
        Debug.Log("  Spot Angle: " + flashlight.spotAngle);
        Debug.Log("  Color: " + flashlight.color);
        Debug.Log("  Enabled: " + flashlight.enabled);
        Debug.Log("  Local Position: " + flashlightObject.transform.localPosition);
        Debug.Log("  Local Rotation: " + flashlightObject.transform.localRotation.eulerAngles);
        Debug.Log("  World Position: " + flashlightObject.transform.position);
        Debug.Log("  Forward: " + flashlightObject.transform.forward);
        Debug.Log("  Parent: " + (flashlightObject.transform.parent != null ? flashlightObject.transform.parent.name : "NULL"));

        hasSetupFlashlight = true;
        Debug.Log("=== Flashlight setup complete ===");
    }

    void StoreOriginalLighting()
    {
        // If normal values aren't set, use current scene values
        if (normalSkybox == null)
        {
            storedNormalSkybox = RenderSettings.skybox;
        }
        else
        {
            storedNormalSkybox = normalSkybox;
        }

        if (sunLight != null)
        {
            storedNormalSunIntensity = normalSunIntensity > 0 ? normalSunIntensity : sunLight.intensity;
            storedNormalSunColor = normalSunColor;
        }

        storedNormalAmbientColor = normalAmbientColor;
        hasStoredOriginals = true;
    }

    void Update()
    {
        // If we haven't found the tracked object yet, keep trying
        if (!hasFoundTrackedObject)
        {
            FindTrackedObject();
            if (!hasFoundTrackedObject)
            {
                return; // Skip this frame if still not found
            }
        }

        if (startLineTransform == null)
        {
            Debug.LogWarning("StartLine transform is null!");
            return;
        }

        if (trackedTransform == null)
        {
            Debug.LogWarning("Tracked object transform is null!");
            hasFoundTrackedObject = false; // Reset flag to retry finding
            return;
        }

        // Calculate distance from the tracked object to the startline
        float distance = Vector3.Distance(trackedTransform.position, startLineTransform.position);

        // Periodic debug logging (not every frame)
        bool shouldLog = Time.time - lastDebugLogTime > debugLogInterval;

        if (showDebugInfo && shouldLog)
        {
            Debug.Log("Distance from startline: " + distance.ToString("F2") + "m | Current blend: " + currentBlend.ToString("F2"));
            lastDebugLogTime = Time.time;
        }

        // Calculate blend factor (0 = dark zone, 1 = normal zone)
        float targetBlend;
        if (distance < darkZoneRadius)
        {
            // Inside dark zone
            targetBlend = 0f;
        }
        else if (distance < darkZoneRadius + transitionWidth)
        {
            // In transition zone
            targetBlend = (distance - darkZoneRadius) / transitionWidth;
        }
        else
        {
            // Outside dark zone (normal lighting)
            targetBlend = 1f;
        }

        // Smooth transition
        currentBlend = Mathf.Lerp(currentBlend, targetBlend, Time.deltaTime * 2f);

        // Apply lighting changes
        ApplyLighting(currentBlend);

        // Update flashlight
        UpdateFlashlight(currentBlend, shouldLog);
    }

    void ApplyLighting(float blend)
    {
        // Interpolate ambient light
        RenderSettings.ambientLight = Color.Lerp(darkAmbientColor, storedNormalAmbientColor, blend);

        // Interpolate sun intensity and color
        if (sunLight != null)
        {
            sunLight.intensity = Mathf.Lerp(darkSunIntensity, storedNormalSunIntensity, blend);
            sunLight.color = Color.Lerp(darkSunColor, storedNormalSunColor, blend);
        }

        // Blend skybox (switch at midpoint)
        if (darkSkybox != null && storedNormalSkybox != null)
        {
            if (blend < 0.5f && RenderSettings.skybox != darkSkybox)
            {
                RenderSettings.skybox = darkSkybox;
                DynamicGI.UpdateEnvironment();
            }
            else if (blend >= 0.5f && RenderSettings.skybox != storedNormalSkybox)
            {
                RenderSettings.skybox = storedNormalSkybox;
                DynamicGI.UpdateEnvironment();
            }
        }

        // Handle fog based on user settings
        bool shouldUseFog = blend < 0.5f ? useFogInDarkZone : useFogInNormalZone;
        RenderSettings.fog = shouldUseFog;

        if (shouldUseFog)
        {
            // Interpolate fog settings
            RenderSettings.fogColor = Color.Lerp(darkFogColor, normalFogColor, blend);
            RenderSettings.fogDensity = Mathf.Lerp(darkFogDensity, normalFogDensity, blend);
        }
    }

    void UpdateFlashlight(float blend, bool shouldLog)
    {
        if (!useFlashlight || flashlight == null)
        {
            if (showFlashlightDebug && shouldLog)
            {
                Debug.Log("Flashlight update skipped - useFlashlight: " + useFlashlight + " | flashlight null: " + (flashlight == null));
            }
            return;
        }

        // Update flashlight position and rotation in case they changed
        Vector3 offset = new Vector3(flashlightOffsetX, flashlightOffsetY, flashlightOffsetZ);
        flashlightObject.transform.localPosition = offset;

        Quaternion rotation = Quaternion.Euler(flashlightRotationX, flashlightRotationY, flashlightRotationZ);
        flashlightObject.transform.localRotation = rotation;

        // Update flashlight properties in case they changed in inspector
        flashlight.intensity = flashlightIntensity;
        flashlight.range = flashlightRange;
        flashlight.spotAngle = flashlightSpotAngle;
        flashlight.color = flashlightColor;

        // Enable/disable based on zone if onlyInDarkZone is true
        bool shouldBeEnabled;
        if (onlyInDarkZone)
        {
            shouldBeEnabled = blend < 0.5f; // On in dark zone, off in normal zone
        }
        else
        {
            shouldBeEnabled = true; // Always on
        }

        flashlight.enabled = shouldBeEnabled;

        if (showFlashlightDebug && shouldLog)
        {
            Debug.Log("=== Flashlight Status ===");
            Debug.Log("  Enabled: " + flashlight.enabled);
            Debug.Log("  Should be enabled: " + shouldBeEnabled);
            Debug.Log("  Intensity: " + flashlight.intensity);
            Debug.Log("  Range: " + flashlight.range);
            Debug.Log("  Current blend: " + blend.ToString("F2"));
            Debug.Log("  Local Position: " + flashlightObject.transform.localPosition);
            Debug.Log("  World Position: " + flashlightObject.transform.position);
            Debug.Log("  Local Rotation: " + flashlightObject.transform.localRotation.eulerAngles);
            Debug.Log("  Forward direction: " + flashlightObject.transform.forward);
            Debug.Log("  Is active in hierarchy: " + flashlightObject.activeInHierarchy);
        }
    }

    // Visualize the zone in editor
    void OnDrawGizmos()
    {
        if (startLine != null)
        {
            // Dark zone
            Gizmos.color = new Color(0.5f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(startLine.transform.position, darkZoneRadius);

            // Transition zone
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.DrawWireSphere(startLine.transform.position, darkZoneRadius + transitionWidth);

            // Line from tracked object to startline
            if (hasFoundTrackedObject && trackedTransform != null && Application.isPlaying)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(trackedTransform.position, startLine.transform.position);

                // Visualize flashlight position and direction
                if (useFlashlight && flashlightObject != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(flashlightObject.transform.position, 0.5f);
                    Gizmos.DrawRay(flashlightObject.transform.position, flashlightObject.transform.forward * (flashlight != null ? flashlight.range : flashlightRange));
                }
            }
        }
    }
}
