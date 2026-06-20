using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityRoyale
{
    [CreateAssetMenu(fileName = "NewCard", menuName = "Unity Royale/Card Data")]
    public class CardData : ScriptableObject
    {
        [Header("Card graphics")]
        public Sprite cardImage;

        [Header("List of Placeables")]
        public PlaceableData[] placeablesData; //link to all the Placeables that this card spawns
        public Vector3[] relativeOffsets; //the relative offsets (from cursor) where the placeables will be dropped

        [Header("Elixir Cost")]
        [SerializeField] private int serializedElixirCost = 0;
        public int ElixirCost
        {
            get
            {
                if (serializedElixirCost > 0) return serializedElixirCost;
                if (name.Contains("Archer")) return 3;
                if (name.Contains("Mage")) return 3; // Reduced from 4 to 3
                if (name.Contains("TwoWarriors")) return 3;
                if (name.Contains("RockWarrior")) return 3; // Reduced from 4 to 3
                if (name.Contains("Rock")) return 2;
                if (name.Contains("CinematicWarriors")) return 3;
                if (name.Contains("Warrior")) return 2;
                return 3; // default fallback
            }
        }
    }
}