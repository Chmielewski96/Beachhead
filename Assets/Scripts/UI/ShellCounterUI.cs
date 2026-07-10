using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Screen-space shell counter. Purely event-driven: subscribes to
/// ResourceManager and never polls. Flashes red when a spend is refused.
///
/// Note the subscription happens in Start, not OnEnable/Awake - the
/// ResourceManager singleton is assigned in ITS Awake, and Unity gives no
/// ordering guarantee between different scripts' Awake calls. Subscribing
/// in Start guarantees every Awake in the scene has already run. This is
/// the classic singleton-vs-subscriber ordering trap from the plan.
/// </summary>
public class ShellCounterUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color insufficientColor = new Color(1f, 0.35f, 0.35f);
    [SerializeField] private float flashDuration = 0.35f;

    private Color baseColor;
    private Coroutine flashRoutine;

    private void Start()
    {
        baseColor = label.color;

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnShellsChanged += HandleShellsChanged;
            ResourceManager.Instance.OnSpendFailed += HandleSpendFailed;
            HandleShellsChanged(ResourceManager.Instance.Shells);
        }
    }

    private void OnDestroy()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnShellsChanged -= HandleShellsChanged;
            ResourceManager.Instance.OnSpendFailed -= HandleSpendFailed;
        }
    }

    private void HandleShellsChanged(int shells)
    {
        label.text = shells.ToString();
    }

    private void HandleSpendFailed()
    {
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
