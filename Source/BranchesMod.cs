using UnityEngine;
using Verse;

namespace NodeResearchBranches
{
    public class BranchesSettings : ModSettings
    {
        public const int DefaultMaxFanRows = 6;
        public int maxFanRows = DefaultMaxFanRows;
        public bool hideCrossEraLines = true;
        public bool hideExtraLines = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref maxFanRows, "maxFanRows", DefaultMaxFanRows);
            Scribe_Values.Look(ref hideCrossEraLines, "hideCrossEraLines", true);
            Scribe_Values.Look(ref hideExtraLines, "hideExtraLines", false);
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
            list.Gap();
            list.CheckboxLabeled("NRB_HideCrossEraLines".Translate(), ref Settings.hideCrossEraLines, "NRB_HideCrossEraLinesDesc".Translate());
            list.CheckboxLabeled("NRB_HideExtraLines".Translate(), ref Settings.hideExtraLines, "NRB_HideExtraLinesDesc".Translate());
            list.End();
        }
    }
}
