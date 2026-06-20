using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

namespace UnityRoyale
{
    public class MLPlayerAgent : Agent
    {
        public GameManager gameManager;
        public DeckData playerDeck;

        [Header("Agent Cooldown Settings")]
        public float actionCooldown = 3f;
        private float lastActionTime = 0f;
        private float lastHeuristicActionTime = 0f;
        public float heuristicCooldown = 3f;

        [Header("Faction & Reset Settings")]
        public Placeable.Faction agentFaction = Placeable.Faction.Player;
        public bool controlMatchReset = true;

        private Building myCastleBuilding;      // Player's castle (Agent's own)
        private Building enemyCastleBuilding;    // Opponent's castle (target to destroy)
        private CardManager cardManager;         // Sync card hand

        private float maxCastleHP = 1f;

        private CardData[] opponentHand = new CardData[3];
        private CardData opponentNextCard;

        /// <summary>
        /// Lazily ensures all references are valid. Called every time observations
        /// are collected because Initialize() may run before GameManager.Start()
        /// sets up castles, and ResetMatch() can invalidate references.
        /// </summary>
        private void EnsureReferences()
        {
            if (gameManager == null)
                gameManager = FindObjectOfType<GameManager>();

            if (gameManager != null)
            {
                if (agentFaction == Placeable.Faction.Player)
                {
                    if (myCastleBuilding == null && gameManager.playersCastle != null)
                        myCastleBuilding = gameManager.playersCastle.GetComponent<Building>();
                    if (enemyCastleBuilding == null && gameManager.opponentCastle != null)
                        enemyCastleBuilding = gameManager.opponentCastle.GetComponent<Building>();
                }
                else
                {
                    if (myCastleBuilding == null && gameManager.opponentCastle != null)
                        myCastleBuilding = gameManager.opponentCastle.GetComponent<Building>();
                    if (enemyCastleBuilding == null && gameManager.playersCastle != null)
                        enemyCastleBuilding = gameManager.playersCastle.GetComponent<Building>();
                }
                if (maxCastleHP <= 1f && gameManager.castlePData != null)
                    maxCastleHP = gameManager.castlePData.hitPoints;
            }

            if (playerDeck == null)
            {
                CardManager cm = FindObjectOfType<CardManager>();
                if (cm != null)
                {
                    playerDeck = cm.playersDeck;
                }
            }

            if (cardManager == null)
            {
                cardManager = FindObjectOfType<CardManager>();
            }
        }

        public override void Initialize()
        {
            EnsureReferences();
        }

        public override void OnEpisodeBegin()
        {
            EnsureReferences();
            if (controlMatchReset && gameManager != null)
            {
                gameManager.ResetMatch();
            }

            if (gameManager != null)
            {
                if (agentFaction == Placeable.Faction.Player)
                {
                    myCastleBuilding = gameManager.playersCastle != null ? gameManager.playersCastle.GetComponent<Building>() : null;
                    enemyCastleBuilding = gameManager.opponentCastle != null ? gameManager.opponentCastle.GetComponent<Building>() : null;
                }
                else
                {
                    myCastleBuilding = gameManager.opponentCastle != null ? gameManager.opponentCastle.GetComponent<Building>() : null;
                    enemyCastleBuilding = gameManager.playersCastle != null ? gameManager.playersCastle.GetComponent<Building>() : null;
                }
            }

            if (agentFaction == Placeable.Faction.Opponent)
            {
                InitializeOpponentHand();
            }

            // Reset action cooldowns to allow immediate play at the start of each match
            lastActionTime = Time.time - actionCooldown;
            lastHeuristicActionTime = Time.time - heuristicCooldown;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            EnsureReferences();

            if (myCastleBuilding == null || enemyCastleBuilding == null)
            {
                // Fallback: add 52 zero observations to match VectorObservationSize
                for (int i = 0; i < 52; i++)
                    sensor.AddObservation(0f);
                return;
            }

            // 1. Castle health (2 observations) — my castle first, then enemy
            sensor.AddObservation(myCastleBuilding.hitPoints / maxCastleHP);
            sensor.AddObservation(enemyCastleBuilding.hitPoints / maxCastleHP);

            // Fetch active units from GameManager based on faction
            Placeable.Faction myFaction = agentFaction;
            Placeable.Faction enemyFaction = (agentFaction == Placeable.Faction.Player) ? Placeable.Faction.Opponent : Placeable.Faction.Player;

            List<ThinkingPlaceable> myUnits = GetThinkingPlaceables(myFaction);
            List<ThinkingPlaceable> enemyUnits = GetThinkingPlaceables(enemyFaction);

            // 2. My units (up to 4, 4 observations each: x, y, z, hp -> 16 observations)
            for (int i = 0; i < 4; i++)
            {
                if (i < myUnits.Count && myUnits[i] != null)
                {
                    Vector3 pos = myUnits[i].transform.position;
                    sensor.AddObservation(pos.x / 5f);
                    sensor.AddObservation(pos.y);
                    sensor.AddObservation(pos.z / 10f);
                    sensor.AddObservation(myUnits[i].hitPoints / 20f);
                }
                else
                {
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                }
            }

            // 3. Enemy units (up to 4, 4 observations each: x, y, z, hp -> 16 observations)
            for (int i = 0; i < 4; i++)
            {
                if (i < enemyUnits.Count && enemyUnits[i] != null)
                {
                    Vector3 pos = enemyUnits[i].transform.position;
                    sensor.AddObservation(pos.x / 5f);
                    sensor.AddObservation(pos.y);
                    sensor.AddObservation(pos.z / 10f);
                    sensor.AddObservation(enemyUnits[i].hitPoints / 20f);
                }
                else
                {
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                    sensor.AddObservation(0f);
                }
            }

            // 4. Current hand cards and next card (4 cards, 4 observations each: value, elixir cost, hp, range -> 16 observations)
            for (int i = 0; i < 3; i++)
            {
                CardData handCard;
                if (agentFaction == Placeable.Faction.Player)
                {
                    handCard = (cardManager != null) ? cardManager.GetCardDataInSlot(i) : null;
                }
                else
                {
                    handCard = GetOpponentHandCard(i);
                }
                AddCardObservations(sensor, handCard);
            }

            CardData nextCard;
            if (agentFaction == Placeable.Faction.Player)
            {
                nextCard = playerDeck != null ? playerDeck.PeekNextCard() : null;
            }
            else
            {
                nextCard = GetOpponentNextCard();
            }
            AddCardObservations(sensor, nextCard);

            // 5. Elixir levels (2 observations -> Total: 52 observations)
            sensor.AddObservation(gameManager != null ? (agentFaction == Placeable.Faction.Player ? gameManager.playerElixir / 10f : gameManager.opponentElixir / 10f) : 0f);
            sensor.AddObservation(gameManager != null ? (agentFaction == Placeable.Faction.Player ? gameManager.opponentElixir / 10f : gameManager.playerElixir / 10f) : 0f);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            // Discrete Action 0: 0 = Wait, 1 = Play Card 0, 2 = Play Card 1, 3 = Play Card 2
            int playAction = actions.DiscreteActions[0];
            bool onCooldown = Time.time < lastActionTime + actionCooldown;

            if (playAction >= 1 && playAction <= 3 && !onCooldown)
            {
                // Continuous Action 0: X offset (-1 to 1) -> maps to (-5.0 to 5.0)
                // Continuous Action 1: Z offset (-1 to 1) -> maps to PLAYER SIDE or OPPONENT SIDE
                float rawX = actions.ContinuousActions[0];
                float rawZ = actions.ContinuousActions[1];

                float spawnX = Mathf.Clamp(rawX * 5f, -5f, 5f);
                float spawnZ;
                if (agentFaction == Placeable.Faction.Player)
                {
                    spawnZ = Mathf.Clamp(((rawZ + 1f) / 2f) * -5.5f - 3f, -8.5f, -3f); // Player side [-8.5, -3]
                }
                else
                {
                    spawnZ = Mathf.Clamp(((rawZ + 1f) / 2f) * 5.5f + 3f, 3f, 8.5f); // Opponent side [3, 8.5]
                }

                int slotIndex = playAction - 1;
                Vector3 spawnPos = new Vector3(spawnX, 0f, spawnZ);

                if (agentFaction == Placeable.Faction.Player)
                {
                    if (cardManager != null)
                    {
                        CardData card = cardManager.GetCardDataInSlot(slotIndex);
                        if (card != null)
                        {
                            if (gameManager != null && gameManager.playerElixir >= card.ElixirCost)
                            {
                                bool success = cardManager.PlayCardFromHand(slotIndex, spawnPos);
                                if (success)
                                {
                                    gameManager.playerElixir -= card.ElixirCost; // Deduct cost
                                     // Scale reward with Elixir cost so high cost cards are rewarded appropriately
                                    AddReward(0.005f * card.ElixirCost);
                                    lastActionTime = Time.time;
                                }
                            }
                            else
                            {
                                // Negative reward for trying to play with insufficient Elixir
                                AddReward(-0.01f);
                            }
                        }
                        else
                        {
                            // Negative reward for trying to play an empty slot
                            AddReward(-0.02f);
                        }
                    }
                    else
                    {
                        AddReward(-0.02f);
                    }
                }
                else // Opponent faction
                {
                    CardData card = GetOpponentHandCard(slotIndex);
                    if (card != null)
                    {
                        if (gameManager != null && gameManager.opponentElixir >= card.ElixirCost)
                        {
                            bool success = PlayCardFromOpponentHand(slotIndex, spawnPos);
                            if (success)
                            {
                                gameManager.opponentElixir -= card.ElixirCost; // Deduct cost
                                AddReward(0.005f * card.ElixirCost);
                                lastActionTime = Time.time;
                            }
                        }
                        else
                        {
                            AddReward(-0.01f);
                        }
                    }
                    else
                    {
                        AddReward(-0.02f);
                    }
                }
            }

            // Step penalty to encourage faster game resolution — ALWAYS applied
            AddReward(-0.0002f);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var discreteActions = actionsOut.DiscreteActions;
            var continuousActions = actionsOut.ContinuousActions;

            // Manual override: holding spacebar plays immediately
            if (Input.GetKey(KeyCode.Space))
            {
                discreteActions[0] = 1;
                Vector3 mousePos = Input.mousePosition;
                continuousActions[0] = (mousePos.x / Screen.width) * 2f - 1f;
                continuousActions[1] = (mousePos.y / Screen.height) * 2f - 1f;
                return;
            }

            // Automatic fallback: play a card randomly every few seconds in heuristic mode
            if (Time.time >= lastHeuristicActionTime + heuristicCooldown)
            {
                discreteActions[0] = Random.Range(1, 4); // Play Slot 0, 1, or 2 randomly
                continuousActions[0] = Random.Range(-1f, 1f);
                continuousActions[1] = Random.Range(-1f, 1f);
                lastHeuristicActionTime = Time.time;
            }
            else
            {
                discreteActions[0] = 0;
            }
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            EnsureReferences();
            bool onCooldown = Time.time < lastActionTime + actionCooldown;

            if (agentFaction == Placeable.Faction.Player)
            {
                if (cardManager == null || gameManager == null) return;

                // Discrete Action 0 branch has size 4 (0: Wait, 1: Slot 0, 2: Slot 1, 3: Slot 2)
                // If on cooldown, or if the card in slot i is null, or if gameManager.playerElixir < ElixirCost, mask it!
                for (int i = 0; i < 3; i++)
                {
                    CardData card = cardManager.GetCardDataInSlot(i);
                    if (onCooldown || card == null || gameManager.playerElixir < card.ElixirCost)
                    {
                        // Action mask index = playAction (slotIndex + 1)
                        actionMask.SetActionEnabled(0, i + 1, false);
                    }
                }
            }
            else // Opponent faction
            {
                if (gameManager == null) return;

                for (int i = 0; i < 3; i++)
                {
                    CardData card = GetOpponentHandCard(i);
                    if (onCooldown || card == null || gameManager.opponentElixir < card.ElixirCost)
                    {
                        actionMask.SetActionEnabled(0, i + 1, false);
                    }
                }
            }
        }

        private float GetCardObservationValue(CardData card)
        {
            if (card == null) return 0f;
            string name = card.name.ToLower();
            if (name.Contains("archers")) return 0.125f;
            if (name.Contains("cinematicwarriors")) return 0.25f;
            if (name.Contains("mage")) return 0.375f;
            if (name.Contains("rockwarrior")) return 0.5f;
            if (name.Contains("rock")) return 0.625f;
            if (name.Contains("twowarriors")) return 0.75f;
            if (name.Contains("warriorinnocuous")) return 0.875f;
            if (name.Contains("warrior")) return 1.0f;
            return 0.05f;
        }

        private CardData GetOpponentHandCard(int index)
        {
            if (index < 0 || index >= opponentHand.Length) return null;
            if (opponentHand[index] == null)
            {
                CPUOpponent cpu = FindObjectOfType<CPUOpponent>();
                if (cpu != null && cpu.aiDeck != null)
                {
                    opponentHand[index] = cpu.aiDeck.GetNextCardFromDeck();
                }
            }
            return opponentHand[index];
        }

        private CardData GetOpponentNextCard()
        {
            if (opponentNextCard == null)
            {
                CPUOpponent cpu = FindObjectOfType<CPUOpponent>();
                if (cpu != null && cpu.aiDeck != null)
                {
                    opponentNextCard = cpu.aiDeck.GetNextCardFromDeck();
                }
            }
            return opponentNextCard;
        }

        private bool PlayCardFromOpponentHand(int slotIndex, Vector3 spawnPos)
        {
            CardData card = opponentHand[slotIndex];
            if (card == null) return false;

            CPUOpponent cpu = FindObjectOfType<CPUOpponent>();
            if (cpu != null && cpu.OnCardUsed != null)
            {
                cpu.OnCardUsed(card, spawnPos, Placeable.Faction.Opponent);
            }

            // Recycle card
            if (cpu != null && cpu.aiDeck != null)
            {
                cpu.aiDeck.RecycleCard(card);
            }

            // Re-fill slot from next card
            opponentHand[slotIndex] = GetOpponentNextCard();

            // Draw new next card
            if (cpu != null && cpu.aiDeck != null)
            {
                opponentNextCard = cpu.aiDeck.GetNextCardFromDeck();
            }
            else
            {
                opponentNextCard = null;
            }

            return true;
        }

        private void InitializeOpponentHand()
        {
            CPUOpponent cpu = FindObjectOfType<CPUOpponent>();
            if (cpu == null || cpu.aiDeck == null) return;

            cpu.aiDeck.ResetDeck();
            for (int i = 0; i < 3; i++)
            {
                opponentHand[i] = cpu.aiDeck.GetNextCardFromDeck();
            }
            opponentNextCard = cpu.aiDeck.GetNextCardFromDeck();
        }

        private void AddCardObservations(VectorSensor sensor, CardData card)
        {
            if (card == null)
            {
                sensor.AddObservation(0f); // Card ID value
                sensor.AddObservation(0f); // Elixir Cost
                sensor.AddObservation(0f); // HP
                sensor.AddObservation(0f); // Range
                return;
            }

            sensor.AddObservation(GetCardObservationValue(card));
            sensor.AddObservation(card.ElixirCost / 10f);

            if (card.placeablesData != null && card.placeablesData.Length > 0 && card.placeablesData[0] != null)
            {
                PlaceableData pData = card.placeablesData[0];
                sensor.AddObservation(pData.hitPoints / 50f);
                sensor.AddObservation(pData.attackRange / 10f);
            }
            else
            {
                sensor.AddObservation(0f); // HP fallback
                sensor.AddObservation(0f); // Range fallback
            }
        }

        private List<ThinkingPlaceable> GetThinkingPlaceables(Placeable.Faction faction)
        {
            List<ThinkingPlaceable> list = new List<ThinkingPlaceable>();
            if (gameManager == null) return list;

            List<ThinkingPlaceable> active = gameManager.GetActiveUnitsByFaction(faction);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null && active[i].pType != Placeable.PlaceableType.Castle)
                {
                    list.Add(active[i]);
                }
            }
            return list;
        }
    }
}
