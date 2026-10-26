using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Snooper
{
    public class SnooperMenu: IClickableMenu
    {
        private static readonly int maxWidthOfBuildingViewer = 448;

	    private static readonly int maxHeightOfBuildingViewer = 512;

        private static readonly int maxWidthOfDescription = 416;

        private readonly float originalZoomLevel;

        private readonly bool originalForceViewportPlayerFollow;
 
        private readonly IModHelper helper;

        private readonly KeybindList openMenuKey;

        private int timeOpened;

        private bool closeAllowed = false;

        public float ZoomLevel;

        private bool isCameraUnlocked = false;

        private readonly Action<float> zoomFunc;

        public SnooperMenu(KeybindList openMenuKey, IModHelper helper, float? zoomLevel, Action<float> zoomFunc)
        {
            this.openMenuKey = openMenuKey;
            this.helper = helper;
            this.helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
            this.helper.Events.Display.MenuChanged += OnMenuChanged;
            this.zoomFunc = zoomFunc;

            Game1.displayHUD = false;

            // from carpenter menu, idk
            Game1.player.forceCanMove();
            ResetBounds();
            if (Game1.options.SnappyMenus)
            {
                populateClickableComponentList();
                snapToDefaultClickableComponent();
            }

            // cache some values
            originalForceViewportPlayerFollow = Game1.currentLocation.forceViewportPlayerFollow;
            originalZoomLevel = Game1.options.desiredBaseZoomLevel;
            Game1.options.desiredBaseZoomLevel = Math.Clamp(zoomLevel ?? Game1.options.desiredBaseZoomLevel, MinZoom(Game1.currentLocation), 2.0f);
            ZoomLevel = Game1.options.desiredBaseZoomLevel;
        }

        #region overrides
        public override void draw(SpriteBatch b)        
        {
            base.draw(b);

            // cursor
            Vector2 cursorPos = new(Game1.getMouseX(), Game1.getMouseY());
            Rectangle cursorSheetCoords = new(80, 0, 16, 16);
            b.Draw(Game1.mouseCursors, cursorPos, cursorSheetCoords, Color.White, 0, Vector2.Zero, Game1.pixelZoom * ZoomLevel, SpriteEffects.None, 1);

            // arrows
            Vector2 upArrowPos = new(Game1.uiViewport.Width / 2 - 16, 18);
            Rectangle upArrowSheetCoords = new(76, 72, 40, 44);
            b.Draw(Game1.mouseCursors, upArrowPos, upArrowSheetCoords, Color.White);

            Vector2 downArrowPos = new(Game1.uiViewport.Width / 2 - 16, Game1.uiViewport.Height - 63);
            Rectangle downArrowSheetCoords = new(13, 76, 40, 44);
            b.Draw(Game1.mouseCursors, downArrowPos, downArrowSheetCoords, Color.White);

            Vector2 leftArrowPos = new(26, Game1.uiViewport.Height / 2 + 10);
            b.Draw(Game1.mouseCursors, leftArrowPos, upArrowSheetCoords, Color.White, -MathHelper.PiOver2, new Vector2(8, 8), 1, SpriteEffects.None, 1);

            Vector2 rightArrowPos = new(Game1.uiViewport.Width - 55, Game1.uiViewport.Height / 2 + 10);
            b.Draw(Game1.mouseCursors, rightArrowPos, downArrowSheetCoords, Color.White, -MathHelper.PiOver2, new Vector2(8, 8), 1, SpriteEffects.None, 1);
        }

        public override void update(GameTime time)
        {
            // Wait to untether camera from player until fade lifts
            // Otherwise camera ends up in weird spot
            if (!isCameraUnlocked && !Game1.fadeToBlack && !Game1.globalFade)
            {
                UnlockCamera();
            }

            // Add a delay so menu doesn't immediately close
            if (timeOpened == 0)
            {
                timeOpened = time.TotalGameTime.Milliseconds;
            }
            else if (time.TotalGameTime.Milliseconds - timeOpened > 100)
            {
                closeAllowed = true;
            }
            
            // from carpenter menu
            base.update(time);
            if (Game1.IsFading())
            {
                return;
            }

            // Mouse at edge of screen movement
            int mouseX = Game1.getOldMouseX(ui_scale: false) + Game1.viewport.X;
            int mouseY = Game1.getOldMouseY(ui_scale: false) + Game1.viewport.Y;
            int deltaX = 0;
            int deltaY = 0;
            if (mouseX - Game1.viewport.X < 64)
            {
                deltaX = -8;
            }
            else if (mouseX - (Game1.viewport.X + Game1.viewport.Width) >= -128)
            {
                deltaX = 8;
            }
            if (mouseY - Game1.viewport.Y < 64)
            {
                deltaY = -8;
            }
            else if (mouseY - (Game1.viewport.Y + Game1.viewport.Height) >= -64)
            {
                deltaY = 8;
            }

            // Arrow key movement
            Keys[] pressedKeys = Keyboard.GetState().GetPressedKeys();
            foreach (Keys key in pressedKeys)
            {
                if (Game1.options.doesInputListContain(Game1.options.moveLeftButton, key))
                {
                    deltaX -= 6;
                }
                else if (Game1.options.doesInputListContain(Game1.options.moveRightButton, key))
                {
                    deltaX += 6;
                }
                else if (Game1.options.doesInputListContain(Game1.options.moveUpButton, key))
                {
                    deltaY -= 6;
                }
                else if (Game1.options.doesInputListContain(Game1.options.moveDownButton, key))
                {
                    deltaY += 6;
                }
            }

            MoveCamera(deltaX, deltaY);
        }

        public override void receiveKeyPress(Keys key)
        {
            if (Game1.IsFading())
            {
                return;
            }

            // Esc or snooper menu button clicked, close
            if ((Game1.options.doesInputListContain(Game1.options.menuButton, key) || (openMenuKey.JustPressed() && closeAllowed)) 
                    && readyToClose() && Game1.locationRequest == null)
            {
                Cleanup();
                exitThisMenu();
            }
        }

        public override void gameWindowSizeChanged(Microsoft.Xna.Framework.Rectangle oldBounds, Microsoft.Xna.Framework.Rectangle newBounds)
        {
            ResetBounds();
        }
        #endregion overrides

        #region events
        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (e.OldMenu == this)
            {
                Cleanup();
            }
        }

        private void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
        {
            if (!Context.IsWorldReady)
            {
                return;
            }
                
            if (e.Delta > 0)
            {
                ZoomLevel += 0.05f;
            }
            else if (e.Delta < 0)
            {
                ZoomLevel -= 0.05f;
            }

            Game1.options.desiredBaseZoomLevel = Math.Clamp(ZoomLevel, MinZoom(Game1.currentLocation), 2.0f);
            ZoomLevel = Game1.options.desiredBaseZoomLevel;
            zoomFunc(ZoomLevel);
        }
        #endregion events

        #region helpers
        private static float MinZoom(GameLocation location)
        {
            // Specially handle a few locations that look kinda janky
            if (location.NameOrUniqueName is "Backwoods" or "BusStop")
            {
                return 0.4f;
            }
            else if (location.NameOrUniqueName is "IslandSouth" or "IslandSouthEast" or "IslandEast")
            {
                return 0.5f;
            }
            return 0.35f;
        }
        private void ResetBounds()
        {
            xPositionOnScreen = Game1.uiViewport.Width / 2 - maxWidthOfBuildingViewer - IClickableMenu.spaceToClearSideBorder;
            yPositionOnScreen = Game1.uiViewport.Height / 2 - maxHeightOfBuildingViewer / 2 - IClickableMenu.spaceToClearTopBorder + 32;
            width = maxWidthOfBuildingViewer + maxWidthOfDescription + IClickableMenu.spaceToClearSideBorder * 2 + 64;
            height = maxHeightOfBuildingViewer + IClickableMenu.spaceToClearTopBorder;
            initialize(xPositionOnScreen, yPositionOnScreen, width, height, showUpperRightCloseButton: false);
        }

        private void UnlockCamera()
        {
            // Untether the camera from the player
            Game1.viewportFreeze = true;
            Game1.currentLocation.forceViewportPlayerFollow = false;
            isCameraUnlocked = true;
        }

        private void MoveCamera(int x, int y)
        {
            if (Game1.currentLocation.IsOutdoors || LocationHasBlackBorder(Game1.currentLocation))
            {
                Game1.panScreen(x * GetZoomSpeed(), y * GetZoomSpeed());
            }
            else
            {
                Game1.viewport.X += x * GetZoomSpeed();
                Game1.viewport.Y += y * GetZoomSpeed();
            }
        }

        private bool LocationHasBlackBorder(GameLocation location)
        {
            if (location.NameOrUniqueName is "BugLand" or "Tunnel" or "WitchSwamp" or "Caldera")
            {
                return true;
            }
            return false;
        }

        private int GetZoomSpeed()
        {
            return Math.Max(1, (int)(2.5 / ZoomLevel));
        }

        private void Cleanup()
        {
            Game1.displayHUD = true;
            Game1.currentLocation.forceViewportPlayerFollow = originalForceViewportPlayerFollow;
            Game1.options.desiredBaseZoomLevel = originalZoomLevel;
            Game1.viewportFreeze = false;
            helper.Events.Input.MouseWheelScrolled -= OnMouseWheelScrolled;
            helper.Events.Display.MenuChanged -= OnMenuChanged;
        }
        #endregion helpers
    }
}