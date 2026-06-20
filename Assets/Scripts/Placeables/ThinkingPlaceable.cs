using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UnityRoyale
{
    public class ThinkingPlaceable : Placeable
    {
        [HideInInspector] public States state = States.Dragged;
        public enum States
        {
            Dragged, //when the player is dragging it as a card on the play field
            Idle, //at the very beginning, when dropped
            Seeking, //going for the target
            Attacking, //attack cycle animation, not moving
            Dead, //dead animation, before removal from play field
        }

        [HideInInspector] public AttackType attackType;
        public enum AttackType
        {
            Melee,
            Ranged,
        }

        [HideInInspector] public ThinkingPlaceable target;
        [HideInInspector] public HealthBar healthBar;

        [HideInInspector] public float hitPoints;
        [HideInInspector] public float attackRange;
        [HideInInspector] public float attackRatio;
        [HideInInspector] public float lastBlowTime = -1000f;
        [HideInInspector] public float damage;
		[HideInInspector] public AudioClip attackAudioClip;
        
        [HideInInspector] public float timeToActNext = 0f;
        [HideInInspector] public float nextRetargetTime = 0f;

		//Inspector references
		[Header("Projectile for Ranged")]
		public GameObject projectilePrefab;
		public Transform projectileSpawnPoint;

		private Projectile projectile;
		protected AudioSource audioSource;

		public UnityAction<ThinkingPlaceable> OnDealDamage, OnProjectileFired;

        public virtual void SetTarget(ThinkingPlaceable t)
        {
            if (target != null)
            {
                try
                {
                    target.OnDie -= TargetIsDead;
                }
                catch (System.Exception)
                {
                    // Ignore exceptions if the target object is already destroyed
                }
            }
            target = t;
            if (t != null)
            {
                t.OnDie += TargetIsDead;
            }
        }

        public virtual void StartAttack()
        {
            state = States.Attacking;
        }

        public virtual void DealBlow()
        {
            lastBlowTime = Time.time;
        }

		//Animation event hooks
		public void DealDamage()
        {
			//only melee units play audio when the attack deals damage
			if(attackType == AttackType.Melee)
				//audioSource.PlayOneShot(attackAudioClip, 1f);

			if(OnDealDamage != null)
				OnDealDamage(this);
		}
		public void FireProjectile()
        {
			//ranged units play audio when the projectile is fired
			//audioSource.PlayOneShot(attackAudioClip, 1f);

			if(OnProjectileFired != null)
				OnProjectileFired(this);
		}

        public virtual void Seek()
        {
            state = States.Seeking;
            nextRetargetTime = Time.time + 1.0f;
        }

        protected void TargetIsDead(Placeable p)
        {
            //Debug.Log("My target " + p.name + " is dead", gameObject);
            state = States.Idle;
            
            target.OnDie -= TargetIsDead;

            timeToActNext = lastBlowTime + attackRatio;
        }
        
        public bool IsTargetInRange(bool isAlreadyAttacking = false)
        {
            if (target == null) return false;
            // Adding a buffer (e.g. 1.2f) to attackRange to account for NavMeshAgent obstacle avoidance
            // stopping the units just outside their exact attack range.
            float buffer = 1.2f;
            if (isAlreadyAttacking)
            {
                buffer += 0.5f; // Extra buffer to prevent jitter/rapid state toggling once attacking
            }
            float rangeWithBuffer = attackRange + buffer;
            
            // Calculate distance to the closest point on the target's collider (especially important for large buildings/castles)
            Vector3 targetPos = target.transform.position;
            Collider targetCollider = target.GetComponent<Collider>();
            if (targetCollider != null && targetCollider.enabled)
            {
                targetPos = targetCollider.ClosestPoint(transform.position);
            }
            
            return (transform.position - targetPos).sqrMagnitude <= rangeWithBuffer * rangeWithBuffer;
        }

        public float SufferDamage(float amount)
        {
            hitPoints -= amount;
            //Debug.Log("Suffering damage, new health: " + hitPoints, gameObject);
            if(state != States.Dead
				&& hitPoints <= 0f)
            {
                Die();
            }

            return hitPoints;
        }

		public virtual void Stop()
		{
			state = States.Idle;
		}

        protected virtual void Die()
        {
            state = States.Dead;
			//audioSource.pitch = Random.Range(.9f, 1.1f);
			//audioSource.PlayOneShot(dieAudioClip, 1f);

			if(OnDie != null)
            	OnDie(this);
        }
    }
}
