namespace Artifika.Map.Core
{
    public enum MarkerState
    {
        Hidden,    // undiscovered
        Visible,   // discovered
        Active,    // quest target or highlighted
        Tracked,   // player is tracking - compass 
        Cleared,   // greyed out
        Disabled 
    }

}