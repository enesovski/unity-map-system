using System.Collections;
using Michsky.UI.Reach;
using Sirenix.OdinInspector;
using UnityEngine;

public class MapNotificationHandler : MonoBehaviour
{
    [Title("Feed Notification")]
    public CustomFeedNotification feedNotification;
    
    [Title("Effects")]
    [SerializeField] private AudioSource audioSource;
    [Space] 
    [SerializeField] private AudioClip defaultNotificationClip;
    [SerializeField, LabelText("POI Discovery Clip")] private AudioClip POIDiscoveryClip;
    [SerializeField] private AudioClip islandDiscoveryClip;

    private void Start()
    {
        feedNotification.MinimizeNotification();
    }
    
    private void OnEnable()
    {
        EventManager.Instance.Subscribe<ZoneDiscoveredEvent>(DisplayNotification);
        EventManager.Instance.Subscribe<IslandDiscoveredEvent>(DisplayNotification);
    }
    
    private void OnDisable()
    {
        EventManager.Instance.Unsubscribe<ZoneDiscoveredEvent>(DisplayNotification);
        EventManager.Instance.Unsubscribe<IslandDiscoveredEvent>(DisplayNotification);
    }


    #region  Notification Overrides

    public void DisplayNotification(ZoneDiscoveredEvent e)
    {
        if (feedNotification.gameObject.activeSelf) StopAllCoroutines();
        string name = LocalizationService.Get("POIs", e.displayNameLocKey, e.displayNameFallback);
        string message = LocalizationService.GetWithParams(
            "Feedback", "Feedback_Notification_Discovered", "{Name} Discovered",
            ("{Name}", name));
        StartCoroutine(NotificationCoroutine(message.ToUpperInvariant(), POIDiscoveryClip));
    }

    public void DisplayNotification(IslandDiscoveredEvent e)
    {
        if (feedNotification.gameObject.activeSelf) StopAllCoroutines();
        string name = LocalizationService.Get("POIs", e.displayNameLocKey, e.displayNameFallback);
        string message = LocalizationService.GetWithParams(
            "Feedback", "Feedback_Notification_Discovered", "{Name} Discovered",
            ("{Name}", name));
        StartCoroutine(NotificationCoroutine(message.ToUpperInvariant(), islandDiscoveryClip));
    }
    
    public void DisplayNotification(IslandRevealedEvent e)
    {
        if (feedNotification.gameObject.activeSelf) StopAllCoroutines();
        string name = e.islandId;
        string message = LocalizationService.GetWithParams(
            "Feedback", "Feedback_Notification_Revealed", "{Name} Revealed",
            ("{Name}", name));
        StartCoroutine(NotificationCoroutine(message.ToUpperInvariant(), islandDiscoveryClip));
    }

    public void DisplayNotification(string message)
    {
        if (feedNotification.gameObject.activeSelf)
        {
            StopAllCoroutines();
        }
        StartCoroutine(NotificationCoroutine(message, defaultNotificationClip));
    }
    #endregion



    private IEnumerator NotificationCoroutine(string message, AudioClip audioClip)
    {
        feedNotification.notificationText = message;

        feedNotification.UpdateUI();
        feedNotification.ExpandNotification();
        
        if (audioClip != null)
            audioSource?.PlayOneShot(audioClip);
        
        
        yield return new WaitForSeconds(feedNotification.ClosingAnimDuration + 2);
        feedNotification.MinimizeNotification();
    }
    
}
