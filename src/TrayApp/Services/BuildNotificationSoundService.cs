using System.Media;

namespace BuildMonitor.TrayApp.Services;

public static class BuildNotificationSoundService
{
    public static void PlayBuildFailed()
    {
        try
        {
            SystemSounds.Hand.Play();
        }
        catch
        {
            // Optional feedback — ignore playback failures.
        }
    }

    public static void PlayBuildSucceeded()
    {
        try
        {
            SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Optional feedback — ignore playback failures.
        }
    }
}
