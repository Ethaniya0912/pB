using UnityEngine;

public class DenseForestSpawner : MonoBehaviour
{
    [SerializeField] private GameObject treePrefab;  // Cylinder 프리팹
    [SerializeField] private int treeCount = 20;
    [SerializeField] private float radius = 3f;

    private void Awake()
    {
        for (int i = 0; i < treeCount; i++)
        {
            var pos = transform.position + new Vector3(
                Random.Range(-radius, radius),
                1f,
                Random.Range(-radius, radius)
            );
            var tree = Instantiate(treePrefab, pos, Quaternion.identity, transform);
            tree.layer = LayerMask.NameToLayer("Terrain");
        }
    }
}