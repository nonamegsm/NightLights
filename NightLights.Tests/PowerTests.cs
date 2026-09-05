using System;
using System.Collections.Generic;
using System.IO;
using NightLights.Power;

namespace NightLights.Tests
{
    internal static class PowerTests
    {
        private static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        private static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        public static void Run()
        {
            SwitchesToPowerSaverAndRestoresOriginal();
            DoesNotOverwriteOriginalOnRepeatedApplyOrRestart();
            ManualPowerPlanChangePreventsRestore();
            FailedRestoreKeepsStateForRetry();
            FailedInitialSetCanRetryWithoutLosingOriginal();
            CorruptStatePreventsPowerMutation();
            MorningRestoreWorksAfterRestartedController();
            DisabledWithNoStateDoesNotCallNativeApi();
            FailedInitialSetThenManualChangeSuppressesRepeatedPolls();
            ActivePowerSaverRestartDoesNotSetAgain();
            UnreadableStatePathPreventsPowerMutationAndPersists();
        }

        private static void SwitchesToPowerSaverAndRestoresOriginal()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                TestAssert.True(controller.Apply(true, true), "Night apply should succeed.");
                TestAssert.Equal(PowerPlanController.PowerSaverSchemeGuid, api.ActiveScheme, "Night apply should activate Power saver.");
                TestAssert.True(File.Exists(statePath), "Night apply should persist restore state.");

                TestAssert.True(controller.Restore(), "Restore should succeed.");
                TestAssert.Equal(Balanced, api.ActiveScheme, "Restore should return to the original scheme.");
                TestAssert.True(!File.Exists(statePath), "Restore should clear state after success.");
            });
        }

        private static void DoesNotOverwriteOriginalOnRepeatedApplyOrRestart()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                TestAssert.True(controller.Apply(true, true), "First night apply should succeed.");
                api.ActiveScheme = HighPerformance;

                var restarted = NewController(api, statePath);
                TestAssert.True(restarted.Apply(true, true), "Repeated apply should respect a manual power plan change.");

                PowerPlanRestoreState state;
                TestAssert.True(PowerPlanRestoreState.TryLoad(statePath, out state), "Restore state should remain available until day/disable.");
                TestAssert.Equal(Balanced, state.OriginalSchemeGuid, "Original scheme should not be overwritten after restart.");
                TestAssert.Equal(HighPerformance, api.ActiveScheme, "Manual active plan should not be forced back to Power saver.");
            });
        }

        private static void ManualPowerPlanChangePreventsRestore()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                TestAssert.True(controller.Apply(true, true), "Night apply should succeed.");
                api.ActiveScheme = HighPerformance;

                TestAssert.True(controller.Restore(), "Restore should treat a manual plan change as handled.");
                TestAssert.Equal(HighPerformance, api.ActiveScheme, "Restore should not overwrite a manual active plan.");
                TestAssert.True(!File.Exists(statePath), "Manual change should clear stale restore state.");
            });
        }

        private static void FailedRestoreKeepsStateForRetry()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                TestAssert.True(controller.Apply(true, true), "Night apply should succeed.");
                api.FailNextSetCount = 2;

                TestAssert.True(!controller.Restore(), "Restore should report failure after retries are exhausted.");
                TestAssert.Equal(PowerPlanController.PowerSaverSchemeGuid, api.ActiveScheme, "Failed restore should leave the active scheme alone.");
                TestAssert.True(File.Exists(statePath), "Failed restore should preserve restore state.");

                TestAssert.True(controller.Restore(), "A later restore should retry and succeed.");
                TestAssert.Equal(Balanced, api.ActiveScheme, "Retry restore should return to the original scheme.");
            });
        }

        private static void FailedInitialSetCanRetryWithoutLosingOriginal()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                api.FailNextSetCount = 2;
                TestAssert.True(!controller.Apply(true, true), "Apply should report failure after retries are exhausted.");
                TestAssert.Equal(Balanced, api.ActiveScheme, "Failed apply should not change active scheme.");

                PowerPlanRestoreState state;
                TestAssert.True(PowerPlanRestoreState.TryLoad(statePath, out state), "Failed apply should keep pending restore state for retry.");
                TestAssert.Equal(Balanced, state.OriginalSchemeGuid, "Pending restore state should keep original scheme.");

                TestAssert.True(controller.Apply(true, true), "A later night apply should retry the Power saver switch.");
                TestAssert.Equal(PowerPlanController.PowerSaverSchemeGuid, api.ActiveScheme, "Retry should activate Power saver.");
            });
        }

        private static void CorruptStatePreventsPowerMutation()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                File.WriteAllText(statePath, "not a valid restore state");

                TestAssert.True(!controller.Apply(true, true), "Corrupt state should fail closed.");
                TestAssert.Equal(0, api.GetRequests, "Corrupt state should not query the native API.");
                TestAssert.Equal(0, api.SetRequests.Count, "Corrupt state should not change the power plan.");
                TestAssert.True(File.Exists(statePath), "Corrupt state should be retained for inspection/recovery.");
            });
        }

        private static void MorningRestoreWorksAfterRestartedController()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                TestAssert.True(controller.Apply(true, true), "Night apply should succeed.");

                var restarted = NewController(api, statePath);
                TestAssert.True(restarted.Apply(true, false), "Morning apply should restore through a restarted controller.");
                TestAssert.Equal(Balanced, api.ActiveScheme, "Morning restore should return to the original scheme.");
                TestAssert.True(!File.Exists(statePath), "Morning restore should clear restore state.");
            });
        }

        private static void DisabledWithNoStateDoesNotCallNativeApi()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                TestAssert.True(controller.Apply(false, true), "Disabled power saving with no state should be idle.");
                TestAssert.Equal(0, api.GetRequests, "Disabled idle path should not query the native API.");
                TestAssert.Equal(0, api.SetRequests.Count, "Disabled idle path should not set the native API.");
                TestAssert.True(!File.Exists(statePath), "Disabled idle path should not create restore state.");
            });
        }

        private static void FailedInitialSetThenManualChangeSuppressesRepeatedPolls()
        {
            WithController(Balanced, (controller, api, statePath) =>
            {
                api.FailNextSetCount = 2;
                TestAssert.True(!controller.Apply(true, true), "Initial apply should fail after retries.");
                TestAssert.Equal(2, api.SetRequests.Count, "Initial apply should retry the failed set.");

                api.ActiveScheme = HighPerformance;
                TestAssert.True(controller.Apply(true, true), "Manual change after failed apply should suppress the night switch.");
                TestAssert.True(controller.Apply(true, true), "Repeated poll should keep respecting the manual plan.");
                TestAssert.Equal(2, api.SetRequests.Count, "Suppressed repeated polls should not force Power saver.");
                TestAssert.Equal(HighPerformance, api.ActiveScheme, "Manual plan should remain active.");
                TestAssert.True(File.Exists(statePath), "Suppressed pending state should remain until day/disable recovery.");
            });
        }

        private static void ActivePowerSaverRestartDoesNotSetAgain()
        {
            WithController(PowerPlanController.PowerSaverSchemeGuid, (controller, api, statePath) =>
            {
                var state = new PowerPlanRestoreState
                {
                    OriginalSchemeGuid = Balanced,
                    ManagedSchemeGuid = PowerPlanController.PowerSaverSchemeGuid,
                    ChangeApplied = true,
                    CreatedUtcTicks = DateTime.UtcNow.Ticks
                };
                File.WriteAllText(statePath, state.Serialize());

                TestAssert.True(controller.Apply(true, true), "Restarted night apply should accept already-active Power saver.");
                TestAssert.Equal(0, api.SetRequests.Count, "Restarted night apply should not set Power saver again.");
                TestAssert.Equal(PowerPlanController.PowerSaverSchemeGuid, api.ActiveScheme, "Power saver should remain active.");
            });
        }

        private static void UnreadableStatePathPreventsPowerMutationAndPersists()
        {
            string directory = Path.Combine(Path.GetTempPath(), "NightLights.PowerTests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string statePath = Path.Combine(directory, "power.state");
            Directory.CreateDirectory(statePath);

            try
            {
                var api = new FakePowerSchemeApi(Balanced);
                var controller = NewController(api, statePath);

                TestAssert.True(!controller.Apply(true, true), "Unreadable state path should fail closed.");
                TestAssert.Equal(0, api.GetRequests, "Unreadable state should not query the native API.");
                TestAssert.Equal(0, api.SetRequests.Count, "Unreadable state should not change the power plan.");
                TestAssert.True(Directory.Exists(statePath), "Unreadable state path should remain in place.");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void WithController(Guid activeScheme, Action<PowerPlanController, FakePowerSchemeApi, string> test)
        {
            string directory = Path.Combine(Path.GetTempPath(), "NightLights.PowerTests." + Guid.NewGuid().ToString("N"));
            string statePath = Path.Combine(directory, "power.state");
            Directory.CreateDirectory(directory);

            try
            {
                var api = new FakePowerSchemeApi(activeScheme);
                var controller = NewController(api, statePath);
                test(controller, api, statePath);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static PowerPlanController NewController(FakePowerSchemeApi api, string statePath)
        {
            return new PowerPlanController(api, statePath, _ => { });
        }

        private sealed class FakePowerSchemeApi : IPowerSchemeApi
        {
            public readonly List<Guid> SetRequests = new List<Guid>();

            public FakePowerSchemeApi(Guid activeScheme)
            {
                ActiveScheme = activeScheme;
            }

            public Guid ActiveScheme { get; set; }
            public int GetRequests { get; private set; }
            public int FailNextGetCount { get; set; }
            public int FailNextSetCount { get; set; }

            public bool TryGetActiveScheme(out Guid schemeGuid, out string error)
            {
                GetRequests++;

                if (FailNextGetCount > 0)
                {
                    FailNextGetCount--;
                    schemeGuid = Guid.Empty;
                    error = "get failed";
                    return false;
                }

                schemeGuid = ActiveScheme;
                error = null;
                return true;
            }

            public bool TrySetActiveScheme(Guid schemeGuid, out string error)
            {
                SetRequests.Add(schemeGuid);

                if (FailNextSetCount > 0)
                {
                    FailNextSetCount--;
                    error = "set failed";
                    return false;
                }

                ActiveScheme = schemeGuid;
                error = null;
                return true;
            }
        }
    }
}
