using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// Verse-free alert mapping: alert class name -> what resolves it. The culprit extraction stays
    /// in AlertRpc (engine-bound); the mapping is unit-tested in RimBridge.Tests.
    /// </summary>
    public static class AlertLogic
    {
        public struct Suggestion
        {
            public string Action;
            public string Via;
        }

        static readonly Dictionary<string, (string action, string via)> Map = new Dictionary<string, (string, string)>
        {
            ["Alert_ColonistNeedsRescuing"] = ("rescue the downed pawn", "decision.candidates mode=triage"),
            ["Alert_LifeThreateningHediff"] = ("tend now + queue surgery if needed", "decision.candidates mode=triage, medical.options"),
            ["Alert_FireInHomeArea"] = ("beat out the fires", "decision.candidates mode=emergency"),
            ["Alert_MajorOrExtremeBreakRisk"] = ("fix mood now (joy, drugs policy, chat)", "state.pawn_context break_risk"),
            ["Alert_MinorBreakRisk"] = ("watch mood, schedule joy", "state.pawn_context"),
            ["Alert_ColonistsIdle"] = ("assign work", "decision.candidates mode=work"),
            ["Alert_CaravanIdle"] = ("give the caravan a destination", "world.caravan"),
            ["Alert_StarvationColonists"] = ("emergency food: harvest, hunt, trade", "state.colony_context shortages"),
            ["Alert_LowFood"] = ("grow/buy food", "state.colony_context"),
            ["Alert_LowMedicine"] = ("buy or harvest herbal medicine", "state.stocks"),
            ["Alert_NeedDoctor"] = ("enable Doctor work on best Medicine pawn", "ui.set_work, state.work_matrix"),
            ["Alert_NeedResearchProject"] = ("pick research", "ui.set_research"),
            ["Alert_NeedBatteries"] = ("build batteries", "ui.build"),
            ["Alert_NeedDefenses"] = ("build turrets/traps", "ui.build"),
            ["Alert_NeedColonistBeds"] = ("build beds", "ui.build"),
            ["Alert_NeedWarmClothes"] = ("make parkas/tuques", "ui.add_bill"),
            ["Alert_NeedMealSource"] = ("fuel a stove + add cooking bill", "ui.add_bill"),
            ["Alert_NeedJoySources"] = ("build horseshoes/chess horizons", "ui.build"),
            ["Alert_PasteDispenserNeedsHopper"] = ("load hopper with raw food", "ui.order haul"),
            ["Alert_AwaitingMedicalOperation"] = ("ensure doctor + medicine + bed", "medical.bills"),
            ["Alert_QuestExpiresSoon"] = ("accept or finish the quest", "quest.detail, ui.letter"),
            ["Alert_BestowerWaiting"] = ("send pawn to bestower", "ui.order"),
            ["Alert_ColonistLeftUnburied"] = ("bury in grave/sarcophagus", "ui.build, ui.order"),
            ["Alert_ToxicBuildup"] = ("roof the pawn or move indoors", "ui.goto"),
            ["Alert_Hypothermia"] = ("warm clothes + heat", "ui.set_policies, ui.build"),
            ["Alert_Heatstroke"] = ("cool clothes + cooler", "ui.set_policies, ui.build"),
            ["Alert_Exhaustion"] = ("let them sleep", "ui.set_schedule"),
            ["Alert_Boredom"] = ("joy sources", "ui.build"),
            ["Alert_ImmobileCaravan"] = ("drop mass or rescue downed carriers", "world.caravan"),
            ["Alert_SlavesUnsuppressed"] = ("suppress or reduce expectations", "ui.prisoner"),
            ["Alert_SlaveRebellionLikely"] = ("suppress now", "ui.prisoner"),
            ["Alert_TatteredApparel"] = ("make or buy clothes", "ui.add_bill"),
            ["Alert_UnhappyNudity"] = ("adjust apparel policy", "ui.set_policies"),
            ["Alert_NeedResearchBench"] = ("build a research bench", "ui.build"),
            ["Alert_AnimalPenNeeded"] = ("build a pen", "ui.build"),
            ["Alert_AnimalPenNotEnclosed"] = ("close the pen", "ui.build"),
            ["Alert_PennedAnimalHungry"] = ("haul food to the pen", "ui.order haul"),
            ["Alert_StarvationAnimals"] = ("feed or slaughter", "ui.order"),
            ["Alert_PredatorInPen"] = ("hunt it", "decision.candidates"),
            ["Alert_HunterHasShieldAndRangedWeapon"] = ("swap loadout", "ui.job Equip"),
            ["Alert_BrawlerHasRangedWeapon"] = ("swap loadout", "ui.job Equip"),
            ["Alert_RolesEmpty"] = ("assign ideology roles", "ideo.detail"),
            ["Alert_AnimaLinkingReady"] = ("link at the anima tree", "ui.order"),
            ["Alert_MechDamaged"] = ("repair the mech", "decision.candidates"),
            ["Alert_NeedMechChargers"] = ("build mech chargers", "ui.build"),
            ["Alert_LowHemogen"] = ("hemogen farm or transfuse", "state.stocks"),
            ["Alert_TimedRaidsArriving"] = ("draft and position", "ui.draft, decision.candidates mode=combat"),
            ["Alert_NeedMeditationSpot"] = ("place a meditation spot", "ui.build"),
            ["Alert_AnimalFilth"] = ("assign cleaning", "ui.set_work"),
            ["Alert_JoyBuildingNoChairs"] = ("place stools nearby", "ui.build"),
            ["Alert_ChessTableNoChairs"] = ("place stools nearby", "ui.build"),
            ["Alert_PokerTableNoChairs"] = ("place stools nearby", "ui.build"),
        };

        public static Suggestion Suggest(string alertClass)
        {
            if (alertClass != null && Map.TryGetValue(alertClass, out var s))
                return new Suggestion { Action = s.action, Via = s.via };
            return new Suggestion { Action = "investigate the culprits", Via = "ui.select" };
        }
    }
}
