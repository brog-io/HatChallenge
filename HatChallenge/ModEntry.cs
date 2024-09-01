using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace HatCollectionMod
{
    public class ModEntry : Mod
    {
        private HatCollectionTab? hatCollectionTab;
        public static ModEntry? Instance { get; private set; }

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            hatCollectionTab = new HatCollectionTab(helper, Monitor);

            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.Patch(
                original: AccessTools.Method(typeof(CollectionsPage), "draw", new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(DrawHatCollectionTab))
            );
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is CollectionsPage collectionsPage)
            {
                AddHatCollectionTab(collectionsPage);
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            hatCollectionTab?.LoadCollectedHats();
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            hatCollectionTab?.SaveCollectedHats();
        }

        private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
        {
            if (hatCollectionTab == null) return;

            foreach (Item item in e.Added)
            {
                if (item is Hat hat)
                {
                    int hatId = GetHatId(hat);
                    hatCollectionTab.UpdateCollectedHats(hatId);
                }
            }
        }

        private int GetHatId(Hat hat)
        {
            // Try to get the 'which' property
            PropertyInfo? whichProperty = typeof(Hat).GetProperty("which");
            if (whichProperty != null)
            {
                object? value = whichProperty.GetValue(hat);
                if (value is int intValue)
                {
                    return intValue;
                }
            }

            // Try to get the 'ParentSheetIndex' property
            PropertyInfo? parentSheetIndexProperty = typeof(Hat).GetProperty("ParentSheetIndex");
            if (parentSheetIndexProperty != null)
            {
                object? value = parentSheetIndexProperty.GetValue(hat);
                if (value is int intValue)
                {
                    return intValue;
                }
            }

            // If we can't find a suitable property, log an error and return -1
            Monitor.Log($"Unable to determine hat ID for {hat.Name}", LogLevel.Error);
            return -1;
        }

        private void AddHatCollectionTab(CollectionsPage collectionsPage)
        {
            if (hatCollectionTab == null) return;

            var tabs = Helper.Reflection.GetField<List<ClickableTextureComponent>>(collectionsPage, "collections").GetValue();
            var tabPosition = new Vector2(collectionsPage.xPositionOnScreen - 48, collectionsPage.yPositionOnScreen + tabs.Count * 64f);

            var newTab = new ClickableTextureComponent(
                "Hat Collection",
                new Rectangle((int)tabPosition.X, (int)tabPosition.Y, 64, 64),
                null,
                "Hat Collection",
                hatCollectionTab.TabIcon,
                new Rectangle(0, 0, 64, 64),
                4f
            );

            tabs.Add(newTab);
            Helper.Reflection.GetField<List<ClickableTextureComponent>>(collectionsPage, "collections").SetValue(tabs);
        }

        public static void DrawHatCollectionTab(CollectionsPage __instance, SpriteBatch b)
        {
            if (Instance?.hatCollectionTab == null) return;

            var currentTab = __instance.currentTab;
            if (currentTab == __instance.collections.Count - 1)  // Assuming the new tab is the last one
            {
                // Use public methods instead of the inaccessible ones
                IClickableMenu.drawTextureBox(b, __instance.xPositionOnScreen, __instance.yPositionOnScreen + IClickableMenu.borderWidth + IClickableMenu.spaceToClearTopBorder + 256, __instance.width, 4, Color.White);
                IClickableMenu.drawTextureBox(b, __instance.xPositionOnScreen + 256, __instance.yPositionOnScreen, 4, __instance.height, Color.White);

                SpriteFont smallFont = Game1.smallFont;
                b.DrawString(smallFont, "Hat Collection", new Vector2(__instance.xPositionOnScreen + 32, __instance.yPositionOnScreen + 32), Game1.textColor);

                // Draw hat collection content
                Instance.hatCollectionTab.Draw(b);
            }
        }
    }

    public class HatCollectionTab
    {
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        public Texture2D? TabIcon { get; private set; }
        private Texture2D? hatTexture;
        private HashSet<int> collectedHats;
        private const int ICON_SIZE = 64;
        private const int ICONS_PER_ROW = 8;
        private Rectangle hoverRect;
        private string hoverText = string.Empty;

        public HatCollectionTab(IModHelper helper, IMonitor monitor)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.collectedHats = new HashSet<int>();
            this.LoadAssets();
        }

        private void LoadAssets()
        {
            try
            {
                this.TabIcon = this.helper.ModContent.Load<Texture2D>("assets/HatCollectionTab.png");
                this.hatTexture = Game1.content.Load<Texture2D>("Characters/Farmer/hats");
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Error loading assets: {ex.Message}", LogLevel.Error);
            }
        }

        public void LoadCollectedHats()
        {
            var savedHats = this.helper.Data.ReadSaveData<List<int>>("CollectedHats") ?? new List<int>();
            this.collectedHats = new HashSet<int>(savedHats);
        }

        public void SaveCollectedHats()
        {
            this.helper.Data.WriteSaveData("CollectedHats", this.collectedHats.ToList());
        }

        public void Draw(SpriteBatch b)
        {
            if (hatTexture == null) return;

            Vector2 position = new Vector2(Game1.activeClickableMenu.xPositionOnScreen + 320, Game1.activeClickableMenu.yPositionOnScreen + 128);
            int count = 0;

            // Load the hat data as Dictionary<string, string>
            var rawHatData = Game1.content.Load<Dictionary<string, string>>("Data/hats");

            // Convert the dictionary to use int keys, skipping any entries that can't be parsed as integers
            var hatData = rawHatData
                .Where(pair => int.TryParse(pair.Key, out _))
                .ToDictionary(pair => int.Parse(pair.Key), pair => pair.Value);

            foreach (var hatPair in hatData)
            {
                var isCollected = this.collectedHats.Contains(hatPair.Key);

                Rectangle destRect = new Rectangle((int)position.X, (int)position.Y, ICON_SIZE, ICON_SIZE);

                Rectangle sourceRect = Game1.getSourceRectForStandardTileSheet(this.hatTexture, hatPair.Key, 20, 20);
                Color hatColor = isCollected ? Color.White : Color.DarkGray;
                b.Draw(this.hatTexture, destRect, sourceRect, hatColor);

                count++;
                if (count % ICONS_PER_ROW == 0)
                {
                    position.X = Game1.activeClickableMenu.xPositionOnScreen + 320;
                    position.Y += ICON_SIZE + 8;
                }
                else
                {
                    position.X += ICON_SIZE + 8;
                }
            }

            if (hoverRect != Rectangle.Empty)
            {
                IClickableMenu.drawHoverText(b, hoverText, Game1.smallFont);
            }
        }

        public void PerformHoverAction(int x, int y)
        {
            Vector2 position = new Vector2(Game1.activeClickableMenu.xPositionOnScreen + 320, Game1.activeClickableMenu.yPositionOnScreen + 128);
            int count = 0;

            // Load and convert hat data
            var rawHatData = Game1.content.Load<Dictionary<string, string>>("Data/hats");
            var hatData = rawHatData
                .Where(pair => int.TryParse(pair.Key, out _))
                .ToDictionary(pair => int.Parse(pair.Key), pair => pair.Value);

            foreach (var hatPair in hatData)
            {
                Rectangle hatRect = new Rectangle((int)position.X, (int)position.Y, ICON_SIZE, ICON_SIZE);
                if (hatRect.Contains(x, y))
                {
                    hoverRect = hatRect;
                    hoverText = hatPair.Value;
                    if (this.collectedHats.Contains(hatPair.Key))
                    {
                        hoverText += " (Collected)";
                    }
                    return;
                }

                count++;
                if (count % ICONS_PER_ROW == 0)
                {
                    position.X = Game1.activeClickableMenu.xPositionOnScreen + 320;
                    position.Y += ICON_SIZE + 8;
                }
                else
                {
                    position.X += ICON_SIZE + 8;
                }
            }

            hoverRect = Rectangle.Empty;
            hoverText = string.Empty;
        }

        public void UpdateCollectedHats(int hatId)
        {
            if (!this.collectedHats.Contains(hatId))
            {
                this.collectedHats.Add(hatId);
                this.SaveCollectedHats();
            }
        }
    }
}