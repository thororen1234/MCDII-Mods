using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.SlateCore;
using UE.UMG;

namespace JumpAttackKills;

/// <summary>The counter on screen: "Jump Attack Kills: 12" in the game's font.</summary>
public class KillCounter : ScreenWidget
{
    // The game's body text style: its font is the Minecraft one of the main menu's Settings and Party.
    const string FontStyle = "/OreUI/UI/Typography/TextStyles/Style_Body_Text.Style_Body_Text_C";
    const float TextSize = 28;

    UTextBlock? text;

    /// <summary>A counter not made yet: keep it in a field, then <see cref="Setup"/> it.</summary>
    public static KillCounter? Create(UObject context) =>
        UWidgetBlueprintLibrary.Create(context, Unreal.ClassOf<KillCounter>(), World.PlayerController(context)) as KillCounter;

    public void Setup()
    {
        // Loaded before any widget is made (loading can let the garbage collector run).
        var info = UMinimapHelpersLibrary.GetDefaultFont();
        var styleClass = Unreal.LoadClass<UCommonTextStyle>(FontStyle);
        if (styleClass != null && UGameplayStatics.SpawnObject(styleClass, this) is UCommonTextStyle style)
        {
            style.GetFont(out var styleFont);
            info.FontObject = styleFont.FontObject;
            info.TypefaceFontName = styleFont.TypefaceFontName;
        }
        info.Size = TextSize;
        var outline = info.OutlineSettings;
        outline.OutlineSize = 1;
        outline.OutlineColor = Ui.Color(0, 0, 0, 1);
        info.OutlineSettings = outline;

        var tree = Ui.Tree(this);
        if (tree == null) return;
        text = Ui.Text(tree, "", 1);
        if (text == null) return;
        text.SetFont(info);
        text.SetShadowOffset(new FVector2D { X = 1, Y = 1 });
        text.SetShadowColorAndOpacity(Ui.Color(0, 0, 0, 0.6f));
        tree.RootWidget = text;
    }

    public void SetCount(int kills) => text?.SetText($"Jump Attack Kills: {kills}");
}
