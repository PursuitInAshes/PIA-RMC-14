using Content.Shared._RMC14.Medical.Defibrillator;
using Content.Shared._RMC14.Suicide;
using Content.Shared._RMC14.Marines.Skills;
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
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Weapons.Ranged;

public sealed class BattlefieldExecutionSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BattlefieldExecutionComponent, GetVerbsEvent<Verb>>(OnBEGetVerbs);
        SubscribeLocalEvent<BattlefieldExecutionComponent, RMCSuicideDoAfterEvent>(OnBEDoAfter); // Change "RMCSuicideDoAfterEvent"
        SubscribeLocalEvent<RMCExecutedComponent, UpdateMobStateEvent>(OnceExecutedUpdateMobState);
    }

    private void OnBEGetVerbs(Entity<BattlefieldExecutionComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract)
            return;

        var user = args.User;
        if (user == args.Target)
            return;

        if (!TryComp<BattlefieldExecutionComponent>(ent, out var executionComp))
            return;

        var target = args.Target;
        if (executionComp.RequiredSkills != null && !_skills.HasAllSkills(args.User, executionComp.RequiredSkills))
            return;

        args.Verbs.Add(new Verb
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
                    var othersMsg = Loc.GetString("rmc-battlefield-execute-start-others", ("user", user, "target", target));
                    _popup.PopupPredicted(selfMsg, othersMsg, user, user, PopupType.LargeCaution);
                }
            },
        });
    }

    private void OnBEDoAfter(Entity<BattlefieldExecutionComponent> ent, ref RMCSuicideDoAfterEvent args) // Change event?
    {
        var user = args.User;
        var target = args.Target;
        if (args.Cancelled)
        {
            _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)}'s execution of {ToPrettyString(target)} was cancelled.");
            var selfMsg = Loc.GetString("rmc-battlefield-execute-cancel-self");
            var othersMsg = Loc.GetString("rmc-battlefield-execute-cancel-others", ("user", user));
            _popup.PopupPredicted(selfMsg, othersMsg, user, user, PopupType.MediumCaution);
            return;
        }

        // Announcements
        // TODO: Global Announcement for execution. If apart of the UN faction, this should be done through ARES. If else, nothing? (For like, CLF/SPP commander edge cases?)

        // TODO: Activate the generic "Execute" function.
    }

    /// <summary>
    /// Generic Function to Execute a person using a weapon with ammo.
    /// Handles reducing ammo, playing sounds, damaging, preventing revival, and admin logging. Functions using this should provide additional logging (as seen above)
    /// and handle their own popups if required.
    /// Accepts: user, target, held, ?examinetext
    /// </summary>
    private void Execute()
    {
        if (_hands.GetActiveItem(user) is not { } held || !TryComp(held, out GunComponent? gun))
        {
            _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)} failed to execute {ToPrettyString(target)}: because they had no valid gun."); // Change, again.
            return;
        }

        var ammo = new List<(EntityUid? Entity, IShootable Shootable)>();
        var ev = new TakeAmmoEvent(1, ammo, Transform(user).Coordinates, user);
        RaiseLocalEvent(held, ev);

        if (ev.Ammo.Count == 0)
        {
            _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)} failed to execute {ToPrettyString(target)} because {ToPrettyString(held)} had no ammo.");
            _audio.PlayPredicted(gun.SoundEmpty, held, ent);
            return;
        }

        foreach (var (bullet, _) in ev.Ammo)
        {
            QueueDel(bullet);
        }

        _admin.Add(LogType.RMCExecution, LogImpact.High, $"{ToPrettyString(user)} has executed {ToPrettyString(target)} using {ToPrettyString(held)}.");
        _damageable.TryChangeDamage(target, target.Comp.Damage, true);
        _mobState.ChangeMobState(target, MobState.Dead);
        EnsureComp<RMCExecutedComponent>(target);
        EnsureComp<CMDefibrillatorBlockedComponent>(target);
        _audio.PlayPredicted(gun.SoundGunshot, held, ent);

        if (examinetext != null)
        {
            args.PushMarkup(target.Comp.Examine);
        }
    }

    // This + the localevent subscribing to it cause any updates to the state of the mob to result in it being dead again.
    // Is there some way to reasonably move this into the generic "Execute" function?
    private void OnceExecutedUpdateMobState(Entity<RMCExecutedComponent> ent, ref UpdateMobStateEvent args)
    {
        args.State = MobState.Dead;
    }
}

// TODO:
// Change location of "Content.Shared/_RMC14/Suicide/RMCExecutedComponent.cs" to be so terrible.
// Update "RMCSuicideDoAfterEvent" to be generic.
