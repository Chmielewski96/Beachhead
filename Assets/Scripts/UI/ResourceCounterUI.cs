using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Screen-space counter for ONE resource type - drop one on a TMP text per
/// resource and set the type. Purely event-driven; filters the shared
/// ResourceManager events down to its own type. Flashes red when a spend
/// of its resource is refused.
///
/// Subscription happens in Start, not Awake/OnEnable: the ResourceManager
/// singleton is assigned in ITS Awake and Unity guarantees no ordering
/// between different scripts' Awakes. Start runs after every Awake.
/// </summary>
public class ResourceCounterUI : MonoBehaviour
{
    [SerializeField] private ResourceType resourceType;
    [SerializeField] private TMP_Text label;
    [Tooltip("Shown before the number, e.g. 'Sand: '")]
    [SerializeField] private string prefix = "";
    [SerializeField] private Color insufficientColor = new Color(1f, 0.35f, 0.35f);
    [SerializeField] private float flashDuration = 0.35f;

    private Color baseColor;
    private Coroutine flashRoutine;

    private void Start()
    {
        baseColor = label.color;

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnResourceChanged += HandleResourceChanged;
            ResourceManager.Instance.OnSpendFailed += HandleSpendFailed;
            HandleResourceChanged(resourceType, ResourceManager.Instance.GetAmount(resourceType));
        }
    }

    private void OnDestroy()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnResourceChanged -= HandleResourceChanged;
            ResourceManager.Instance.OnSpendFailed -= HandleSpendFailed;
        }
    }

    private void HandleResourceChanged(ResourceType type, int newAmount)
    {
        if (type != resourceType)
            return;

        label.text = prefix + newAmount;
    }

    private void HandleSpendFailed(ResourceType type)
    {
        if (type != resourceType)
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(Flash());
    }

    private IEnumerator Flash()
    {
        label.color = insufficientColor;
        yield return new WaitForSeconds(flashDuration);
        label.color = baseColor;
        flashRoutine = null;
    }
}
