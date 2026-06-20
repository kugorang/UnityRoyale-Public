using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;
using System;

namespace UnityRoyale
{
    public class CardManager : MonoBehaviour
    {
        public Camera mainCamera; //public reference
        public LayerMask playingFieldMask;
        public GameObject cardPrefab;
        public DeckData playersDeck;
		public MeshRenderer forbiddenAreaRenderer;
		
        public UnityAction<CardData, Vector3, Placeable.Faction> OnCardUsed;
        
        [Header("UI Elements")]
        public RectTransform backupCardTransform; //the smaller card that sits in the deck
        public RectTransform cardsDashboard; //the UI panel that contains the actual playable cards
        public RectTransform cardsPanel; //the UI panel that contains all cards, the deck, and the dashboard (center aligned)
        
        private Card[] cards;
        private bool cardIsActive = false; //when true, a card is being dragged over the play field
        private GameObject previewHolder;
        private Vector3 inputCreationOffset = new Vector3(0f, 0f, 1f); //offsets the creation of units so that they are not under the player's finger

        private void Awake()
        {
            previewHolder = new GameObject("PreviewHolder");
            cards = new Card[3]; //3 is the length of the dashboard
        }

        public void LoadDeck()
        {
            DeckLoader newDeckLoaderComp = gameObject.AddComponent<DeckLoader>();
            newDeckLoaderComp.OnDeckLoaded += DeckLoaded;
            newDeckLoaderComp.LoadDeck(playersDeck);
        }

        //...

		private void DeckLoaded()
		{
            ResetDeckAndUI();
		}

        //moves the preview card from the deck to the active card dashboard instantly in logic, while animating visually
        private void PromoteCardFromDeck(int position)
        {
            if (backupCardTransform == null)
            {
                Debug.LogError("PromoteCardFromDeck: backupCardTransform is null!");
                return;
            }

            backupCardTransform.SetParent(cardsDashboard, true);
            //move and scale into position
            backupCardTransform.DOAnchorPos(new Vector2(220f * (position+1), 0f), 0.25f).SetEase(Ease.OutQuad);
            backupCardTransform.localScale = Vector3.one;

            //store a reference to the Card component in the array
            Card cardScript = backupCardTransform.GetComponent<Card>();
            cardScript.cardId = position;
            cards[position] = cardScript;

            //setup listeners on Card events
            cardScript.OnTapDownAction += CardTapped;
            cardScript.OnDragAction += CardDragged;
            cardScript.OnTapReleaseAction += CardReleased;

            backupCardTransform = null;
        }

        //adds a new card to the deck on the left instantly, ready to be used
        private void AddCardToDeck()
        {
            CardData nextCard = playersDeck.GetNextCardFromDeck();
            if (nextCard == null)
            {
                return;
            }

            //create new card
            backupCardTransform = Instantiate<GameObject>(cardPrefab, cardsPanel).GetComponent<RectTransform>();
            backupCardTransform.localScale = Vector3.one * 0.7f;
            
            //send it to the bottom left corner and animate it sliding in
            backupCardTransform.anchoredPosition = new Vector2(180f, -300f);
            backupCardTransform.DOAnchorPos(new Vector2(180f, 0f), 0.25f).SetEase(Ease.OutQuad);

            //populate CardData on the Card script
            Card cardScript = backupCardTransform.GetComponent<Card>();
            cardScript.InitialiseWithData(nextCard);
        }

        private void CardTapped(int cardId)
        {
            cards[cardId].GetComponent<RectTransform>().SetAsLastSibling();
			forbiddenAreaRenderer.enabled = true;
        }

        private void CardDragged(int cardId, Vector2 dragAmount)
        {
            cards[cardId].transform.Translate(dragAmount);

            //raycasting to check if the card is on the play field
            RaycastHit hit;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            
            bool planeHit = Physics.Raycast(ray, out hit, Mathf.Infinity, playingFieldMask);

            if(planeHit)
            {
                if(!cardIsActive)
                {
                    cardIsActive = true;
                    previewHolder.transform.position = hit.point;
                    cards[cardId].ChangeActiveState(true); //hide card

                    //retrieve arrays from the CardData
                    PlaceableData[] dataToSpawn = cards[cardId].cardData.placeablesData;
                    Vector3[] offsets = cards[cardId].cardData.relativeOffsets;

                    //spawn all the preview Placeables and parent them to the cardPreview
                    for(int i=0; i<dataToSpawn.Length; i++)
                    {
                        GameObject newPlaceable = GameObject.Instantiate<GameObject>(dataToSpawn[i].associatedPrefab,
                                                                                    hit.point + offsets[i] + inputCreationOffset,
                                                                                    Quaternion.identity,
                                                                                    previewHolder.transform);
                    }
                }
                else
                {
                    //temporary copy has been created, we move it along with the cursor
                    previewHolder.transform.position = hit.point;
                }
            }
            else
            {
                if(cardIsActive)
                {
                    cardIsActive = false;
                    cards[cardId].ChangeActiveState(false); //show card

                    ClearPreviewObjects();
                }
            }
        }

        private void CardReleased(int cardId)
        {
            if (cardId < 0 || cardId >= cards.Length || cards[cardId] == null) return;

            //raycasting to check if the card is on the play field
            RaycastHit hit;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, playingFieldMask))
            {
                float spawnZ = hit.point.z + inputCreationOffset.z;
                CardData cardData = cards[cardId].cardData;
                GameManager gm = FindObjectOfType<GameManager>();

                // Rule check: cannot spawn in forbidden area (opponent's side or river, spawnZ must be <= -3.0f)
                if (spawnZ <= -3.0f && gm != null && gm.playerElixir >= cardData.ElixirCost)
                {
                    gm.playerElixir -= cardData.ElixirCost; // Deduct cost
                    ClearPreviewObjects();
                    PlayCardFromHand(cardId, hit.point + inputCreationOffset);
                }
                else
                {
                    // Snap back due to insufficient Elixir or invalid placement in forbidden area
                    cards[cardId].ChangeActiveState(false);
                    ClearPreviewObjects();
                    cards[cardId].GetComponent<RectTransform>().DOAnchorPos(new Vector2(220f * (cardId+1), 0f),
                                                                            .2f).SetEase(Ease.OutQuad);
                }
            }
            else
            {
                cards[cardId].GetComponent<RectTransform>().DOAnchorPos(new Vector2(220f * (cardId+1), 0f),
                                                                        .2f).SetEase(Ease.OutQuad);
            }

			forbiddenAreaRenderer.enabled = false;
        }

        //happens when the card is put down on the playing field, and while dragging (when moving out of the play field)
        private void ClearPreviewObjects()
        {
            //destroy all the preview Placeables
            for(int i=0; i<previewHolder.transform.childCount; i++)
            {
                Destroy(previewHolder.transform.GetChild(i).gameObject);
            }
        }

        public void ResetDeckAndUI()
        {
            StopAllCoroutines();
            ClearCards();
            if (playersDeck != null)
            {
                playersDeck.ResetDeck();
                
                // Draw and promote all 3 slots instantly to prevent race conditions and empty slots
                AddCardToDeck();
                PromoteCardFromDeck(0);

                AddCardToDeck();
                PromoteCardFromDeck(1);

                AddCardToDeck();
                PromoteCardFromDeck(2);

                // Add the next card to the deck preview
                AddCardToDeck();
            }
        }

        public void ClearCards()
        {
            if (cards != null)
            {
                for (int i = 0; i < cards.Length; i++)
                {
                    if (cards[i] != null)
                    {
                        Destroy(cards[i].gameObject);
                        cards[i] = null;
                    }
                }
            }

            if (cardsDashboard != null)
            {
                foreach (Transform child in cardsDashboard)
                {
                    if (child != null && child.gameObject.GetComponent<Card>() != null)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }

            if (cardsPanel != null)
            {
                foreach (Transform child in cardsPanel)
                {
                    if (child != null && child.gameObject.GetComponent<Card>() != null)
                    {
                        Destroy(child.gameObject);
                    }
                }
            }
        }

        public CardData GetCardDataInSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= cards.Length || cards[slotIndex] == null)
                return null;
            return cards[slotIndex].cardData;
        }

        public bool PlayCardFromHand(int cardId, Vector3 position)
        {
            if (cardId < 0 || cardId >= cards.Length || cards[cardId] == null)
                return false;

            CardData playedCard = cards[cardId].cardData;

            if (OnCardUsed != null)
                OnCardUsed(playedCard, position, Placeable.Faction.Player);

            Destroy(cards[cardId].gameObject);
            cards[cardId] = null; // Clear the slot

            if (playersDeck != null)
            {
                playersDeck.RecycleCard(playedCard);
            }

            // Promote the current backup card to this slot instantly
            PromoteCardFromDeck(cardId);

            // Draw the next card instantly to the backup slot
            AddCardToDeck();

            return true;
        }
    }

}
