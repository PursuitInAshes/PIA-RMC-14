using Content.Shared._RMC14.Medical.Defibrillator;
using Content.Shared._RMC14.Suicide;
using Content.Shared._RMC14.Marines.Skills;
using Content.Shared._RMC14.Marines.Announce;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._RMC14.Weapons.Ranged.Execution;

public sealed class BattlefieldExecutionSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedMarineAnnounceSystem _marineAnnounce = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BattlefieldExecutionCapableComponent, GetVerbsEvent<Verb>>(OnBEGetVerbs);
        SubscribeLocalEvent<BattlefieldExecutionWeaponComponent, RMCSuicideDoAfterEvent>(OnBEDoAfter); // Change "RMCSuicideDoAfterEvent"
        SubscribeLocalEvent<RMCExecutedComponent, UpdateMobStateEvent>(OnceExecutedUpdateMobState);
    }

    private void OnBEGetVerbs(Entity<BattlefieldExecutionCapableComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        var user = args.User;
        var target = args.Target;

        if (!args.CanInteract)
            return;

        if (user == target)
            return;

        if (_hands.GetActiveItem(user) is not { } heldItem)
            return;

        if (!TryComp(heldItem, out BattlefieldExecutionWeaponComponentComp executionComp))
            return;

        if (executionComp.RequiredSkills != null && !_skills.HasAllSkills(args.User, executionComp.RequiredSkills))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("rmc-battlefield-execution-verb"),
            Act = () =>
            {
                var ev = new RMCSuicideDoAfterEvent(); // Change
                var doAfter = new DoAfterArgs(EntityManager, user, executionComp.Delay, ev, target)
                {
                    BreakOnMove = true,
                    NeedHand = true,
                    BreakOnHandChange = true,
                    ForceVisible = true,
                };

                if (_doAfter.TryStartDoAfter(doAfter))
                {
                    _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)} started to execute {ToPrettyString(target)}.");
                    var selfMsg = Loc.GetString("rmc-battlefield-execute-start-self", ("target", target));
                    var othersMsg = Loc.GetString("rmc-battlefield-execute-start-others", ("user", user), ("target", target));
                    _popup.PopupPredicted(selfMsg, othersMsg, user, user, PopupType.LargeCaution);
                }
            },
        });
    }

    private void OnBEDoAfter(Entity<BattlefieldExecutionWeaponComponent> ent, ref RMCSuicideDoAfterEvent args) // Change event
    {
        var user = args.User;
        var target = args.Target;
        var heldItem = _hands.GetActiveItem(user);
        var examineText = Loc.GetString("rmc-battlefield-executed-examine");

        if (args.Cancelled)
        {
            _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)}'s execution of {ToPrettyString(target)} was cancelled.");
            var selfMsg = Loc.GetString("rmc-battlefield-execute-cancel-self");
            var othersMsg = Loc.GetString("rmc-battlefield-execute-cancel-others", ("user", user));
            _popup.PopupPredicted(selfMsg, othersMsg, user, user, PopupType.MediumCaution);
            return;
        }

        var aresExecutionText = Loc.GetString("rmc-battlefield-execute-ares-message");
        SoundSpecifier aresExecutionAudio = new SoundPathSpecifier("/Audio/_RMC14/AI/announce.ogg");
        var aresExecutionTitle = Loc.GetString("rmc-battlefield-execute-ares-announce");
        if (user.NpcFactionMember.factions != UNMC) // Change
            return;
        else
        {
            _marineAnnounce.AnnounceARES(default, aresExecutionText, aresExecutionAudio, aresExecutionTitle);
        }

        _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)} executed {ToPrettyString(target)}.");
        ExecuteTarget(user, target, heldItem, examineText);
    }

    /// <summary>
    /// Generic Function to Execute a person using a weapon with ammo.
    /// Handles reducing ammo, playing sounds, damaging, preventing revival, and admin logging. Functions using this should provide additional logging for start and completion of the execution.
    /// </summary>
    public void ExecuteTarget(EntityUid user, EntityUid target, EntityUid heldItem, string? examineText)
    {
        if (!TryComp(heldItem, out GunComponent? gun))
        {
            _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)} failed to execute {ToPrettyString(target)}: because they had no valid gun."); // Change, again.
            return;
        }

        var ammo = new List<(EntityUid? Entity, IShootable Shootable)>();
        var ev = new TakeAmmoEvent(1, ammo, Transform(user).Coordinates, user);
        RaiseLocalEvent(heldItem, ev);

        if (ev.Ammo.Count == 0)
        {
            _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)} failed to execute {ToPrettyString(target)} because {ToPrettyString(heldItem)} had no ammo.");
            _audio.PlayPredicted(gun.SoundEmpty, heldItem, user);
            return;
        }

        foreach (var (bullet, _) in ev.Ammo)
        {
            QueueDel(bullet);
        }

        _damageable.TryChangeDamage(target, target.Comp.Damage, true);
        _mobState.ChangeMobState(target, MobState.Dead);
        EnsureComp<RMCExecutedComponent>(target);
        EnsureComp<CMDefibrillatorBlockedComponent>(target);
        _audio.PlayPredicted(gun.SoundGunshot, heldItem, user);

        if (examineText != null)
        {
            args.PushMarkup(target.BattlefieldExecutionWeaponComponent.ExmaineText);
        }
    }

    private void OnceExecutedUpdateMobState(Entity<RMCExecutedComponent> ent, ref UpdateMobStateEvent args)
    {
        args.State = MobState.Dead;
    }
}

// TODO:
// Change location of "Content.Shared/_RMC14/Suicide/RMCExecutedComponent.cs" to be not so terrible.
// Update "RMCSuicideDoAfterEvent" to be generic to execution.
