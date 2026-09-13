using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UISpriteSwapper : MonoBehaviour
{
    [Header("UI Element to Swap")]
    [SerializeField] public Image targetImage; // The UI Image component

    [Header("Sprites to Cycle Through")]
    [SerializeField] public Sprite[] sprites; // Array of sprites to swap between

    [Header("Swap Settings")]
    [SerializeField] public float swapInterval = 2f; // Seconds between swaps
    [SerializeField] public bool loop = true; // Whether to loop indefinitely

    private int currentIndex = 0;
    private Coroutine swapRoutine;

    private void Awake()
    {
        // Validate references
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }

        if (targetImage == null)
        {
            Debug.LogError("UISpriteSwapper: No Image component found or assigned.");
            enabled = false;
            return;
        }

        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogError("UISpriteSwapper: No sprites assigned.");
            enabled = false;
            return;
        }
    }

    private void OnEnable()
    {
        // Start swapping when enabled
        swapRoutine = StartCoroutine(SwapSprites());
    }

    private void OnDisable()
    {
        // Stop swapping when disabled
        if (swapRoutine != null)
        {
            StopCoroutine(swapRoutine);
        }
    }

    private IEnumerator SwapSprites()
    {
        while (true)
        {
            // Set the current sprite
            targetImage.sprite = sprites[currentIndex];

            // Move to the next sprite
            currentIndex++;

            // Loop or stop
            if (currentIndex >= sprites.Length)
            {
                if (loop)
                {
                    currentIndex = 0;
                }
                else
                {
                    yield break; // Stop coroutine if not looping
                }
            }

            // Wait before swapping again
            yield return new WaitForSeconds(swapInterval);
        }
    }
}