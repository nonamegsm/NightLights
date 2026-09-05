using System;
using System.Globalization;
using System.IO;

namespace NightLights.Power
{
    public enum PowerPlanRestoreStateLoadStatus
    {
        Missing,
        Loaded,
        Error
    }

    public sealed class PowerPlanRestoreState
    {
        public Guid OriginalSchemeGuid { get; set; }
        public Guid ManagedSchemeGuid { get; set; }
        public bool ChangeApplied { get; set; }
        public long CreatedUtcTicks { get; set; }

        public static bool TryLoad(string path, out PowerPlanRestoreState state)
        {
            string error;
            return Load(path, out state, out error) == PowerPlanRestoreStateLoadStatus.Loaded;
        }

        public static PowerPlanRestoreStateLoadStatus Load(string path, out PowerPlanRestoreState state, out string error)
        {
            state = null;
            error = null;

            try
            {
                if (Directory.Exists(path))
                {
                    error = "Restore state path is a directory.";
                    return PowerPlanRestoreStateLoadStatus.Error;
                }

                if (!File.Exists(path))
                {
                    return PowerPlanRestoreStateLoadStatus.Missing;
                }

                Guid original = Guid.Empty;
                Guid managed = Guid.Empty;
                bool changeApplied = false;
                long createdUtcTicks = 0;

                foreach (var line in File.ReadAllLines(path))
                {
                    var index = line.IndexOf('=');
                    if (index <= 0) continue;

                    string key = line.Substring(0, index).Trim();
                    string value = line.Substring(index + 1).Trim();

                    if (key == nameof(OriginalSchemeGuid))
                    {
                        Guid.TryParse(value, out original);
                    }
                    else if (key == nameof(ManagedSchemeGuid))
                    {
                        Guid.TryParse(value, out managed);
                    }
                    else if (key == nameof(ChangeApplied))
                    {
                        bool.TryParse(value, out changeApplied);
                    }
                    else if (key == nameof(CreatedUtcTicks))
                    {
                        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out createdUtcTicks);
                    }
                }

                if (original == Guid.Empty || managed == Guid.Empty)
                {
                    error = "Restore state is missing a valid original or managed power-plan GUID.";
                    return PowerPlanRestoreStateLoadStatus.Error;
                }

                state = new PowerPlanRestoreState
                {
                    OriginalSchemeGuid = original,
                    ManagedSchemeGuid = managed,
                    ChangeApplied = changeApplied,
                    CreatedUtcTicks = createdUtcTicks
                };
                return PowerPlanRestoreStateLoadStatus.Loaded;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return PowerPlanRestoreStateLoadStatus.Error;
            }
        }

        public string Serialize()
        {
            return
                nameof(OriginalSchemeGuid) + "=" + OriginalSchemeGuid + Environment.NewLine +
                nameof(ManagedSchemeGuid) + "=" + ManagedSchemeGuid + Environment.NewLine +
                nameof(ChangeApplied) + "=" + ChangeApplied + Environment.NewLine +
                nameof(CreatedUtcTicks) + "=" + CreatedUtcTicks.ToString(CultureInfo.InvariantCulture) + Environment.NewLine;
        }
    }
}
