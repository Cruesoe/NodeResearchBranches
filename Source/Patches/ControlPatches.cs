using BetterResearchMenu;
using UnityEngine;
using Verse;

namespace NodeResearchBranches.Patches
{
    /// <summary>Node Research's physics button and force sliders stay visible but greyed out and inert.</summary>
    public static class ControlPatches
    {
        // Matches Node Research's DrawGraphControls: 24px button, then three 22px sliders 6px apart.
        private const float ButtonSize = 24f;
        private const float SliderTop = ButtonSize + 6f;
        private const float SliderBlockHeight = 3 * 22f + 2 * 6f;

        public struct Saved
        {
            public bool physics;
            public float center, spacing, contracting;
        }

        public static void Prefix(Rect controlAreaRect, out Saved __state)
        {
            var s = BetterResearchMenuMod.settings;
            __state = new Saved { physics = s.physicsEnabled, center = s.centerForceMultiplier, spacing = s.spacingForceMultiplier, contracting = s.contractingForceMultiplier };
            s.physicsEnabled = false;

            // Swallow clicks so the button and sliders never change or save Node Research's settings.
            var ev = Event.current;
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag || ev.type == EventType.MouseUp)
                && (ButtonRect(controlAreaRect).Contains(ev.mousePosition) || SliderRect(controlAreaRect).Contains(ev.mousePosition)))
                ev.Use();
        }

        public static void Postfix(Rect controlAreaRect, Saved __state)
        {
            var s = BetterResearchMenuMod.settings;
            s.physicsEnabled = __state.physics;
            s.centerForceMultiplier = __state.center;
            s.spacingForceMultiplier = __state.spacing;
            s.contractingForceMultiplier = __state.contracting;

            var buttonRect = ButtonRect(controlAreaRect);
            var sliderRect = SliderRect(controlAreaRect);
            Widgets.DrawBoxSolid(sliderRect, new Color(0.06f, 0.08f, 0.1f, 0.6f));
            TooltipHandler.TipRegion(buttonRect, "NRB_PhysicsReplaced".Translate());
            TooltipHandler.TipRegion(sliderRect, "NRB_PhysicsReplaced".Translate());
        }

        private static Rect ButtonRect(Rect area) => new Rect(area.x, area.y, ButtonSize, ButtonSize);

        private static Rect SliderRect(Rect area) => new Rect(area.x, area.y + SliderTop, area.width, SliderBlockHeight);
    }
}
