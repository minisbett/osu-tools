// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch.Difficulty;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Taiko.Difficulty;
using osu.Game.Scoring;

namespace PerformanceCalculator.Performance
{
    [Command(Name = "minimal-score", Description = "Computes the performance (pp) of an online score and outputs it in a minimal format.")]
    public class MinimalScorePerformanceCommand : ApiCommand
    {
        [Argument(0, "score-id", "The score's online ID.")]
        public ulong ScoreId { get; set; }

        public override void Execute()
        {
            base.Execute();

            SoloScoreInfo apiScore = GetJsonFromApi<SoloScoreInfo>($"scores/{ScoreId}");
            APIBeatmap apiBeatmap = GetJsonFromApi<APIBeatmap>($"beatmaps/lookup?id={apiScore.BeatmapID}");

            var ruleset = LegacyHelper.GetRulesetFromLegacyID(apiScore.RulesetID);
            var workingBeatmap = ProcessorWorkingBeatmap.FromFileOrId(apiScore.BeatmapID.ToString());
            var score = apiScore.ToScoreInfo(apiScore.Mods.Select(m => m.ToMod(ruleset)).ToArray(), apiBeatmap);
            score.Ruleset = ruleset.RulesetInfo;
            score.BeatmapInfo!.Metadata = new BeatmapMetadata
            {
                Title = apiBeatmap.Metadata.Title,
                Artist = apiBeatmap.Metadata.Artist,
                Author = new RealmUser { Username = apiBeatmap.Metadata.Author.Username },
            };

            DifficultyAttributes attributes = ruleset.CreateDifficultyCalculator(workingBeatmap).Calculate(score.Mods);
            PerformanceAttributes performanceAttributes = ruleset.CreatePerformanceCalculator().Calculate(score, attributes);

            Console.WriteLine(attributes.StarRating + " " + performanceAttributes.Total);
        }
    }
}
