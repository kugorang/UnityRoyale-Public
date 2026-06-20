using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UnityRoyale
{
    public class CPUOpponent : MonoBehaviour
    {
        public DeckData aiDeck;
        public UnityAction<CardData, Vector3, Placeable.Faction> OnCardUsed;

        public Placeable.Faction faction = Placeable.Faction.Opponent;
        private bool act = false;
        private Coroutine actingCoroutine;

		public float opponentLoopTime = 1f;

        public void LoadDeck()
        {
            DeckLoader oldLoader = GetComponent<DeckLoader>();
            if (oldLoader != null)
            {
                Destroy(oldLoader);
            }
            DeckLoader newDeckLoaderComp = gameObject.AddComponent<DeckLoader>();
            newDeckLoaderComp.OnDeckLoaded += DeckLoaded;
            newDeckLoaderComp.LoadDeck(aiDeck);
        }

		private void DeckLoaded()
		{
			//Debug.Log("AI deck loaded"); // Suppressed: fires every episode reset

			//StartActing();
        }

		public void StartActing()
		{
            StopActing();
            act = true;
            actingCoroutine = StartCoroutine(CreateRandomCards());
		}

        public void StopActing()
        {
            act = false;
            if (actingCoroutine != null)
            {
                StopCoroutine(actingCoroutine);
                actingCoroutine = null;
            }
        }

        //TODO: create a proper AI
		private IEnumerator CreateRandomCards()
		{
            GameManager gm = FindObjectOfType<GameManager>();
            while(act)
            {
			    yield return new WaitForSeconds(opponentLoopTime);

                if (gm == null)
                    gm = FindObjectOfType<GameManager>();

                if (gm != null && OnCardUsed != null && aiDeck != null)
                {
                    CardData nextCard = aiDeck.PeekNextCard();
                    if (nextCard != null)
                    {
                        if (gm.opponentElixir >= nextCard.ElixirCost)
                        {
                            // We have enough Elixir! Consume it from deck and play it.
                            CardData playedCard = aiDeck.GetNextCardFromDeck();
                            aiDeck.RecycleCard(playedCard);
                            gm.opponentElixir -= nextCard.ElixirCost;

                            float spawnZ = (faction == Placeable.Faction.Player) ? Random.Range(-8.5f, -3f) : Random.Range(3f, 8.5f);
                            Vector3 newPos = new Vector3(Random.Range(-5f, 5f), 0f, spawnZ);
                            OnCardUsed(nextCard, newPos, faction);
                        }
                    }
                }
            }
		}
	}
}