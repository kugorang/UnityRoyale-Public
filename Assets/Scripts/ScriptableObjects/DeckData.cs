using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace UnityRoyale
{
    [CreateAssetMenu(fileName = "NewDeck", menuName = "Unity Royale/Deck Data")]
    public class DeckData : ScriptableObject
    {
        public AssetLabelReference[] labelsToInclude; //set by designers

        private CardData[] cards; //the deck of actual cards, needs to be shuffled
        private Queue<CardData> activeDeckQueue = new Queue<CardData>();

        public void CardsRetrieved(IList<CardData> cardDataDownloaded)
        {
            //load the actual cards data into an array, ready to use
            int totalCards = cardDataDownloaded.Count;
            cards = new CardData[totalCards];
            for(int c=0; c<totalCards; c++)
            {
                cards[c] = cardDataDownloaded[c];
            }
            ShuffleCards();
            InitQueue();
        }

        public void ShuffleCards()
        {
            if (cards == null || cards.Length == 0) return;

            // Use System.Random with a Guid-based hash seed to guarantee true randomness
            // even if the Unity Editor's random seed is fixed by ML-Agents.
            System.Random rnd = new System.Random(Guid.NewGuid().GetHashCode());
            for (int i = cards.Length - 1; i > 0; i--)
            {
                int r = rnd.Next(0, i + 1);
                CardData tmp = cards[i];
                cards[i] = cards[r];
                cards[r] = tmp;
            }
        }

        public void ResetDeck()
        {
            ShuffleCards();
            InitQueue();
        }

        private void InitQueue()
        {
            if (activeDeckQueue == null) activeDeckQueue = new Queue<CardData>();
            activeDeckQueue.Clear();
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    activeDeckQueue.Enqueue(card);
                }
            }
        }

        public CardData PeekNextCard()
        {
            if (activeDeckQueue == null || activeDeckQueue.Count == 0)
            {
                InitQueue();
            }

            if (activeDeckQueue == null || activeDeckQueue.Count == 0)
            {
                return null;
            }

            return activeDeckQueue.Peek();
        }

        //returns the next card in the deck. You probably want to shuffle cards first
        public CardData GetNextCardFromDeck()
        {
            if (activeDeckQueue == null || activeDeckQueue.Count == 0)
            {
                InitQueue();
            }

            if (activeDeckQueue == null || activeDeckQueue.Count == 0)
            {
                Debug.LogError("No cards loaded in deck: " + name);
                return null;
            }

            return activeDeckQueue.Dequeue();
        }

        public void RecycleCard(CardData card)
        {
            if (card == null) return;
            if (activeDeckQueue == null) activeDeckQueue = new Queue<CardData>();
            activeDeckQueue.Enqueue(card);
        }
    }
}
