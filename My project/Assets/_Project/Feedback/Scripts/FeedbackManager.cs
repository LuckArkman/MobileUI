using UnityEngine;

namespace ARProject.Feedback
{
    public class FeedbackManager : MonoBehaviour
    {
        public void ShowAlert(string message, Color color)
        {
            Debug.Log($"[In-Game Alert] {message}");
            // HUD logic here
        }

        public void PlaySound(string soundName)
        {
            // Audio logic here
        }

        public void TriggerHaptics()
        {
            Handheld.Vibrate();
        }
    }
}
