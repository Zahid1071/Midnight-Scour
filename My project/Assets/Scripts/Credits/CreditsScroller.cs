using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class CreditsScroller : MonoBehaviour
{
    [Header("Scroll Settings")]
    [SerializeField] private float scrollSpeed = 80f;
    [SerializeField] private float endYPosition = 2000f;

    [Header("Fade Settings")]
    [SerializeField] private Image fadeImage;
    [SerializeField] private float fadeDuration = 1f;

    [Header("Scene Settings")]
    [SerializeField] private string menuSceneName = "MainMenu";

    private RectTransform contentRect;
    private bool isTransitioning;

    private void Start()
    {
        contentRect = GetComponent<RectTransform>();

        if (fadeImage != null)
        {
            fadeImage.color = new Color(0f, 0f, 0f, 0f);
            fadeImage.raycastTarget = false;
        }
    }

    private void Update()
    {
        if (isTransitioning)
            return;

        contentRect.anchoredPosition += Vector2.up * scrollSpeed * Time.deltaTime;

        if (contentRect.anchoredPosition.y >= endYPosition)
        {
            GoToMainMenu();
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space))
        {
            GoToMainMenu();
        }
    }

    private void GoToMainMenu()
    {
        if (isTransitioning)
            return;

        isTransitioning = true;

        if (fadeImage != null)
        {
            StartCoroutine(FadeAndLoadScene());
        }
        else
        {
            SceneManager.LoadScene(menuSceneName);
        }
    }

    private IEnumerator FadeAndLoadScene()
    {
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float alpha = Mathf.Clamp01(timer / fadeDuration);
            fadeImage.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        SceneManager.LoadScene(menuSceneName);
    }
}
