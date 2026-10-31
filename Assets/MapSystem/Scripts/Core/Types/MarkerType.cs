namespace Artifika.Map.Core
{
    public enum MarkerType
    {
        POI = 0,
        
        Player = 10,
        Companion = 11,
        Teammate = 12,

        Pin = 20,
        SpawnPoint = 21,
        DeathMarker = 22,
        Base = 23,
        
        CorruptedArea = 30,
        
        QuestObjective = 40,
        QuestArea = 41
        
    }

    public enum MarkerReplicationMode
    {
        LocalOnly = 0,
        SharedServerAuthoritative = 1
    }
}
