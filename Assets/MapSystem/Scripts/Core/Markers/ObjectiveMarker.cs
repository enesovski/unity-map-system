using Artifika.Quests;

namespace Artifika.Map.Core
{
    public class ObjectiveMarker : DynamicMarker
    {
        public ObjectiveMarker(string markerId, string questTitle, string objectiveDescription)
            : base(markerId, MarkerType.QuestObjective)
        {
            DisplayName = questTitle;
            Description = objectiveDescription;
            State       = MarkerState.Active;
        }
    }

}
    
