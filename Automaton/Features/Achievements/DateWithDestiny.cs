using Automaton.Features.Commands;
using Automaton.Features.Debugging;
using Automaton.FeaturesSetup;
using Automaton.Helpers;
using Automaton.IPC;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using visland.Helpers;

namespace Automaton.Features.Achievements;
internal class DateWithDestiny : Feature
{
    public override string Name => "Date with Destiny";
    public override string Description => "It's a FATE bot. Requires vnavmesh and whatever you want for combat. Yo-Kai mode can be activated by having a yokai minion summoned.";
    public override FeatureType FeatureType => FeatureType.Achievements;

    private bool active = false;
    private string step;
    private static Vector3 TargetPos;
    private Throttle action = new();
    private NavmeshIPC navmesh;
    private Random random;

    private enum Z
    {
        MiddleLaNoscea = 134,
        LowerLaNoscea = 135,
        EasternLaNoscea = 137,
        WesternLaNoscea = 138,
        UpperLaNoscea = 139,
        WesternThanalan = 140,
        CentralThanalan = 141,
        EasternThanalan = 145,
        SouthernThanalan = 146,
        NorthernThanalan = 147,
        CentralShroud = 148,
        EastShroud = 152,
        SouthShroud = 153,
        NorthShroud = 154,
        OuterLaNoscea = 180,
        CoerthasWesternHighlands = 397,
        TheDravanianForelands = 398,
        TheDravanianHinterlands = 399,
        TheChurningMists = 400,
        TheSeaofClouds = 401,
        AzysLla = 402,
        TheFringes = 612,
        TheRubySea = 613,
        Yanxia = 614,
        ThePeaks = 620,
        TheLochs = 621,
        TheAzimSteppe = 622,
    }

    private const uint YokaiWatch = 15222;
    private static readonly List<uint> YokaiMinions = [200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 390, 391, 392, 393];
    private static readonly List<uint> YokaiLegendaryMedals = [15167, 15168, 15169, 15170, 15171, 15172, 15173, 15174, 15175, 15176, 15177, 15178, 15179, 15180, 30803, 30804, 30805, 30806];
    private static readonly List<List<Z>> YokaiZones =
        [
            [Z.CentralShroud, Z.LowerLaNoscea, Z.CentralThanalan], // Jibanyan
            [Z.EastShroud, Z.WesternLaNoscea, Z.EasternThanalan], // Komasan
            [Z.SouthShroud, Z.UpperLaNoscea, Z.SouthernThanalan], // Whisper
            [Z.NorthShroud, Z.OuterLaNoscea, Z.MiddleLaNoscea], // Blizzaria
            [Z.WesternThanalan, Z.CentralShroud, Z.LowerLaNoscea], // Kyubi
            [Z.CentralThanalan, Z.EastShroud, Z.WesternLaNoscea], // Komajiro
            [Z.EasternThanalan, Z.SouthShroud, Z.UpperLaNoscea], // Manjimutt
            [Z.SouthernThanalan, Z.NorthShroud, Z.OuterLaNoscea], // Noko
            [Z.MiddleLaNoscea, Z.WesternThanalan, Z.CentralShroud], // Venoct
            [Z.LowerLaNoscea, Z.CentralThanalan, Z.EastShroud], // Shogunyan
            [Z.WesternLaNoscea, Z.EasternThanalan, Z.SouthShroud], // Hovernyan
            [Z.UpperLaNoscea, Z.SouthernThanalan, Z.NorthShroud], // Robonyan
            [Z.OuterLaNoscea, Z.MiddleLaNoscea, Z.WesternThanalan], // USApyon
            [Z.CoerthasWesternHighlands, Z.TheDravanianForelands, Z.TheDravanianHinterlands, Z.TheChurningMists, Z.TheSeaofClouds, Z.AzysLla], // Lord Enma
            [Z.CoerthasWesternHighlands, Z.TheDravanianForelands, Z.TheDravanianHinterlands, Z.TheChurningMists, Z.TheSeaofClouds, Z.AzysLla], // Lord Ananta
            [Z.TheFringes, Z.TheRubySea, Z.Yanxia, Z.ThePeaks, Z.TheLochs, Z.TheAzimSteppe], // Zazel
            [Z.TheFringes, Z.TheRubySea, Z.Yanxia, Z.ThePeaks, Z.TheLochs, Z.TheAzimSteppe], // Damona
        ];
    private readonly IEnumerable<(uint Minion, uint Medal, List<Z> Zones)> yokai = YokaiMinions.Zip(YokaiLegendaryMedals, (x, y) => (Minion: x, Medal: y)).Zip(YokaiZones, (xy, z) => (xy.Minion, xy.Medal, z));

    private ushort nextFateID;
    private byte fateMaxLevel;
    private ushort fateID;
    private ushort FateID
    {
        get => fateID; set
        {
            if (fateID != value)
            {
                SyncFate(value);
            }
            fateID = value;
        }
    }

    protected override DrawConfigDelegate DrawConfigTree => (ref bool hasChanged) =>
    {
        if (ImGui.Button("Start/Stop"))
        {
            active ^= true;
            step = string.Empty;
            navmesh.Stop();
        }
#if DEBUG
        if (Config.showDebugFeatures)
        {
            ImGui.SameLine();
            if (ImGui.Button("Swap Yokai Zones"))
            {
                var zone = yokai.FirstOrDefault(x => x.Minion == CurrentCompanion).Zones;
                var z = zone[zone.IndexOf(((Z)Svc.ClientState.TerritoryType) + 2) % zone.Count];
                unsafe { Telepo.Instance()->Teleport(CoordinatesHelper.GetZoneMainAetheryte((uint)z), 0); }
            }
        }
#endif
        ImGui.TextUnformatted($"Status: {(active ? "on" : "off")}. Step: {step}");
        ImGui.TextUnformatted($"Filtered FATEs:");
        var fates = GetFates();
        if (fates != null)
            foreach (var fate in fates)
                ImGui.TextUnformatted($"{fate.Name} @ {fate.Position} {Vector3.DistanceSquared(fate.Position, Svc.ClientState.LocalPlayer.Position)} {fate.Progress}% {fate.TimeRemaining}");
        ImGui.TextUnformatted($"Unfiltered FATEs:");
        foreach (var fate in Svc.Fates)
        {
            ImGui.TextUnformatted($"{fate.Name} @ {fate.Position} {fate.Progress}% {fate.TimeRemaining}/{fate.Duration}");
            ImGui.SameLine();
            if (ImGui.Button($"P###{fate.FateId}"))
            {
                if (navmesh.IsRunning())
                    navmesh.Stop();
                else
                    navmesh.PathfindAndMoveTo(fate.Position, Svc.Condition[ConditionFlag.InFlight]);
            }
#if DEBUG
            if (Config.showDebugFeatures)
            {
                ImGui.SameLine();
                if (ImGui.Button($"T###{fate.FateId}"))
                    PositionDebug.SetPos(fate.Position);
            }
#endif
            ImGui.SameLine();
            if (ImGui.Button($"F###{fate.FateId}"))
                CoordinatesHelper.Place(Svc.ClientState.TerritoryType, fate.Position.X, fate.Position.Z);
        }
    };

    public override void Enable()
    {
        base.Enable();
        navmesh = new();
        random = new();
        Svc.Framework.Update += OnUpdate;
    }

    public override void Disable()
    {
        base.Disable();
        Svc.Framework.Update -= OnUpdate;
    }

    private unsafe void OnUpdate(IFramework framework)
    {
        if (!active || Svc.Fates.Count == 0 || Svc.Condition[ConditionFlag.Unknown57]) return;
        if (navmesh.IsRunning())
        {
            if (DistanceToTarget() > 5)
            {
                step = "Naving to FATE";
                return;
            }
            else
                navmesh.Stop();
        }

        if (Svc.Condition[ConditionFlag.InCombat] && !Svc.Condition[ConditionFlag.Mounted]) return;
        var cf = FateManager.Instance()->CurrentFate;
        if (cf is not null)
        {
            FateID = cf->FateId;
            fateMaxLevel = cf->MaxLevel;
            if (Svc.Condition[ConditionFlag.Mounted])
                ExecuteDismount();
            if (!Svc.Condition[ConditionFlag.InCombat] && Svc.Targets.Target == null)
            {
                var target = GetFateMob();
                if (target != null)
                {
                    if (Svc.Targets.Target == null)
                    {
                        step = "Targeting new FATE mob";
                        Svc.Targets.Target = target;
                    }
                    if (!navmesh.PathfindInProgress())
                    {
                        step = "Moving to new mob";
                        TargetPos = target.Position;
                        navmesh.PathfindAndMoveTo(TargetPos, false);
                        return;
                    }
                }
            }
        }
        else
            FateID = 0;

        if (cf is null)
        {
            if (YokaiMinions.Contains(CurrentCompanion))
            {
                // if watch isn't equipped, equip it
                if (HaveYokaiMinionsMissing() && !HasWatchEquipped() && InventoryManager.Instance()->GetInventoryItemCount(YokaiWatch) > 0)
                {
                    Svc.Log.Debug("Equipping watch watch");
                    step = "Equipping Yo-Kai watch";
                    Equip.EquipItem(15222);
                }
                // fate farm until 15 legendary medals
                var medal = yokai.FirstOrDefault(x => x.Minion == CurrentCompanion).Medal;
                if (InventoryManager.Instance()->GetInventoryItemCount(medal) >= 10)
                {
                    // check for other companions, summon them, repeat
                    Svc.Log.Debug("Have 10 of the relevant Legendary Medal. Swapping minions");
                    step = "Swapping minions";
                    var minion = yokai.FirstOrDefault(x => CompanionUnlocked(x.Minion) && InventoryManager.Instance()->GetInventoryItemCount(x.Medal) < 10).Minion;
                    if (minion != default)
                    {
                        ECommons.Automation.Chat.Instance.SendMessage($"/minion {Svc.Data.GetExcelSheet<Companion>().GetRow(minion).Singular}");
                        return;
                    }
                }
                // get zone of minion
                var zones = yokai.FirstOrDefault(x => x.Minion == CurrentCompanion).Zones;
                // if not in zone, go to it
                if (!zones.Contains((Z)Svc.ClientState.TerritoryType))
                {
                    Svc.Log.Debug("Have Yokai minion equipped but not in appropiate zone. Teleporting");
                    step = "Swapping zones";
                    Telepo.Instance()->Teleport(CoordinatesHelper.GetZoneMainAetheryte((uint)zones.First()), 0);
                    return;
                }
            }
            if (!Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.Casting])
            {
                step = "Mounting";
                ExecuteMount();
                return;
            }

            if (Svc.Condition[ConditionFlag.Mounted] && !Svc.Condition[ConditionFlag.InFlight])
            {
                step = "Jumping";
                ExecuteJump();
                return;
            }

            var nextFate = GetFates().FirstOrDefault();
            if (nextFate is not null && Svc.Condition[ConditionFlag.InFlight] && !navmesh.PathfindInProgress())
            {
                Svc.Log.Debug("Finding path to fate");
                step = "Finding path to fate";
                nextFateID = nextFate.FateId;
                TargetPos = nextFate.Position;
                navmesh.PathfindAndMoveTo(TargetPos, true);
            }
        }
    }

    private unsafe void ExecuteActionSafe(ActionType type, uint id) => action.Exec(() => ActionManager.Instance()->UseAction(type, id));
    private void ExecuteMount() => ExecuteActionSafe(ActionType.GeneralAction, 24); // flying mount roulette
    private void ExecuteDismount() => ExecuteActionSafe(ActionType.GeneralAction, 23);
    private void ExecuteJump() => ExecuteActionSafe(ActionType.GeneralAction, 2);

    private IOrderedEnumerable<Dalamud.Game.ClientState.Fates.Fate> GetFates()
        => Svc.Fates.Where(f => f.GameData.Rule == 1 && f.State != FateState.Preparation && (f.Duration <= 900 || f.Progress > 0) && f.Progress <= 90 && f.TimeRemaining > 120)
        .OrderBy(f => Vector3.DistanceSquared(Svc.ClientState.LocalPlayer.Position, f.Position));
    private unsafe GameObject GetFateMob()
        => Svc.Objects.OrderBy(x => Vector3.DistanceSquared(x.Position, Svc.ClientState.LocalPlayer.Position))
        .ThenByDescending(x => (x as Character)?.MaxHp ?? 0)
        .ThenByDescending(x => ObjectFunctions.GetAttackableEnemyCountAroundPoint(x.Position, 5))
        .Where(x => x.Struct() != null && x.Struct()->FateId == FateID)
        .Where(x => !x.IsDead && x.IsTargetable && x.IsHostile() && x.ObjectKind == ObjectKind.BattleNpc && x.SubKind == (byte)BattleNpcSubKind.Enemy)
        .Where(x => Math.Sqrt(Math.Pow(x.Position.X - CurrentFate->Location.X, 2) + Math.Pow(x.Position.Z - CurrentFate->Location.Z, 2)) < CurrentFate->Radius)
        .FirstOrDefault();

    private unsafe uint CurrentCompanion => Svc.ClientState.LocalPlayer.Struct()->Character.CompanionObject->Character.GameObject.DataID;
    private unsafe bool CompanionUnlocked(uint id) => UIState.Instance()->IsCompanionUnlocked(id);
    private unsafe bool HasWatchEquipped() => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->GetInventorySlot(10)->ItemID == YokaiWatch;
    private unsafe bool HaveYokaiMinionsMissing() => yokai.Any(x => CompanionUnlocked(x.Minion));

    private unsafe FateContext* CurrentFate => FateManager.Instance()->GetFateById(nextFateID);
    private unsafe float DistanceToFate() => Vector3.DistanceSquared(CurrentFate->Location, Svc.ClientState.LocalPlayer.Position);
    private unsafe float DistanceToTarget() => Vector3.DistanceSquared(TargetPos, Svc.ClientState.LocalPlayer.Position);
    private unsafe Vector3 GetRandomPointInFate(ushort fateID)
    {
        var fate = FateManager.Instance()->GetFateById(fateID);
        var angle = random.NextDouble() * 2 * Math.PI;
        var randomPoint = new Vector3((float)(fate->Radius / 2 * Math.Cos(angle)), fate->Location.Y, (float)(fate->Radius / 2 * Math.Sin(angle)));
        return navmesh.NearestPoint(randomPoint, 5, 5);
    }

    private void SyncFate(ushort value)
    {
        if (value != 0)
        {
            if (Svc.ClientState.LocalPlayer.Level > fateMaxLevel)
                ECommons.Automation.Chat.Instance.SendMessage("/lsync");
        }
    }
}
