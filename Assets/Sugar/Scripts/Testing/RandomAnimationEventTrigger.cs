using System.Collections;
using UnityEngine;

public class RandomAnimationEventTrigger : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Vector2 waitTimeRange = new Vector2(1f, 3f);
    [SerializeField] private string intParameterName = "AnimID";
    [SerializeField] private string triggerParameterName = "Event";
    [SerializeField] private int maxRandomInt = 2;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        StartCoroutine(TriggerEvents());
    }

    private IEnumerator TriggerEvents()
    {
        while (true)
        {
            float waitTime = Random.Range(waitTimeRange.x, waitTimeRange.y);
            yield return new WaitForSeconds(waitTime);

            animator.SetInteger(intParameterName, Random.Range(0, maxRandomInt + 1));
            animator.SetTrigger(triggerParameterName);
        }
    }
}
