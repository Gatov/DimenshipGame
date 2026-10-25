using System.Text.Json;
using System.Text.Json.Serialization;
using Dimenship.Core.Content;
using Dimenship.Core.Production;
using Dimenship.Core.Programs;
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
    /// The format's version. Stays at 1 through the conveyor step as well: a line gained a length
    /// and a belt, which changes the shape of every transport record, but there are still no saves
    /// in the wild and an upgrader for a format nobody wrote would be a fiction. A newer save is
    /// refused; an older one would run the upgrader chain once a second version exists.
    /// </summary>
    public const int CurrentVersion = 4;

    /// <summary>
    /// The chain, empty and present. Version 1 needs no upgraders — there is nothing older to
    /// upgrade from — but the seam exists from the first release, because retrofitting it later
    /// means guessing what a version-1 save looked like from whatever survived.
    /// </summary>
    public static IReadOnlyList<ISaveUpgrader> Upgraders { get; } = new ISaveUpgrader[]
    {
        new PriorityMovesOntoThePlan(),
        new ClaimsStartEmpty(),
        new NothingQueuedByHandWasHeld(),
    };

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
            return SaveLoadResult.Failed(new SaveError("state", "is required; without it the save carries no world."));
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

        // Nothing reads the tick a save was written at, which is exactly why its absence would
        // otherwise go unnoticed: the file says when it is from, or it is not a whole save.
        if (envelope.SavedAtTick is null)
        {
            errors.Add(new SaveError("savedAtTick", "is required."));
        }

        if (envelope.ContentVersion is null)
        {
            errors.Add(new SaveError("contentVersion", "is required."));
        }
        else if (envelope.ContentVersion != catalog.ContentVersion)
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
        CheckLayout(state, scenario!, errors);
        if (errors.Count == 0)
        {
            CheckWorkpieces(state, catalog, errors);
        }

        if (errors.Count == 0)
        {
            CheckClaims(state, catalog, errors);
        }

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
                LengthTicks = t.LengthTicks,
                Belt = Capture(t.Belt),
                LoadedLastTick = t.LoadedLastTick,
                DeliveredLastTick = t.DeliveredLastTick,
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
            Tasks = state.Tasks.All.Select(t => new TaskDto
            {
                Id = t.Id.Value,
                Executor = t.ExecutorId.Value,
                Conditions = Capture(t.Script.Conditions),
                Action = Capture(t.Script.Action),
                State = t.State.ToString(),
                CompletedRuns = t.CompletedRuns,
                RunActive = t.RunActive,
                RunAwaitingDeposit = t.RunAwaitingDeposit,
                WorkDoneThisRun = t.WorkDoneThisRun,
                EnergyChargedThisRun = t.EnergyChargedThisRun,
                MovedQuantity = t.MovedQuantity,
                LoadedQuantity = t.LoadedQuantity,
                LastReason = t.LastReason?.ToString(),
                PostponedAtTick = t.PostponedAtTick,
                EnqueuedAtTick = t.EnqueuedAtTick,
                FirstStartedAtTick = t.FirstStartedAtTick,
                CompletedAtTick = t.CompletedAtTick,
                Priority = state.Plans.Owning(t.Id) is null ? t.Priority.ToString() : null,
                Held = state.Plans.Owning(t.Id) is null ? t.Held : null,
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
                Destination = p.Destination?.Value,
                CommittedAtTick = p.CommittedAtTick,
                SpawnedTasks = p.SpawnedTasks.Select(t => t.Value).ToList(),
                CompletedTasks = p.CompletedTasks,
                State = p.State.ToString(),
                Priority = p.Priority.ToString(),
                Held = p.Held,
                LastProgressAtTick = p.LastProgressAtTick,
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
                RelatedSubjectId = a.RelatedSubjectId,
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
        Claims = state.Claims.Entries.Select(c => new ClaimDto
        {
            Plan = c.Plan.Value,
            Storage = c.Storage.Value,
            Item = c.Item.Value,
            Held = c.Held,
        }).ToList(),
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
        Held = window.Held.ToArray(),
    };

    private static List<AttemptDto> Capture(IReadOnlyList<TaskAttempt> history) =>
        history.Select(a => new AttemptDto
        {
            Tick = a.Tick,
            Outcome = a.Outcome.ToString(),
            Reason = a.Reason?.ToString(),
            ByTask = a.ByTask?.Value,
            ByPlan = a.ByPlan?.Value,
        }).ToList();

    private static TaskActionDto Capture(TaskAction action) =>
        action switch
        {
            Produce produce => new TaskActionDto
            {
                Kind = "produce",
                Schematic = produce.Schematic.Value,
                Runs = produce.Runs,
            },
            Transfer transfer => new TaskActionDto
            {
                Kind = "transfer",
                Item = transfer.Item.Value,
                Quantity = transfer.Quantity,
                From = transfer.From.Value,
                To = transfer.To.Value,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown task action."),
        };

    private static List<ConditionDto> Capture(IReadOnlyList<Condition> conditions) =>
        conditions.Select(c => new ConditionDto
        {
            Kind = c.Kind.ToString(),
            Operands = c.Operands.Select(o => Capture(o)).ToList(),
            Op = c.Op.ToString(),
            Value = Capture(c.Value),
        }).ToList();

    /// <summary>
    /// One operand, tagged by kind. A <see cref="ParameterRef"/> cannot reach a save today —
    /// <c>SimulationEngine.Enqueue</c> refuses one, because a parameter has no binding outside a
    /// program — but it is written faithfully rather than refused here, so the program runtime that
    /// binds parameters needs no format bump and no second condition language.
    /// </summary>
    private static OperandDto Capture(Operand operand) =>
        operand switch
        {
            Literal literal => new OperandDto { Kind = "literal", Literal = literal.Value },
            ParameterRef parameter => new OperandDto { Kind = "parameter", Name = parameter.Name },
            TargetRef target => new OperandDto
            {
                Kind = "target",
                TargetKind = target.Kind.ToString(),
                Id = target.Id,
            },
            EnumRef enumRef => new OperandDto
            {
                Kind = "enum",
                EnumKind = enumRef.Kind,

                // Throws rather than writing a null name: a reference the wire vocabulary cannot
                // spell got into memory, which is a programmer's mistake and not a file's. Restore
                // reports the same problem instead, because there it came off disk.
                EnumName = EnumNames.Name(enumRef) ?? throw new ArgumentOutOfRangeException(
                    nameof(operand),
                    operand,
                    $"'{enumRef.Value}' names no member of '{enumRef.Kind}'."),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operand), operand, "Unknown operand."),
        };

    /// <summary>
    /// The belt as a sparse, position-ordered list, index 0 at the destination end. Empty slots are
    /// left out: the belt's size is <c>lengthTicks</c>, written beside it, so a reader rebuilds the
    /// padding and a diff between two saves shows the cargo that moved rather than the gaps around
    /// it.
    /// </summary>
    private static List<BeltSlotDto> Capture(IReadOnlyList<BeltSlot?> belt)
    {
        var slots = new List<BeltSlotDto>();
        for (var i = 0; i < belt.Count; i++)
        {
            if (belt[i] is not { } slot)
            {
                continue;
            }

            slots.Add(new BeltSlotDto
            {
                Position = i,
                Task = slot.Task.Value,
                Item = slot.Item.Value,
                Quantity = slot.Quantity,
            });
        }

        return slots;
    }

    /// <summary>
    /// Puts saved cargo back onto a belt already sized from the line's length. A position the belt
    /// does not have is reported rather than dropped or clamped: it means a route was shortened in
    /// content while a save sat mid-haul, and both losing that cargo and piling it onto the last
    /// slot are a vessel silently changing how much material it owns across a load.
    /// </summary>
    private static void Restore(
        TransportInstance line, IReadOnlyList<BeltSlotDto>? slots, string path, List<SaveError> errors)
    {
        var read = new SaveFields(errors);
        var all = read.List(slots, path);
        for (var i = 0; i < all.Count; i++)
        {
            var slot = all[i];
            var at = $"{path}[{i}]";
            if (slot.Position is not { } position)
            {
                read.Value(slot.Position, $"{at}.position");
                continue;
            }

            if (position < 0 || position >= line.Belt.Count)
            {
                errors.Add(new SaveError(
                    path,
                    $"carries cargo at position {position} on a belt {line.Belt.Count} ticks long."));
                continue;
            }

            if (line.Belt[(int)position] is not null)
            {
                errors.Add(new SaveError(path, $"carries two loads at position {position}."));
                continue;
            }

            line.Belt[(int)position] = new BeltSlot
            {
                Task = new TaskId(read.Value(slot.Task, $"{at}.task")),
                Item = new ItemId(read.Text(slot.Item, $"{at}.item")),
                Quantity = read.Value(slot.Quantity, $"{at}.quantity"),
            };
        }
    }

    private static TaskAction? Restore(TaskActionDto? dto, string path, List<SaveError> errors)
    {
        // A run count or a transfer quantity is optional: absent is a standing order, which is a
        // value, not a gap.
        var read = new SaveFields(errors);
        switch (dto?.Kind)
        {
            case "produce":
                return new Produce(new SchematicId(read.Text(dto!.Schematic, $"{path}.action.schematic")), dto.Runs);
            case "transfer":
                return new Transfer(
                    new ItemId(read.Text(dto!.Item, $"{path}.action.item")),
                    dto.Quantity,
                    new StorageId(read.Text(dto.From, $"{path}.action.from")),
                    new StorageId(read.Text(dto.To, $"{path}.action.to")));
            case null:
                errors.Add(new SaveError($"{path}.action", "is required."));
                return null;
            default:
                errors.Add(new SaveError($"{path}.action.kind", $"'{dto!.Kind}' is not a task action."));
                return null;
        }
    }

    private static List<Condition> Restore(
        IReadOnlyList<ConditionDto>? dtos, string path, List<SaveError> errors)
    {
        var read = new SaveFields(errors);
        var all = read.List(dtos, $"{path}.conditions");
        var conditions = new List<Condition>();
        for (var i = 0; i < all.Count; i++)
        {
            var at = $"{path}.conditions[{i}]";
            var dto = all[i];

            var before = errors.Count;
            var kind = read.Enum<ConditionKind>(dto.Kind, $"{at}.kind");
            var op = read.Enum<Comparison>(dto.Op, $"{at}.op");
            if (errors.Count > before)
            {
                continue;
            }

            var listed = read.List(dto.Operands, $"{at}.operands");
            var operands = new List<Operand>();
            var broken = false;
            for (var j = 0; j < listed.Count; j++)
            {
                if (Restore(listed[j], $"{at}.operands[{j}]", errors) is { } operand)
                {
                    operands.Add(operand);
                }
                else
                {
                    broken = true;
                }
            }

            if (Restore(dto.Value, $"{at}.value", errors) is not { } value || broken)
            {
                continue;
            }

            conditions.Add(new Condition(kind, operands, op, value));
        }

        return conditions;
    }

    private static Operand? Restore(OperandDto? dto, string path, List<SaveError> errors)
    {
        switch (dto?.Kind)
        {
            case "literal" when dto.Literal is { } literal:
                return new Literal(literal);
            case "parameter" when dto.Name is { } name:
                return new ParameterRef(name);
            case "target" when dto.Id is { } id && dto.TargetKind is { } targetName:
                return new SaveFields(errors).Named<TargetKind>(targetName, $"{path}.targetKind") is { } targetKind
                    ? new TargetRef(targetKind, id)
                    : null;
            case "enum" when dto.EnumKind is { } enumKind && dto.EnumName is { } member:
                if (EnumNames.Value(enumKind, member) is not { } ordinal)
                {
                    errors.Add(new SaveError(
                        $"{path}.enumName",
                        $"'{member}' is not a member of '{enumKind}'."));
                    return null;
                }

                return new EnumRef(enumKind, ordinal);
            case null:
                errors.Add(new SaveError(path, "is required."));
                return null;
            default:
                // Both an unknown kind and a known one missing its fields land here: either way
                // the operand cannot be rebuilt, and the author needs the kind named to find it.
                errors.Add(new SaveError(
                    path,
                    $"'{dto!.Kind}' is not an operand kind, or its fields are missing."));
                return null;
        }
    }

    // ---- restore -----------------------------------------------------------------------------

    private static WorldState? Restore(WorldStateDto dto, List<SaveError> errors)
    {
        var read = new SaveFields(errors);

        // The four branches nothing else can be rebuilt without. Each is named, so a save missing
        // two of them says which two.
        var clock = read.Branch(dto.Clock, "clock");
        var vesselDto = read.Branch(dto.Vessel, "vessel");
        var tasksDto = read.Branch(dto.Tasks, "tasks");
        var streams = read.Branch(dto.RandomStreams, "randomStreams");
        if (clock is null || vesselDto is null || tasksDto is null || streams is null)
        {
            return null;
        }

        var vessel = new VesselState
        {
            Hold = new StorageId(read.Text(vesselDto.Hold, "vessel.hold")),
            Energy = Budget(read.Branch(vesselDto.Energy, "vessel.energy"), "vessel.energy", read),
            Compute = Compute(read.Branch(vesselDto.Compute, "vessel.compute"), "vessel.compute", read),
            Reservations = new ReservationLedger(),
        };

        var storages = read.List(vesselDto.Storages, "vessel.storages");
        for (var i = 0; i < storages.Count; i++)
        {
            var s = storages[i];
            var at = $"vessel.storages[{i}]";
            var storage = new StorageInstance
            {
                Id = new StorageId(read.Text(s.Id, $"{at}.id")),
                Archetype = new StorageArchetypeId(read.Text(s.Archetype, $"{at}.archetype")),
                NameOverride = s.NameOverride,
            };

            var stock = read.List(s.Stock, $"{at}.stock");
            for (var j = 0; j < stock.Count; j++)
            {
                storage.Stock.Add(new StoredItem(
                    new ItemId(read.Text(stock[j].Item, $"{at}.stock[{j}].item")),
                    read.Value(stock[j].Amount, $"{at}.stock[{j}].amount")));
            }

            vessel.Storages.Add(storage);
        }

        var facilities = read.List(vesselDto.Facilities, "vessel.facilities");
        for (var i = 0; i < facilities.Count; i++)
        {
            var f = facilities[i];
            var at = $"vessel.facilities[{i}]";
            var facility = new FacilityInstance
            {
                Id = new ExecutorId(read.Text(f.Id, $"{at}.id")),
                Archetype = new FacilityArchetypeId(read.Text(f.Archetype, $"{at}.archetype")),
                NameOverride = f.NameOverride,
                LocalStorage = new StorageId(read.Text(f.LocalStorage, $"{at}.localStorage")),
                Built = read.Value(f.Built, $"{at}.built"),
                WorkRatePermille = read.Value(f.WorkRatePermille, $"{at}.workRatePermille"),
                EnergyEfficiencyPermille = read.Value(f.EnergyEfficiencyPermille, $"{at}.energyEfficiencyPermille"),
                IntegrityPermille = read.Value(f.IntegrityPermille, $"{at}.integrityPermille"),
                Configured = f.Configured is null ? null : new SchematicId(f.Configured),
                SwitchOverRemaining = read.Value(f.SwitchOverRemaining, $"{at}.switchOverRemaining"),
                SwitchTarget = f.SwitchTarget is { } target ? new TaskId(target) : null,
                Current = f.Current is { } current ? new TaskId(current) : null,
                Status = read.Enum<ExecutorStatus>(f.Status, $"{at}.status"),
                BlockReason = read.OptionalEnum<PostponeReason>(f.BlockReason, $"{at}.blockReason"),
                PowerDrawLastTick = read.Value(f.PowerDrawLastTick, $"{at}.powerDrawLastTick"),
                Utilization = Restore(f.Utilization, $"{at}.utilization", read),
            };

            foreach (var id in read.List(f.Queue, $"{at}.queue"))
            {
                facility.Queue.Add(new TaskId(id));
            }

            foreach (var id in read.List(f.Programs, $"{at}.programs"))
            {
                facility.Programs.Add(new ProgramInstanceId(id));
            }

            vessel.Facilities.Add(facility);
        }

        var transports = read.List(vesselDto.Transports, "vessel.transports");
        for (var i = 0; i < transports.Count; i++)
        {
            var t = transports[i];
            var at = $"vessel.transports[{i}]";
            var line = new TransportInstance
            {
                Id = new ExecutorId(read.Text(t.Id, $"{at}.id")),
                Archetype = new TransportArchetypeId(read.Text(t.Archetype, $"{at}.archetype")),
                NameOverride = t.NameOverride,
                From = new StorageId(read.Text(t.From, $"{at}.from")),
                To = new StorageId(read.Text(t.To, $"{at}.to")),
                Built = read.Value(t.Built, $"{at}.built"),
                ThroughputPermille = read.Value(t.ThroughputPermille, $"{at}.throughputPermille"),
                LengthTicks = read.Value(t.LengthTicks, $"{at}.lengthTicks"),
                Current = t.Current is { } current ? new TaskId(current) : null,
                LoadedLastTick = read.Value(t.LoadedLastTick, $"{at}.loadedLastTick"),
                DeliveredLastTick = read.Value(t.DeliveredLastTick, $"{at}.deliveredLastTick"),
                PowerDrawLastTick = read.Value(t.PowerDrawLastTick, $"{at}.powerDrawLastTick"),
                Status = read.Enum<ExecutorStatus>(t.Status, $"{at}.status"),
                BlockReason = read.OptionalEnum<PostponeReason>(t.BlockReason, $"{at}.blockReason"),
            };

            // A line with no slots has nowhere to put anything, and Unload would look at a head
            // that is not there. The loader cannot author one; a hand-edited save can. A missing
            // length is already reported, and is sized to one slot here only so the belt below
            // can still be read and its own problems reported in the same load.
            if (t.LengthTicks is not null && line.LengthTicks < 1)
            {
                errors.Add(new SaveError(
                    $"{at}.lengthTicks",
                    $"is {line.LengthTicks}; a line has to be at least one tick long."));
            }

            if (line.LengthTicks < 1)
            {
                line.LengthTicks = 1;
            }

            line.SizeBelt();
            Restore(line, t.Belt, $"{at}.belt", errors);

            foreach (var id in read.List(t.Queue, $"{at}.queue"))
            {
                line.Queue.Add(new TaskId(id));
            }

            vessel.Transports.Add(line);
        }

        var reactors = read.List(vesselDto.Reactors, "vessel.reactors");
        for (var i = 0; i < reactors.Count; i++)
        {
            var r = reactors[i];
            var at = $"vessel.reactors[{i}]";
            var reactor = new ReactorInstance
            {
                Id = new ExecutorId(read.Text(r.Id, $"{at}.id")),
                Archetype = new ReactorArchetypeId(read.Text(r.Archetype, $"{at}.archetype")),
                NameOverride = r.NameOverride,
                Built = read.Value(r.Built, $"{at}.built"),
                IntegrityPermille = read.Value(r.IntegrityPermille, $"{at}.integrityPermille"),
                FuelStore = new StorageId(read.Text(r.FuelStore, $"{at}.fuelStore")),
                OutputPermille = read.Value(r.OutputPermille, $"{at}.outputPermille"),
                Status = read.Enum<ExecutorStatus>(r.Status, $"{at}.status"),
                BlockReason = read.OptionalEnum<PostponeReason>(r.BlockReason, $"{at}.blockReason"),
                Utilization = Restore(r.Utilization, $"{at}.utilization", read),
            };

            foreach (var id in read.List(r.Programs, $"{at}.programs"))
            {
                reactor.Programs.Add(new ProgramInstanceId(id));
            }

            vessel.Reactors.Add(reactor);
        }

        foreach (var sink in read.List(vesselDto.Sinks, "vessel.sinks"))
        {
            vessel.Sinks.Add(new PowerSinkId(sink));
        }

        var reservations = read.List(vesselDto.Reservations, "vessel.reservations");
        for (var i = 0; i < reservations.Count; i++)
        {
            var r = reservations[i];
            var at = $"vessel.reservations[{i}]";
            vessel.Reservations.Held.Add(new Reservation(
                new StorageId(read.Text(r.Storage, $"{at}.storage")),
                new ItemId(read.Text(r.Item, $"{at}.item")),
                read.Value(r.Quantity, $"{at}.quantity"),
                new ProgramInstanceId(read.Value(r.Owner, $"{at}.owner"))));
        }

        var tasks = new TaskRegistry { NextTaskId = read.Value(tasksDto.NextTaskId, "tasks.nextTaskId") };
        var savedPriority = new List<(TaskId Task, bool Given, bool HeldGiven, string At)>();

        var taskDtos = read.List(tasksDto.Tasks, "tasks.tasks");
        for (var i = 0; i < taskDtos.Count; i++)
        {
            var t = taskDtos[i];
            var at = $"tasks.tasks[{i}]";

            // A task whose action or conditions cannot be read is skipped rather than rebuilt
            // half-formed; the errors collected here fail the whole load, so the skipped task
            // never reaches a world.
            if (Restore(t.Action, at, errors) is not { } action)
            {
                continue;
            }

            // The timestamps are optional, because the task holds them as optional: a task never
            // postponed, started or finished has no tick for it. Priority and held are required
            // or forbidden by plan membership, which is checked once every plan is recorded.
            var task = new TaskInstance
            {
                Id = new TaskId(read.Value(t.Id, $"{at}.id")),
                Script = new TaskScript(Restore(t.Conditions, at, errors), action),
                ExecutorId = new ExecutorId(read.Text(t.Executor, $"{at}.executor")),
                State = read.Enum<TaskState>(t.State, $"{at}.state"),
                CompletedRuns = read.Value(t.CompletedRuns, $"{at}.completedRuns"),
                RunActive = read.Value(t.RunActive, $"{at}.runActive"),
                RunAwaitingDeposit = read.Value(t.RunAwaitingDeposit, $"{at}.runAwaitingDeposit"),
                WorkDoneThisRun = read.Value(t.WorkDoneThisRun, $"{at}.workDoneThisRun"),
                EnergyChargedThisRun = read.Value(t.EnergyChargedThisRun, $"{at}.energyChargedThisRun"),
                MovedQuantity = read.Value(t.MovedQuantity, $"{at}.movedQuantity"),
                LoadedQuantity = read.Value(t.LoadedQuantity, $"{at}.loadedQuantity"),
                LastReason = read.OptionalEnum<PostponeReason>(t.LastReason, $"{at}.lastReason"),
                PostponedAtTick = t.PostponedAtTick,
                EnqueuedAtTick = t.EnqueuedAtTick,
                FirstStartedAtTick = t.FirstStartedAtTick,
                CompletedAtTick = t.CompletedAtTick,
                Priority = read.OptionalEnum<Priority>(t.Priority, $"{at}.priority") ?? Priority.Normal,
                Held = t.Held ?? false,
            };

            task.RestoreHistory(Restore(t.History, $"{at}.history", read));
            tasks.Add(task);
            savedPriority.Add((task.Id, t.Priority is not null, t.Held is not null, at));
        }

        foreach (var id in read.List(tasksDto.Retired, "tasks.retired"))
        {
            tasks.Retire(new TaskId(id));
        }

        var progress = read.Branch(dto.Progress, "progress");
        var plansDto = read.Branch(dto.Plans, "plans");
        var missionsDto = read.Branch(dto.Missions, "missions");
        var alertsDto = read.Branch(dto.Alerts, "alerts");
        var journalDto = read.Branch(dto.Journal, "journal");
        var robotsDto = read.Branch(dto.Robots, "robots");

        var state = new WorldState
        {
            ScenarioId = read.Text(dto.ScenarioId, "scenarioId"),
            Clock = new OperationalClock
            {
                Tick = read.Value(clock.Tick, "clock.tick"),
                AutoPauseOnCriticalAlert = read.Value(clock.AutoPauseOnCriticalAlert, "clock.autoPauseOnCriticalAlert"),
            },

            // Extended rather than rejected: domains are append-only, so a shorter array is an
            // older save and never a re-pointed one.
            Random = new RandomState { Streams = streams.ToArray() }
                .Extended(ScenarioSeeder.DefaultSeed),
            Vessel = vessel,
            Tasks = tasks,
            Progress = new ProgressLedger(),
            Plans = new PlanRegistry
            {
                NextPlanId = plansDto is null ? 0 : read.Value(plansDto.NextPlanId, "plans.nextPlanId"),
            },
            Claims = new ClaimLedger(),
            Missions = new MissionLedger
            {
                NextMissionId = missionsDto is null ? 0 : read.Value(missionsDto.NextMissionId, "missions.nextMissionId"),
            },
            Alerts = new AlertLedger
            {
                NextAlertId = alertsDto is null ? 0 : read.Value(alertsDto.NextAlertId, "alerts.nextAlertId"),
            },
            Journal = new JournalLedger
            {
                TotalEmitted = journalDto is null ? 0 : read.Value(journalDto.TotalEmitted, "journal.totalEmitted"),
            },
            Programs = new ProgramLedger
            {
                NextInstanceId = read.Value(dto.NextProgramInstanceId, "nextProgramInstanceId"),
            },
            Robots = new RobotLedger
            {
                NextRobotId = robotsDto is null ? 0 : read.Value(robotsDto.NextRobotId, "robots.nextRobotId"),
            },
            Case = new CaseLedger(),
        };

        if (progress is not null)
        {
            foreach (var id in read.List(progress.UnlockedSchematics, "progress.unlockedSchematics"))
            {
                state.Progress.UnlockedSchematics.Add(new SchematicId(id));
            }

            foreach (var id in read.List(progress.DiscoveredItems, "progress.discoveredItems"))
            {
                state.Progress.DiscoveredItems.Add(new ItemId(id));
            }

            foreach (var flag in read.List(progress.Flags, "progress.flags"))
            {
                state.Progress.Flags.Add(flag);
            }
        }

        var planDtos = plansDto is null ? Array.Empty<PlanDto>() : read.List(plansDto.Plans, "plans.plans");
        for (var i = 0; i < planDtos.Count; i++)
        {
            var p = planDtos[i];
            var at = $"plans.plans[{i}]";
            var committedAt = read.Value(p.CommittedAtTick, $"{at}.committedAtTick");
            state.Plans.Record(new CommittedPlan
            {
                Id = new PlanId(read.Value(p.Id, $"{at}.id")),
                Goal = new ItemAmount(
                    new ItemId(read.Text(p.GoalItem, $"{at}.goalItem")),
                    read.Value(p.GoalQuantity, $"{at}.goalQuantity")),
                Destination = p.Destination is null ? null : new StorageId(p.Destination),
                CommittedAtTick = committedAt,
                SpawnedTasks = read.List(p.SpawnedTasks, $"{at}.spawnedTasks").Select(t => new TaskId(t)).ToList(),
                CompletedTasks = read.Value(p.CompletedTasks, $"{at}.completedTasks"),
                State = read.Enum<PlanState>(p.State, $"{at}.state"),
                Priority = read.Enum<Priority>(p.Priority, $"{at}.priority"),
                Held = read.Value(p.Held, $"{at}.held"),

                // The one plan field that may be absent, the way a utilization window's `held` may
                // be. K8 appended it after version 4 without a bump, so every save from before K8
                // (version 1 to 3, whose upgraders know nothing of it, and early version 4) lacks
                // it. Such a plan has made no progress anyone measured, so the clock the waiting
                // alert reads starts at its commit, as it did when the field was first read.
                LastProgressAtTick = p.LastProgressAtTick ?? committedAt,
            });
        }

        // Membership decides which answer is the right one, so this waits until every plan is
        // recorded. Two answers to one question are reported, never reconciled by guessing.
        foreach (var (task, given, heldGiven, at) in savedPriority)
        {
            var plan = state.Plans.Owning(task);
            if (plan is not null && heldGiven)
            {
                errors.Add(new SaveError(
                    $"{at}.held",
                    $"task {task} belongs to plan {plan.Id}, whose held flag it reads; it carries none of its own."));
            }
            else if (plan is null && !heldGiven)
            {
                errors.Add(new SaveError($"{at}.held", $"task {task} has no plan, so it needs a held flag."));
            }

            if (plan is not null && given)
            {
                errors.Add(new SaveError(
                    $"{at}.priority",
                    $"task {task} belongs to plan {plan.Id}, whose priority it reads; it carries none of its own."));
            }
            else if (plan is null && !given)
            {
                errors.Add(new SaveError($"{at}.priority", $"task {task} has no plan, so it needs a priority."));
            }
        }

        foreach (var claim in read.List(dto.Claims, "claims"))
        {
            if (claim.Plan is not { } plan || claim.Storage is null || claim.Item is null || claim.Held is not > 0)
            {
                errors.Add(new SaveError("claims", "a claim needs a plan, a storage, an item and a positive holding."));
                continue;
            }

            state.Claims.Change(new PlanId(plan), new StorageId(claim.Storage), new ItemId(claim.Item), claim.Held.Value);
        }

        var missionDtos = missionsDto is null ? Array.Empty<MissionDto>() : read.List(missionsDto.Missions, "missions.missions");
        for (var i = 0; i < missionDtos.Count; i++)
        {
            var m = missionDtos[i];
            var at = $"missions.missions[{i}]";
            var mission = new Mission
            {
                Id = new MissionId(read.Value(m.Id, $"{at}.id")),
                Target = new StratumId(read.Text(m.Target, $"{at}.target")),
                Kind = read.Enum<MissionKind>(m.Kind, $"{at}.kind"),
                Phase = read.Enum<MissionPhase>(m.Phase, $"{at}.phase"),
                Dock = new ExecutorId(read.Text(m.Dock, $"{at}.dock")),
                DepartedAtTick = read.Value(m.DepartedAtTick, $"{at}.departedAtTick"),
                ArrivesAtTick = read.Value(m.ArrivesAtTick, $"{at}.arrivesAtTick"),
                Destination = new StorageId(read.Text(m.Destination, $"{at}.destination")),
                ForPlan = m.ForPlan is { } plan ? new PlanId(plan) : null,
                RngStream = read.Value(m.RngStream, $"{at}.rngStream"),
            };

            foreach (var robot in read.List(m.Group, $"{at}.group"))
            {
                mission.Group.Add(new RobotId(robot));
            }

            var manifest = read.List(m.Manifest, $"{at}.manifest");
            for (var j = 0; j < manifest.Count; j++)
            {
                mission.Manifest.Add(new ItemAmount(
                    new ItemId(read.Text(manifest[j].Item, $"{at}.manifest[{j}].item")),
                    read.Value(manifest[j].Quantity, $"{at}.manifest[{j}].quantity")));
            }

            state.Missions.Missions.Add(mission);
        }

        var alertDtos = alertsDto is null ? Array.Empty<AlertDto>() : read.List(alertsDto.Alerts, "alerts.alerts");
        for (var i = 0; i < alertDtos.Count; i++)
        {
            var a = alertDtos[i];
            var at = $"alerts.alerts[{i}]";
            state.Alerts.Alerts.Add(new Alert
            {
                Id = new AlertId(read.Value(a.Id, $"{at}.id")),
                Severity = read.Enum<AlertSeverity>(a.Severity, $"{at}.severity"),
                Code = read.Enum<AlertCode>(a.Code, $"{at}.code"),
                SubjectId = read.Text(a.SubjectId, $"{at}.subjectId"),
                RaisedAtTick = read.Value(a.RaisedAtTick, $"{at}.raisedAtTick"),
                RootCause = read.OptionalEnum<PostponeReason>(a.RootCause, $"{at}.rootCause"),
                RelatedSubjectId = a.RelatedSubjectId,
                Acknowledged = read.Value(a.Acknowledged, $"{at}.acknowledged"),
                Pinned = read.Value(a.Pinned, $"{at}.pinned"),
            });
        }

        var eventDtos = journalDto is null ? Array.Empty<EventDto>() : read.List(journalDto.Events, "journal.events");
        for (var i = 0; i < eventDtos.Count; i++)
        {
            var e = eventDtos[i];
            var at = $"journal.events[{i}]";
            state.Journal.Events.Enqueue(new SimEvent(
                read.Value(e.Tick, $"{at}.tick"),
                read.Enum<EventCategory>(e.Category, $"{at}.category"),
                read.Enum<EventCode>(e.Code, $"{at}.code"),
                read.Text(e.Subject, $"{at}.subject"),
                read.Branch(e.Data, $"{at}.data") ?? SimEvent.NoData));
        }

        var robotDtos = robotsDto is null ? Array.Empty<RobotDto>() : read.List(robotsDto.Robots, "robots.robots");
        for (var i = 0; i < robotDtos.Count; i++)
        {
            var r = robotDtos[i];
            var at = $"robots.robots[{i}]";
            var robot = new Robot
            {
                Id = new RobotId(read.Value(r.Id, $"{at}.id")),
                Frame = new RobotFrameId(read.Text(r.Frame, $"{at}.frame")),
                NameOverride = r.NameOverride,
                IntegrityPermille = read.Value(r.IntegrityPermille, $"{at}.integrityPermille"),
                Group = r.Group is null ? null : new RobotGroupId(r.Group),
                OnMission = r.OnMission is { } mission ? new MissionId(mission) : null,
            };

            var sockets = read.List(r.Sockets, $"{at}.sockets");
            for (var j = 0; j < sockets.Count; j++)
            {
                robot.Sockets.Add(new RobotSocket(
                    new SocketId(read.Text(sockets[j].Socket, $"{at}.sockets[{j}].socket")),
                    new StorageId(read.Text(sockets[j].Storage, $"{at}.sockets[{j}].storage"))));
            }

            state.Robots.Robots.Add(robot);
        }

        return state;
    }

    private static EnergyLedger Budget(BudgetDto? dto, string path, SaveFields read) => new()
    {
        Capacity = dto is null ? 0 : read.Value(dto.Capacity, $"{path}.capacity"),
        DrawLastTick = dto is null ? 0 : read.Value(dto.DrawLastTick, $"{path}.drawLastTick"),
        CapHits = dto is null ? 0 : read.Value(dto.CapHits, $"{path}.capHits"),
        StarvedTicks = dto is null ? 0 : read.Value(dto.StarvedTicks, $"{path}.starvedTicks"),
    };

    private static ComputeLedger Compute(BudgetDto? dto, string path, SaveFields read) => new()
    {
        Capacity = dto is null ? 0 : read.Value(dto.Capacity, $"{path}.capacity"),
        DrawLastTick = dto is null ? 0 : read.Value(dto.DrawLastTick, $"{path}.drawLastTick"),
        CapHits = dto is null ? 0 : read.Value(dto.CapHits, $"{path}.capHits"),
        StarvedTicks = dto is null ? 0 : read.Value(dto.StarvedTicks, $"{path}.starvedTicks"),
    };

    /// <summary>
    /// A utilization window. Every bucket array must be as long as <c>working</c>, since the
    /// window indexes them all by one head; a short one would throw on the tick that reached it,
    /// long after the load said yes.
    /// <para>
    /// <c>held</c> is the one array that may be absent, and loads as zeros. It was appended after
    /// saves already existed, without a version bump, and a window from before it held nothing.
    /// Every other array was there from the start, so its absence is a damaged save.
    /// </para>
    /// </summary>
    private static UtilizationWindow Restore(UtilizationDto? dto, string path, SaveFields read)
    {
        if (read.Branch(dto, path) is null || read.Branch(dto!.Working, $"{path}.working") is not { } working)
        {
            return UtilizationWindow.Empty();
        }

        long[] Bucket(long[]? buckets, string name)
        {
            if (read.Branch(buckets, $"{path}.{name}") is not { } present)
            {
                return new long[working.Length];
            }

            if (present.Length != working.Length)
            {
                read.Mismatch($"{path}.{name}", present.Length, working.Length);
                return new long[working.Length];
            }

            return present.ToArray();
        }

        var head = read.Value(dto.Head, $"{path}.head");
        if (dto.Head is not null && (head < 0 || head >= working.Length))
        {
            read.OutOfRange($"{path}.head", head, working.Length);
            head = 0;
        }

        return new UtilizationWindow
        {
            WindowTicks = read.Value(dto.WindowTicks, $"{path}.windowTicks"),
            BucketTicks = read.Value(dto.BucketTicks, $"{path}.bucketTicks"),
            Head = head,
            Measured = read.Value(dto.Measured, $"{path}.measured"),
            Working = working.ToArray(),
            Idle = Bucket(dto.Idle, "idle"),
            WaitingInput = Bucket(dto.WaitingInput, "waitingInput"),
            WaitingOutput = Bucket(dto.WaitingOutput, "waitingOutput"),
            Throttled = Bucket(dto.Throttled, "throttled"),
            SwitchingOver = Bucket(dto.SwitchingOver, "switchingOver"),
            Held = dto.Held is null ? new long[working.Length] : Bucket(dto.Held, "held"),
        };
    }

    private static List<TaskAttempt> Restore(IReadOnlyList<AttemptDto>? history, string path, SaveFields read)
    {
        var all = read.List(history, path);
        var attempts = new List<TaskAttempt>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            var a = all[i];
            var at = $"{path}[{i}]";
            attempts.Add(new TaskAttempt(
                read.Value(a.Tick, $"{at}.tick"),
                read.Enum<TaskAttemptOutcome>(a.Outcome, $"{at}.outcome"),
                read.OptionalEnum<PostponeReason>(a.Reason, $"{at}.reason"),
                a.ByTask is { } byTask ? new TaskId(byTask) : null,
                a.ByPlan is { } byPlan ? new PlanId(byPlan) : null));
        }

        return attempts;
    }

    /// <summary>
    /// Every id the world names, checked against the catalog it will run on. Reported together:
    /// a campaign that has drifted from its content has usually drifted in more than one place,
    /// and finding out one id at a time is finding out slowly.
    /// </summary>
    /// <summary>
    /// A holding the world cannot back is reported, never clamped: a clamp would be a vessel
    /// silently changing who owns its material across a load (D3, K6b). Each holding must belong
    /// to an active plan, name a storage and an item that exist, stay within what its plan's
    /// withdrawals still need there, and, summed across plans, within the stock present.
    /// </summary>
    private static void CheckClaims(WorldState state, ContentCatalog catalog, List<SaveError> errors)
    {
        var totals = new Dictionary<(StorageId, ItemId), long>();
        var index = 0;
        foreach (var (planId, storage, item, held) in state.Claims.Entries)
        {
            var at = $"claims[{index++}]";
            var plan = state.Plans.Plans.FirstOrDefault(p => p.Id == planId);
            if (plan is null || plan.State != PlanState.Active)
            {
                errors.Add(new SaveError(at, $"plan {planId} is not an active plan, so it can hold nothing."));
                continue;
            }

            if (state.Vessel.Storages.All(s => s.Id != storage))
            {
                errors.Add(new SaveError(at, $"no storage '{storage}' aboard."));
                continue;
            }

            if (catalog.Item(item) is null)
            {
                errors.Add(new SaveError(at, $"no item '{item}' in the catalog loaded."));
                continue;
            }

            var need = ClaimMath.Need(catalog, state, plan, storage, item);
            if (held > need)
            {
                errors.Add(new SaveError(
                    at, $"plan {planId} holds {held} of {item} at {storage}, and its work there needs only {need}."));
            }

            totals[(storage, item)] = totals.GetValueOrDefault((storage, item)) + held;
        }

        foreach (var ((storage, item), total) in totals)
        {
            var present = state.Vessel.Storages.First(s => s.Id == storage).Stock
                .Where(s => s.Item == item).Sum(s => s.Amount);
            if (total > present)
            {
                errors.Add(new SaveError(
                    "claims", $"{total} of {item} is held at {storage}, where only {present} is present."));
            }
        }
    }

    /// <summary>
    /// A storage, facility or route the scenario has and the save does not is content drift (K4).
    /// The save holds the vessel whole, and nothing builds or removes a node in play, so the only
    /// way to get here is a scenario that gained one after the save was written: K4's treatment
    /// lines are the first. Loading anyway would run a vessel without them and say nothing, and
    /// adding them would put lines aboard a campaign that never had them. Either is a decision a
    /// loader should not take quietly, so every missing node is listed instead, the same as
    /// <c>contentVersion</c> already refuses a save from other content.
    /// </summary>
    private static void CheckLayout(WorldState state, Scenario scenario, List<SaveError> errors)
    {
        var storages = state.Vessel.Storages.Select(s => s.Id).ToHashSet();
        var facilities = state.Vessel.Facilities.Select(f => f.Id).ToHashSet();
        var transports = state.Vessel.Transports.Select(t => t.Id).ToHashSet();

        foreach (var storage in scenario.Storages.Where(s => !storages.Contains(s.Id)))
        {
            errors.Add(new SaveError(
                "vessel.storages", $"the scenario has a storage '{storage.Id}' this save does not."));
        }

        foreach (var facility in scenario.Facilities.Where(f => !facilities.Contains(f.Id)))
        {
            errors.Add(new SaveError(
                "vessel.facilities", $"the scenario has a facility '{facility.Id}' this save does not."));
        }

        foreach (var route in scenario.Routes.Where(r => !transports.Contains(r.Id)))
        {
            errors.Add(new SaveError(
                "vessel.transports", $"the scenario has a route '{route.Id}' this save does not."));
        }
    }

    /// <summary>
    /// A workpiece somewhere it is not accepted is content drift (K3, D2 Decision 4): stock in such
    /// a storage, cargo on a belt bound for one, or a transfer task naming one. This is what a
    /// catalog edit that turns an existing item into a workpiece looks like to a campaign already
    /// holding it in Resource Storage. Every reference is listed, and nothing is moved or dropped,
    /// because either would be the vessel silently changing what material it owns across a load.
    /// Run after <see cref="CheckDrift"/> has passed, so every archetype it reads exists.
    /// </summary>
    private static void CheckWorkpieces(WorldState state, ContentCatalog catalog, List<SaveError> errors)
    {
        var acceptance = new WorkpieceAcceptance(
            catalog, state.Vessel.Facilities.Select(f => (f.LocalStorage, catalog.Facility(f.Archetype)!.Type)));

        for (var i = 0; i < state.Vessel.Storages.Count; i++)
        {
            var storage = state.Vessel.Storages[i];
            foreach (var stock in storage.Stock)
            {
                if (stock.Amount > 0 && !acceptance.Accepts(storage.Id, stock.Item))
                {
                    errors.Add(new SaveError(
                        $"vessel.storages[{i}].stock", WorkpieceAcceptance.Refusal(stock.Item, storage.Id)));
                }
            }
        }

        for (var i = 0; i < state.Vessel.Transports.Count; i++)
        {
            var line = state.Vessel.Transports[i];
            for (var j = 0; j < line.Belt.Count; j++)
            {
                if (line.Belt[j] is { } slot && !acceptance.Accepts(line.To, slot.Item))
                {
                    errors.Add(new SaveError(
                        $"vessel.transports[{i}].belt[{j}].item", WorkpieceAcceptance.Refusal(slot.Item, line.To)));
                }
            }
        }

        for (var i = 0; i < state.Tasks.All.Count; i++)
        {
            if (state.Tasks.All[i].Script.Action is Transfer transfer && !acceptance.Accepts(transfer.To, transfer.Item))
            {
                errors.Add(new SaveError(
                    $"tasks.tasks[{i}].action", WorkpieceAcceptance.Refusal(transfer.Item, transfer.To)));
            }
        }
    }

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

            // Cargo in flight is material the vessel owns. An item the catalog has dropped is
            // reported here rather than left on the belt, where the first delivery would try to
            // deposit something that no longer has a capacity.
            for (var j = 0; j < line.Belt.Count; j++)
            {
                if (line.Belt[j] is { } slot && catalog.Item(slot.Item) is null)
                {
                    errors.Add(new SaveError(
                        $"vessel.transports[{i}].belt[{j}].item",
                        $"no item '{slot.Item}' in the catalog loaded."));
                }
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

        for (var i = 0; i < state.Tasks.All.Count; i++)
        {
            var task = state.Tasks.All[i];
            switch (task.Script.Action)
            {
                case Produce produce when !catalog.Schematics.TryGet(produce.Schematic, out _):
                    errors.Add(new SaveError(
                        $"tasks.tasks[{i}].action.schematic",
                        $"no schematic '{produce.Schematic}' in the catalog loaded."));
                    break;
                case Transfer transfer when catalog.Item(transfer.Item) is null:
                    errors.Add(new SaveError(
                        $"tasks.tasks[{i}].action.item",
                        $"no item '{transfer.Item}' in the catalog loaded."));
                    break;
            }

            for (var j = 0; j < task.Script.Conditions.Count; j++)
            {
                var condition = task.Script.Conditions[j];
                foreach (var operand in condition.Operands.Append(condition.Value))
                {
                    CheckDrift(operand, $"tasks.tasks[{i}].conditions[{j}]", state, catalog, errors);
                }
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

    /// <summary>
    /// One condition operand's target, against whichever authority can answer for it. Items and
    /// schematics are the catalog's to know; executors and storages are the world's own ids, and a
    /// condition naming one the save does not contain is as silent a failure as a renamed item.
    /// <para>
    /// <c>SimulationEngine.Enqueue</c> refuses an unknown target when a task is created. A save
    /// bypasses that path entirely, so the same question is asked again here — a condition that
    /// can never be true is a task that never starts for a reason nobody can see.
    /// </para>
    /// </summary>
    private static void CheckDrift(
        Operand operand, string path, WorldState state, ContentCatalog catalog, List<SaveError> errors)
    {
        if (operand is not TargetRef target)
        {
            return;
        }

        var known = target.Kind switch
        {
            TargetKind.Item => catalog.Item(new ItemId(target.Id)) is not null,
            TargetKind.Schematic => catalog.Schematics.TryGet(new SchematicId(target.Id), out _),
            TargetKind.Storage => state.Vessel.Storages.Any(s => s.Id.Value == target.Id),
            TargetKind.Executor =>
                state.Vessel.Facilities.Any(f => f.Id.Value == target.Id)
                || state.Vessel.Transports.Any(t => t.Id.Value == target.Id),
            _ => false,
        };

        if (!known)
        {
            var kind = target.Kind.ToString().ToLowerInvariant();
            errors.Add(new SaveError(path, $"names no {kind} '{target.Id}'."));
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

/// <summary>
/// Version 1 to 2: priority moves from each task onto its plan (K6a). A version 1 save from K2
/// carried a priority on every task, and a plan's tasks all carried the plan's, because the only
/// way to set one was <c>SetPriority(PlanId)</c>; the plan takes the highest among them, which is
/// that value. A version 1 save from before K2 carried none, and everything becomes
/// <c>Normal</c>, which is what it was. Nothing was held in version 1.
/// </summary>
internal sealed class PriorityMovesOntoThePlan : ISaveUpgrader
{
    public int From => 1;

    public WorldStateDto Upgrade(WorldStateDto older)
    {
        var tasks = older.Tasks?.Tasks ?? Array.Empty<TaskDto>();
        var byId = new Dictionary<long, TaskDto>();
        foreach (var task in tasks)
        {
            if (task.Id is { } id)
            {
                byId[id] = task;
            }
        }

        var owned = new HashSet<long>();
        var plans = new List<PlanDto>();
        foreach (var plan in older.Plans?.Plans ?? Array.Empty<PlanDto>())
        {
            var highest = Priority.Normal;
            var seen = false;
            foreach (var id in plan.SpawnedTasks ?? Array.Empty<long>())
            {
                owned.Add(id);
                if (byId.TryGetValue(id, out var task)
                    && task.Priority is { } named
                    && Enum.TryParse<Priority>(named, out var priority)
                    && (!seen || priority > highest))
                {
                    highest = priority;
                    seen = true;
                }
            }

            plans.Add(plan with { Priority = highest.ToString(), Held = false });
        }

        var upgradedTasks = tasks
            .Select(t => t.Id is { } id && owned.Contains(id)
                ? t with { Priority = null }
                : t with { Priority = t.Priority ?? nameof(Priority.Normal) })
            .ToList();

        return older with
        {
            Tasks = older.Tasks is null ? null : older.Tasks with { Tasks = upgradedTasks },
            Plans = older.Plans is null ? null : older.Plans with { Plans = plans },
        };
    }
}

/// <summary>
/// Version 2 to 3: the claims ledger (K6b). Nothing was held before it existed, so an upgraded
/// world starts with no holdings; each active plan claims at the next arrival at a storage it
/// withdraws from, which is when the invariant is next re-established.
/// </summary>
internal sealed class ClaimsStartEmpty : ISaveUpgrader
{
    public int From => 2;

    public WorldStateDto Upgrade(WorldStateDto older) => older with { Claims = Array.Empty<ClaimDto>() };
}

/// <summary>
/// Version 3 to 4: a task queued by hand can be held (K6c). Nothing could hold one before, so
/// every plan-less task loads released, and a plan task carries no flag of its own, as it carries
/// no priority.
/// </summary>
internal sealed class NothingQueuedByHandWasHeld : ISaveUpgrader
{
    public int From => 3;

    public WorldStateDto Upgrade(WorldStateDto older)
    {
        var owned = new HashSet<long>(
            (older.Plans?.Plans ?? Array.Empty<PlanDto>()).SelectMany(p => p.SpawnedTasks ?? Array.Empty<long>()));
        var tasks = (older.Tasks?.Tasks ?? Array.Empty<TaskDto>())
            .Select(t => t with { Held = t.Id is { } id && owned.Contains(id) ? null : false })
            .ToList();

        return older with { Tasks = older.Tasks is null ? null : older.Tasks with { Tasks = tasks } };
    }
}
