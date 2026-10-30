using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.SlateCore;
using UE.UMG;

namespace StatsOverlay;

/// <summary>
/// The overlay: an icon and a number for each display that's turned on, side by side or one under the other, in the
/// game's font. <see cref="Set"/> gives a display its text; an empty text hides it. It's made in the hotbar's units:
/// the mod scales it to the hotbar's size on screen.
/// </summary>
public class StatsBar : ScreenWidget
{
    // The game's body text style: its font is the Minecraft one of the main menu's Settings and Party.
    const string FontStyle = "/OreUI/UI/Typography/TextStyles/Style_Body_Text.Style_Body_Text_C";
    // Sizes in the hotbar's units (its lower bar is 160 high).
    const float TextSize = 36;
    const int IconSize = 44;
    const float IconGap = 8;
    const float ItemGap = 28;
    const float RowGap = 6;

    // The game's assets, loaded before any widget is made (loading can let the garbage collector run).
    UObject? font;
    FName typeface;
    List<UTexture2D?> icons = new();

    UPanelWidget? root;
    List<UHorizontalBox?> items = new();
    List<UTextBlock?> texts = new();
    // What each display shows now (empty: hidden), kept to fill the widgets when they're made again.
    List<string> shown = new();
    bool column;

    /// <summary>An overlay not made yet: keep it in a field, then <see cref="Setup"/> it (which loads the game's assets).</summary>
    public static StatsBar? Create(UObject context) =>
        UWidgetBlueprintLibrary.Create(context, Unreal.ClassOf<StatsBar>(), World.PlayerController(context)) as StatsBar;

    /// <summary>Loads the font and an icon per display (<paramref name="iconPaths"/>, in display order), and makes the widgets.</summary>
    public void Setup(List<string> iconPaths)
    {
        // The style's font and typeface, taken from a style object made from its class.
        var styleClass = Unreal.LoadClass<UCommonTextStyle>(FontStyle);
        if (styleClass != null && UGameplayStatics.SpawnObject(styleClass, this) is UCommonTextStyle style)
        {
            style.GetFont(out var styleFont);
            font = styleFont.FontObject;
            typeface = styleFont.TypefaceFontName;
        }
        foreach (var path in iconPaths)
        {
            icons.Add(Load(path) as UTexture2D);
            shown.Add("");
        }
        Build();
    }

    /// <summary>
    /// Puts the overlay under the game's UI if it isn't on screen (the game can rebuild its UI). Not ShowAt: its check puts
    /// the widget back at the ShowAt spot every half second, and the overlay is moved every tick.
    /// </summary>
    public void KeepOnScreen()
    {
        if (!IsInViewport()) AddToPlayerScreen(UnderGameUI);
    }

    /// <summary>Side by side (false) or one under the other (true).</summary>
    public void Style(bool asColumn)
    {
        if (asColumn == column) return;
        column = asColumn;
        Build();
    }

    /// <summary>A display's text; empty hides it.</summary>
    public void Set(int display, string text)
    {
        if (display < 0 || display >= shown.Count || shown[display] == text) return;
        shown[display] = text;
        Fill(display);
    }

    void Fill(int display)
    {
        if (display >= items.Count) return;
        var item = items[display];
        var text = texts[display];
        if (item == null || text == null) return;
        var value = shown[display];
        if (value == "")
        {
            item.SetVisibility(ESlateVisibility.Collapsed);
            return;
        }
        text.SetText(value);
        item.SetVisibility(ESlateVisibility.HitTestInvisible);
    }

    void Build()
    {
        var tree = Ui.Tree(this);
        if (tree == null) return;
        items.Clear();
        texts.Clear();
        if (column) root = Ui.Column(tree);
        else root = Ui.Row(tree);
        tree.RootWidget = root;
        if (root == null) return;
        for (int i = 0; i < icons.Count; i++)
        {
            var item = Ui.Row(tree);
            var text = Ui.Text(tree, "", 1);
            items.Add(item);
            texts.Add(text);
            if (item == null || text == null) continue;
            text.SetFont(Font());
            text.SetShadowOffset(new FVector2D { X = 1, Y = 1 });
            text.SetShadowColorAndOpacity(Ui.Color(0, 0, 0, 0.6f));
            var icon = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
            if (icon != null && icons[i] != null)
            {
                // The brush's size is the icon's: the textures are bigger.
                icon.SetBrush(UWidgetBlueprintLibrary.MakeBrushFromTexture(icons[i], IconSize, IconSize));
                var iconSlot = item.AddChildToHorizontalBox(icon);
                iconSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
                iconSlot?.SetPadding(new FMargin { Right = IconGap });
            }
            item.AddChildToHorizontalBox(text)?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);

            if (root is UVerticalBox list) list.AddChildToVerticalBox(item)?.SetPadding(new FMargin { Top = i == 0 ? 0 : RowGap });
            else if (root is UHorizontalBox line)
            {
                var slot = line.AddChildToHorizontalBox(item);
                slot?.SetPadding(new FMargin { Left = i == 0 ? 0 : ItemGap });
                slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            }
            Fill(i);
        }
    }

    /// <summary>The game's Minecraft font with an outline, or the default font if it isn't in the game.</summary>
    FSlateFontInfo Font()
    {
        var info = UMinimapHelpersLibrary.GetDefaultFont();
        if (font != null)
        {
            info.FontObject = font;
            info.TypefaceFontName = typeface;
        }
        info.Size = TextSize;
        var outline = info.OutlineSettings;
        outline.OutlineSize = 1;
        outline.OutlineColor = Ui.Color(0, 0, 0, 1);
        info.OutlineSettings = outline;
        return info;
    }

    UObject? Load(string path) =>
        UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path)));
}
