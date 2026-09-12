// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using Humanizer;
using JetBrains.Annotations;
using McMaster.Extensions.CommandLineUtils;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Catch.Difficulty;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Taiko.Difficulty;
using osu.Game.Scoring;

namespace PerformanceCalculator.Simulate
{
    public abstract class SimulateCommand : ProcessorCommand
    {
        public abstract Ruleset Ruleset { get; }

        [UsedImplicitly]
        [Required]
        [Argument(0, Name = "beatmap", Description = "Required. Can be either a path to beatmap file (.osu) or beatmap ID.")]
        public string Beatmap { get; } = null!;

        [UsedImplicitly]
        [Option(Template = "-a|--accuracy <accuracy>", Description = "Accuracy. Enter as decimal 0-100. Defaults to 100. Scales hit results as well and is rounded to the nearest possible value for the beatmap.")]
        public double Accuracy { get; } = 100;

        [UsedImplicitly]
        [Option(CommandOptionType.MultipleValue, Template = "-m|--mod <mod>", Description = "One for each mod. The mods to compute the performance with. Values: hr, dt, hd, fl, etc...")]
        public string[] Mods { get; } = [];

        [UsedImplicitly]
        [Option(CommandOptionType.MultipleValue, Template = "-o|--mod-option <option>",
            Description = "The options of mods, with one for each setting. Specified as acryonym_settingkey=value. Example: DT_speed_change=1.35")]
        public string[] ModOptions { get; set; } = [];

        [UsedImplicitly]
        [Option(Template = "-X|--misses <misses>", Description = "Number of misses. Defaults to 0.")]
        public int Misses { get; }

        [UsedImplicitly]
        [Option(Template = "-l|--legacy-total-score <score>", Description = "Amount of legacy total score.")]
        public long? LegacyTotalScore { get; }

        //
        // Options implemented in the ruleset-specific commands
        // -> Catch renames Mehs/Goods to (tiny-)droplets
        // -> Mania does not have Combo
        // -> Taiko does not have Mehs
        //
        [UsedImplicitly]
        public virtual int? Mehs { get; }

        [UsedImplicitly]
        public virtual int? Goods { get; }

        [UsedImplicitly]
        public virtual int? Combo { get; }

        [UsedImplicitly]
        public virtual double PercentCombo { get; }

        public override void Execute()
        {
            var ruleset = Ruleset;

            var workingBeatmap = ProcessorWorkingBeatmap.FromFileOrId(Beatmap);
            var mods = ParseMods(ruleset, Mods, ModOptions);
            var beatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

            int beatmapMaxCombo = beatmap.GetMaxCombo();
            var statistics = GenerateHitResults(beatmap, mods);
            var scoreInfo = new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
            {
                Accuracy = GetAccuracy(beatmap, statistics, mods),
                MaxCombo = Combo ?? (int)Math.Round(PercentCombo / 100 * beatmapMaxCombo),
                Statistics = statistics,
                LegacyTotalScore = LegacyTotalScore,
                Mods = mods
            };

            var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
            var difficultyAttributes = difficultyCalculator.Calculate(mods);
            var timedDifficultyAttributes = difficultyCalculator.CalculateTimed(mods)[beatmap.HitObjects.Count / 2];
            var performanceCalculator = ruleset.CreatePerformanceCalculator();
            var performanceAttributes = performanceCalculator?.Calculate(scoreInfo, difficultyAttributes);

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string rulesetName = ruleset.ShortName is "fruits" ? "catch" : ruleset.ShortName;

            Type diffAttribsType = ruleset switch
            {
                OsuRuleset _ => typeof(OsuDifficultyAttributes),
                TaikoRuleset _ => typeof(TaikoDifficultyAttributes),
                CatchRuleset _ => typeof(CatchDifficultyAttributes),
                ManiaRuleset _ => typeof(ManiaDifficultyAttributes),
                _ => throw new InvalidOperationException("Unsupported ruleset.")
            };

            List<string> diffAttribs = [.. diffAttribsType.GetProperties().Where(x => x.Name is not "Mods").Select(property => $"{property.Name} = {property.GetValue(difficultyAttributes)}")];

            Console.WriteLine(
                $$"""
                  yield return new(
                      "beatmaps/{{rulesetName}}/{{System.IO.Path.GetFileName(Beatmap)}}",
                      {{(mods.Length is 0 ? "null" : $"\"{string.Join("", mods.Select(x => x.Acronym))}\"")}},
                      new Native{{rulesetName.Humanize()}}DifficultyAttributes(new()
                      {
                          {{string.Join(",\n        ", diffAttribs)}}
                      })
                  );
                  """);

            Console.WriteLine("\n\n");

            List<string> timedDiffAttribs = [.. diffAttribsType.GetProperties().Where(x => x.Name is not "Mods").Select(property => $"{property.Name} = {property.GetValue(timedDifficultyAttributes.Attributes)}")];

            Console.WriteLine(
                $$"""
                  yield return new(
                      "beatmaps/{{rulesetName}}/{{System.IO.Path.GetFileName(Beatmap)}}",
                      {{(mods.Length is 0 ? "null" : $"\"{string.Join("", mods.Select(x => x.Acronym))}\"")}},
                      {{beatmap.HitObjects.Count / 2}},
                      new NativeTimed{{rulesetName.Humanize()}}DifficultyAttributes(new({{timedDifficultyAttributes.Time}}, new {{rulesetName.Humanize()}}DifficultyAttributes
                      {
                          {{string.Join(",\n        ", timedDiffAttribs)}}
                      }))
                  );
                  """);

            Console.WriteLine("\n\n");

            Type perfAttribsType = ruleset switch
            {
                OsuRuleset _ => typeof(OsuPerformanceAttributes),
                TaikoRuleset _ => typeof(TaikoPerformanceAttributes),
                CatchRuleset _ => typeof(CatchPerformanceAttributes),
                ManiaRuleset _ => typeof(ManiaPerformanceAttributes),
                _ => throw new InvalidOperationException("Unsupported ruleset.")
            };

            List<string> perfAttribs = [.. perfAttribsType.GetProperties().Select(property => $"{property.Name} = {property.GetValue(performanceAttributes) ?? "null"}")];

            Console.WriteLine(
                $$"""
                  yield return new(
                      "beatmaps/{{rulesetName}}/{{System.IO.Path.GetFileName(Beatmap)}}",
                      {{(mods.Length is 0 ? "null" : $"\"{string.Join("", mods.Select(x => x.Acronym))}\"")}},
                      new NativeScoreInfo
                      {
                          MaxCombo = {{scoreInfo.MaxCombo}},
                          Accuracy = {{scoreInfo.Accuracy}},
                          LegacyTotalScore = {{scoreInfo.LegacyTotalScore?.ToString() ?? "null"}},
                          CountMiss = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.Miss)}},
                          CountMeh = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.Meh)}},
                          CountOk = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.Ok)}},
                          CountGood = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.Good)}},
                          CountGreat = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.Great)}},
                          CountPerfect = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.Perfect)}},
                          CountSmallTickMiss = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.SmallTickMiss)}},
                          CountSmallTickHit = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.SmallTickHit)}},
                          CountLargeTickMiss = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.LargeTickMiss)}},
                          CountLargeTickHit = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.LargeTickHit)}},
                          CountSliderTailHit = {{scoreInfo.Statistics.GetValueOrDefault(HitResult.SliderTailHit)}}
                      },
                      new Native{{rulesetName.Humanize()}}PerformanceAttributes(new()
                      {
                          {{string.Join(",\n        ", perfAttribs)}}
                      })
                  );
                  """);
        }

        protected abstract Dictionary<HitResult, int> GenerateHitResults(IBeatmap beatmap, Mod[] mods);

        protected virtual double GetAccuracy(IBeatmap beatmap, Dictionary<HitResult, int> statistics, Mod[] mods) => 0;
    }
}
