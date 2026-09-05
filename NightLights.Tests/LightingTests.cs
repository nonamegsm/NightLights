using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NightLights.Rgb;

namespace NightLights.Tests
{
    internal static class LightingTests
    {
        public static void Run() => RunAsync().GetAwaiter().GetResult();

        private static async Task RunAsync()
        {
            var module = new FakeModule("Test");
            var modules = new[] { module };
            var coordinator = new LightingCoordinator();
            await coordinator.ApplyAsync(modules, false, false);
            TestAssert.Equal("save", string.Join(",", module.Calls), "Daytime startup captures baseline");
            module.Calls.Clear();
            await coordinator.ApplyAsync(modules, true, true);
            TestAssert.Equal("save,off", string.Join(",", module.Calls), "Force night snapshots BEFORE turning off");
            module.Calls.Clear();
            await coordinator.ApplyAsync(modules, true, false);
            await coordinator.ApplyAsync(modules, true, true);
            TestAssert.Equal("off,off", string.Join(",", module.Calls), "Night polling and resume never replace day profile");
            module.Calls.Clear();
            module.FailRestore = true;
            await coordinator.ApplyAsync(modules, false, false);
            module.FailRestore = false;
            await coordinator.ApplyAsync(modules, false, false);
            await coordinator.ApplyAsync(modules, false, false);
            TestAssert.Equal("restore,restore", string.Join(",", module.Calls), "Restore failure retries, successful day remains untouched");
            module.Calls.Clear();
            await coordinator.SetColorAsync(modules, 1, 2, 3, 25);
            await coordinator.ApplyAsync(modules, true, true, false);
            TestAssert.Equal("color,off", string.Join(",", module.Calls), "An explicit profile is not overwritten if sunset happens while color dialog is open");

            var broken = new FakeModule("Broken") { ThrowOnOff = true };
            var working = new FakeModule("Working");
            await new LightingCoordinator().ApplyAsync(new[] { broken, working }, true, false);
            TestAssert.Equal("off", string.Join(",", working.Calls), "A failed module cannot block another module");
            module.Calls.Clear();
            await new LightingCoordinator().ApplyAsync(modules, true, false);
            TestAssert.Equal("off", string.Join(",", module.Calls), "Night startup never refreshes an existing profile");
        }

        private sealed class FakeModule : ILightingModule
        {
            public FakeModule(string name) { Name = name; }
            public string Name { get; }
            public List<string> Calls { get; } = new List<string>();
            public bool FailRestore, ThrowOnOff;
            public Task<bool> RefreshSnapshotAsync() { Calls.Add("save"); return Task.FromResult(true); }
            public Task<bool> TurnOffAsync()
            {
                Calls.Add("off");
                if (ThrowOnOff) throw new InvalidOperationException("device disconnected");
                return Task.FromResult(true);
            }
            public Task<bool> RestoreAsync() { Calls.Add("restore"); return Task.FromResult(!FailRestore); }
            public Task<bool> SetStaticColorProfileAsync(byte r, byte g, byte b, int brightnessPercent)
            { Calls.Add("color"); return Task.FromResult(true); }
        }
    }
}
