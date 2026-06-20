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

        private Building myCastleBuilding;      // Player's castle (Agent's own)
        private Building enemyCastleBuilding;    // Opponent's castle (target to destroy)
        private CardManager cardManager;         // Sync card hand

        private float maxCastleHP = 1f;

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
                if (myCastleBuilding == null && gameManager.playersCastle != null)
                    myCastleBuilding = gameManager.playersCastle.GetComponent<Building>();
                if (enemyCastleBuilding == null && gameManager.opponentCastle != null)
                    enemyCastleBuilding = gameManager.opponentCastle.GetComponent<Building>();
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
            if (gameManager != null)
            {
                gameManager.ResetMatch();
                // Re-acquire castle references after reset
                myCastleBuilding = gameManager.playersCastle.GetComponent<Building>();
                enemyCastleBuilding = gameManager.opponentCastle.GetComponent<Building>();
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
                // Fallback: add 40 zero observations to match VectorObservationSize
                for (int i = 0; i < 40; i++)
                    sensor.AddObservation(0f);
                return;
            }

            // 1. Castle health (2 observations) — my castle first, then enemy
            sensor.AddObservation(myCastleBuilding.hitPoints / maxCastleHP);
            sensor.AddObservation(enemyCastleBuilding.hitPoints / maxCastleHP);

            // Fetch active units from GameManager
            List<ThinkingPlaceable> myUnits = GetThinkingPlaceables(Placeable.Faction.Player);
            List<ThinkingPlaceable> enemyUnits = GetThinkingPlaceables(Placeable.Faction.Opponent);

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

            // 4. Current hand cards and next card (4 observations instead of 1 -> Total: 38 observations)
            for (int i = 0; i < 3; i++)
            {
                CardData handCard = (cardManager != null) ? cardManager.GetCardDataInSlot(i) : null;
                sensor.AddObservation(GetCardObservationValue(handCard));
            }

            CardData nextCard = playerDeck != null ? playerDeck.PeekNextCard() : null;
            sensor.AddObservation(GetCardObservationValue(nextCard));

            // 5. Elixir levels (2 observations -> Total: 40 observations)
            sensor.AddObservation(gameManager != null ? gameManager.playerElixir / 10f : 0f);
            sensor.AddObservation(gameManager != null ? gameManager.opponentElixir / 10f : 0f);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            // Discrete Action 0: 0 = Wait, 1 = Play Card 0, 2 = Play Card 1, 3 = Play Card 2
            int playAction = actions.DiscreteActions[0];
            bool onCooldown = Time.time < lastActionTime + actionCooldown;

            if (playAction >= 1 && playAction <= 3 && !onCooldown)
            {
                // Continuous Action 0: X offset (-1 to 1) -> maps to (-5.0 to 5.0)
                // Continuous Action 1: Z offset (-1 to 1) -> maps to (-8.5 to -3.0) PLAYER SIDE
                float rawX = actions.ContinuousActions[0];
                float rawZ = actions.ContinuousActions[1];

                float spawnX = Mathf.Clamp(rawX * 5f, -5f, 5f);
                float spawnZ = Mathf.Clamp(((rawZ + 1f) / 2f) * -5.5f - 3f, -8.5f, -3f);

                int slotIndex = playAction - 1;
                Vector3 spawnPos = new Vector3(spawnX, 0f, spawnZ);

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
            if (cardManager == null || gameManager == null) return;

            bool onCooldown = Time.time < lastActionTime + actionCooldown;

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
