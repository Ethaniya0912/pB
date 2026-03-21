using UnityEngine;

public class ParrySparkGPUManager : MonoBehaviour
{
    struct SparkData
    {
        public Vector4 positionAge;
        public Vector4 velocityLife;
        public Vector4 baseSize;
    }

    [Header("Dependencies")]
    public ComputeShader physicsCompute;
    public Material renderMaterial;

    [Header("Settings")]
    public int maxSparks = 1000;
    public Vector2 particleBaseSize = new Vector2(0.1f, 0.3f);

    [Header("Auto Play & Destroy")]
    public bool playOnEnable = true;
    public int autoBurstCount = 100;
    public float autoDestroyTime = 2.0f;

    private ComputeBuffer sparkBuffer;
    private ComputeBuffer argsBuffer;
    private Mesh quadMesh;
    private SparkData[] sparkDataArray;

    void Awake()
    {
        // Remove interfering components safely
        if (TryGetComponent<MeshRenderer>(out var mr)) Destroy(mr);
        if (TryGetComponent<MeshFilter>(out var mf)) Destroy(mf);
        if (TryGetComponent<ParticleSystem>(out var ps)) Destroy(ps);

        if (physicsCompute == null || renderMaterial == null)
        {
            Debug.LogError("[GPU Spark Error] ComputeShader or Material is missing!");
            return;
        }

        renderMaterial = new Material(renderMaterial);

        // Securely pass shader parameters
        renderMaterial.SetFloat("_ShutterAngle", 180f);
        renderMaterial.SetFloat("_TargetFPS", 60f);

        // 48 bytes per element (Vector4 x 3)
        sparkBuffer = new ComputeBuffer(maxSparks, 48);
        sparkDataArray = new SparkData[maxSparks];

        for (int i = 0; i < maxSparks; i++)
        {
            sparkDataArray[i] = new SparkData
            {
                positionAge = new Vector4(0, 0, 0, 100f),
                velocityLife = new Vector4(0, 0, 0, 1f)
            };
        }

        sparkBuffer.SetData(sparkDataArray);

        // Re-enable ArgsBuffer for DrawMeshInstancedIndirect
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        quadMesh = CreateQuadMesh();

        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)quadMesh.GetIndexCount(0);
        args[1] = (uint)maxSparks;
        argsBuffer.SetData(args);

        renderMaterial.SetBuffer("_SparkBuffer", sparkBuffer);

        Debug.Log($"[GPU Spark] {gameObject.name} - Memory allocated and material instantiated.");
    }

    void OnEnable()
    {
        if (playOnEnable)
        {
            Debug.Log($"[GPU Spark] {gameObject.name} - Auto burst sequence started!");
            BurstSparks(transform.position, transform.forward, autoBurstCount);

            Destroy(gameObject, autoDestroyTime);
        }
    }

    void Update()
    {
        if (sparkBuffer == null || argsBuffer == null) return;

        int kernel = physicsCompute.FindKernel("UpdateSparks");

        // Clamp DeltaTime to prevent physics breakdown during Hit Stops
        float safeDeltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.05f);

        // Securely pass values using SetFloat and SetInt
        physicsCompute.SetFloat("_DeltaTime", safeDeltaTime);
        physicsCompute.SetInt("_MaxSparks", maxSparks);

        physicsCompute.SetBuffer(kernel, "_SparkBuffer", sparkBuffer);

        int threadGroups = Mathf.CeilToInt(maxSparks / 64f);
        physicsCompute.Dispatch(kernel, threadGroups, 1, 1);

        Bounds renderBounds = new Bounds(transform.position, Vector3.one * 100f);

        // Safely submit to URP Render Pipeline
        Graphics.DrawMeshInstancedIndirect(quadMesh, 0, renderMaterial, renderBounds, argsBuffer);
    }

    public void BurstSparks(Vector3 hitPosition, Vector3 baseDirection, int count)
    {
        if (sparkBuffer == null) return;

        sparkBuffer.GetData(sparkDataArray);

        if (baseDirection == Vector3.zero)
            baseDirection = Vector3.up;

        int spawned = 0;
        for (int i = 0; i < maxSparks && spawned < count; i++)
        {
            if (sparkDataArray[i].positionAge.w >= sparkDataArray[i].velocityLife.w)
            {
                // Spherical scattering for explosive effect
                Vector3 randomOffset = Random.insideUnitSphere * 2.5f;
                Vector3 randDir = (baseDirection + randomOffset).normalized;

                Vector3 vel = randDir * Random.Range(15f, 40f);
                float life = Random.Range(0.2f, 0.6f);
                float randomScale = Random.Range(0.5f, 1.5f);

                // Add jitter to starting position
                Vector3 jitterPos = hitPosition + Random.insideUnitSphere * 0.1f;

                sparkDataArray[i].positionAge = new Vector4(jitterPos.x, jitterPos.y, jitterPos.z, 0f);
                sparkDataArray[i].velocityLife = new Vector4(vel.x, vel.y, vel.z, life);
                sparkDataArray[i].baseSize = new Vector4(particleBaseSize.x * randomScale, particleBaseSize.y * randomScale, 0f, 0f);

                spawned++;
            }
        }
        sparkBuffer.SetData(sparkDataArray);

        Debug.Log($"[GPU Spark] Blast initialized! Position: {hitPosition}, Target Dir: {baseDirection}, Amount: {spawned}");
    }

    Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return mesh;
    }

    private void OnDestroy()
    {
        if (sparkBuffer != null) sparkBuffer.Release();
        if (argsBuffer != null) argsBuffer.Release();
    }
}