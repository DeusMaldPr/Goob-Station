// SPDX-FileCopyrightText: 2023 Colin-Tel <113523727+Colin-Tel@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Flareguy <78941145+Flareguy@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 ShadowCommander <10494922+ShadowCommander@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 themias <89101928+themias@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Aiden <aiden@djkraz.com>
// SPDX-FileCopyrightText: 2024 Aidenkrz <aiden@djkraz.com>
// SPDX-FileCopyrightText: 2024 Crotalus <Crotalus@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Ed <96445749+TheShuEd@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Hreno <hrenor@gmail.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2024 Tayrtahn <tayrtahn@gmail.com>
// SPDX-FileCopyrightText: 2024 deltanedas <39013340+deltanedas@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2024 lzk <124214523+lzk228@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 nikthechampiongr <32041239+nikthechampiongr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 username <113782077+whateverusername0@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 whateverusername0 <whateveremail>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Conchelle <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Killerqu00 <47712032+Killerqu00@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 SX-7 <92227810+SX-7@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 SX-7 <sn1.test.preria.2002@gmail.com>
// SPDX-FileCopyrightText: 2025 ScarKy0 <106310278+ScarKy0@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.GameTicking;
using Content.Server.Shuttles.Systems;
using Content.Shared.Cuffs.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Content.Shared.Objectives.Systems;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;
using System.Text;
using Content.Goobstation.Common.CCVar;
using Content.Goobstation.Common.ServerCurrency;
using Content.Server.Objectives.Commands;
using Content.Shared.CCVar;
using Content.Shared.Prototypes;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;
using Content.Shared.Administration.Logs;

namespace Content.Server.Objectives;

// heavily edited by goobstation contributor gang
// if you wanna upstream something think twice
public sealed class ObjectivesSystem : SharedObjectivesSystem
{
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EmergencyShuttleSystem _emergencyShuttle = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly ICommonCurrencyManager _currencyMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;

    private IEnumerable<string>? _objectives;

    private bool _showGreentext;

    private int _goobcoinsPerGreentext = 5;
    private int _goobcoinsServerMultiplier = 1;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);

        Subs.CVar(_cfg, CCVars.GameShowGreentext, value => _showGreentext = value, true);

        _prototypeManager.PrototypesReloaded += CreateCompletions;
        Subs.CVar(_cfg, GoobCVars.GoobcoinsPerGreentext, value => _goobcoinsPerGreentext = value, true);
        Subs.CVar(_cfg, GoobCVars.GoobcoinServerMultiplier, value => _goobcoinsServerMultiplier = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _prototypeManager.PrototypesReloaded -= CreateCompletions;
    }

    /// <summary>
    /// Adds objective text for each game rule's players on round end.
    /// </summary>
    private void OnRoundEndText(RoundEndTextAppendEvent ev)
    {
        // go through each gamerule getting data for the roundend summary.
        var summaries = new Dictionary<string, Dictionary<string, Dictionary<string, List<(EntityUid, string)>>>>();
        var query = EntityQueryEnumerator<GameRuleComponent>();
        while (query.MoveNext(out var uid, out var gameRule))
        {
            if (!_gameTicker.IsGameRuleAdded(uid, gameRule))
                continue;

            var info = new ObjectivesTextGetInfoEvent(new List<(EntityUid, string)>(), string.Empty);
            RaiseLocalEvent(uid, ref info);
            if (info.Minds.Count == 0)
                continue;

            // first group the gamerules by their agents, for example 2 different dragons
            var agent = info.Faction ?? info.AgentName;
            if (!summaries.ContainsKey(agent))
                summaries[agent] = new Dictionary<string, Dictionary<string, List<(EntityUid, string)>>>();

            // next group them by agent names, for example different traitors, blood brother teams, etc.
            if (!summaries[agent].ContainsKey(info.AgentName))
                summaries[agent][info.AgentName] = new Dictionary<string, List<(EntityUid, string)>>();

            var prepend = new ObjectivesTextPrependEvent("");
            RaiseLocalEvent(uid, ref prepend);

            // next group them by their prepended texts
            // for example with traitor rule, group them by the codewords they share
            var summary = summaries[agent][info.AgentName];
            if (summary.ContainsKey(prepend.Text))
            {
                // same prepended text (usually empty) so combine them
                summary[prepend.Text].AddRange(info.Minds);
            }
            else
            {
                summary[prepend.Text] = info.Minds;
            }
        }

        // convert the data into summary text
        foreach (var (faction, summariesFaction) in summaries)
        {
            foreach (var (agent, summary) in summariesFaction)
            {
                // first get the total number of players that were in these game rules combined
                var total = 0;
                var totalInCustody = 0;
                foreach (var (_, minds) in summary)
                {
                    total += minds.Count;
                    totalInCustody += minds.Where(pair => IsInCustody(pair.Item1)).Count();
                }

                var result = new StringBuilder();
                result.AppendLine(Loc.GetString("objectives-round-end-result", ("count", total), ("agent", agent)));
                if (agent == Loc.GetString("traitor-round-end-agent-name"))
                {
                    result.AppendLine(Loc.GetString("objectives-round-end-result-in-custody", ("count", total), ("custody", totalInCustody), ("agent", agent)));
                }
                // next add all the players with its own prepended text
                foreach (var (prepend, minds) in summary)
                {
                    if (prepend != string.Empty)
                        result.Append(prepend);

                    // add space between the start text and player list
                    result.AppendLine();

                    AddSummary(result, agent, minds);
                }

                ev.AddLine(result.AppendLine().ToString());
            }
        }
    }

    private void AddSummary(StringBuilder result, string agent, List<(EntityUid, string)> minds)
    {
        var agentSummaries = new List<(string summary, float successRate, int completedObjectives)>();

        foreach (var (mindId, name) in minds)
        {
            if (!TryComp<MindComponent>(mindId, out var mind))
                continue;

            var userid = mind.OriginalOwnerUserId;
            var title = GetTitle((mindId, mind), name);
            var custody = IsInCustody(mindId, mind) ? Loc.GetString("objectives-in-custody") : string.Empty;

            var objectives = mind.Objectives;
            if (objectives.Count == 0)
            {
                agentSummaries.Add((Loc.GetString("objectives-no-objectives", ("custody", custody), ("title", title), ("agent", agent)), 0f, 0));
                continue;
            }

            var completedObjectives = 0;
            var totalObjectives = 0;
            var agentSummary = new StringBuilder();
            agentSummary.AppendLine(Loc.GetString("objectives-with-objectives", ("custody", custody), ("title", title), ("agent", agent)));

            foreach (var objectiveGroup in objectives.GroupBy(o => Comp<ObjectiveComponent>(o).LocIssuer))
            {
                //TO DO:
                //check for the right group here. Getting the target issuer is easy: objectiveGroup.Key
                //It should be compared to the type of the group's issuer.
                agentSummary.AppendLine(objectiveGroup.Key);

                foreach (var objective in objectiveGroup)
                {
                    var info = GetInfo(objective, mindId, mind);
                    if (info == null)
                        continue;

                    var objectiveTitle = info.Value.Title;
                    var progress = info.Value.Progress;
                    totalObjectives++;

                    // Goob (even tho the entire file got massacred by John already)
                    // Logging objective status for admins
                    IFormattable? username = ToPrettyString(mind.CurrentEntity);
                    if (username is null &&
                        userid.HasValue &&
                        _player.TryGetPlayerData(userid.Value, out var data))
                        username = System.Runtime.CompilerServices.FormattableStringFactory.Create(data.UserName);

                    _adminLog.Add(Shared.Database.LogType.AntagObjective,
                                    Shared.Database.LogImpact.Low,
                                    $"{username:subject} achieved {progress}% of objective {objectiveTitle}");

                    agentSummary.Append("- ");
                    if (!_showGreentext)
                    {
                        agentSummary.AppendLine(objectiveTitle);
                    }
                    else if (progress > 0.99f)
                    {
                        agentSummary.AppendLine(Loc.GetString(
                            "objectives-objective-success",
                            ("objective", objectiveTitle),
                            ("progress", progress)
                        ));
                        completedObjectives++;

                        // Easiest place to give people points for completing objectives lol
                        if(userid.HasValue)
                            _currencyMan.AddCurrency(userid.Value, _goobcoinsPerGreentext * _goobcoinsServerMultiplier);
                    }
                    else if (progress <= 0.99f && progress >= 0.5f)
                    {
                        agentSummary.AppendLine(Loc.GetString(
                            "objectives-objective-partial-success",
                            ("objective", objectiveTitle),
                            ("progress", progress)
                        ));
                    }
                    else if (progress < 0.5f && progress > 0f)
                    {
                        agentSummary.AppendLine(Loc.GetString(
                            "objectives-objective-partial-failure",
                            ("objective", objectiveTitle),
                            ("progress", progress)
                        ));
                    }
                    else
                    {
                        agentSummary.AppendLine(Loc.GetString(
                            "objectives-objective-fail",
                            ("objective", objectiveTitle),
                            ("progress", progress)
                        ));
                    }
                }
            }

            var successRate = totalObjectives > 0 ? (float)completedObjectives / totalObjectives : 0f;
            agentSummaries.Add((agentSummary.ToString(), successRate, completedObjectives));
        }

        var sortedAgents = agentSummaries.OrderByDescending(x => x.successRate)
                                       .ThenByDescending(x => x.completedObjectives);

        foreach (var (summary, _, _) in sortedAgents)
        {
            result.AppendLine(summary);
        }
    }

    public EntityUid? GetRandomObjective(EntityUid mindId, MindComponent mind, ProtoId<WeightedRandomPrototype> objectiveGroupProto, float maxDifficulty)
    {
        if (!_prototypeManager.TryIndex(objectiveGroupProto, out var groupsProto))
        {
            Log.Error($"Tried to get a random objective, but can't index WeightedRandomPrototype {objectiveGroupProto}");
            return null;
        }

        // Make a copy of the weights so we don't trash the prototype by removing entries
        var groups = groupsProto.Weights.ShallowClone();

        while (_random.TryPickAndTake(groups, out var groupName))
        {
            if (!_prototypeManager.TryIndex<WeightedRandomPrototype>(groupName, out var group))
            {
                Log.Error($"Couldn't index objective group prototype {groupName}");
                return null;
            }

            var objectives = group.Weights.ShallowClone();
            while (_random.TryPickAndTake(objectives, out var objectiveProto))
            {
                if (TryCreateObjective((mindId, mind), objectiveProto, out var objective)
                    && Comp<ObjectiveComponent>(objective.Value).Difficulty <= maxDifficulty)
                    return objective;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether a target is considered 'in custody' (cuffed on the shuttle).
    /// </summary>
    private bool IsInCustody(EntityUid mindId, MindComponent? mind = null)
    {
        if (!Resolve(mindId, ref mind))
            return false;

        // Ghosting will not save you
        bool originalEntityInCustody = false;
        EntityUid? originalEntity = GetEntity(mind.OriginalOwnedEntity);
        if (originalEntity.HasValue && originalEntity != mind.OwnedEntity)
        {
            originalEntityInCustody = TryComp<CuffableComponent>(originalEntity, out var origCuffed) && origCuffed.CuffedHandCount > 0
                   && _emergencyShuttle.IsTargetEscaping(originalEntity.Value);
        }

        return originalEntityInCustody || (TryComp<CuffableComponent>(mind.OwnedEntity, out var cuffed) && cuffed.CuffedHandCount > 0
               && _emergencyShuttle.IsTargetEscaping(mind.OwnedEntity.Value));
    }

    /// <summary>
    /// Get the title for a player's mind used in round end.
    /// Pass in the original entity name which is shown alongside username.
    /// </summary>
    public string GetTitle(Entity<MindComponent?> mind, string name = "")
    {
        if (Resolve(mind, ref mind.Comp) &&
            mind.Comp.OriginalOwnerUserId != null &&
            _player.TryGetPlayerData(mind.Comp.OriginalOwnerUserId.Value, out var sessionData))
        {
            var username = sessionData.UserName;

            var nameWithJobMaybe = name;
            if (_job.MindTryGetJobName(mind, out var jobName))
                nameWithJobMaybe += ", " + jobName;

            return Loc.GetString("objectives-player-user-named", ("user", username), ("name", nameWithJobMaybe));
        }

        return Loc.GetString("objectives-player-named", ("name", name));
    }


    private void CreateCompletions(PrototypesReloadedEventArgs unused)
    {
        CreateCompletions();
    }

    /// <summary>
    /// Get all objective prototypes by their IDs.
    /// This is used for completions in <see cref="AddObjectiveCommand"/>
    /// </summary>
    public IEnumerable<string> Objectives()
    {
        if (_objectives == null)
            CreateCompletions();

        return _objectives!;
    }

    private void CreateCompletions()
    {
        _objectives = _prototypeManager.EnumeratePrototypes<EntityPrototype>()
            .Where(p => p.HasComponent<ObjectiveComponent>())
            .Select(p => p.ID)
            .Order();
    }
}

/// <summary>
/// Raised on the game rule to get info for any objectives.
/// If its minds list is set then the players will have their objectives shown in the round end text.
/// AgentName is the generic name for a player in the list.
/// </summary>
/// <remarks>
/// The objectives system already checks if the game rule is added so you don't need to check that in this event's handler.
/// </remarks>
[ByRefEvent]
public record struct ObjectivesTextGetInfoEvent(List<(EntityUid, string)> Minds, string AgentName, string? Faction = null);

/// <summary>
/// Raised on the game rule before text for each agent's objectives is added, letting you prepend something.
/// </summary>
[ByRefEvent]
public record struct ObjectivesTextPrependEvent(string Text);
