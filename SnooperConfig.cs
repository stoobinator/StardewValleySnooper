using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace Snooper
{
    public sealed class SnooperConfig
    {
        public KeybindList Keybind { get; set; } = KeybindList.Parse("F3");

        public bool PauseOnNewLocation = false;

        public bool PauseOnNewFloor = false;

        public bool PauseOnTreasureFloors = false;
    }
}