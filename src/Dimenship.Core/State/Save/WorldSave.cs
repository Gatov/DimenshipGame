using System.Text.Json;
using System.Text.Json.Serialization;
using Dimenship.Core.Content;
using Dimenship.Core.Planning;
using Dimenship.Core.Production;
using Dimenship.Core.Simulation;

namespace Dimenship.Core.State.Save;

/// <summary>Source-generated serialization for the save file, for the same reason content has one.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(SaveEnvelope))]
internal sealed partial class SaveJsonContext : JsonSerializerContext;

/// <summary>Why a save could not be loaded. Collected, so one load reports every problem it found.</summary>
public sealed record SaveError(string Path, string Message)
{
    public override string ToString() => Path.Length == 0 ? Message : $"{Path}: {Message}";
}

/// <summary>What a load produced. A partially rebuilt world is worse than none.</summary>
public sealed record SaveLoadResult(WorldState? State, IReadOnlyList<SaveError> Errors)
{
    public bool Succeeded => Errors.Count == 0 && State is not null;

    public static SaveLoadResult Failed(params SaveError[] errors) => new(null, errors);

    public static SaveLoadResult Failed(IReadOnlyList<SaveError> errors) => new(null, errors);
}

/// <summary>
/// Writes a world to text and reads one back.
/// <para>
/// The contract is that <b>(catalog, state) is sufficient</b>: an engine rebuilt from a loaded
/// state and the catalog it names behaves identically to the one that saved it. Anything that
/// breaks that is a missing field here, and the determinism test is what finds it.
/// </para>
/// <para>
/// Nothing the catalog can answer is written. A save carries ids and the deltas a campaign made,
/// so retitling a machine or rebalancing its work rate reaches a game already in progress. The
/// content-drift check is the other half of that bargain: a save naming an id the catalog no longer
/// has fails, listing every one, rather than quietly loading a vessel with a hole in it.
/// </para>
/// </summary>
public static class WorldSave
{
    /// <summary>
    /// The format's version. A newer save is refused; an older one runs the upgrader chain.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The chain, empty and present. Version 1 needs no upgraders — there is nothing older to
    /// upgrade from — but the seam exists from the first release, because retrofitting it later
    /// means guessing what a version-1 save looked like from whatever survived.
    /// </summary>
    public static IReadOnlyList<ISaveUpgrader> Upgraders { get; } = Array.Empty<ISaveUpgrader>();

    public static string Write(ContentCatalog catalog, WorldState state) =>
        JsonSerializer.Serialize(
            new SaveEnvelope
            {
                SaveVersion = CurrentVersion,
                ContentVersion = catalog.ContentVersion,
                SavedAtTick = state.Clock.Tick,
                State = Capture(state),
            },
            SaveJsonContext.Default.SaveEnvelope);

    /// <summary>
    /// Reads a world back. The scenario is looked up by the id the save pins rather than restored
    /// from it, which is what lets an edited layout reach an existing campaign.
    /// </summary>
    public static SaveLoadResult Read(
        string json, ContentCatalog catalog, IReadOnlyList<Scenario> scenarios)
    {
        SaveEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(json, SaveJsonContext.Default.SaveEnvelope);
        }
        catch (JsonException failure)
        {
            return SaveLoadResult.Failed(new SaveError(failure.Path ?? string.Empty, failure.Message));
        }

        if (envelope?.State is not { } dto)
        {
            return SaveLoadResult.Failed(new SaveError(string.Empty, "the save carries no world."));
        }

        if (envelope.SaveVersion is not { } version)
        {
            return SaveLoadResult.Failed(new SaveError("saveVersion", "is required."));
        }

        if (version > CurrentVersion)
        {
            // Refused rather than attempted. A newer file was written by a build that knew things
            // this one does not, and a best-effort read of it would silently drop them.
            return SaveLoadResult.Failed(new SaveError(
                "saveVersion",
                $"this save is version {version} and this build reads up to {CurrentVersion}. " +
                "It was written by a newer version of the game."));
        }

        if (version < CurrentVersion)
        {
            foreach (var upgrader in Upgraders.Where(u => u.From >= version).OrderBy(u => u.From))
            {
                dto = upgrader.Upgrade(dto);
            }
        }

        var errors = new List<SaveError>();

        if (envelope.ContentVersion != catalog.ContentVersion)
        {
            // Reported, not absorbed. Drift may be harmless and may not, and the loader is not the
            // place that gets to decide — what it can do is say exactly what changed underneath.
            errors.Add(new SaveError(
                "contentVersion",
                $"this save was made against content '{envelope.ContentVersion}' and the catalog " +
                $"loaded is '{catalog.ContentVersion}'."));
        }

        var scenario = scenarios.FirstOrDefault(s => s.Id == dto.ScenarioId);
        if (scenario is null)
        {
            errors.Add(new SaveError(
                "scenarioId", $"no scenario '{dto.ScenarioId}' in the content loaded."));
        }

        var state = Restore(dto, errors);
        if (state is null || errors.Count > 0)
        {
            return SaveLoadResult.Failed(errors);
        }

        CheckDrift(state, catalog, errors);

        return errors.Count > 0 ? SaveLoadResult.Failed(errors) : new SaveLoadResult(state, errors);
    }

    // ---- capture -----------------------------------------------------------------------------

    private static WorldStateDto Capture(WorldState state) => new()
    {
        ScenarioId = state.ScenarioId,
        Clock = new ClockDto
        {
            Tick = state.Clock.Tick,
            AutoPauseOnCriticalAlert = state.Clock.AutoPauseOnCriticalAlert,
        },
        RandomStreams = state.Random.Streams.ToArray(),
        Vessel = new VesselDto
        {
            Hold = state.Vessel.Hold.Value,
            Storages = state.Vessel.Storages.Select(s => new StorageDto
            {
                Id = s.Id.Value,
                Archetype = s.Archetype.Value,
                NameOverride = s.NameOverride,
                Stock = s.Stock
                    .Select(x => new StoredItemDto { Item = x.Item.Value, Amount = x.Amount })
                    .ToList(),
            }).ToList(),
            Facilities = state.Vessel.Facilities.Select(f => new FacilityDto
            {
                Id = f.Id.Value,
                Archetype = f.Archetype.Value,
                NameOverride = f.NameOverride,
                LocalStorage = f.LocalStorage.Value,
                Built = f.Built,
                WorkRatePermille = f.WorkRatePermille,
                EnergyEfficiencyPermille = f.EnergyEfficiencyPermille,
                IntegrityPermille = f.IntegrityPermille,
                Configured = f.Configured?.Value,
                SwitchOverRemaining = f.SwitchOverRemaining,
                SwitchTarget = f.SwitchTarget?.Value,
                Queue = f.Queue.Select(t => t.Value).ToList(),
                Current = f.Current?.Value,
                Programs = f.Programs.Select(p => p.Value).ToList(),
                Status = f.Status.ToString(),
                BlockReason = f.BlockReason?.ToString(),
                PowerDrawLastTick = f.PowerDrawLastTick,
                Utilization = Capture(f.Utilization),
            }).ToList(),
            Transports = state.Vessel.Transports.Select(t => new TransportDto
            {
                Id = t.Id.Value,
                Archetype = t.Archetype.Value,
                NameOverride = t.NameOverride,
                From = t.From.Value,
                To = t.To.Value,
                Built = t.Built,
                ThroughputPermille = t.ThroughputPermille,
                Queue = t.Queue.Select(q => q.Value).ToList(),
                Current = t.Current?.Value,
                MovedLastTick = t.MovedLastTick,
                PowerDrawLastTick = t.PowerDrawLastTick,
                Status = t.Status.ToString(),
                BlockReason = t.BlockReason?.ToString(),
            }).ToList(),
            Reactors = state.Vessel.Reactors.Select(r => new ReactorDto
            {
                Id = r.Id.Value,
                Archetype = r.Archetype.Value,
                NameOverride = r.NameOverride,
                Built = r.Built,
                IntegrityPermille = r.IntegrityPermille,
                FuelStore = r.FuelStore.Value,
                OutputPermille = r.OutputPermille,
                Programs = r.Programs.Select(p => p.Value).ToList(),
                Status = r.Status.ToString(),
                BlockReason = r.BlockReason?.ToString(),
                Utilization = Capture(r.Utilization),
            }).ToList(),
            Sinks = state.Vessel.Sinks.Select(s => s.Value).ToList(),
            Energy = new BudgetDto
            {
                Capacity = state.Vessel.Energy.Capacity,
                DrawLastTick = state.Vessel.Energy.DrawLastTick,
                CapHits = state.Vessel.Energy.CapHits,
                StarvedTicks = state.Vessel.Energy.StarvedTicks,
            },
            Compute = new BudgetDto
            {
                Capacity = state.Vessel.Compute.Capacity,
                DrawLastTick = state.Vessel.Compute.DrawLastTick,
                CapHits = state.Vessel.Compute.CapHits,
                StarvedTicks = state.Vessel.Compute.StarvedTicks,
            },
            Reservations = state.Vessel.Reservations.Held.Select(r => new ReservationDto
            {
                Storage = r.Storage.Value,
                Item = r.Item.Value,
                Quantity = r.Quantity,
                Owner = r.Owner.Value,
            }).ToList(),
        },
        Tasks = new TasksDto
        {
            NextTaskId = state.Tasks.NextTaskId,
            Production = state.Tasks.Production.Select(t => new ProductionTaskDto
            {
                Id = t.Id.Value,
                Schematic = t.SchematicId.Value,
                RequestedRuns = t.RequestedRuns,
                Executor = t.ExecutorId.Value,
                State = t.State.ToString(),
                CompletedRuns = t.CompletedRuns,
                RunActive = t.RunActive,
                RunAwaitingDeposit = t.RunAwaitingDeposit,
                WorkDoneThisRun = t.WorkDoneThisRun,
                EnergyChargedThisRun = t.EnergyChargedThisRun,
                LastReason = t.LastReason?.ToString(),
                PostponedAtTick = t.PostponedAtTick,
                History = Capture(t.History),
            }).ToList(),
            Transport = state.Tasks.Transport.Select(t => new TransportTaskDto
            {
                Id = t.Id.Value,
                Item = t.Item.Value,
                RequestedQuantity = t.RequestedQuantity,
                Executor = t.ExecutorId.Value,
                Source = t.Source.Value,
                Destination = t.Destination.Value,
                State = t.State.ToString(),
                MovedQuantity = t.MovedQuantity,
                LastReason = t.LastReason?.ToString(),
                PostponedAtTick = t.PostponedAtTick,
                History = Capture(t.History),
            }).ToList(),
            Retired = state.Tasks.Retired.Select(t => t.Value).ToList(),
        },
        Progress = new ProgressDto
        {
            // Sorted, because a set has no order of its own and writing one in hash order would
            // make two saves of one world differ byte for byte.
            UnlockedSchematics = state.Progress.UnlockedSchematics
                .Select(s => s.Value).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            DiscoveredItems = state.Progress.DiscoveredItems
                .Select(s => s.Value).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            Flags = state.Progress.Flags.OrderBy(s => s, StringComparer.Ordinal).ToList(),
        },
        Plans = new PlansDto
        {
            NextPlanId = state.Plans.NextPlanId,
            Plans = state.Plans.Plans.Select(p => new PlanDto
            {
                Id = p.Id.Value,
                GoalItem = p.Goal.Item.Value,
                GoalQuantity = p.Goal.Quantity,
                CommittedAtTick = p.CommittedAtTick,
                SpawnedTasks = p.SpawnedTasks.Select(t => t.Value).ToList(),
                Shortages = p.Shortages.Select(s => new ShortageDto
                {
                    Item = s.Item.Value,
                    Missing = s.Missing,
                    Kind = s.Kind.ToString(),
                }).ToList(),
                CompletedTasks = p.CompletedTasks,
                State = p.State.ToString(),
            }).ToList(),
        },
        Missions = new MissionsDto
        {
            NextMissionId = state.Missions.NextMissionId,
            Missions = state.Missions.Missions.Select(m => new MissionDto
            {
                Id = m.Id.Value,
                Target = m.Target.Value,
                Kind = m.Kind.ToString(),
                Phase = m.Phase.ToString(),
                Dock = m.Dock.Value,
                Group = m.Group.Select(r => r.Value).ToList(),
                DepartedAtTick = m.DepartedAtTick,
                ArrivesAtTick = m.ArrivesAtTick,
                Manifest = m.Manifest
                    .Select(a => new ItemAmountDto { Item = a.Item.Value, Quantity = a.Quantity })
                    .ToList(),
                Destination = m.Destination.Value,
                ForPlan = m.ForPlan?.Value,
                RngStream = m.RngStream,
            }).ToList(),
        },
        Alerts = new AlertsDto
        {
            NextAlertId = state.Alerts.NextAlertId,
            Alerts = state.Alerts.Alerts.Select(a => new AlertDto
            {
                Id = a.Id.Value,
                Severity = a.Severity.ToString(),
                Code = a.Code.ToString(),
                SubjectId = a.SubjectId,
                RaisedAtTick = a.RaisedAtTick,
                RootCause = a.RootCause?.ToString(),
                Acknowledged = a.Acknowledged,
                Pinned = a.Pinned,
            }).ToList(),
        },
        Journal = new JournalDto
        {
            TotalEmitted = state.Journal.TotalEmitted,
            Events = state.Journal.Events.Select(e => new EventDto
            {
                Tick = e.Tick,
                Category = e.Category.ToString(),
                Code = e.Code.ToString(),
                Subject = e.Subject,
                Data = e.Data
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
            }).ToList(),
        },
        NextProgramInstanceId = state.Programs.NextInstanceId,
        Robots = new RobotsDto
        {
            NextRobotId = state.Robots.NextRobotId,
            Robots = state.Robots.Robots.Select(r => new RobotDto
            {
                Id = r.Id.Value,
                Frame = r.Frame.Value,
                NameOverride = r.NameOverride,
                Sockets = r.Sockets.Select(socket => new RobotSocketDto
                {
                    Socket = socket.Socket.Value,
                    Storage = socket.Storage.Value,
                }).ToList(),
                IntegrityPermille = r.IntegrityPermille,
                Group = r.Group?.Value,
                OnMission = r.OnMission?.Value,
            }).ToList(),
        },
    };

    private static UtilizationDto Capture(UtilizationWindow window) => new()
    {
        WindowTicks = window.WindowTicks,
        BucketTicks = window.BucketTicks,
        Head = window.Head,
        Measured = window.Measured,
        Working = window.Working.ToArray(),
        Idle = window.Idle.ToArray(),
        WaitingInput = window.WaitingInput.ToArray(),
        WaitingOutput = window.WaitingOutput.ToArray(),
        Throttled = window.Throttled.ToArray(),
        SwitchingOver = window.SwitchingOver.ToArray(),
    };

    private static List<AttemptDto> Capture(IReadOnlyList<TaskAttempt> history) =>
        history.Select(a => new AttemptDto
        {
            Tick = a.Tick,
            Outcome = a.Outcome.ToString(),
            Reason = a.Reason?.ToString(),
        }).ToList();

    // ---- restore -----------------------------------------------------------------------------

    private static WorldState? Restore(WorldStateDto dto, List<SaveError> errors)
    {
        if (dto.Clock is not { } clock || dto.Vessel is not { } vesselDto
            || dto.Tasks is not { } tasksDto || dto.RandomStreams is not { } streams)
        {
            errors.Add(new SaveError("state", "is missing one of its required branches."));
            return null;
        }

        var vessel = new VesselState
        {
            Hold = new StorageId(vesselDto.Hold ?? string.Empty),
            Energy = Budget(vesselDto.Energy),
            Compute = Compute(vesselDto.Compute),
            Reservations = new ReservationLedger(),
        };

        foreach (var s in vesselDto.Storages ?? Array.Empty<StorageDto>())
        {
            var storage = new StorageInstance
            {
                Id = new StorageId(s.Id ?? string.Empty),
                Archetype = new StorageArchetypeId(s.Archetype ?? string.Empty),
                NameOverride = s.NameOverride,
            };

            foreach (var stock in s.Stock ?? Array.Empty<StoredItemDto>())
            {
                storage.Stock.Add(new StoredItem(new ItemId(stock.Item ?? string.Empty), stock.Amount ?? 0));
            }

            vessel.Storages.Add(storage);
        }

        foreach (var f in vesselDto.Facilities ?? Array.Empty<FacilityDto>())
        {
            var facility = new FacilityInstance
            {
                Id = new ExecutorId(f.Id ?? string.Empty),
                Archetype = new FacilityArchetypeId(f.Archetype ?? string.Empty),
                NameOverride = f.NameOverride,
                LocalStorage = new StorageId(f.LocalStorage ?? string.Empty),
                Built = f.Built ?? true,
                WorkRatePermille = f.WorkRatePermille ?? 1000,
                EnergyEfficiencyPermille = f.EnergyEfficiencyPermille ?? 1000,
                IntegrityPermille = f.IntegrityPermille ?? 1000,
                Configured = f.Configured is null ? null : new SchematicId(f.Configured),
                SwitchOverRemaining = f.SwitchOverRemaining ?? 0,
                SwitchTarget = f.SwitchTarget is { } target ? new TaskId(target) : null,
                Current = f.Current is { } current ? new TaskId(current) : null,
                Status = Enum.Parse<ExecutorStatus>(f.Status ?? nameof(ExecutorStatus.NoTasksQueued)),
                BlockReason = f.BlockReason is null ? null : Enum.Parse<PostponeReason>(f.BlockReason),
                PowerDrawLastTick = f.PowerDrawLastTick ?? 0,
                Utilization = Restore(f.Utilization),
            };

            foreach (var id in f.Queue ?? Array.Empty<long>())
            {
                facility.Queue.Add(new TaskId(id));
            }

            foreach (var id in f.Programs ?? Array.Empty<long>())
            {
                facility.Programs.Add(new ProgramInstanceId(id));
            }

            vessel.Facilities.Add(facility);
        }

        foreach (var t in vesselDto.Transports ?? Array.Empty<TransportDto>())
        {
            var line = new TransportInstance
            {
                Id = new ExecutorId(t.Id ?? string.Empty),
                Archetype = new TransportArchetypeId(t.Archetype ?? string.Empty),
                NameOverride = t.NameOverride,
                From = new StorageId(t.From ?? string.Empty),
                To = new StorageId(t.To ?? string.Empty),
                Built = t.Built ?? true,
                ThroughputPermille = t.ThroughputPermille ?? 1000,
                Current = t.Current is { } current ? new TaskId(current) : null,
                MovedLastTick = t.MovedLastTick ?? 0,
                PowerDrawLastTick = t.PowerDrawLastTick ?? 0,
                Status = Enum.Parse<ExecutorStatus>(t.Status ?? nameof(ExecutorStatus.NoTasksQueued)),
                BlockReason = t.BlockReason is null ? null : Enum.Parse<PostponeReason>(t.BlockReason),
            };

            foreach (var id in t.Queue ?? Array.Empty<long>())
            {
                line.Queue.Add(new TaskId(id));
            }

            vessel.Transports.Add(line);
        }

        foreach (var r in vesselDto.Reactors ?? Array.Empty<ReactorDto>())
        {
            var reactor = new ReactorInstance
            {
                Id = new ExecutorId(r.Id ?? string.Empty),
                Archetype = new ReactorArchetypeId(r.Archetype ?? string.Empty),
                NameOverride = r.NameOverride,
                Built = r.Built ?? true,
                IntegrityPermille = r.IntegrityPermille ?? 1000,
                FuelStore = new StorageId(r.FuelStore ?? string.Empty),
                OutputPermille = r.OutputPermille ?? 1000,
                Status = Enum.Parse<ExecutorStatus>(r.Status ?? nameof(ExecutorStatus.NoTasksQueued)),
                BlockReason = r.BlockReason is null ? null : Enum.Parse<PostponeReason>(r.BlockReason),
                Utilization = Restore(r.Utilization),
            };

            foreach (var id in r.Programs ?? Array.Empty<long>())
            {
                reactor.Programs.Add(new ProgramInstanceId(id));
            }

            vessel.Reactors.Add(reactor);
        }

        foreach (var sink in vesselDto.Sinks ?? Array.Empty<string>())
        {
            vessel.Sinks.Add(new PowerSinkId(sink));
        }

        foreach (var r in vesselDto.Reservations ?? Array.Empty<ReservationDto>())
        {
            vessel.Reservations.Held.Add(new Reservation(
                new StorageId(r.Storage ?? string.Empty),
                new ItemId(r.Item ?? string.Empty),
                r.Quantity ?? 0,
                new ProgramInstanceId(r.Owner ?? 0)));
        }

        var tasks = new TaskRegistry { NextTaskId = tasksDto.NextTaskId ?? 0 };

        foreach (var t in tasksDto.Production ?? Array.Empty<ProductionTaskDto>())
        {
            var task = new ProductionTask
            {
                Id = new TaskId(t.Id ?? 0),
                SchematicId = new SchematicId(t.Schematic ?? string.Empty),
                RequestedRuns = t.RequestedRuns,
                ExecutorId = new ExecutorId(t.Executor ?? string.Empty),
                State = Enum.Parse<TaskState>(t.State ?? nameof(TaskState.NotStarted)),
                CompletedRuns = t.CompletedRuns ?? 0,
                RunActive = t.RunActive ?? false,
                RunAwaitingDeposit = t.RunAwaitingDeposit ?? false,
                WorkDoneThisRun = t.WorkDoneThisRun ?? 0,
                EnergyChargedThisRun = t.EnergyChargedThisRun ?? 0,
                LastReason = t.LastReason is null ? null : Enum.Parse<PostponeReason>(t.LastReason),
                PostponedAtTick = t.PostponedAtTick,
            };

            task.RestoreHistory(Restore(t.History));
            tasks.Add(task);
        }

        foreach (var t in tasksDto.Transport ?? Array.Empty<TransportTaskDto>())
        {
            var task = new TransportTask
            {
                Id = new TaskId(t.Id ?? 0),
                Item = new ItemId(t.Item ?? string.Empty),
                RequestedQuantity = t.RequestedQuantity,
                ExecutorId = new ExecutorId(t.Executor ?? string.Empty),
                Source = new StorageId(t.Source ?? string.Empty),
                Destination = new StorageId(t.Destination ?? string.Empty),
                State = Enum.Parse<TaskState>(t.State ?? nameof(TaskState.NotStarted)),
                MovedQuantity = t.MovedQuantity ?? 0,
                LastReason = t.LastReason is null ? null : Enum.Parse<PostponeReason>(t.LastReason),
                PostponedAtTick = t.PostponedAtTick,
            };

            task.RestoreHistory(Restore(t.History));
            tasks.Add(task);
        }

        foreach (var id in tasksDto.Retired ?? Array.Empty<long>())
        {
            tasks.Retire(new TaskId(id));
        }

        var state = new WorldState
        {
            ScenarioId = dto.ScenarioId ?? string.Empty,
            Clock = new OperationalClock
            {
                Tick = clock.Tick ?? 0,
                AutoPauseOnCriticalAlert = clock.AutoPauseOnCriticalAlert ?? false,
            },

            // Extended rather than rejected: domains are append-only, so a shorter array is an
            // older save and never a re-pointed one.
            Random = new RandomState { Streams = streams.ToArray() }
                .Extended(ScenarioSeeder.DefaultSeed),
            Vessel = vessel,
            Tasks = tasks,
            Progress = new ProgressLedger(),
            Plans = new PlanRegistry { NextPlanId = dto.Plans?.NextPlanId ?? 0 },
            Missions = new MissionLedger { NextMissionId = dto.Missions?.NextMissionId ?? 0 },
            Alerts = new AlertLedger { NextAlertId = dto.Alerts?.NextAlertId ?? 0 },
            Journal = new JournalLedger { TotalEmitted = dto.Journal?.TotalEmitted ?? 0 },
            Programs = new ProgramLedger { NextInstanceId = dto.NextProgramInstanceId ?? 0 },
            Robots = new RobotLedger { NextRobotId = dto.Robots?.NextRobotId ?? 0 },
            Case = new CaseLedger(),
        };

        foreach (var id in dto.Progress?.UnlockedSchematics ?? Array.Empty<string>())
        {
            state.Progress.UnlockedSchematics.Add(new SchematicId(id));
        }

        foreach (var id in dto.Progress?.DiscoveredItems ?? Array.Empty<string>())
        {
            state.Progress.DiscoveredItems.Add(new ItemId(id));
        }

        foreach (var flag in dto.Progress?.Flags ?? Array.Empty<string>())
        {
            state.Progress.Flags.Add(flag);
        }

        foreach (var p in dto.Plans?.Plans ?? Array.Empty<PlanDto>())
        {
            state.Plans.Record(new CommittedPlan
            {
                Id = new PlanId(p.Id ?? 0),
                Goal = new ItemAmount(new ItemId(p.GoalItem ?? string.Empty), p.GoalQuantity ?? 0),
                CommittedAtTick = p.CommittedAtTick ?? 0,
                SpawnedTasks = (p.SpawnedTasks ?? Array.Empty<long>()).Select(t => new TaskId(t)).ToList(),
                Shortages = (p.Shortages ?? Array.Empty<ShortageDto>()).Select(s => new PlanShortage(
                    new ItemId(s.Item ?? string.Empty),
                    s.Missing ?? 0,
                    Enum.Parse<ShortageKind>(s.Kind ?? nameof(ShortageKind.RawResource)))).ToList(),
                CompletedTasks = p.CompletedTasks ?? 0,
                State = Enum.Parse<PlanState>(p.State ?? nameof(PlanState.Active)),
            });
        }

        foreach (var m in dto.Missions?.Missions ?? Array.Empty<MissionDto>())
        {
            var mission = new Mission
            {
                Id = new MissionId(m.Id ?? 0),
                Target = new StratumId(m.Target ?? string.Empty),
                Kind = Enum.Parse<MissionKind>(m.Kind ?? nameof(MissionKind.Mining)),
                Phase = Enum.Parse<MissionPhase>(m.Phase ?? nameof(MissionPhase.Preparing)),
                Dock = new ExecutorId(m.Dock ?? string.Empty),
                DepartedAtTick = m.DepartedAtTick ?? 0,
                ArrivesAtTick = m.ArrivesAtTick ?? 0,
                Destination = new StorageId(m.Destination ?? string.Empty),
                ForPlan = m.ForPlan is { } plan ? new PlanId(plan) : null,
                RngStream = m.RngStream ?? 0,
            };

            foreach (var robot in m.Group ?? Array.Empty<long>())
            {
                mission.Group.Add(new RobotId(robot));
            }

            foreach (var amount in m.Manifest ?? Array.Empty<ItemAmountDto>())
            {
                mission.Manifest.Add(
                    new ItemAmount(new ItemId(amount.Item ?? string.Empty), amount.Quantity ?? 0));
            }

            state.Missions.Missions.Add(mission);
        }

        foreach (var a in dto.Alerts?.Alerts ?? Array.Empty<AlertDto>())
        {
            state.Alerts.Alerts.Add(new Alert
            {
                Id = new AlertId(a.Id ?? 0),
                Severity = Enum.Parse<AlertSeverity>(a.Severity ?? nameof(AlertSeverity.Info)),
                Code = Enum.Parse<AlertCode>(a.Code ?? nameof(AlertCode.ExecutorBlocked)),
                SubjectId = a.SubjectId ?? string.Empty,
                RaisedAtTick = a.RaisedAtTick ?? 0,
                RootCause = a.RootCause is null ? null : Enum.Parse<PostponeReason>(a.RootCause),
                Acknowledged = a.Acknowledged ?? false,
                Pinned = a.Pinned ?? false,
            });
        }

        foreach (var e in dto.Journal?.Events ?? Array.Empty<EventDto>())
        {
            state.Journal.Events.Enqueue(new SimEvent(
                e.Tick ?? 0,
                Enum.Parse<EventCategory>(e.Category ?? nameof(EventCategory.Production)),
                Enum.Parse<EventCode>(e.Code ?? nameof(EventCode.TaskQueued)),
                e.Subject ?? string.Empty,
                e.Data ?? SimEvent.NoData));
        }

        foreach (var r in dto.Robots?.Robots ?? Array.Empty<RobotDto>())
        {
            var robot = new Robot
            {
                Id = new RobotId(r.Id ?? 0),
                Frame = new RobotFrameId(r.Frame ?? string.Empty),
                NameOverride = r.NameOverride,
                IntegrityPermille = r.IntegrityPermille ?? 1000,
                Group = r.Group is null ? null : new RobotGroupId(r.Group),
                OnMission = r.OnMission is { } mission ? new MissionId(mission) : null,
            };

            foreach (var socket in r.Sockets ?? Array.Empty<RobotSocketDto>())
            {
                robot.Sockets.Add(new RobotSocket(
                    new SocketId(socket.Socket ?? string.Empty),
                    new StorageId(socket.Storage ?? string.Empty)));
            }

            state.Robots.Robots.Add(robot);
        }

        return state;
    }

    private static EnergyLedger Budget(BudgetDto? dto) => new()
    {
        Capacity = dto?.Capacity ?? 0,
        DrawLastTick = dto?.DrawLastTick ?? 0,
        CapHits = dto?.CapHits ?? 0,
        StarvedTicks = dto?.StarvedTicks ?? 0,
    };

    private static ComputeLedger Compute(BudgetDto? dto) => new()
    {
        Capacity = dto?.Capacity ?? 0,
        DrawLastTick = dto?.DrawLastTick ?? 0,
        CapHits = dto?.CapHits ?? 0,
        StarvedTicks = dto?.StarvedTicks ?? 0,
    };

    private static UtilizationWindow Restore(UtilizationDto? dto)
    {
        if (dto?.Working is null)
        {
            return UtilizationWindow.Empty();
        }

        return new UtilizationWindow
        {
            WindowTicks = dto.WindowTicks ?? UtilizationWindow.DefaultWindowTicks,
            BucketTicks = dto.BucketTicks ?? UtilizationWindow.DefaultBucketTicks,
            Head = dto.Head ?? 0,
            Measured = dto.Measured ?? 0,
            Working = dto.Working.ToArray(),
            Idle = (dto.Idle ?? new long[dto.Working.Length]).ToArray(),
            WaitingInput = (dto.WaitingInput ?? new long[dto.Working.Length]).ToArray(),
            WaitingOutput = (dto.WaitingOutput ?? new long[dto.Working.Length]).ToArray(),
            Throttled = (dto.Throttled ?? new long[dto.Working.Length]).ToArray(),
            SwitchingOver = (dto.SwitchingOver ?? new long[dto.Working.Length]).ToArray(),
        };
    }

    private static List<TaskAttempt> Restore(IReadOnlyList<AttemptDto>? history) =>
        (history ?? Array.Empty<AttemptDto>()).Select(a => new TaskAttempt(
            a.Tick ?? 0,
            Enum.Parse<TaskAttemptOutcome>(a.Outcome ?? nameof(TaskAttemptOutcome.Started)),
            a.Reason is null ? null : Enum.Parse<PostponeReason>(a.Reason))).ToList();

    /// <summary>
    /// Every id the world names, checked against the catalog it will run on. Reported together:
    /// a campaign that has drifted from its content has usually drifted in more than one place,
    /// and finding out one id at a time is finding out slowly.
    /// </summary>
    private static void CheckDrift(WorldState state, ContentCatalog catalog, List<SaveError> errors)
    {
        for (var i = 0; i < state.Vessel.Storages.Count; i++)
        {
            var storage = state.Vessel.Storages[i];
            if (catalog.Storage(storage.Archetype) is null)
            {
                errors.Add(new SaveError(
                    $"vessel.storages[{i}].archetype",
                    $"no storage archetype '{storage.Archetype}' in the catalog loaded."));
            }

            foreach (var stock in storage.Stock)
            {
                if (catalog.Item(stock.Item) is null)
                {
                    errors.Add(new SaveError(
                        $"vessel.storages[{i}].stock",
                        $"no item '{stock.Item}' in the catalog loaded."));
                }
            }
        }

        for (var i = 0; i < state.Vessel.Facilities.Count; i++)
        {
            var facility = state.Vessel.Facilities[i];
            if (catalog.Facility(facility.Archetype) is null)
            {
                errors.Add(new SaveError(
                    $"vessel.facilities[{i}].archetype",
                    $"no facility archetype '{facility.Archetype}' in the catalog loaded."));
            }

            if (facility.Configured is { } configured
                && !catalog.Schematics.TryGet(configured, out _))
            {
                errors.Add(new SaveError(
                    $"vessel.facilities[{i}].configured",
                    $"no schematic '{configured}' in the catalog loaded."));
            }
        }

        for (var i = 0; i < state.Vessel.Transports.Count; i++)
        {
            var line = state.Vessel.Transports[i];
            if (catalog.Transport(line.Archetype) is null)
            {
                errors.Add(new SaveError(
                    $"vessel.transports[{i}].archetype",
                    $"no transport archetype '{line.Archetype}' in the catalog loaded."));
            }
        }

        for (var i = 0; i < state.Vessel.Sinks.Count; i++)
        {
            if (catalog.Sinks.All(s => s.Id != state.Vessel.Sinks[i]))
            {
                errors.Add(new SaveError(
                    $"vessel.sinks[{i}]",
                    $"no power sink '{state.Vessel.Sinks[i]}' in the catalog loaded."));
            }
        }

        for (var i = 0; i < state.Tasks.Production.Count; i++)
        {
            var task = state.Tasks.Production[i];
            if (!catalog.Schematics.TryGet(task.SchematicId, out _))
            {
                errors.Add(new SaveError(
                    $"tasks.production[{i}].schematic",
                    $"no schematic '{task.SchematicId}' in the catalog loaded."));
            }
        }

        for (var i = 0; i < state.Tasks.Transport.Count; i++)
        {
            var task = state.Tasks.Transport[i];
            if (catalog.Item(task.Item) is null)
            {
                errors.Add(new SaveError(
                    $"tasks.transport[{i}].item",
                    $"no item '{task.Item}' in the catalog loaded."));
            }
        }

        foreach (var unlocked in state.Progress.UnlockedSchematics.OrderBy(s => s.Value, StringComparer.Ordinal))
        {
            if (!catalog.Schematics.TryGet(unlocked, out _))
            {
                errors.Add(new SaveError(
                    "progress.unlockedSchematics",
                    $"no schematic '{unlocked}' in the catalog loaded."));
            }
        }
    }
}

/// <summary>
/// One step of the version chain: reads a world written at <see cref="From"/> and hands back the
/// shape the next version expects.
/// </summary>
public interface ISaveUpgrader
{
    int From { get; }

    WorldStateDto Upgrade(WorldStateDto older);
}
