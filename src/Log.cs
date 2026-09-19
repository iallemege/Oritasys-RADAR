using BepInEx.Logging;

namespace RDA
{
    internal static class Log
    {
        internal static ManualLogSource? Source;
        internal static bool Verbose;

        internal static void Info(string message) => Source?.LogInfo(message);

        internal static void Warn(string message) => Source?.LogWarning(message);

        internal static void Error(string message) => Source?.LogError(message);

        internal static void Debug(string message)
        {
            if (Verbose)
            {
                Source?.LogInfo("[dbg] " + message);
            }
        }
    }
}
