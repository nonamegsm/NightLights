using System.Threading.Tasks;

namespace NightLights.Rgb
{
    internal interface ILightingModule
    {
        string Name { get; }
        Task<bool> RefreshSnapshotAsync();
        Task<bool> TurnOffAsync();
        Task<bool> RestoreAsync();
        Task<bool> SetStaticColorProfileAsync(byte r, byte g, byte b, int brightnessPercent);
    }

    internal sealed class FuryLightingModule : ILightingModule
    {
        private readonly FuryLightController _controller = new FuryLightController();
        public string Name => "Kingston FURY";
        public Task<bool> RefreshSnapshotAsync() => _controller.RefreshSnapshotAsync();
        public Task<bool> TurnOffAsync() => _controller.TurnOffAsync();
        public Task<bool> RestoreAsync() => _controller.RestoreAsync();
        public Task<bool> SetStaticColorProfileAsync(byte r, byte g, byte b, int brightnessPercent) =>
            _controller.SetStaticColorProfileAsync(r, g, b, brightnessPercent);
    }

    internal sealed class MysticLightingModule : ILightingModule
    {
        private readonly MysticLightController _controller = new MysticLightController();
        public string Name => "MSI Mystic Light";
        public Task<bool> RefreshSnapshotAsync() => Task.Run(() => _controller.RefreshSnapshot());
        public Task<bool> TurnOffAsync() => Task.Run(() => _controller.TurnOff());
        public Task<bool> RestoreAsync() => Task.Run(() => _controller.Restore());
        public Task<bool> SetStaticColorProfileAsync(byte r, byte g, byte b, int brightnessPercent) =>
            Task.Run(() => _controller.SetStaticColorProfile(r, g, b, brightnessPercent));
    }
}
