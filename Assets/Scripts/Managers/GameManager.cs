using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.AddressableAssets;

namespace UnityRoyale
{
    public class GameManager : MonoBehaviour
    {
		[Header("Settings")]
		public bool autoStart = false;

		[Header("Public References")]
        public MonoBehaviour navMesh;
		public GameObject playersCastle, opponentCastle;
		public GameObject introTimeline;
        public PlaceableData castlePData;
		public ParticlePool appearEffectPool;
        public MLPlayerAgent mlAgent;
        public MLPlayerAgent opponentMLAgent;

        public CPUOpponent opponentCPU => CPUOpponent;

        private CardManager cardManager;
        private CPUOpponent CPUOpponent;
        private InputManager inputManager;
		private AudioManager audioManager;
		private UIManager UIManager;
		private CinematicsManager cinematicsManager;

        private List<ThinkingPlaceable> playerUnits, opponentUnits;
        private List<ThinkingPlaceable> playerBuildings, opponentBuildings;
        private List<ThinkingPlaceable> allPlayers, allOpponents; //contains both Buildings and Units
		private List<ThinkingPlaceable> allThinkingPlaceables;
		private List<Projectile> allProjectiles;
        private bool gameOver = false;
        
        [Header("Elixir Economy")]
        public float playerElixir = 5.0f;
        public float opponentElixir = 5.0f;
        public float maxElixir = 10.0f;
        public float elixirRegenRate = 0.7f; // about 1 Elixir per 1.4 seconds

        private bool updateAllPlaceables = false; //used to force an update of all AIBrains in the Update loop
        private const float THINKING_DELAY = 2f;
        private int navMeshLogCounter = 0;
        private int useCardLogCounter = 0;
        private const int LOG_INTERVAL = 60; // Log once every 60 calls

        private void EnsureNavMeshReference()
        {
            if (navMesh != null) return;

            // 1. Try to find any component whose class name is "NavMeshSurface"
            foreach (var mono in FindObjectsOfType<MonoBehaviour>())
            {
                if (mono != null && mono.GetType().Name == "NavMeshSurface")
                {
                    navMesh = mono;
                    // Debug.Log($"[DEBUG][NavMeshLog] Dynamically assigned navMesh reference using type name matching: {mono.GetType().FullName} ({mono.GetType().Assembly.GetName().Name})");
                    return;
                }
            }

            // 2. Try GameObject name fallback
            GameObject go = GameObject.Find("NavMesh Surface");
            if (go != null)
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp != null && comp.GetType().Name == "NavMeshSurface")
                    {
                        navMesh = comp as MonoBehaviour;
                        // Debug.Log($"[DEBUG][NavMeshLog] Dynamically assigned navMesh reference from GameObject 'NavMesh Surface' using type name matching: {comp.GetType().FullName}");
                        return;
                    }
                }
            }

            // 3. Try Resources.FindObjectsOfTypeAll (inactive elements)
            foreach (var mono in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mono != null && mono.GetType().Name == "NavMeshSurface" && mono.gameObject.scene.name != null)
                {
                    navMesh = mono;
                    // Debug.Log($"[DEBUG][NavMeshLog] Dynamically assigned navMesh reference from inactive objects using type name matching: {mono.GetType().FullName}");
                    return;
                }
            }

            Debug.LogError("[DEBUG][NavMeshLog] CRITICAL: No NavMeshSurface component found in the scene by any type-name lookup method!");
        }

        private void RebuildNavMeshDynamic()
        {
            EnsureNavMeshReference();
            if (navMesh != null)
            {
                var method = navMesh.GetType().GetMethod("BuildNavMesh");
                if (method != null)
                {
                    method.Invoke(navMesh, null);
                    if (navMeshLogCounter++ % LOG_INTERVAL == 0)
                    {
                        // Debug.Log($"[NavMesh] BuildNavMesh called on {navMesh.gameObject.name} (logged every {LOG_INTERVAL} calls)");
                    }
                }
                else
                {
                    Debug.LogError($"[DEBUG][NavMeshLog] BuildNavMesh method not found on component {navMesh.GetType().FullName}!");
                }
            }
            else
            {
                Debug.LogWarning("[DEBUG][NavMeshLog] BuildNavMesh skipped because no NavMeshSurface component is referenceable.");
            }
        }

        private void Awake()
        {
            EnsureNavMeshReference();

            cardManager = GetComponent<CardManager>();
            CPUOpponent = GetComponent<CPUOpponent>();
            inputManager = GetComponent<InputManager>();
			//audioManager = GetComponentInChildren<AudioManager>();
			cinematicsManager = GetComponentInChildren<CinematicsManager>();
			UIManager = GetComponent<UIManager>();

            if (mlAgent != null && mlAgent.isActiveAndEnabled)
            {
                autoStart = true;
            }

			if(autoStart && introTimeline != null)
				introTimeline.SetActive(false);

            //listeners on other managers
            cardManager.OnCardUsed += UseCard;
            CPUOpponent.OnCardUsed += UseCard;

            //initialise Placeable lists, for the AIs to pick up and find a target
            playerUnits = new List<ThinkingPlaceable>();
            playerBuildings = new List<ThinkingPlaceable>();
            opponentUnits = new List<ThinkingPlaceable>();
            opponentBuildings = new List<ThinkingPlaceable>();
            allPlayers = new List<ThinkingPlaceable>();
            allOpponents = new List<ThinkingPlaceable>();
			allThinkingPlaceables = new List<ThinkingPlaceable>();
			allProjectiles = new List<Projectile>();
        }

        private void Start()
        {
            EnsureNavMeshReference();

			//Insert castles into lists
			SetupPlaceable(playersCastle, castlePData, Placeable.Faction.Player);
            SetupPlaceable(opponentCastle, castlePData, Placeable.Faction.Opponent);

			cardManager.LoadDeck();
            CPUOpponent.LoadDeck();

			//audioManager.GoToDefaultSnapshot();

            playerElixir = 5.0f;
            opponentElixir = 5.0f;

			if(autoStart)
				StartMatch();
        }

		//called by the intro cutscene
		public void StartMatch()
		{
			// CPUOpponent is always the enemy (Opponent faction)
			CPUOpponent.faction = Placeable.Faction.Opponent;
			CPUOpponent.StartActing();
		}

        //the Update loop pings all the ThinkingPlaceables in the scene, and makes them act
        private void Update()
        {
            if(gameOver)
                return;

            playerElixir = Mathf.Min(maxElixir, playerElixir + Time.deltaTime * elixirRegenRate);
            opponentElixir = Mathf.Min(maxElixir, opponentElixir + Time.deltaTime * elixirRegenRate);


            ThinkingPlaceable targetToPass; //ref
			ThinkingPlaceable p; //ref

			for(int pN=0; pN<allThinkingPlaceables.Count; pN++)
            {
                p = allThinkingPlaceables[pN];

                if(updateAllPlaceables)
                    p.state = ThinkingPlaceable.States.Idle; //forces the assignment of a target in the switch below

                switch(p.state)
                {
                    case ThinkingPlaceable.States.Idle:
                        //this if is for innocuous testing Units
                        if(p.targetType == Placeable.PlaceableTarget.None)
                            break;

                        //find closest target using sight range constraints and assign it to the ThinkingPlaceable
                        bool targetFound = FindBestTarget(p, out targetToPass);
                        if(!targetFound)
                        {
                            break;
                        }
                        p.SetTarget(targetToPass);
						p.Seek();
                        break;


                    case ThinkingPlaceable.States.Seeking:
                        if (p.target == null)
                        {
                            p.state = ThinkingPlaceable.States.Idle;
                            break;
                        }
                        // Periodically retarget to the closest enemy to prevent stuck units and handle new spawns
                        if (Time.time >= p.nextRetargetTime)
                        {
                            p.state = ThinkingPlaceable.States.Idle;
                            break;
                        }
						if(p.IsTargetInRange())
                    	{
							p.StartAttack();
						}
                        else if (p.target != null)
                        {
                            float dist = Vector3.Distance(p.transform.position, p.target.transform.position);
                            // If they are close (less than range + 1.5 units) but not attacking
                            if (dist < p.attackRange + 1.5f)
                            {
                                NavMeshAgent agent = p.GetComponent<NavMeshAgent>();
                                // If they have stopped moving or velocity is very low (throttled log)
                                if (agent != null && agent.enabled && agent.velocity.sqrMagnitude < 0.01f && Time.time >= p.nextRetargetTime - 0.05f)
                                {
                                    // Log stuck status
                                    // Debug.LogWarning($"[StuckLog] {p.gameObject.name} ({p.faction}) target: {p.target.name}. " +
                                    //                  $"Dist: {dist:F2}, Range: {p.attackRange:F2}, isStopped: {agent.isStopped}, " +
                                    //                  $"PathStatus: {agent.pathStatus}");
                                }
                            }
                        }
                        break;
                        

					case ThinkingPlaceable.States.Attacking:
                        if (p.target == null)
                        {
                            p.state = ThinkingPlaceable.States.Idle;
                            break;
                        }
						if(p.IsTargetInRange(true))
						{
							if(Time.time >= p.lastBlowTime + p.attackRatio)
							{
								p.DealBlow();
								//Animation will produce the damage, calling animation events OnDealDamage and OnProjectileFired. See ThinkingPlaceable
							}
						}
						else
						{
							if(p.pType == Placeable.PlaceableType.Unit)
							{
								p.Seek();
							}
							else
							{
								p.state = ThinkingPlaceable.States.Idle;
							}
						}
						break;

					case ThinkingPlaceable.States.Dead:
						Debug.LogError("A dead ThinkingPlaceable shouldn't be in this loop");
						break;
                }
            }

			Projectile currProjectile;
			float progressToTarget;
			for(int prjN = allProjectiles.Count - 1; prjN >= 0; prjN--)
            {
                if (prjN >= allProjectiles.Count)
                    continue;

				currProjectile = allProjectiles[prjN];
                if (currProjectile == null)
                {
                    allProjectiles.RemoveAt(prjN);
                    continue;
                }

				progressToTarget = currProjectile.Move();
				if(progressToTarget >= 1f)
				{
					if(currProjectile.target != null && currProjectile.target.state != ThinkingPlaceable.States.Dead) //target might be dead already as this projectile is flying
					{
						float newHP = currProjectile.target.SufferDamage(currProjectile.damage);
                        if (currProjectile.target.healthBar != null)
                        {
                            currProjectile.target.healthBar.SetHealth(newHP);
                        }

                        // ML-Agent Reward (Agent = Player faction)
                        if (mlAgent != null && mlAgent.isActiveAndEnabled)
                        {
                            if (currProjectile.target.faction == Placeable.Faction.Opponent) // Agent(Player) hits Enemy
                            {
                                mlAgent.AddReward(currProjectile.damage * 0.005f);
                            }
                            else if (currProjectile.target.faction == Placeable.Faction.Player) // Enemy hits Agent(Player)
                            {
                                mlAgent.AddReward(-currProjectile.damage * 0.005f);
                            }
                        }
					}

					if (currProjectile != null)
                    {
                        Destroy(currProjectile.gameObject);
                    }
                    
                    if (prjN < allProjectiles.Count)
                    {
                        allProjectiles.RemoveAt(prjN);
                    }
				}
			}

            updateAllPlaceables = false; //is set to true by UseCard()
        }

        private List<ThinkingPlaceable> GetAttackList(Placeable.Faction f, Placeable.PlaceableTarget t)
        {
            switch(t)
            {
                case Placeable.PlaceableTarget.Both:
                    return (f == Placeable.Faction.Player) ? allOpponents : allPlayers;
				case Placeable.PlaceableTarget.OnlyBuildings:
                    return (f == Placeable.Faction.Player) ? opponentBuildings : playerBuildings;
				default:
					Debug.LogError("What faction is this?? Not Player nor Opponent.");
					return null;
            }
        }

        private bool FindClosestInList(Vector3 p, List<ThinkingPlaceable> list, out ThinkingPlaceable t)
        {
            t = null;
            bool targetFound = false;
            float closestDistanceSqr = Mathf.Infinity; //anything closer than here becomes the new designated target

            for(int i=0; i<list.Count; i++)
            {                
                if (list[i] == null || list[i].gameObject == null)
                    continue;

				float sqrDistance = (p - list[i].transform.position).sqrMagnitude;
                if(sqrDistance < closestDistanceSqr)
                {
                    t = list[i];
                    closestDistanceSqr = sqrDistance;
                    targetFound = true;
                }
            }

            return targetFound;
        }

        private bool FindClosestWithinRange(Vector3 p, List<ThinkingPlaceable> list, float range, out ThinkingPlaceable t)
        {
            t = null;
            bool targetFound = false;
            float closestDistanceSqr = range * range; // must be within range

            for(int i=0; i<list.Count; i++)
            {                
                if (list[i] == null || list[i].gameObject == null)
                    continue;

                float sqrDistance = (p - list[i].transform.position).sqrMagnitude;
                if(sqrDistance < closestDistanceSqr)
                {
                    t = list[i];
                    closestDistanceSqr = sqrDistance;
                    targetFound = true;
                }
            }

            return targetFound;
        }

        private bool IsPathToTargetComplete(Vector3 start, Vector3 targetPos)
        {
            UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
            if (UnityEngine.AI.NavMesh.CalculatePath(start, targetPos, UnityEngine.AI.NavMesh.AllAreas, path))
            {
                return path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
            }
            return false;
        }

        private bool FindClosestWithinRangeWithPath(Vector3 startPos, List<ThinkingPlaceable> list, float range, out ThinkingPlaceable t)
        {
            t = null;
            bool targetFound = false;
            float closestDistanceSqr = range * range;

            List<KeyValuePair<ThinkingPlaceable, float>> candidates = new List<KeyValuePair<ThinkingPlaceable, float>>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].gameObject == null)
                    continue;

                float sqrDistance = (startPos - list[i].transform.position).sqrMagnitude;
                if (sqrDistance < closestDistanceSqr)
                {
                    candidates.Add(new KeyValuePair<ThinkingPlaceable, float>(list[i], sqrDistance));
                }
            }

            // Sort candidates by distance (closest first)
            candidates.Sort((a, b) => a.Value.CompareTo(b.Value));

            // Check path completeness starting from the closest
            for (int i = 0; i < candidates.Count; i++)
            {
                ThinkingPlaceable candidate = candidates[i].Key;
                if (IsPathToTargetComplete(startPos, candidate.transform.position))
                {
                    t = candidate;
                    targetFound = true;
                    break;
                }
            }

            return targetFound;
        }

        private bool FindBestTarget(ThinkingPlaceable p, out ThinkingPlaceable bestTarget)
        {
            bestTarget = null;
            if (p.targetType == Placeable.PlaceableTarget.None)
                return false;

            List<ThinkingPlaceable> enemyUnits = (p.faction == Placeable.Faction.Player) ? opponentUnits : playerUnits;
            List<ThinkingPlaceable> enemyBuildings = (p.faction == Placeable.Faction.Player) ? opponentBuildings : playerBuildings;

            if (p.targetType == Placeable.PlaceableTarget.OnlyBuildings)
            {
                return FindClosestInList(p.transform.position, enemyBuildings, out bestTarget);
            }
            else // Both
            {
                // 1. Look for the closest unit within sight range that has a complete path (restored to 10.0f)
                float sightRange = Mathf.Max(10.0f, p.attackRange);
                bool unitFound = FindClosestWithinRangeWithPath(p.transform.position, enemyUnits, sightRange, out bestTarget);
                if (unitFound)
                    return true;

                // 2. Fallback to closest building (no range limit, so they walk towards the enemy castle)
                return FindClosestInList(p.transform.position, enemyBuildings, out bestTarget);
            }
        }

        public void UseCard(CardData cardData, Vector3 position, Placeable.Faction pFaction)
        {
            if (useCardLogCounter++ % LOG_INTERVAL == 0)
            {
                // Debug.Log($"[UseCard] Card: {cardData.name}, Faction: {pFaction}, Position: {position}, Time: {Time.time} (logged every {LOG_INTERVAL} calls)");
            }
            for(int pNum=0; pNum<cardData.placeablesData.Length; pNum++)
            {
                PlaceableData pDataRef = cardData.placeablesData[pNum];
                Quaternion rot = (pFaction == Placeable.Faction.Player) ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                //Prefab to spawn is the associatedPrefab if it's the Player faction, otherwise it's alternatePrefab. But if alternatePrefab is null, then first one is taken
                GameObject prefabToSpawn = (pFaction == Placeable.Faction.Player) ? pDataRef.associatedPrefab : ((pDataRef.alternatePrefab == null) ? pDataRef.associatedPrefab : pDataRef.alternatePrefab);
                GameObject newPlaceableGO = Instantiate<GameObject>(prefabToSpawn, position + cardData.relativeOffsets[pNum], rot);
                
                SetupPlaceable(newPlaceableGO, pDataRef, pFaction);

				appearEffectPool.UseParticles(position + cardData.relativeOffsets[pNum]);
            }
			//audioManager.PlayAppearSFX(position);

            updateAllPlaceables = true; //will force all AIBrains to update next time the Update loop is run
        }


        //setups all scripts and listeners on a Placeable GameObject
        private void SetupPlaceable(GameObject go, PlaceableData pDataRef, Placeable.Faction pFaction)
        {
            EnsureNavMeshReference();

            //Add the appropriate script
                switch(pDataRef.pType)
                {
                    case Placeable.PlaceableType.Unit:
                        Unit uScript = go.GetComponent<Unit>();
                        uScript.Activate(pFaction, pDataRef); //enables NavMeshAgent
                        uScript.OnDealDamage -= OnPlaceableDealtDamage;
						uScript.OnDealDamage += OnPlaceableDealtDamage;
                        uScript.OnProjectileFired -= OnProjectileFired;
						uScript.OnProjectileFired += OnProjectileFired;
                        AddPlaceableToList(uScript); //add the Unit to the appropriate list
                        UIManager.AddHealthUI(uScript);
                        break;

                    case Placeable.PlaceableType.Building:
                    case Placeable.PlaceableType.Castle:
                        Building bScript = go.GetComponent<Building>();
                        bScript.Activate(pFaction, pDataRef);
                        bScript.OnDealDamage -= OnPlaceableDealtDamage;
						bScript.OnDealDamage += OnPlaceableDealtDamage;
                        bScript.OnProjectileFired -= OnProjectileFired;
						bScript.OnProjectileFired += OnProjectileFired;
                        AddPlaceableToList(bScript); //add the Building to the appropriate list
                        UIManager.AddHealthUI(bScript);

                        //special case for castles
                        if(pDataRef.pType == Placeable.PlaceableType.Castle)
                        {
                            bScript.OnDie -= OnCastleDead;
                            bScript.OnDie += OnCastleDead;
                        }
                        
                        RebuildNavMeshDynamic();
                        break;

                    case Placeable.PlaceableType.Obstacle:
                        Obstacle oScript = go.GetComponent<Obstacle>();
                        oScript.Activate(pDataRef);
                        RebuildNavMeshDynamic();
                        break;

                    case Placeable.PlaceableType.Spell:
                        //Spell sScript = newPlaceable.AddComponent<Spell>();
                        //sScript.Activate(pFaction, cardData.hitPoints);
                        //TODO: activate the spell and… ?
                        break;
                }

                go.GetComponent<Placeable>().OnDie -= OnPlaceableDead;
                go.GetComponent<Placeable>().OnDie += OnPlaceableDead;
        }

		private void OnProjectileFired(ThinkingPlaceable p)
		{
			Vector3 adjTargetPos = p.target.transform.position;
			adjTargetPos.y = 1.5f;
            Vector3 diff = adjTargetPos - p.projectileSpawnPoint.position;
            Quaternion rot = (diff.sqrMagnitude > 0.001f) ? Quaternion.LookRotation(diff) : Quaternion.identity;

			Projectile prj = Instantiate<GameObject>(p.projectilePrefab, p.projectileSpawnPoint.position, rot).GetComponent<Projectile>();
			prj.target = p.target;
			prj.damage = p.damage;
			allProjectiles.Add(prj);
		}

		private void OnPlaceableDealtDamage(ThinkingPlaceable p)
		{
			if(p.target.state != ThinkingPlaceable.States.Dead)
			{
				float newHealth = p.target.SufferDamage(p.damage);
                if (p.target.healthBar != null)
                {
                    p.target.healthBar.SetHealth(newHealth);
                }

                // ML-Agent Reward (Agent = Player faction)
                if (mlAgent != null && mlAgent.isActiveAndEnabled)
                {
                    if (p.faction == Placeable.Faction.Player) // Agent(Player) deals damage
                    {
                        mlAgent.AddReward(p.damage * 0.005f);
                    }
                    else if (p.faction == Placeable.Faction.Opponent) // Enemy deals damage to Agent
                    {
                        mlAgent.AddReward(-p.damage * 0.005f);
                    }
                }
			}
		}

		private void OnCastleDead(Placeable c)
		{
            // Debug.Log($"[MatchState] OnCastleDead called! Castle: {c.gameObject.name}, Faction: {c.faction}");
            bool isTraining = mlAgent != null && mlAgent.isActiveAndEnabled && mlAgent.controlMatchReset;

            if (mlAgent != null && mlAgent.isActiveAndEnabled)
            {
                if (c.faction == Placeable.Faction.Opponent) // Agent(Player) wins!
                {
                    mlAgent.AddReward(1.0f);
                }
                else // Agent(Player)'s castle died -> Agent loses!
                {
                    mlAgent.AddReward(-1.0f);
                }
                mlAgent.EndEpisode();
            }

            if (opponentMLAgent != null && opponentMLAgent.isActiveAndEnabled)
            {
                if (c.faction == Placeable.Faction.Player) // Agent(Opponent) wins!
                {
                    opponentMLAgent.AddReward(1.0f);
                }
                else // Agent(Opponent) loses!
                {
                    opponentMLAgent.AddReward(-1.0f);
                }
                opponentMLAgent.EndEpisode();
            }

            if (isTraining)
            {
                return; // skip cutscenes and UI in training mode
            }

			cinematicsManager.PlayCollapseCutscene(c.faction);
            c.OnDie -= OnCastleDead;
            gameOver = true; //stops the thinking loop

			//stop all the ThinkingPlaceables		
			ThinkingPlaceable thkPl;
			for(int pN=0; pN<allThinkingPlaceables.Count; pN++)
            {
				thkPl = allThinkingPlaceables[pN];
				if(thkPl.state != ThinkingPlaceable.States.Dead)
				{
					thkPl.Stop();
					thkPl.transform.LookAt(c.transform.position);
					UIManager.RemoveHealthUI(thkPl);
				}
			}

			//audioManager.GoToEndMatchSnapshot();
			CPUOpponent.StopActing();
		}

		public void OnEndGameCutsceneOver()
		{
			UIManager.ShowGameOverUI();
		}

        private void OnPlaceableDead(Placeable p)
        {
            p.OnDie -= OnPlaceableDead; //remove the listener
            
            switch(p.pType)
            {
                case Placeable.PlaceableType.Unit:
					Unit u = (Unit)p;
                    RemovePlaceableFromList(u);
					u.OnDealDamage -= OnPlaceableDealtDamage;
					u.OnProjectileFired -= OnProjectileFired;
					UIManager.RemoveHealthUI(u);
					StartCoroutine(Dispose(u));
                    break;

                case Placeable.PlaceableType.Building:
                case Placeable.PlaceableType.Castle:
					Building b = (Building)p;
                    if (p.pType != Placeable.PlaceableType.Castle)
                    {
                        RemovePlaceableFromList(b);
                    }
					UIManager.RemoveHealthUI(b);
					b.OnDealDamage -= OnPlaceableDealtDamage;
					b.OnProjectileFired -= OnProjectileFired;
                    StartCoroutine(RebuildNavmesh()); //need to fix for normal buildings
					
					//we don't dispose of the Castle
					if(p.pType != Placeable.PlaceableType.Castle)
						StartCoroutine(Dispose(b));
                    break;

                case Placeable.PlaceableType.Obstacle:
                    StartCoroutine(RebuildNavmesh());
                    break;

                case Placeable.PlaceableType.Spell:
                    //TODO: can spells die?
                    break;
            }
        }

		private IEnumerator Dispose(ThinkingPlaceable p)
		{
			yield return new WaitForSeconds(3f);

            if (p != null && p.gameObject != null)
            {
			    Destroy(p.gameObject);
            }
		}

        private IEnumerator RebuildNavmesh()
        {
            yield return new WaitForEndOfFrame();

            EnsureNavMeshReference();

            RebuildNavMeshDynamic();
            //FIX: dragged obstacles are included in the navmesh when it's baked
        }

        private void AddPlaceableToList(ThinkingPlaceable p)
        {
			allThinkingPlaceables.Add(p);

			if(p.faction == Placeable.Faction.Player)
            {
				allPlayers.Add(p);
            	
				if(p.pType == Placeable.PlaceableType.Unit)
                    playerUnits.Add(p);
				else
                    playerBuildings.Add(p);
            }
            else if(p.faction == Placeable.Faction.Opponent)
            {
				allOpponents.Add(p);
            	
				if(p.pType == Placeable.PlaceableType.Unit)
                    opponentUnits.Add(p);
				else
                    opponentBuildings.Add(p);
            }
            else
            {
                Debug.LogError("Error in adding a Placeable in one of the player/opponent lists");
            }
        }

        private void RemovePlaceableFromList(ThinkingPlaceable p)
        {
			allThinkingPlaceables.Remove(p);

			if(p.faction == Placeable.Faction.Player)
            {
				allPlayers.Remove(p);
            	
				if(p.pType == Placeable.PlaceableType.Unit)
                    playerUnits.Remove(p);
				else
                    playerBuildings.Remove(p);
            }
            else if(p.faction == Placeable.Faction.Opponent)
            {
				allOpponents.Remove(p);
            	
				if(p.pType == Placeable.PlaceableType.Unit)
                    opponentUnits.Remove(p);
				else
                    opponentBuildings.Remove(p);
            }
            else
            {
                Debug.LogError("Error in removing a Placeable from one of the player/opponent lists");
            }
        }

        public List<ThinkingPlaceable> GetActiveUnitsByFaction(Placeable.Faction faction)
        {
            return (faction == Placeable.Faction.Player) ? allPlayers : allOpponents;
        }

        public void ResetMatch()
        {
            // Debug.Log("[MatchState] ResetMatch started!");
            
            // 0. Stop CPU Opponent before starting the reset
            if (CPUOpponent != null)
            {
                CPUOpponent.StopActing();
            }

            // 1. Destroy all spawned units (keeping castles)
            // Debug.Log($"[MatchState] ResetMatch: allThinkingPlaceables count before destroy = {allThinkingPlaceables.Count}");
            for (int i = allThinkingPlaceables.Count - 1; i >= 0; i--)
            {
                ThinkingPlaceable p = allThinkingPlaceables[i];
                if (p != null)
                {
                    bool isCastle = (p.gameObject == playersCastle || p.gameObject == opponentCastle);
                    // Debug.Log($"[MatchState] - Index {i}: {p.gameObject.name} ({p.faction}), isCastle = {isCastle}");
                    if (!isCastle)
                    {
                        UIManager.RemoveHealthUI(p);
                        Destroy(p.gameObject);
                    }
                }
                else
                {
                    // Debug.Log($"[MatchState] - Index {i}: null");
                }
            }

            // 2. Clear lists (keeping castles)
            allThinkingPlaceables.Clear();
            allPlayers.Clear();
            allOpponents.Clear();
            playerUnits.Clear();
            opponentUnits.Clear();
            playerBuildings.Clear();
            opponentBuildings.Clear();

            // 3. Destroy all projectiles
            for (int i = 0; i < allProjectiles.Count; i++)
            {
                if (allProjectiles[i] != null)
                    Destroy(allProjectiles[i].gameObject);
            }
            allProjectiles.Clear();

            // 4. Reset UIManager UI and CardManager cards
            UIManager.ResetUI();
            if (cardManager != null)
            {
                cardManager.ResetDeckAndUI();
                // Also ensure we remove any existing health bar for the castles to prevent duplicates
                if (playersCastle != null) UIManager.RemoveHealthUI(playersCastle.GetComponent<ThinkingPlaceable>());
                if (opponentCastle != null) UIManager.RemoveHealthUI(opponentCastle.GetComponent<ThinkingPlaceable>());
            }

            // 5. Re-setup castles
            if (playersCastle != null)
            {
                Placeable p = playersCastle.GetComponent<Placeable>();
                p.OnDie -= OnCastleDead;
                p.OnDie -= OnPlaceableDead;
                
                Building bScript = playersCastle.GetComponent<Building>();
                bScript.OnDealDamage -= OnPlaceableDealtDamage;
                bScript.OnProjectileFired -= OnProjectileFired;
                if (bScript.destructionTimeline != null) bScript.destructionTimeline.Stop();
                if (bScript.constructionTimeline != null) bScript.constructionTimeline.Stop();
                bScript.hitPoints = castlePData.hitPoints;
                bScript.state = ThinkingPlaceable.States.Dragged;
                
                SetupPlaceable(playersCastle, castlePData, Placeable.Faction.Player);
            }

            if (opponentCastle != null)
            {
                Placeable p = opponentCastle.GetComponent<Placeable>();
                p.OnDie -= OnCastleDead;
                p.OnDie -= OnPlaceableDead;

                Building bScript = opponentCastle.GetComponent<Building>();
                bScript.OnDealDamage -= OnPlaceableDealtDamage;
                bScript.OnProjectileFired -= OnProjectileFired;
                if (bScript.destructionTimeline != null) bScript.destructionTimeline.Stop();
                if (bScript.constructionTimeline != null) bScript.constructionTimeline.Stop();
                bScript.hitPoints = castlePData.hitPoints;
                bScript.state = ThinkingPlaceable.States.Dragged;

                SetupPlaceable(opponentCastle, castlePData, Placeable.Faction.Opponent);
            }

            // 6. Reset decks (load Player's deck for UI cards; Opponent deck only needs currentCard reset)
            if (CPUOpponent != null && CPUOpponent.aiDeck != null)
            {
                CPUOpponent.aiDeck.ResetDeck();
            }

            // 7. Reset game state
            gameOver = false;
            updateAllPlaceables = false;
            playerElixir = 5.0f;
            opponentElixir = 5.0f;

            // 8. Re-start CPU Opponent as enemy
            if (CPUOpponent != null)
            {
                CPUOpponent.faction = Placeable.Faction.Opponent;
                CPUOpponent.StartActing();
            }
            
            // Debug.Log("[MatchState] ResetMatch completed!");
        }

#if UNITY_EDITOR || !ML_TRAINING
        private GUIStyle elixirBubbleStyle;
        private GUIStyle elixirTextStyle;
        private GUIStyle opponentTextStyle;

        private void OnGUI()
        {
            // Initialize styles
            if (elixirBubbleStyle == null)
            {
                Texture2D bgTex = new Texture2D(1, 1);
                bgTex.SetPixel(0, 0, new Color(0.12f, 0.08f, 0.2f, 0.8f)); // Dark translucent purple bg
                bgTex.Apply();

                elixirBubbleStyle = new GUIStyle();
                elixirBubbleStyle.normal.background = bgTex;
                elixirBubbleStyle.padding = new RectOffset(8, 8, 4, 4);
                elixirBubbleStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (elixirTextStyle == null)
            {
                elixirTextStyle = new GUIStyle();
                elixirTextStyle.normal.textColor = new Color(0.85f, 0.45f, 1f, 1f); // Vibrant light purple
                elixirTextStyle.fontSize = 17;
                elixirTextStyle.fontStyle = FontStyle.Bold;
                elixirTextStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (opponentTextStyle == null)
            {
                opponentTextStyle = new GUIStyle();
                opponentTextStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 0.7f); // semi-translucent grey
                opponentTextStyle.fontSize = 10;
                opponentTextStyle.fontStyle = FontStyle.Normal;
                opponentTextStyle.alignment = TextAnchor.MiddleCenter;
            }

            int width = 110;
            int height = 46;
            
            // Default position fallback (centered above hand at bottom)
            float x = Screen.width / 2f - width / 2f;
            float y = Screen.height - height - 120f;

            bool isLayoutInitialized = false;

            if (cardManager != null && cardManager.cardsDashboard != null)
            {
                Vector3[] corners = new Vector3[4];
                cardManager.cardsDashboard.GetWorldCorners(corners);
                
                float dashboardWidth = corners[2].x - corners[0].x;
                float dashboardHeight = corners[2].y - corners[3].y;
                
                // Only position relative to dashboard if layout has completed (width/height > 1px)
                if (dashboardWidth > 1f && dashboardHeight > 1f)
                {
                    isLayoutInitialized = true;
                    
                    // Check if we have enough horizontal space on the right of the dashboard
                    float spaceOnRight = Screen.width - corners[2].x;
                    if (spaceOnRight >= width + 25f)
                    {
                        // Landscape/Wide layout: position to the right of the dashboard
                        x = corners[2].x + 15f;
                        float dashboardCenterY = corners[3].y + dashboardHeight / 2f;
                        y = Screen.height - dashboardCenterY - height / 2f;
                    }
                    else
                    {
                        // Portrait/Narrow layout (Simulator View): position centered horizontally, directly above dashboard
                        x = Screen.width / 2f - width / 2f;
                        float dashboardTopY = Screen.height - corners[2].y; // Convert to top-left GUI coordinates
                        y = dashboardTopY - height - 15f; // 15px margin above dashboard
                    }
                }
            }

            // Ensure bubble stays on screen
            x = Mathf.Clamp(x, 10f, Screen.width - width - 10f);
            y = Mathf.Clamp(y, 10f, Screen.height - height - 10f);

            GUILayout.BeginArea(new Rect(x, y, width, height), elixirBubbleStyle);
            
            // Draw Player Elixir
            GUILayout.Label($"Elixir: {playerElixir:F1}", elixirTextStyle);
            
            // Draw Opponent Elixir (smaller font for debug)
            GUILayout.Label($"Opponent: {opponentElixir:F1}", opponentTextStyle);

            GUILayout.EndArea();
        }
#endif
    }
}