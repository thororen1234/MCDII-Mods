using System.Collections.Generic;
using NeoRune;
using UE.Achievements;
using UE.CoreUObject;
using UE.Dungeons;
using UE.Engine;
using UE.GameplayAbilities;
using UE.InputCore;
using UE.SpicewoodGAS;

namespace JumpAttackKills;

/// <summary>
/// Jump Attack Kills: counts the mobs you kill with a jump attack, shows the count on screen and saves it.
/// The game sends no kill message the client can read, so a kill is a mob near you whose health reaches 0 while your
/// jump attack is going on, or just after it (the damage lands at the end, and the server's word on it comes later).
/// </summary>
[ModSetting.Toggle(ShowSetting, "Show Counter", Default = true)]
[ModSetting.Select(PlaceSetting, "Position", "Top left", "Top center", "Top right", "Bottom left", "Bottom right", Default = 1)]
[ModSetting.Slider(SizeSetting, "Size", Default = 1, Min = 0.5, Max = 2, Step = 0.05, Percentage = true)]
[ModSetting.Toggle(LogSetting, "Log Jump Attacks", Default = false,
    Description = "Writes how many mobs were near and how many died for each jump attack to JumpAttackKills.log.")]
[ModSetting.EventButton(ResetSetting, "Reset Count")]
public class ModActor : AActor, ISettingsEvents
{
    const string ShowSetting = "show_counter";
    const string PlaceSetting = "position";
    const string SizeSetting = "size";
    const string LogSetting = "log";
    const string ResetSetting = "reset";
    const string SaveName = "kills";

    // How far from you a mob can die and still count, in cm (the jump attack's splash reaches less).
    const double Radius = 1200;
    // How long after the jump attack ends a death still counts.
    const double Grace = 1.5;
    // How often the player's jump attack ability is looked for (it's granted again on respawn and gear changes).
    const float AbilityInterval = 2f;
    // Distance from the screen's edge.
    const float Margin = 24;

    bool show = true;
    int place = 1;
    float size = 1;
    bool log;

    Counts counts;
    KillCounter? counter;
    readonly List<UGA_HeavyJumpAttack> jumpAttacks = [];
    double nextAbilities;

    // While a jump attack is tracked: when tracking ends, the mobs already counted or dead before it, and for the log
    // how many were near when it started and how many it killed.
    bool tracking;
    double trackUntil;
    readonly HashSet<AActor> done = [];
    int near;
    int killed;

    public struct Counts
    {
        public int Kills;
        // Whether the kills from before the mod was installed were taken from the game's achievement.
        public bool Imported;
    }

    protected override void ReceiveBeginPlay()
    {
        ModSave.Load(SaveName, out counts);
    }

    public override void ReceiveTick(float deltaSeconds)
    {
        var player = World.Player(this);
        Show(player != null);
        if (player == null) return;

        var now = World.RealTime(this);
        if (now >= nextAbilities)
        {
            nextAbilities = now + AbilityInterval;
            FindJumpAttacks(player);
            if (!counts.Imported) Import();
        }

        bool active = false;
        foreach (var ability in jumpAttacks)
            if (UKismetSystemLibrary.IsValid(ability) && ability.bIsActive) active = true;
        if (active)
        {
            if (!tracking) Start(player);
            trackUntil = now + Grace;
        }
        if (!tracking) return;
        Check(player);
        if (!active && now >= trackUntil) Finish();
    }

    void FindJumpAttacks(APawn player)
    {
        jumpAttacks.Clear();
        var asc = UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(player);
        if (asc == null) return;
        foreach (var spec in asc.ActivatableAbilities.Items)
        {
            if (spec.Ability is UGA_HeavyJumpAttack ability) jumpAttacks.Add(ability);
            foreach (var instance in spec.NonReplicatedInstances)
                if (instance is UGA_HeavyJumpAttack nonReplicated) jumpAttacks.Add(nonReplicated);
            foreach (var instance in spec.ReplicatedInstances)
                if (instance is UGA_HeavyJumpAttack replicated) jumpAttacks.Add(replicated);
        }
    }

    /// <summary>
    /// Takes the kills from before the mod was installed from the game's jump attack kill achievement, once: its count
    /// (the highest, if it has tiers) when that's more than the mod's. Tried until the achievement has a count.
    /// </summary>
    void Import()
    {
        if (World.PlayerController(this) is not AGameplayPlayerController controller) return;
        var achievements = controller.AchievementSystemComponent;
        if (achievements == null) return;
        bool found = false;
        int amount = 0;
        int max = 0;
        foreach (var entry in achievements.AchievementList.Entries)
            if (entry.Achievement?.Condition is UConditionCountJumpAttackKills condition)
            {
                found = true;
                int current = (int)condition.CurrentAmount;
                if (current < amount) continue;
                amount = current;
                max = (int)condition.MaxAmount;
            }
        // 0 can be a count that hasn't come from the server yet: try again later.
        if (!found || amount == 0) return;
        FileLog.Write($"Jump attack kill achievement: {amount} of {max}, mod's count {counts.Kills}");
        counts.Imported = true;
        if (amount > counts.Kills) counts.Kills = amount;
        ModSave.Save(SaveName, counts);
        counter?.SetCount(counts.Kills);
    }

    /// <summary>A jump attack started: the mobs dead already don't count.</summary>
    void Start(APawn player)
    {
        tracking = true;
        done.Clear();
        near = 0;
        killed = 0;
        foreach (var mob in World.FindAll(this, Unreal.ClassOf<AMobCharacter>()))
        {
            if (!Health(mob, out var health)) continue;
            if (health <= 0) done.Add(mob);
            else if (player.GetDistanceTo(mob) <= Radius) near++;
        }
    }

    /// <summary>Counts the mobs near you that died since the jump attack started.</summary>
    void Check(APawn player)
    {
        int before = killed;
        foreach (var mob in World.FindAll(this, Unreal.ClassOf<AMobCharacter>()))
        {
            if (done.Contains(mob) || !Health(mob, out var health) || health > 0) continue;
            if (player.GetDistanceTo(mob) > Radius) continue;
            done.Add(mob);
            killed++;
        }
        if (killed == before) return;
        counts.Kills += killed - before;
        ModSave.Save(SaveName, counts);
        counter?.SetCount(counts.Kills);
    }

    void Finish()
    {
        tracking = false;
        done.Clear();
        if (log) FileLog.Write($"Jump attack: {near} mobs near, {killed} killed");
    }

    /// <summary>A mob's health; false when it has none to read.</summary>
    static bool Health(AActor mob, out double health)
    {
        health = 0;
        var asc = UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(mob);
        if (asc == null) return false;
        foreach (var set in asc.SpawnedAttributes)
            if (set is UATR_Health healthSet)
            {
                health = healthSet.Health.CurrentValue;
                return true;
            }
        return false;
    }

    /// <summary>Shows the counter while there's a player and it's turned on, making it the first time.</summary>
    void Show(bool inGame)
    {
        if (!show || !inGame)
        {
            counter?.SetVisibility(UE.UMG.ESlateVisibility.Collapsed);
            return;
        }
        if (counter == null)
        {
            counter = KillCounter.Create(this);
            if (counter == null) return;
            counter.Setup();
            counter.SetCount(counts.Kills);
            Place();
        }
        counter.SetVisibility(UE.UMG.ESlateVisibility.HitTestInvisible);
    }

    void Place()
    {
        if (counter == null) return;
        var anchor = place switch
        {
            0 => new FVector2D { X = 0, Y = 0 },
            2 => new FVector2D { X = 1, Y = 0 },
            3 => new FVector2D { X = 0, Y = 1 },
            4 => new FVector2D { X = 1, Y = 1 },
            _ => new FVector2D { X = 0.5, Y = 0 },
        };
        var offset = new FVector2D
        {
            X = anchor.X == 0 ? Margin : anchor.X == 1 ? -Margin : 0,
            Y = anchor.Y == 0 ? Margin : -Margin,
        };
        counter.SetRenderTransformPivot(anchor);
        counter.SetRenderScale(new FVector2D { X = size, Y = size });
        counter.ShowAt(offset, anchor, ScreenWidget.UnderGameUI);
    }

    public void OnSettingChanged(string id, string value)
    {
        if (id == ShowSetting) show = ModSettings.ToBool(value);
        else if (id == PlaceSetting) place = ModSettings.ToInt(value);
        else if (id == SizeSetting) size = (float)UKismetMathLibrary.FClamp(ModSettings.ToNumber(value), 0.5, 2);
        else if (id == LogSetting) log = ModSettings.ToBool(value);
        else return;
        Place();
    }

    public void OnSettingsReset()
    {
        show = true;
        place = 1;
        size = 1;
        log = false;
        Place();
    }

    public void OnButtonPressed(string id)
    {
        if (id != ResetSetting) return;
        counts.Kills = 0;
        ModSave.Save(SaveName, counts);
        counter?.SetCount(0);
    }

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey) { }
}

[Asset("/Game/Mods/BlueprintLoader/BPI_ModSettings")]
public interface ISettingsEvents
{
    void OnButtonPressed(string Id);
    void OnKeybindChanged(string Id, FKey Key, FKey SecondaryKey);
    void OnSettingChanged(string Id, string Value);
    void OnSettingsReset();
}
