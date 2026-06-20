using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UnityRoyale
{
    public class Card : MonoBehaviour, IDragHandler, IPointerUpHandler, IPointerDownHandler
    {
        public UnityAction<int, Vector2> OnDragAction;
        public UnityAction<int> OnTapDownAction, OnTapReleaseAction;

        [HideInInspector] public int cardId;
        [HideInInspector] public CardData cardData;

        public Image portraitImage; //Inspector-set reference
        private CanvasGroup canvasGroup;

        private TMPro.TextMeshProUGUI attackText;
        private TMPro.TextMeshProUGUI healthText;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();

            // Find TextMeshPro components in children by name (case-insensitive with fallbacks)
            var tmps = GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            foreach (var tmp in tmps)
            {
                string nameLower = tmp.gameObject.name.ToLower();
                if (nameLower == "dmg" || nameLower == "attack" || nameLower.Contains("dmg") || nameLower.Contains("attack"))
                {
                    attackText = tmp;
                }
                else if (nameLower == "hp" || nameLower == "health" || nameLower.Contains("hp") || nameLower.Contains("health"))
                {
                    healthText = tmp;
                }
            }

            // Fallback: if not found by name, assign by order (exactly 2 TMPro components exist in prefab)
            if ((attackText == null || healthText == null) && tmps.Length >= 2)
            {
                if (attackText == null) attackText = tmps[0];
                if (healthText == null) healthText = tmps[1];
            }

            // Debug.Log($"[CardUI] Awake: card name={gameObject.name}, attackText found={attackText != null}, healthText found={healthText != null}");
        }

        //called by CardManager, it feeds CardData so this card can display the placeable's portrait
        public void InitialiseWithData(CardData cData)
        {
            cardData = cData;
            portraitImage.sprite = cardData.cardImage;

            // Display Elixir Cost dynamically at the top-left of the card
            CreateElixirUI();

            if (cardData.placeablesData != null && cardData.placeablesData.Length > 0)
            {
                PlaceableData mainP = cardData.placeablesData[0];
                if (attackText != null)
                {
                    attackText.text = mainP.damagePerAttack.ToString("F0");
                }
                if (healthText != null)
                {
                    healthText.text = mainP.hitPoints.ToString("F0");
                }
                // Debug.Log($"[CardUI] Initialise: Card={cData.name}, Attack={mainP.damagePerAttack}, Health={mainP.hitPoints}, TextUpdated={attackText != null && healthText != null}");
            }
            else
            {
                Debug.LogWarning($"[CardUI] Initialise: Card={cData.name} has no placeablesData!");
            }
        }

        public void OnPointerDown(PointerEventData pointerEvent)
        {
            if(OnTapDownAction != null)
                OnTapDownAction(cardId);
        }

        public void OnDrag(PointerEventData pointerEvent)
        {
            if(OnDragAction != null)
                OnDragAction(cardId, pointerEvent.delta);
        }

        public void OnPointerUp(PointerEventData pointerEvent)
        {
            if(OnTapReleaseAction != null)
                OnTapReleaseAction(cardId);
        }

        public void ChangeActiveState(bool isActive)
        {
            canvasGroup.alpha = (isActive) ? .05f : 1f;
        }

        private void CreateElixirUI()
        {
            // Clean up existing if any
            Transform oldBg = transform.Find("ElixirBg");
            if (oldBg != null) Destroy(oldBg.gameObject);

            // Create background for Elixir cost
            GameObject elixirBgGo = new GameObject("ElixirBg");
            elixirBgGo.transform.SetParent(this.transform, false);
            Image bgImage = elixirBgGo.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.08f, 0.2f, 0.9f); // Darker purple translucent background
            
            RectTransform bgRect = elixirBgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 1f); // top-left
            bgRect.anchorMax = new Vector2(0f, 1f);
            bgRect.pivot = new Vector2(0f, 1f);
            bgRect.anchoredPosition = new Vector3(4f, -4f, 0f); // 4px padding from top-left
            bgRect.sizeDelta = new Vector2(36f, 36f); // Made larger and square (circle-like)

            // Add text inside background
            GameObject elixirTextGo = new GameObject("ElixirText");
            elixirTextGo.transform.SetParent(elixirBgGo.transform, false);
            TMPro.TextMeshProUGUI costText = elixirTextGo.AddComponent<TMPro.TextMeshProUGUI>();
            costText.fontSize = 15f; // Increased font size for readability
            costText.fontStyle = TMPro.FontStyles.Bold;
            costText.color = new Color(0.98f, 0.5f, 1f, 1f); // Vibrant light purple
            costText.alignment = TMPro.TextAlignmentOptions.Center;
            costText.text = cardData.ElixirCost.ToString();

            RectTransform textRect = elixirTextGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; // Stretch
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }
    }
}