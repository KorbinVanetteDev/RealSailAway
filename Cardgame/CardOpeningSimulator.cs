using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;



public class CardOpeningSimulator : MonoBehaviour
{
    [Header("Scene Bindings")]
    [SerializeField] private string canvasName = "Canvas";
    [SerializeField] private string cardTemplateName = "Card";
    [SerializeField] private Sprite packBodySprite;
    [SerializeField] private Sprite packTopFlapSprite;

    [Header("Pack Flow")]
    [SerializeField] private bool autoOpenOnStart = false;
    [SerializeField] private float autoOpenDelay = 0.5f;
    [SerializeField] private float ripDuration = 0.35f;
    [SerializeField] private float cardLaunchDuration = 0.4f;
    [SerializeField] private float cardStaggerDelay = 0.12f;

    [Header("Audio")]
    [SerializeField] private AudioClip packRipClip;
    [SerializeField] private AudioClip cardFlipClip;
    [SerializeField] private AudioClip commonRevealClip;
    [SerializeField] private AudioClip fullArtRevealClip;
    [SerializeField] private AudioSource audioSource;

    private RectTransform canvasRoot;
    private ThisCard cardTemplate;
    private RectTransform cardTemplateRect;

    private RectTransform simulatorRoot;
    private RectTransform cardSpawnRoot;
    private RectTransform packRoot;
    private RectTransform packBodyRect;
    private Image packBodyImage;
    private RectTransform packTopRect;
    private Image packTopFlapImage;
    private CanvasGroup packCanvasGroup;
    private Button packButton;
    private Button collectButton;
    private Text statusText;
    private Text collectButtonText;
    private Text bankedText;

    private readonly List<GameObject> spawnedCards = new List<GameObject>();
    private readonly List<int> pulledCardIndices = new List<int>();
    private readonly Dictionary<GameObject, bool> cardFlipStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<GameObject, bool> cardSoundPlayed = new Dictionary<GameObject, bool>();
    private readonly Dictionary<GameObject, string> cardResourceTexts = new Dictionary<GameObject, string>();
    private readonly List<string> revealedResourceTexts = new List<string>();
    private readonly HashSet<GameObject> revealedCards = new HashSet<GameObject>();
    private int pendingResources;
    private int pendingWood;
    private int pendingIron;
    private int pendingCloth;
    private int totalCollectedResources;
    private int lastCardIndex = -1;
    private bool isOpening;
    private bool isFlipping;

    /// <summary>Initialize the simulator, find scene references, and optionally auto-open the pack.</summary>
    private void Start()
    {
        // Reset the pack opened flag for each new pack purchase
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ResetPackOpened();
        }

        ResolveSceneReferences();
        BuildPackUi();
        HideTemplateCard();

        // Ensure cursor is visible for the card-opening UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (autoOpenOnStart)
        {
            StartCoroutine(AutoOpenRoutine());
        }
    }

    /// <summary>Locate the Canvas and Card template GameObjects by name for runtime reference.</summary>
    private void ResolveSceneReferences()
    {
        GameObject canvasObject = GameObject.Find(canvasName);
        if (canvasObject != null)
        {
            canvasRoot = canvasObject.GetComponent<RectTransform>();
        }

        GameObject cardObject = GameObject.Find(cardTemplateName);
        if (cardObject != null)
        {
            cardTemplate = cardObject.GetComponent<ThisCard>();
            cardTemplateRect = cardObject.GetComponent<RectTransform>();
        }
    }

    /// <summary>Create and layout all UI elements (pack, cards spawn area, buttons, status text) via code.</summary>
    private void BuildPackUi()
    {
        if (canvasRoot == null || cardTemplate == null)
        {
            return;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        simulatorRoot = CreateRect("PackSimulator", canvasRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        simulatorRoot.SetAsLastSibling();

        // Ensure there is an AudioSource available for UI sounds
        if (audioSource == null)
        {
            audioSource = simulatorRoot.gameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = simulatorRoot.gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
        }

        // If audio clips weren't assigned in inspector (e.g. simulator created at runtime by another class),
        // attempt to auto-load them from Resources/Audio or top-level Resources folder.
        EnsureAudioClipsLoaded();

        cardSpawnRoot = CreateRect("CardSpawnRoot", simulatorRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1400f, 800f));

        packRoot = CreateRect("PackRoot", simulatorRoot, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f), Vector2.zero, new Vector2(360f, 480f));
        packRoot.localRotation = Quaternion.Euler(0f, 0f, -8f);
        packCanvasGroup = packRoot.gameObject.AddComponent<CanvasGroup>();

        // Main card back image - full pack body
        packBodyRect = CreateRect("PackBody", packRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(300f, 420f));
        packBodyImage = CreatePanel(packBodyRect, "PackBodyImage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 420f), Color.white);

        // Top flap uses the sprite's own proportions so a full-size RIPPER image is not forced into a thin strip.
        packTopRect = CreateRect("PackTopFlap", packRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(300f, 420f));
        packTopFlapImage = CreatePanel(packTopRect, "TopFlap", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 420f), new Color32(220, 220, 220, 255));

        ApplyPackBackArtwork();
        ApplyPackTopArtwork(packTopFlapImage);

        Image packButtonImage = packRoot.gameObject.AddComponent<Image>();
        packButtonImage.color = new Color(1f, 1f, 1f, 0f);
        packButton = packRoot.gameObject.AddComponent<Button>();
        packButton.onClick.AddListener(OnPackClicked);

        collectButton = CreateButton(simulatorRoot, "CollectButton", "Collect Resources", font, new Vector2(0.5f, 0.055f), new Vector2(340f, 64f), new Color32(81, 160, 92, 255));
        collectButtonText = collectButton.GetComponentInChildren<Text>();
        collectButton.onClick.AddListener(CollectResources);
        collectButton.gameObject.SetActive(false);

        statusText = CreateText(simulatorRoot, "Status", "Click the pack to open it", font, 28, FontStyle.Bold, TextAnchor.MiddleCenter, new Color32(244, 236, 219, 255), new Vector2(0.5f, 0.88f), new Vector2(760f, 44f), Vector2.zero);
        AddOutline(statusText, new Color32(13, 19, 29, 210), new Vector2(1f, -1f));
        bankedText = CreateText(simulatorRoot, "Banked", "Collected Resources: 0", font, 24, FontStyle.Bold, TextAnchor.MiddleCenter, new Color32(214, 235, 211, 255), new Vector2(0.5f, 0.02f), new Vector2(760f, 40f), Vector2.zero);
        AddOutline(bankedText, new Color32(13, 19, 29, 210), new Vector2(1f, -1f));

    }

    /// <summary>Disable the card template GameObject so it doesn't appear in the scene.</summary>
    private void HideTemplateCard()
    {
        if (cardTemplateRect != null)
        {
            cardTemplateRect.gameObject.SetActive(false);
        }
    }

    /// <summary>Load and assign the main pack body sprite, with fallback resource paths.</summary>
    private void ApplyPackBackArtwork()
    {
        if (packBodyImage == null)
        {
            return;
        }

        Sprite backSprite = packBodySprite;
        if (backSprite == null)
        {
            backSprite = Resources.Load<Sprite>("CARDGAMEPART/PackBack");
        }

        if (backSprite == null)
        {
            backSprite = Resources.Load<Sprite>("PackBack");
        }

        if (backSprite == null)
        {
            return;
        }

        packBodyImage.sprite = backSprite;
        packBodyImage.overrideSprite = backSprite;
        packBodyImage.preserveAspect = true;
        packBodyImage.color = Color.white;
    }

    /// <summary>Load and assign the pack top flap (ripper) sprite with preserve aspect ratio.</summary>
    private void ApplyPackTopArtwork(Image topFlapImage)
    {
        if (topFlapImage == null)
        {
            return;
        }

        Sprite flapSprite = packTopFlapSprite;
        if (flapSprite == null)
        {
            flapSprite = Resources.Load<Sprite>("CARDGAMEPART/Ripper");
        }

        if (flapSprite == null)
        {
            flapSprite = Resources.Load<Sprite>("Ripper");
        }

        if (flapSprite != null)
        {
            topFlapImage.sprite = flapSprite;
            topFlapImage.overrideSprite = flapSprite;
            topFlapImage.preserveAspect = true;
            topFlapImage.color = Color.white;
            topFlapImage.SetNativeSize();
        }
    }

    /// <summary>Update pack body and flap sprites dynamically (called by other pack systems).</summary>
    public void ConfigurePackArt(Sprite bodySprite, Sprite topFlapSprite)
    {
        packBodySprite = bodySprite;
        packTopFlapSprite = topFlapSprite;

        if (packBodyImage != null)
        {
            ApplyPackBackArtwork();
        }

        if (packTopFlapImage != null)
        {
            ApplyPackTopArtwork(packTopFlapImage);
        }
    }

    /// <summary>Delay before auto-opening the pack if autoOpenOnStart is enabled.</summary>
    private IEnumerator AutoOpenRoutine()
    {
        yield return new WaitForSeconds(autoOpenDelay);
        OnPackClicked();
    }

    /// <summary>Handle pack click event; validate state and start the opening sequence.</summary>
    private void OnPackClicked()
    {
        if (isOpening || cardTemplate == null || CardDatabase.cardList.Count == 0)
        {
            return;
        }
        // Prevent reopening if GameManager says a pack has already been opened this session
        if (GameManager.Instance != null && GameManager.Instance.HasOpenedPack())
        {
            if (statusText != null)
                statusText.text = "Pack already opened";
            return;
        }

        if (pendingResources > 0)
        {
            statusText.text = "Collect current resources first";
            return;
        }

        StartCoroutine(OpenPackRoutine());
    }

    /// <summary>Orchestrate full pack opening: rip, spawn cards, animate them, then fade pack away.</summary>
    private IEnumerator OpenPackRoutine()
    {
        isOpening = true;
        packButton.interactable = false;
        collectButton.gameObject.SetActive(false);
        //statusText.text = "Ripping open the top...";

        ClearSpawnedCards();
        pulledCardIndices.Clear();
        pendingResources = 0;
        pendingWood = 0;
        pendingIron = 0;
        pendingCloth = 0;
        revealedResourceTexts.Clear();
        revealedCards.Clear();

        yield return AnimatePackRip();

        //statusText.text = "Cards coming out...";

        Vector2[] targets =
        {
            new Vector2(-360f, 40f),
            new Vector2(0f, 40f),
            new Vector2(360f, 40f)
        };

        // Choose three cards by weighted sampling (with replacement, fully randomized)
        List<int> picks = new List<int>();
        for (int k = 0; k < 3; k++)
        {
            picks.Add(ChooseWeightedIndexWithReplacement());
        }

        for (int i = 0; i < picks.Count; i++)
        {
            int cardIndex = picks[i];
            pulledCardIndices.Add(cardIndex);

            Card cardData = CardDatabase.cardList[cardIndex];
            pendingResources += cardData.power;
            string cardResourceText = BuildCardResourceText(cardData);

            // Calculate specific resources based on card name
            if (cardData.cardName == "Wood")
                pendingWood += cardData.power;
            else if (cardData.cardName == "Iron")
                pendingIron += cardData.power;
            else if (cardData.cardName == "Cloth")
                pendingCloth += cardData.power;

            GameObject cardInstance = Instantiate(cardTemplate.gameObject, cardSpawnRoot, false);
            cardInstance.name = "OpenedCard_" + i;
            cardInstance.SetActive(true);
            spawnedCards.Add(cardInstance);

            ThisCard cardView = cardInstance.GetComponent<ThisCard>();
            RectTransform cardRect = cardInstance.GetComponent<RectTransform>();

            cardView.cardBack = true;
            cardView.ApplyCard(cardIndex);
            cardResourceTexts[cardInstance] = cardResourceText;

            CanvasGroup group = cardInstance.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = cardInstance.AddComponent<CanvasGroup>();
            }

            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);

            Vector2 startPos = packRoot.anchoredPosition + new Vector2(0f, 130f);
            yield return AnimateCardFromPack(cardRect, group, startPos, targets[i], i);

            // Add click handler for card flip
            Button cardButton = cardInstance.AddComponent<Button>();
            int capturedCardIndex = cardIndex;
            cardButton.onClick.AddListener(() => OnCardClicked(cardInstance, capturedCardIndex));
            cardFlipStates[cardInstance] = true; // showing back side initially

            yield return new WaitForSeconds(cardStaggerDelay);
        }

        // Wait briefly to let cards settle
        yield return new WaitForSeconds(0.3f);

        // Animate pack disappearing (fade and shrink)
        yield return AnimatePackDisappear();

        statusText.gameObject.SetActive(false);
        bankedText.gameObject.SetActive(false);

        UpdateCollectButtonText();
        UpdateCollectButtonState();
        isOpening = false;
    }

    /// <summary>Animate the pack top flap tearing off with rotation, wobble, and fade out.</summary>
    private IEnumerator AnimatePackRip()
    {
        // Play pack rip sound
        if (audioSource != null && packRipClip != null)
        {
            audioSource.PlayOneShot(packRipClip);
        }
        Vector2 startPos = new Vector2(0f, 190f);
        Vector2 endPos = new Vector2(-160f, 250f);
        Quaternion startRot = Quaternion.identity;
        Quaternion endRot = Quaternion.Euler(0f, 0f, -90f);
        Color startColor = Color.white;
        Color endColor = new Color(1f, 1f, 1f, 0f);

        float elapsed = 0f;
        while (elapsed < ripDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / ripDuration);
            float eased = EaseOutCubic(t);

            // Dynamic wobble for tearing effect
            float wobble = Mathf.Sin(t * Mathf.PI * 1.5f) * 10f;
            packTopRect.anchoredPosition = Vector2.Lerp(startPos, endPos, eased) + new Vector2(0f, wobble);
            packTopRect.localRotation = Quaternion.Lerp(startRot, endRot, eased);
            packTopRect.localScale = Vector3.Lerp(Vector3.one, new Vector3(1.1f, 0.8f, 1f), eased);

            foreach (Image image in packTopRect.GetComponentsInChildren<Image>(true))
            {
                image.color = Color.Lerp(startColor, endColor, eased);
            }

            yield return null;
        }

        packTopRect.anchoredPosition = endPos;
        packTopRect.localRotation = endRot;
        packTopRect.localScale = new Vector3(1.1f, 0.8f, 1f);

        foreach (Image image in packTopRect.GetComponentsInChildren<Image>(true))
        {
            image.color = endColor;
        }
    }

    /// <summary>Animate pack top flap returning to original position (unused in current flow).</summary>
    private IEnumerator AnimatePackReset()
    {
        Vector2 startPos = packTopRect.anchoredPosition;
        Vector2 endPos = new Vector2(0f, 190f);
        Quaternion startRot = packTopRect.localRotation;
        Quaternion endRot = Quaternion.identity;
        Image[] topImages = packTopRect.GetComponentsInChildren<Image>(true);
        Color endColor = Color.white;

        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / 0.25f);
            float eased = EaseOutCubic(t);

            packTopRect.anchoredPosition = Vector2.Lerp(startPos, endPos, eased);
            packTopRect.localRotation = Quaternion.Lerp(startRot, endRot, eased);
            packTopRect.localScale = Vector3.Lerp(new Vector3(0.98f, 0.95f, 1f), Vector3.one, eased);

            for (int i = 0; i < topImages.Length; i++)
            {
                topImages[i].color = Color.Lerp(topImages[i].color, endColor, eased);
            }

            yield return null;
        }

        packTopRect.anchoredPosition = endPos;
        packTopRect.localRotation = endRot;
        packTopRect.localScale = Vector3.one;

        for (int i = 0; i < topImages.Length; i++)
        {
            topImages[i].color = endColor;
        }
    }

    /// <summary>Slide pack downward and deactivate it after opening completes.</summary>
    private IEnumerator AnimatePackDisappear()
    {
        float disappearDuration = 0.6f;
        float elapsed = 0f;
        Vector3 startScale = packRoot.localScale;
        Vector2 startPos = packRoot.anchoredPosition;
        Vector2 endPos = startPos + Vector2.down * 300f; // slide pack down

        while (elapsed < disappearDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / disappearDuration);
            float eased = EaseOutCubic(t);

            // Slide pack down smoothly (no fade)
            packRoot.anchoredPosition = Vector2.Lerp(startPos, endPos, eased);

            yield return null;
        }

        packRoot.gameObject.SetActive(false);
        // Reset for next opening
        packRoot.anchoredPosition = startPos;
        packRoot.localScale = startScale;
    }

    /// <summary>Launch a single card from pack position to target position with arc trajectory and fade-in.</summary>
    private IEnumerator AnimateCardFromPack(RectTransform cardRect, CanvasGroup group, Vector2 startPos, Vector2 targetPos, int orderIndex)
    {
        float elapsed = 0f;
        float rotationTarget = (orderIndex - 1) * -10f;

        cardRect.anchoredPosition = startPos;
        // Make cards start larger so final size is more prominent
        cardRect.localScale = Vector3.one * 0.5f;
        cardRect.localRotation = Quaternion.Euler(0f, 0f, 0f);
        group.alpha = 0f;

        while (elapsed < cardLaunchDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / cardLaunchDuration);
            float eased = EaseOutCubic(t);

            float heightArc = Mathf.Sin(t * Mathf.PI) * 120f;
            Vector2 pos = Vector2.Lerp(startPos, targetPos, eased);
            pos.y += heightArc;

            cardRect.anchoredPosition = pos;
            cardRect.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.2f, eased);
            cardRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, rotationTarget, eased));
            group.alpha = Mathf.Lerp(0f, 1f, eased);

            yield return null;
        }

        cardRect.anchoredPosition = targetPos;
        cardRect.localScale = Vector3.one * 1.2f;
        cardRect.localRotation = Quaternion.Euler(0f, 0f, rotationTarget);
        group.alpha = 1f;
    }

    /// <summary>Validate pending resources and initiate the resource collection animation and scene transition.</summary>
    private void CollectResources()
    {
        if (pendingResources <= 0 || isOpening)
        {
            return;
        }

        StartCoroutine(CollectRoutine());
    }

    /// <summary>Animate cards floating up and fading out, add resources to GameManager, then load shop scene.</summary>
    private IEnumerator CollectRoutine()
    {
        collectButton.interactable = false;
        statusText.gameObject.SetActive(true);
        bankedText.gameObject.SetActive(true);
        packRoot.gameObject.SetActive(true);
        packCanvasGroup.alpha = 1f;
        packTopRect.gameObject.SetActive(true);
        packBodyRect.gameObject.SetActive(true);
        statusText.text = "Collecting resources...";

        foreach (GameObject cardObject in spawnedCards)
        {
            if (cardObject == null)
            {
                continue;
            }

            RectTransform rect = cardObject.GetComponent<RectTransform>();
            CanvasGroup group = cardObject.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = cardObject.AddComponent<CanvasGroup>();
            }

            Vector2 start = rect.anchoredPosition;
            Vector2 end = start + new Vector2(0f, 100f);

            float elapsed = 0f;
            while (elapsed < 0.22f)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / 0.22f);
                float eased = EaseOutCubic(t);

                rect.anchoredPosition = Vector2.Lerp(start, end, eased);
                rect.localScale = Vector3.one * Mathf.Lerp(1.2f, 0.5f, eased);
                group.alpha = Mathf.Lerp(1f, 0f, eased);

                yield return null;
            }
        }

        totalCollectedResources += pendingResources;

        // Add resources to GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddResources(pendingWood, pendingIron, pendingCloth);
        }

        bankedText.text = "Collected Resources: " + totalCollectedResources;
        
        // Show specific resources collected
        string collectedText = "Collected: ";
        if (pendingWood > 0)
            collectedText += "Wood +" + pendingWood + " ";
        if (pendingIron > 0)
            collectedText += "Iron +" + pendingIron + " ";
        if (pendingCloth > 0)
            collectedText += "Cloth +" + pendingCloth;
        
        statusText.text = collectedText.Trim();

        pendingResources = 0;
        pendingWood = 0;
        pendingIron = 0;
        pendingCloth = 0;
        collectButton.gameObject.SetActive(false);
        ClearSpawnedCards();

        // Mark pack opened this session so player can't open another immediately
        if (GameManager.Instance != null)
            GameManager.Instance.MarkPackOpened();

        // Hide cursor before returning to shop (shop locks cursor on start)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Load shop scene immediately
        SceneManager.LoadScene("ShopScene");
    }

    /// <summary>Destroy all spawned card instances and clear tracking dictionaries.</summary>
    private void ClearSpawnedCards()
    {
        foreach (GameObject cardObject in spawnedCards)
        {
            if (cardObject != null)
            {
                Destroy(cardObject);
            }
        }

        spawnedCards.Clear();
        cardFlipStates.Clear();
        cardSoundPlayed.Clear();
    }

    /// <summary>Handle card click: flip back-side cards or re-zoom already revealed full-art cards.</summary>
    private void OnCardClicked(GameObject cardInstance, int cardIndex)
    {
        if (isFlipping)
            return;

        if (cardInstance == null)
        {
            return;
        }

        // If the card is still showing its back, flip it
        if (cardFlipStates.ContainsKey(cardInstance) && cardFlipStates[cardInstance])
        {
            StartCoroutine(FlipCardRoutine(cardInstance, cardIndex));
            return;
        }

        // If the card is already revealed and it's a full-art card, allow zooming it
        ThisCard cardView = cardInstance.GetComponent<ThisCard>();
        if (cardView != null && cardView.thisCard != null && cardView.thisCard.Count > 0)
        {
            Card cardData = cardView.thisCard[0];
            if (cardData != null && cardData.isFullArt)
            {
                // Play the dramatic full-art reveal again for a closer look (without sounds, since they already played)
                StartCoroutine(FullArtRevealRoutine(cardInstance, cardView, cardIndex, playSounds: false));
            }
        }
    }

    /// <summary>Orchestrate card flip animation and trigger full-art reveal for special cards.</summary>
    private IEnumerator FlipCardRoutine(GameObject cardInstance, int cardIndex)
    {
        isFlipping = true;
        RectTransform cardRect = cardInstance.GetComponent<RectTransform>();
        ThisCard cardView = cardInstance.GetComponent<ThisCard>();

        if (cardFlipStates[cardInstance])
        {
            // Currently showing back, flip to front
            yield return AnimateCardFlip(cardRect, cardView, cardIndex, toFront: true);
            cardFlipStates[cardInstance] = false;
            AddRevealedCard(cardInstance);
            Button cardButton = cardInstance.GetComponent<Button>();
            if (cardButton != null)
            {
                // Keep full-art cards clickable so the user can zoom them again;
                // disable interaction only for non-full-art revealed cards.
                Card cardDataForButton = null;
                ThisCard viewForButton = cardInstance.GetComponent<ThisCard>();
                if (viewForButton != null && viewForButton.thisCard != null && viewForButton.thisCard.Count > 0)
                {
                    cardDataForButton = viewForButton.thisCard[0];
                }

                if (cardDataForButton != null && cardDataForButton.isFullArt)
                {
                    cardButton.interactable = true;
                }
                else
                {
                    cardButton.interactable = false;
                }
            }
            UpdateCollectButtonText();
            UpdateCollectButtonState();

            // If this card is a full-art card, play dramatic reveal
            Card cardData = CardDatabase.cardList[cardIndex];
            if (cardData != null && cardData.isFullArt)
            {
                yield return StartCoroutine(FullArtRevealRoutine(cardInstance, cardView, cardIndex));
            }
        }

        isFlipping = false;
    }

    /// <summary>Create dramatic full-art card reveal with darkened backdrop, enlarged card, and other cards faded.</summary>
    private IEnumerator FullArtRevealRoutine(GameObject cardInstance, ThisCard cardView, int cardIndex, bool playSounds = true)
    {
        // Block input
        isFlipping = true;
        isOpening = true;

        // Create a darkened backdrop
        GameObject backdrop = new GameObject("FullArtBackdrop", typeof(RectTransform), typeof(CanvasGroup), typeof(UnityEngine.UI.Image));
        RectTransform backRect = backdrop.GetComponent<RectTransform>();
        backRect.SetParent(simulatorRoot, false);
        backRect.anchorMin = Vector2.zero;
        backRect.anchorMax = Vector2.one;
        backRect.offsetMin = Vector2.zero;
        backRect.offsetMax = Vector2.zero;
        Image backImage = backdrop.GetComponent<Image>();
        backImage.color = new Color(0f, 0f, 0f, 0f);
        CanvasGroup backCg = backdrop.GetComponent<CanvasGroup>();

        // Create a copy of the card to show large
        GameObject bigCard = Instantiate(cardInstance, simulatorRoot, false);
        bigCard.name = "FullArtBigCard";
        RectTransform bigRect = bigCard.GetComponent<RectTransform>();
        bigRect.SetAsLastSibling();

        // Position and size for dramatic effect
        Vector2 originalAnchorMin = bigRect.anchorMin;
        Vector2 originalAnchorMax = bigRect.anchorMax;
        Vector2 originalOffsetMin = bigRect.offsetMin;
        Vector2 originalOffsetMax = bigRect.offsetMax;
        Vector3 originalScale = bigRect.localScale;

        bigRect.anchorMin = new Vector2(0.5f, 0.5f);
        bigRect.anchorMax = new Vector2(0.5f, 0.5f);
        bigRect.pivot = new Vector2(0.5f, 0.5f);
        // Move the big card up on screen for a "straight up" dramatic reveal
        bigRect.anchoredPosition = new Vector2(0f, 150f);
        bigRect.localScale = Vector3.zero;
        bigRect.localRotation = Quaternion.identity;

        // Fade out other cards
        List<CanvasGroup> otherGroups = new List<CanvasGroup>();
        foreach (GameObject g in spawnedCards)
        {
            if (g == null || g == cardInstance) continue;
            CanvasGroup cg = g.GetComponent<CanvasGroup>();
            if (cg == null) cg = g.AddComponent<CanvasGroup>();
            otherGroups.Add(cg);
        }

        // Animate backdrop fade and big card pop
        float duration = 0.6f;
        float elapsed = 0f;
        float scaleTarget = 2.2f; // bigger dramatic scale
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutCubic(t);

            backImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(0f, 0.7f, eased));
            backCg.alpha = Mathf.Lerp(0f, 1f, eased);

            float scale = Mathf.Lerp(0f, scaleTarget, eased);
            bigRect.localScale = Vector3.one * scale;

            foreach (CanvasGroup cg in otherGroups)
            {
                cg.alpha = Mathf.Lerp(1f, 0.15f, eased);
            }

            yield return null;
        }

        bigRect.localScale = Vector3.one * scaleTarget;

        // Play a dramatic full-art reveal sound for emphasis only on first reveal
        if (playSounds && audioSource != null && fullArtRevealClip != null)
        {
            audioSource.PlayOneShot(fullArtRevealClip);
        }

        // Wait for player click or short delay
        float waitTime = 5.0f;
        float waited = 0f;
        bool clicked = false;
        while (waited < waitTime && !clicked)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                clicked = true;
                break;
            }
            waited += Time.deltaTime;
            yield return null;
        }

        // Animate out
        duration = 0.45f;
        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseInCubic(t);

            backImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.7f, 0f, eased));
            float scale = Mathf.Lerp(scaleTarget, 1f, eased);
            bigRect.localScale = Vector3.one * scale;

            foreach (CanvasGroup cg in otherGroups)
            {
                cg.alpha = Mathf.Lerp(0.15f, 1f, eased);
            }

            yield return null;
        }

        // Clean up
        Destroy(backdrop);
        Destroy(bigCard);

        // Restore original states
        foreach (CanvasGroup cg in otherGroups)
        {
            cg.alpha = 1f;
        }

        isOpening = false;
        isFlipping = false;

        yield return null;
    }

    /// <summary>Animate the card flip effect with scale-down/scale-up at midpoint and audio/sound control.</summary>
    private IEnumerator AnimateCardFlip(RectTransform cardRect, ThisCard cardView, int cardIndex, bool toFront)
    {
        float flipDuration = 0.4f;
        float elapsed = 0f;

        Vector3 startScale = cardRect.localScale;
        CanvasGroup canvasGroup = cardRect.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = cardRect.gameObject.AddComponent<CanvasGroup>();
        }

        // First half: scale down
        while (elapsed < flipDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / (flipDuration * 0.5f));
            float eased = EaseInOutCubic(t);

            float scale = Mathf.Lerp(1f, 0f, eased);
            cardRect.localScale = startScale * scale;
            canvasGroup.alpha = Mathf.Lerp(1f, 0.3f, eased);

            yield return null;
        }

        // Switch display at midpoint
        cardView.cardBack = !toFront;

        // Play flip and reveal sounds only if this is the first time revealing this card
        GameObject cardGameObject = cardRect.gameObject;
        if (!cardSoundPlayed.ContainsKey(cardGameObject) || !cardSoundPlayed[cardGameObject])
        {
            // Play flip sound
            if (audioSource != null && cardFlipClip != null)
            {
                audioSource.PlayOneShot(cardFlipClip);
            }

            // Play reveal sound depending on full art
            bool isFull = false;
            if (cardView != null && cardView.thisCard != null && cardView.thisCard.Count > 0)
            {
                Card cd = cardView.thisCard[0];
                if (cd != null && cd.isFullArt)
                    isFull = true;
            }

            if (audioSource != null)
            {
                if (isFull && fullArtRevealClip != null)
                    audioSource.PlayOneShot(fullArtRevealClip);
                else if (!isFull && commonRevealClip != null)
                    audioSource.PlayOneShot(commonRevealClip);
            }

            // Mark sounds as played for this card
            cardSoundPlayed[cardGameObject] = true;
        }

        // Second half: scale back up
        float secondHalfStart = elapsed;
        while (elapsed < flipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01((elapsed - secondHalfStart) / (flipDuration * 0.5f));
            float eased = EaseInOutCubic(t);

            float scale = Mathf.Lerp(0f, 1f, eased);
            cardRect.localScale = startScale * scale;
            canvasGroup.alpha = Mathf.Lerp(0.3f, 1f, eased);

            yield return null;
        }

        cardRect.localScale = startScale;
        canvasGroup.alpha = 1f;
    }

    /// <summary>Add a card to the revealed set and track its resource text if not duplicate.</summary>
    private void AddRevealedCard(GameObject cardInstance)
    {
        if (cardInstance == null || !cardResourceTexts.ContainsKey(cardInstance) || revealedCards.Contains(cardInstance))
        {
            return;
        }

        revealedCards.Add(cardInstance);

        string resourceText = cardResourceTexts[cardInstance];
        if (!revealedResourceTexts.Contains(resourceText))
        {
            revealedResourceTexts.Add(resourceText);
        }
    }

    /// <summary>Set the collect button label to display current pending resource total.</summary>
    private void UpdateCollectButtonText()
    {
        if (collectButtonText == null)
        {
            return;
        }

        collectButtonText.text = "Collect Resources";
    }

    /// <summary>Enable/disable collect button based on whether all cards are revealed and resources are pending.</summary>
    private void UpdateCollectButtonState()
    {
        if (collectButton == null)
        {
            return;
        }

        bool canCollect = pendingResources > 0 && !isOpening && AreAllCardsRevealed();
        collectButton.gameObject.SetActive(canCollect);
        collectButton.interactable = canCollect;

        if (statusText != null && pendingResources > 0 && !canCollect)
        {
            statusText.text = "Reveal all cards before collecting";
        }
    }

    /// <summary>Check if all spawned cards have been revealed by the player.</summary>
    private bool AreAllCardsRevealed()
    {
        int revealedCardCount = 0;

        foreach (GameObject cardObject in spawnedCards)
        {
            if (cardObject != null && revealedCards.Contains(cardObject))
            {
                revealedCardCount++;
            }
        }

        return revealedCardCount > 0 && revealedCardCount == spawnedCards.Count;
    }

    /// <summary>Format a card's resource info into displayable text (e.g., "Wood +100").</summary>
    private string BuildCardResourceText(Card cardData)
    {
        if (cardData.cardName == "Wood")
            return "Wood +" + cardData.power;

        if (cardData.cardName == "Iron")
            return "Iron +" + cardData.power;

        if (cardData.cardName == "Cloth")
            return "Cloth +" + cardData.power;

        return cardData.cardName + " +" + cardData.power;
    }

    /// <summary>Smooth cubic easing that accelerates at start and decelerates at end (t³ for first half, custom cubic for second).</summary>
    private float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    /// <summary>Cubic easing that accelerates from slow to fast (t³), used for fade-in animations.</summary>
    private float EaseInCubic(float t)
    {
        return t * t * t;
    }

    /// <summary>Select next card using weighted random based on card.cost field (prevents consecutive duplicates).</summary>
    private int ChooseNextCardIndex()
    {
        if (CardDatabase.cardList.Count <= 1)
        {
            lastCardIndex = 0;
            return 0;
        }

        // Use cost field as pull rate weight for all cards
        List<Card> cards = CardDatabase.cardList;
        float totalWeight = 0f;
        List<float> weights = new List<float>();

        foreach (Card card in cards)
        {
            float weight = Mathf.Max(1f, card.cost); // Use cost as weight, minimum 1
            weights.Add(weight);
            totalWeight += weight;
        }

        // Random selection based on weights
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            currentWeight += weights[i];
            if (randomValue <= currentWeight)
            {
                int index = i;
                
                // Prevent duplicate consecutive draws
                if (index == lastCardIndex && cards.Count > 1)
                {
                    index = (index + 1) % cards.Count;
                }
                
                lastCardIndex = index;
                return index;
            }
        }

        return cards.Count - 1;
    }

    /// <summary>Select card by inverse rarity weights (rarity 4→weight 1, rarity 1→weight 4) for alternate pull logic.</summary>
    private int SelectCardByWeightedRarity()
    {
        List<Card> cards = CardDatabase.cardList;
        if (cards.Count == 0)
            return 0;

        // Calculate inverse weights: rarity 4 = weight 1, rarity 1 = weight 4
        float totalWeight = 0f;
        List<float> weights = new List<float>();

        foreach (Card card in cards)
        {
            int rarity = card.rarity > 0 ? card.rarity : 1;
            float weight = 5f - rarity; // Rarity 1 = weight 4, Rarity 4 = weight 1
            weights.Add(weight);
            totalWeight += weight;
        }

        // Random selection based on weights
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        for (int i = 0; i < weights.Count; i++)
        {
            currentWeight += weights[i];
            if (randomValue <= currentWeight)
            {
                return i;
            }
        }

        return cards.Count - 1;
    }

    /// <summary>Return a single card index sampled by weight (with replacement).</summary>
    private int ChooseWeightedIndexWithReplacement()
    {
        List<Card> cards = CardDatabase.cardList;
        if (cards == null || cards.Count == 0)
            return 0;

        float totalWeight = 0f;
        for (int i = 0; i < cards.Count; i++)
        {
            totalWeight += Mathf.Max(1f, cards[i].cost);
        }

        float randomValue = Random.Range(0f, totalWeight);
        float current = 0f;
        for (int i = 0; i < cards.Count; i++)
        {
            current += Mathf.Max(1f, cards[i].cost);
            if (randomValue <= current)
                return i;
        }

        return cards.Count - 1;
    }

    /// <summary>Create a RectTransform GameObject with specified anchors, position, and size for UI layout.</summary>
    private RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return rect;
    }

    /// <summary>Create a panel (Image component) with specified color and non-interactive raycast target.</summary>
    private Image CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
    {
        RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, anchoredPosition, sizeDelta);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>Create a text element with specified font, size, style, alignment, color, and text outline.</summary>
    private Text CreateText(Transform parent, string name, string value, Font font, int fontSize, FontStyle style, TextAnchor alignment, Color color, Vector2 anchor, Vector2 size, Vector2 anchored)
    {
        RectTransform rect = CreateRect(name, parent, anchor, anchor, anchored, size);
        Text text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Create a button with label text, background color, and configured interaction styling.</summary>
    private Button CreateButton(Transform parent, string name, string label, Font font, Vector2 anchor, Vector2 size, Color color)
    {
        RectTransform rect = CreateRect(name, parent, anchor, anchor, Vector2.zero, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;

        Button button = rect.gameObject.AddComponent<Button>();

        Text labelText = CreateText(rect, "Label", label, font, 24, FontStyle.Bold, TextAnchor.MiddleCenter, new Color32(24, 32, 34, 255), new Vector2(0.5f, 0.5f), size, Vector2.zero);
        AddOutline(labelText, new Color32(255, 255, 255, 38), new Vector2(1f, -1f));

        return button;
    }

    /// <summary>Add or update an Outline component on text with specified color and offset distance.</summary>
    private void AddOutline(Text text, Color color, Vector2 distance)
    {
        Outline outline = text.gameObject.GetComponent<Outline>();
        if (outline == null)
        {
            outline = text.gameObject.AddComponent<Outline>();
        }

        outline.effectColor = color;
        outline.effectDistance = distance;
    }

    /// <summary>Cubic easing that decelerates from fast to slow (1-(1-t)³), used for most position animations.</summary>
    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    
    /// <summary>Auto-load audio clips from Resources/Audio/ folder if not assigned in inspector (for runtime-created simulator).</summary>
    private void EnsureAudioClipsLoaded()
    {
        if (packRipClip == null)
        {
            packRipClip = Resources.Load<AudioClip>("Audio/pack_rip") ?? Resources.Load<AudioClip>("pack_rip");
        }

        if (cardFlipClip == null)
        {
            cardFlipClip = Resources.Load<AudioClip>("Audio/card_flip") ?? Resources.Load<AudioClip>("card_flip");
        }

        if (commonRevealClip == null)
        {
            commonRevealClip = Resources.Load<AudioClip>("Audio/common_reveal") ?? Resources.Load<AudioClip>("common_reveal");
        }

        if (fullArtRevealClip == null)
        {
            fullArtRevealClip = Resources.Load<AudioClip>("Audio/full_art_reveal") ?? Resources.Load<AudioClip>("full_art_reveal");
        }
    }
}
