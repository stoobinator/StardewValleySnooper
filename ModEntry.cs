using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Xna.Framework;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using xTile.Dimensions;

namespace Snooper
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private const string QuarryMineAlias = "stoobinatorQuarryMine";

        public SnooperMenu? Menu = null;

        public IModHelper? ModHelper = null;

        public SnooperConfig Config = new();

        public List<string> VisitedLocationNames = new();

        public bool IsMenuOpenQueued = false;

        public bool IsOutside = false;

        private float? zoomLevel = null;

        private static bool IsEventHappening => Game1.eventUp || Game1.CurrentEvent != null;

        #region overrides
        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<SnooperConfig>();
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Player.Warped += OnWarped;
        }
        #endregion

        #region events
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
            {
                return;
            }

            configMenu.Register(
                mod: ModManifest,
                reset: () => Config ??= new(),
                save: () => Helper.WriteConfig(this.Config!)
            );
            configMenu.AddKeybindList(
                mod: ModManifest,
                name: () => "Keybind",
                getValue: () => Config.Keybind,
                setValue: value => Config.Keybind = value
            );
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Auto-open in overworld",
                tooltip: () => "Automatically open the snooper the first time you enter a location that day.",
                getValue: () => Config.PauseOnNewLocation,
                setValue: value => Config.PauseOnNewLocation = value
            );
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Auto-open in dungeons",
                tooltip: () => "Automatically open the snooper on entering a floor in the Mines, Skull Cavern, etc.",
                getValue: () => Config.PauseOnNewFloor,
                setValue: value => Config.PauseOnNewFloor = value
            );
            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "...even on treasure floors",
                tooltip: () => "Automatically open the snooper on entering a treasure floor.",
                getValue: () => Config.PauseOnNewFloor,
                setValue: value => Config.PauseOnNewFloor = value
            );
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            VisitedLocationNames = new();
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!IsMenuOpenQueued || IsEventHappening) 
            {
                return;
            }
            IsMenuOpenQueued = false;

            if (TryLaunchMenu())
            {
                if (Game1.currentLocation.IsOutdoors 
                        && Config.PauseOnNewLocation
                        && !VisitedLocationNames.Contains(Game1.player.currentLocation.NameOrUniqueName))
                {
                    VisitedLocationNames.Add(Game1.player.currentLocation.NameOrUniqueName);
                }  
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
            {
                return;
            }
            
            if (Config.Keybind.JustPressed())
            {
                TryLaunchMenu();
            }
        }

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (Config.PauseOnNewLocation 
                   && e.NewLocation.IsOutdoors 
                   && !VisitedLocationNames.Contains(e.NewLocation.NameOrUniqueName))
            {
                if (TryLaunchMenu())
                {
                    VisitedLocationNames.Add(e.NewLocation.NameOrUniqueName);
                }
            }
            // special case for quarry mine
            else if ((Config.PauseOnNewLocation || Config.PauseOnNewFloor)
                        && e.NewLocation is MineShaft { mineLevel: 77377 })
            {
                if (!VisitedLocationNames.Contains(QuarryMineAlias) && TryLaunchMenu())
                {
                    VisitedLocationNames.Add(QuarryMineAlias);
                }
            }
            else if (Config.PauseOnNewFloor
                        && MineShaft.IsGeneratedLevel(e.NewLocation)
                        && (Config.PauseOnTreasureFloors || !IsTreasureFloor(e.NewLocation)))
            {
                TryLaunchMenu();
            }
            else if (Config.PauseOnNewFloor
                        && VolcanoDungeon.IsGeneratedLevel(e.NewLocation.NameOrUniqueName, out int level)
                        && level is not 0 or 5
                        && !VisitedLocationNames.Contains(e.NewLocation.NameOrUniqueName))
            {
                if (TryLaunchMenu())
                {
                    VisitedLocationNames.Add(e.NewLocation.NameOrUniqueName);
                }
            }
        }
        #endregion

        #region helpers
        private bool TryLaunchMenu()
        {
            if ((Menu != null && Game1.activeClickableMenu == Menu)
                    || Game1.activeClickableMenu != null)
            {
                return false;
            }

            if (IsEventHappening)
            {
                IsMenuOpenQueued = true;
                return false;
            }

            Menu = new(Config.Keybind, Helper, zoomLevel, (zoomLevel) => this.zoomLevel = zoomLevel);
            Game1.activeClickableMenu = Menu;
            Game1.playSound("bigSelect");
            return true;
        }

        private static bool IsTreasureFloor(GameLocation location)
        {
            if (location is not MineShaft)
            {
                return false;
            }

            // Check the hard-coded treasure floors (every 10 in mines, 100/200/300 in skull cavern)
            int mineLevel = ((MineShaft)location).mineLevel;
            if (mineLevel is 10 or 20 or 30 or 40 or 50 or 60 or 70 or 80 or 90 or 100 or 110 or 120 or 220 or 320 or 420)
            {
                return true;
            }
            // No random treasure rooms in mines
            else if (mineLevel < 121)
            {
                return false;
            }

            // Check if it's a random treasure room in skull cavern
            var isTreasureRoom = (NetBool?)location.NetFields.GetFields().FirstOrDefault(field => field?.Name == "GameLocation (MineShaft): netIsTreasureRoom", null);
            return isTreasureRoom?.Value == true;
        }
        #endregion
    }
}