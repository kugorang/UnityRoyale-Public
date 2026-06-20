using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace UnityRoyale
{
    //humanoid or anyway a walking placeable
    public class Unit : ThinkingPlaceable
    {
        //data coming from the PlaceableData
        private float speed;

        private Animator animator;
        private NavMeshAgent navMeshAgent;
        private ThinkingPlaceable lastLoggedTarget;
        private int seekCallCount = 0;
        private float lastMovedTime;
        private Vector3 lastPosition;


        private void Awake()
        {
            pType = Placeable.PlaceableType.Unit;

            //find references to components
            animator = GetComponent<Animator>();
            navMeshAgent = GetComponent<NavMeshAgent>(); //will be disabled until Activate is called
			audioSource = GetComponent<AudioSource>();
        }

        //called by GameManager when this Unit is played on the play field
        public void Activate(Faction pFaction, PlaceableData pData)
        {
            faction = pFaction;
            hitPoints = pData.hitPoints;
            targetType = pData.targetType;
            attackRange = pData.attackRange;
            attackRatio = pData.attackRatio;
            speed = pData.speed;
            damage = pData.damagePerAttack;
			attackAudioClip = pData.attackClip;
			dieAudioClip = pData.dieClip;
            //TODO: add more as necessary
            
            animator.SetFloat("MoveSpeed", speed); //will act as multiplier to the speed of the run animation clip

            state = States.Idle;

            // Sample the nearest valid position on the NavMesh before enabling the agent
            UnityEngine.AI.NavMeshHit hit;
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out hit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                transform.position = hit.position;
            }

            navMeshAgent.enabled = true;
            navMeshAgent.speed = speed; // Set speed after enabling to avoid Unity resetting it
            navMeshAgent.Warp(transform.position);
            lastLoggedTarget = null;

            lastMovedTime = Time.time;
            lastPosition = transform.position;
        }

        public override void SetTarget(ThinkingPlaceable t)
        {
            base.SetTarget(t);
        }

        public override void Seek()
        {
            if(target == null)
                return;

            base.Seek();

            bool pathRequested = false;
            if (navMeshAgent.enabled)
            {
                if (!navMeshAgent.isOnNavMesh)
                {
                    UnityEngine.AI.NavMeshHit hit;
                    if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out hit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        navMeshAgent.Warp(hit.position);
                    }
                }

                if (navMeshAgent.isOnNavMesh)
                {
                    pathRequested = navMeshAgent.SetDestination(target.transform.position);
                    navMeshAgent.isStopped = false;
                }
            }
            animator.SetBool("IsMoving", true);

            if (target != lastLoggedTarget || !pathRequested || navMeshAgent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                lastLoggedTarget = target;
#if UNITY_EDITOR && !ML_TRAINING
                if (seekCallCount++ % 60 == 0)
                {
                    // Debug.Log($"[SeekLog] {gameObject.name} ({faction}) target: {target.name}, " +
                    //           $"OnNavMesh: {navMeshAgent.isOnNavMesh}, PathStatus: {navMeshAgent.pathStatus}");
                }
#endif
            }
        }

        private void Update()
        {
            if (state != States.Seeking || !navMeshAgent.enabled || !navMeshAgent.isOnNavMesh)
            {
                lastMovedTime = Time.time;
                lastPosition = transform.position;
                return;
            }

            // If we moved significantly, update the last moved time
            if (Vector3.Distance(transform.position, lastPosition) > 0.05f)
            {
                lastMovedTime = Time.time;
                lastPosition = transform.position;
            }
            else
            {
                // If we've been in Seeking state but haven't moved for more than 2.0 seconds
                if (Time.time - lastMovedTime > 2.0f)
                {
                    // Debug.LogWarning($"[Telemetry][StuckLog] {gameObject.name} ({faction}) has been SEEKING but STUCK at {transform.position} for {Time.time - lastMovedTime:F1}s! " +
                    //                  $"Target: {target?.name}, Destination: {navMeshAgent.destination}, isStopped: {navMeshAgent.isStopped}, " +
                    //                  $"velocity: {navMeshAgent.velocity}, pathStatus: {navMeshAgent.pathStatus}, speed: {navMeshAgent.speed}");
                    
                    // Attempt auto-recovery: Warp onto NavMesh again
                    UnityEngine.AI.NavMeshHit hit;
                    if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out hit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        navMeshAgent.Warp(hit.position);
                        navMeshAgent.isStopped = false;
                        if (target != null)
                        {
                            navMeshAgent.SetDestination(target.transform.position);
                        }
                    }
                    lastMovedTime = Time.time; // reset timer to prevent spamming logs every frame
                }
            }
        }

		//Unit has gotten to its target. This function puts it in "attack mode", but doesn't delive any damage (see DealBlow)
        public override void StartAttack()
        {
            base.StartAttack();

            navMeshAgent.isStopped = true;
            animator.SetBool("IsMoving", false);
        }

		//Starts the attack animation, and is repeated according to the Unit's attackRatio
        public override void DealBlow()
        {
            base.DealBlow();

            animator.SetTrigger("Attack");
            if (target != null)
            {
                Vector3 direction = target.transform.position - transform.position;
                direction.y = 0f; // Lock rotation to horizontal plane
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.forward = direction.normalized;
                }
            }
        }

		public override void Stop()
		{
			base.Stop();

			navMeshAgent.isStopped = true;
			animator.SetBool("IsMoving", false);
		}

        protected override void Die()
        {
            base.Die();

            navMeshAgent.enabled = false;
            animator.SetTrigger("IsDead");
        }
    }
}
