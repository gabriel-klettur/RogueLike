using System;
using UnityEngine;

namespace Valkur.Gameplay.NPC
{
    /// <summary>
    /// Base interactable NPC component. Handles interaction range detection and triggers.
    /// Maps to Python's neutral NPC interaction system.
    /// </summary>
    public class NPCInteractable : MonoBehaviour
    {
        [Header("Interaction")]
        [SerializeField] private float interactionRange = 2f;
        [SerializeField] private string npcName = "NPC";
        [SerializeField] private string dialogueKey = "";

        private bool _playerInRange;
        private Transform _playerTransform;

        public string NPCName => npcName;
        public string DialogueKey => dialogueKey;
        public bool PlayerInRange => _playerInRange;
        public float InteractionRange => interactionRange;

        public event Action<NPCInteractable> OnInteract;
        public event Action<NPCInteractable> OnPlayerEnterRange;
        public event Action<NPCInteractable> OnPlayerExitRange;

        private void Update()
        {
            if (_playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
                else return;
            }

            float dist = Vector2.Distance(transform.position, _playerTransform.position);
            bool inRange = dist <= interactionRange;

            if (inRange && !_playerInRange)
            {
                _playerInRange = true;
                OnPlayerEnterRange?.Invoke(this);
            }
            else if (!inRange && _playerInRange)
            {
                _playerInRange = false;
                OnPlayerExitRange?.Invoke(this);
            }
        }

        /// <summary>
        /// Called when the player presses the interact key while in range.
        /// </summary>
        public void Interact()
        {
            if (!_playerInRange) return;
            OnInteract?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactionRange);
        }
    }
}
