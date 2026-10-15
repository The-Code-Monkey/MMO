using System;

namespace MMO.Net
{
    /// <summary>
    /// Pure parsing of the command line that turns the standalone binary into a dedicated zone
    /// server. Kept free of Unity types so the argument rules are unit-testable in EditMode.
    /// </summary>
    public static class DedicatedServerArgs
    {
        public const string ZoneFlag = "-zone";

        /// <summary>
        /// Returns the value following <c>-zone</c> (case-insensitive flag), or null when the
        /// switch is absent or has no value. Mirrors Unity's own <c>-name value</c> convention.
        /// </summary>
        public static string ParseZone(string[] args)
        {
            if (args == null)
                return null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], ZoneFlag, StringComparison.OrdinalIgnoreCase))
                {
                    string value = args[i + 1];
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Process-wide switch marking this process as a dedicated zone server. The launcher sets it
    /// once when started with <c>-zone &lt;name&gt;</c>; <see cref="WorldBootstrap"/> reads it to
    /// start the FishNet listener on whichever zone scene the launcher loads. Left false, a client
    /// build that loads a zone scene (FishNet syncs the scene to clients) never opens a server.
    /// </summary>
    public static class DedicatedServerMode
    {
        /// <summary>True once the launcher has committed this process to hosting a zone.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Name of the zone scene this process hosts, or null when not a server.</summary>
        public static string ZoneName { get; private set; }

        /// <summary>Commits this process to hosting <paramref name="zoneName"/>.</summary>
        public static void Enable(string zoneName)
        {
            IsActive = true;
            ZoneName = zoneName;
        }

        /// <summary>Clears the switch. Test-only; production never un-serves a process.</summary>
        public static void Reset()
        {
            IsActive = false;
            ZoneName = null;
        }
    }
}
