using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace CustomCapes;

/// <summary>
/// Custom Capes: wear your own cape PNGs, picked in the inventory's Collectibles screen under the game's capes, or with
/// F3. Only you see them: everyone else sees the cape picked in the game's own menu. The keys are settings in
/// BetterBlueprintLoader's Mods tab.
/// </summary>
[ModSetting.Heading("Keys")]
[ModSetting.Keybind(NextSetting, "Next Cape", Default = DefaultNextKey,
    Description = "Wears the next cape in the Capes folder; after the last one, the game's cape again.")]
[ModSetting.Keybind(ReloadSetting, "Reload Capes", Default = DefaultReloadKey,
    Description = "Reads the cape files again, to see changes you just saved in your image editor.")]
[ModSetting.Keybind(ExportSetting, "Save Game Cape", Default = DefaultExportKey,
    Description = "Saves the game's cape you wear as a PNG in the Capes folder's _game folder, to start your own from.")]
[ModSetting.Heading("Animated Capes")]
[ModSetting.Slider(SpeedSetting, "Frames Per Second", Default = DefaultSpeed, Min = 1, Max = 30, Step = 1,
    Description = "How fast animated capes play.")]
public class ModActor : AActor, ISettingsEvents
{
    const string NextSetting = "next_key";
    const string ReloadSetting = "reload_key";
    const string ExportSetting = "export_key";
    const string SpeedSetting = "frames_per_second";
    const float DefaultSpeed = 10;
    const string DefaultNextKey = "F3";
    const string DefaultReloadKey = "F4";
    const string DefaultExportKey = "F5";
    // The game puts its cape back when the character or gear changes: this often, the custom one goes back on.
    const float CheckInterval = 1f;
    const float MenuInterval = 0.25f;
    // The inventory's Collectibles screen (capes and pets), and the screen around it, which is active while it shows.
    const string CollectiblesClass = "W_Collectibles.W_Collectibles_C";
    const string CollectiblesScreenClass = "W_Collectibles_Activatable.W_Collectibles_Activatable_C";
    // The Collectibles item grid: the cape row goes in the list it's in.
    const string ItemGridName = "ItemGrid";
    // Where the row sits when the item grid has no list to go in: just past the scrollbar, with its bottom level with
    // the scrollbar's.
    const float RowLeft = 16;
    const float RowBottom = 330; // higher than CustomSkins to prevent overlap

    CapeSwapper? capes;
    // When Check and WatchMenu run next (real time): from the tick, which keeps running while menus pause the game.
    // Not PausableTimers: calling a ModActor's methods from one crashed the game (CustomSkins, 2026-10-05).
    double nextCheck;
    double nextMenuWatch;
    CapeRow? row;
    UUserWidget? rowIn;
    // The Collectibles item grid: a floating row sits over its bottom.
    UWidget? grid;
    // Without a list to go in, the row shows over the screen while the Collectibles screen is open.
    bool floating;
    bool menuShown;
    bool started;

    // The Custom tab, while the row is in the item grid's place.
    CustomTab? tab;
    bool tabbed;

    // The keys, each setting's two (the second empty unless set in the Mods tab).
    FKey nextKey = new FKey { KeyName = DefaultNextKey };
    FKey nextKey2 = new FKey();
    FKey reloadKey = new FKey { KeyName = DefaultReloadKey };
    FKey reloadKey2 = new FKey();
    FKey exportKey = new FKey { KeyName = DefaultExportKey };
    FKey exportKey2 = new FKey();
    float framesPerSecond = DefaultSpeed;

    protected override void ReceiveBeginPlay()
    {
        // Menus can pause the game, and the inventory, which shows the character, is one.
        SetTickableWhenPaused(true);
    }

    /// <summary>
    /// Whether another copy of the mod runs in this level (a second mod loader spawns one too). Two copies each put their
    /// own cape back every second, undoing the cape picked in the other: the newcomer leaves.
    /// </summary>
    bool HasTwin()
    {
        foreach (var other in World.FindAll(this, Unreal.ClassOf<ModActor>()))
            if (other != null && other != this && UKismetSystemLibrary.IsValid(other)) return true;
        return false;
    }

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey)
    {
        if (id == NextSetting)
        {
            nextKey = key;
            nextKey2 = secondaryKey;
        }
        else if (id == ReloadSetting)
        {
            reloadKey = key;
            reloadKey2 = secondaryKey;
        }
        else if (id == ExportSetting)
        {
            exportKey = key;
            exportKey2 = secondaryKey;
        }
    }

    public void OnSettingsReset()
    {
        nextKey = new FKey { KeyName = DefaultNextKey };
        reloadKey = new FKey { KeyName = DefaultReloadKey };
        exportKey = new FKey { KeyName = DefaultExportKey };
        nextKey2 = new FKey();
        reloadKey2 = new FKey();
        exportKey2 = new FKey();
        framesPerSecond = DefaultSpeed;
    }

    public void OnSettingChanged(string id, string value)
    {
        if (id == SpeedSetting) framesPerSecond = (float)UKismetMathLibrary.FClamp(ModSettings.ToNumber(value), 1, 30);
    }
    public void OnButtonPressed(string id) { }

    static bool Pressed(APlayerController controller, FKey key, FKey secondary) =>
        controller.WasInputKeyJustPressed(key) || controller.WasInputKeyJustPressed(secondary);

    public override void ReceiveTick(float deltaSeconds)
    {
        if (!started)
        {
            if (HasTwin())
            {
                FileLog.Write("Another copy of Custom Capes is running in this level (is a second mod loader installed?): this one stopped");
                K2_DestroyActor();
                return;
            }
            started = true;
            capes = CapeSwapper.Create(this);
        }
        capes?.WatchPreviews();
        var now = World.RealTime(this);
        capes?.Animate(now, framesPerSecond);
        if (now >= nextCheck)
        {
            nextCheck = now + CheckInterval;
            Check();
        }
        if (now >= nextMenuWatch)
        {
            nextMenuWatch = now + MenuInterval;
            WatchMenu();
        }
        var controller = World.PlayerController(this);
        if (controller == null || capes == null) return;
        if (Pressed(controller, nextKey, nextKey2))
        {
            capes.Next();
            row?.Refresh();
        }
        if (Pressed(controller, reloadKey, reloadKey2))
        {
            capes.Reload();
            row?.Refresh();
        }
        if (Pressed(controller, exportKey, exportKey2)) capes.ExportGameCape();
    }

    void Check() => capes?.Check();

    /// <summary>Puts the cape row in the Collectibles screen when it opens, with the capes in the folder now.</summary>
    void WatchMenu()
    {
        if (capes == null) return;
        UUserWidget? collectibles = null;
        bool shown = false;
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var widgets, Unreal.ClassOf<UUserWidget>(), false);
        foreach (var widget in widgets)
        {
            if (widget == null) continue;
            var cls = UKismetSystemLibrary.Conv_SoftClassReferenceToString(UKismetSystemLibrary.Conv_ClassToSoftClassReference(UGameplayStatics.GetObjectClass(widget)));
            // The screen shown now: closed ones can stay around until they're cleaned up, and putting the row in one
            // of those took it off the screen.
            if (cls.Contains(CollectiblesClass) && (collectibles == null || OnActiveScreen(widget))) collectibles = widget;
            else if (cls.Contains(CollectiblesScreenClass) && widget is UCommonActivatableWidget screen && screen.IsActivated()) shown = true;
        }

        if (shown && !menuShown)
        {
            if (row == null)
            {
                // Kept in the field before it's set up: setting it up loads the game's assets, which can let the
                // garbage collector run.
                row = CapeRow.Create(this);
                if (row != null && !row.Setup(capes)) row = null;
            }
            else row.Refresh();
        }
        menuShown = shown;
        if (row == null) return;
        if (shown && collectibles != null && collectibles != rowIn && OnActiveScreen(collectibles)) PlaceRow(collectibles);
        if (shown && tabbed && tab != null)
        {
            tab.Keep();
            row.Fit(tab.TileWidth, tab.TileHeight, tab.Columns);
            if (tab.Chosen) row.Rescan();
        }
        if (!floating) return;
        row.SetVisibility(shown ? ESlateVisibility.Visible : ESlateVisibility.Collapsed);
        if (!shown) return;
        // The game can rebuild its UI and take the row off the screen with it.
        if (!row.IsInViewport()) row.AddToViewport(ScreenWidget.AboveGameUI);
        FollowGrid();
    }

    /// <summary>Keeps the floating row across the bottom of the item grid, as wide as it.</summary>
    void FollowGrid()
    {
        if (row == null || grid == null || !UKismetSystemLibrary.IsValid(grid)) return;
        var geometry = grid.GetCachedGeometry();
        var size = USlateBlueprintLibrary.GetLocalSize(geometry);
        if (size.X <= 0) return;
        var height = row.GetDesiredSize().Y;
        if (height <= 0) height = 130;
        USlateBlueprintLibrary.LocalToViewport(this, geometry, new FVector2D { X = 0, Y = size.Y }, out var pixel, out var corner);
        row.SetDesiredSizeInViewport(new FVector2D { X = size.X, Y = height });
        row.SetPositionInViewport(new FVector2D { X = corner.X, Y = corner.Y - height }, false);
    }

    void PlaceRow(UUserWidget collectibles)
    {
        if (row == null) return;
        rowIn = collectibles;
        row.RemoveFromParent();
        row.SetVisibility(ESlateVisibility.Visible);
        tabbed = false;
        grid = Find(collectibles.WidgetTree?.RootWidget, ItemGridName);

        // The Custom tab's page, laid over the item grid: after CustomSkins' section.
        if (grid?.GetParent() is UGridPanel)
        {
            if (tab == null) tab = CustomTab.Create(this);
            if (tab != null && tab.Attach(grid, row, false))
            {
                floating = false;
                tabbed = true;
                FileLog.Write("Cape section added to the Collectibles screen's Custom tab");
                return;
            }
        }
        // Or the nearest list the item grid is in: the row goes at its end.
        var parent = grid?.GetParent();
        while (parent != null && parent is not UVerticalBox) parent = parent.GetParent();
        if (parent is UVerticalBox list)
        {
            floating = false;
            list.AddChildToVerticalBox(row)?.SetPadding(new FMargin { Top = 8 });
            FileLog.Write($"Cape row added to the Collectibles screen, in {UKismetSystemLibrary.GetObjectName(list)}");
            return;
        }
        floating = true;
        // Not ShowAt: its check puts the widget back at the ShowAt spot every half second, while FollowGrid moves it.
        row.AddToViewport(ScreenWidget.AboveGameUI);
        FollowGrid();
        FileLog.Write($"Cape row shown over the Collectibles screen (item grid {(grid == null ? "not found" : "not in a list")})");
    }

    /// <summary>Whether a widget is on a screen that's active (shown, not closed or covered by another).</summary>
    static bool OnActiveScreen(UWidget widget) =>
        UCommonUILibrary.FindParentWidgetOfType(widget, Unreal.ClassOf<UCommonActivatableWidget>()) is UCommonActivatableWidget screen
        && screen.IsActivated();

    /// <summary>The first widget with a name under a widget, or null.</summary>
    static UWidget? Find(UWidget? root, string name)
    {
        var queue = new List<UWidget>();
        if (root != null) queue.Add(root);
        for (int i = 0; i < queue.Count && i < 512; i++)
        {
            if (UKismetSystemLibrary.GetObjectName(queue[i]) == name) return queue[i];
            if (queue[i] is UPanelWidget panel)
                for (int c = 0; c < panel.GetChildrenCount(); c++)
                {
                    var child = panel.GetChildAt(c);
                    if (child != null) queue.Add(child);
                }
        }
        return null;
    }
}

[Asset("/Game/Mods/BlueprintLoader/BPI_ModSettings")]
public interface ISettingsEvents
{
    void OnButtonPressed(string Id);
    void OnKeybindChanged(string Id, FKey Key, FKey SecondaryKey);
    void OnSettingChanged(string Id, string Value);
    void OnSettingsReset();
}
