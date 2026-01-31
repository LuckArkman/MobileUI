using UnityEngine;

namespace ARProject.Core
{
    /// <summary>
    /// Manages application performance settings.
    /// </summary>
    public class PerformanceManager : MonoBehaviour
    {
        [SerializeField] private int targetFrameRate = 60;

        private void Start()
        {
            Application.targetFrameRate = targetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        public void SetLowPowerMode(bool enabled)
        {
            if (enabled)
            {
                Application.targetFrameRate = 30;
                // Reduce resolution or disable expensive effects here
            }
            else
            {
                Application.targetFrameRate = 60;
            }
        }
    }
}
