using UnityEngine;
using Verse;

namespace NodeResearchBranches
{
    public class BranchesSettings : ModSettings
    {
        public const int DefaultMaxFanRows = 6;
        public int maxFanRows = DefaultMaxFanRows;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref maxFanRows, "maxFanRows", DefaultMaxFanRows);
        }
    }

    public class BranchesMod : Mod
    {
        public static BranchesSettings Settings = null!;

        public BranchesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BranchesSettings>();
        }

        public override string SettingsCategory() => "Node Research: Branches";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("NRB_MaxFanRows".Translate(Settings.maxFanRows), tooltip: "NRB_MaxFanRowsDesc".Translate());
            Settings.maxFanRows = Mathf.RoundToInt(list.Slider(Settings.maxFanRows, 3f, 20f));
            if (list.ButtonText("NRB_ResetDefault".Translate()))
                Settings.maxFanRows = BranchesSettings.DefaultMaxFanRows;
            list.End();
        }
    }
}
