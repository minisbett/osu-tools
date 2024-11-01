// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Edit;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace PerformanceCalculatorGUI.Screens.ObjectInspection
{
    public partial class ManiaObjectInspectorRuleset : DrawableManiaEditorRuleset
    {
        private readonly ManiaDifficultyHitObject[] difficultyHitObjects;

        [Resolved]
        private ObjectDifficultyValuesContainer objectDifficultyValuesContainer { get; set; }

        private IBeatmap beatmap;

        public ManiaObjectInspectorRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods, ExtendedManiaDifficultyCalculator difficultyCalculator, double clockRate)
            : base(ruleset, beatmap, mods)
        {
            this.beatmap = beatmap;
            difficultyHitObjects = difficultyCalculator.GetDifficultyHitObjects(beatmap, clockRate)
                                                       .Cast<ManiaDifficultyHitObject>().ToArray();
        }

        public override bool PropagatePositionalInputSubTree => false;

        public override bool PropagateNonPositionalInputSubTree => false;

        public override bool AllowBackwardsSeeks => true;

        protected override Playfield CreatePlayfield() => new ManiaObjectInspectorPlayfield(beatmap);

        protected override void Update()
        {
            base.Update();
            objectDifficultyValuesContainer.CurrentDifficultyHitObject.Value = difficultyHitObjects.LastOrDefault(x => x.StartTime < Clock.CurrentTime);
        }

        private partial class ManiaObjectInspectorPlayfield : ManiaPlayfield
        {
            protected override GameplayCursorContainer CreateCursor() => null;

            public ManiaObjectInspectorPlayfield(IBeatmap beatmap) : base((beatmap as ManiaBeatmap).Stages)
            {
                DisplayJudgements.Value = false;
            }
        }
    }
}
