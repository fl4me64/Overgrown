using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Mud : MonoBehaviour
{
    [Tooltip("1: no slowdown; 0.5: half speed; 0: stuck")]
    [Range(0f, 1f)]
    [SerializeField] private float playerSpeedMultiplier = 0.5f;
    [Range(0f, 1f)]
    [SerializeField] private float dogSpeedMultiplier = 0.6f;

    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string enemyTag = "Enemy";

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"Entered: {other}.");
        if (other.CompareTag(playerTag))
        {            
            var mower = other.GetComponentInParent<MowerController>();
            if (mower != null)
            {
                mower.ApplyMudPathSpeedModifier(GetInstanceID(), playerSpeedMultiplier);
            }
        }
        else if (other.CompareTag(enemyTag))
        {
            var dog = other.GetComponentInParent<DogFSM>();
            if (dog != null)
                dog.ApplyMudPathSpeedModifier(GetInstanceID(), dogSpeedMultiplier);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"Exited: {other}.");
        if (other.CompareTag(playerTag))
        {
            var mower = other.GetComponentInParent<MowerController>();
            if (mower != null)
            {
                mower.RemoveMudPathSpeedModifier(GetInstanceID());
            }
        }
        else if (other.CompareTag(enemyTag))
        {
            var dog = other.GetComponentInParent<DogFSM>();
            if (dog != null)
                dog.RemoveMudPathSpeedModifier(GetInstanceID());
        }
    }
}
