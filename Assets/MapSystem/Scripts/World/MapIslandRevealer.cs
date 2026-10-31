using Artifika.Map.Core;
using HighlightPlus;
using MoreMountains.Feedbacks;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Artifika.Map
{

    public class MapIslandRevealer : MonoBehaviour, IInteractionTarget
    {
        [Title("Island")]
        [SerializeField, Required] private IslandDefinition islandDefinition;

        [Title("Feedback")]
        [SerializeField] private MMF_Player feedback;

        [Title("Highlight")]
        [SerializeField] private HighlightEffect highlightFx;
        

        private void Awake()
        {
            InteractionHighlight.Ensure(gameObject, highlightFx, alwaysOn: false);
        }

        private void Start()
        {
            var repo = MapSystemContext.Instance?.IslandRepository;
            if (repo != null && repo.IsRevealed(islandDefinition.IslandId))
                DisableInteractable();
        }


        public void Interact()
        {
            if (islandDefinition == null) return;

            var repo = MapSystemContext.Instance?.IslandRepository;
            if (repo != null && repo.IsRevealed(islandDefinition.IslandId)) return;

            if (feedback != null)
                feedback.PlayFeedbacks();
            else
                RevealIsland(); 
        }

        public bool BuildPrompt(in InteractorContext ctx, InteractionPrompt prompt)
        {
            // Revealed islands switch this component off: nothing left to do here.
            if (!enabled || islandDefinition == null) return false;

            prompt.Add(InteractionPromptAction.Tap(InteractionInput.Primary,
                InteractionUtility.Text("Interact_RevealIsland", "RevealIsland")));
            return true;
        }

        public void Execute(in InteractorContext ctx, InteractionInput input)
        {
            if (input == InteractionInput.Primary) Interact();
        }

        public void RevealIsland()
        {
            if (islandDefinition == null) return;

            var repo = MapSystemContext.Instance?.IslandRepository;
            if (repo == null)
            {
                Debug.LogWarning("[MapIslandRevealer] MapSystemContext.Instance or IslandRepository is null.", this);
                return;
            }

            repo.RevealIsland(islandDefinition.IslandId);
            DisableInteractable();
        }

        private void DisableInteractable()
        {
            enabled = false; // BuildPrompt now returns false, so the detector drops the highlight
        }
    }
}