using Content.Shared._RMC14.Medical.Defibrillator;
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

namespace Content.Shared._RMC14.Suicide;

// this ignores the suicide cvar since we don't want upstream suicides
public sealed class RMCSuicideSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCSuicideComponent, GetVerbsEvent<Verb>>(OnSuicideGetVerbs);
        SubscribeLocalEvent<RMCSuicideComponent, RMCSuicideDoAfterEvent>(OnSuicideDoAfter);
        SubscribeLocalEvent<RMCExecutedComponent, UpdateMobStateEvent>(OnHasSuicidedUpdateMobState);
    }

    private void OnSuicideGetVerbs(Entity<RMCSuicideComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract)
            return;

        var user = args.User;
        if (user != args.Target || args.Hands is not { } hands)
            return;

        if (!_hands.TryGetActiveItem(args.Target, out var active) ||
            !HasComp<GunComponent>(active))
        {
            return;
        }

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("rmc-suicide"),
            Act = () =>
            {
                var time = _timing.CurTime;
                if (time < ent.Comp.LastAttempt + ent.Comp.Cooldown)
                {
                    _popup.PopupClient(Loc.GetString("rmc-suicide-fumble-self"), user, user, PopupType.SmallCaution);
                    return;
                }

                ent.Comp.LastAttempt = time;

                var ev = new RMCSuicideDoAfterEvent();
                var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.Delay, ev, user)
                {
                    BreakOnMove = true,
                    NeedHand = true,
                    BreakOnHandChange = true,
                    ForceVisible = true,
                };

                if (_doAfter.TryStartDoAfter(doAfter))
                {
                    _admin.Add(LogType.RMCSuicide, LogImpact.High, $"{ToPrettyString(user)} started to suicide.");
                    var selfMsg = Loc.GetString("rmc-suicide-start-self");
                    var othersMsg = Loc.GetString("rmc-suicide-start-others", ("user", user));
                    _popup.PopupPredicted(selfMsg, othersMsg, user, user, PopupType.LargeCaution);
                }
            },
        });
    }

    private void OnSuicideDoAfter(Entity<RMCSuicideComponent> ent, ref RMCSuicideDoAfterEvent args)
    {
        var user = args.User;
        if (args.Cancelled)
        {
            _admin.Add(LogType.RMCSuicide, LogImpact.High, $"{ToPrettyString(user)}'s suicide was cancelled.");
            var selfMsg = Loc.GetString("rmc-suicide-cancel-self");
            var othersMsg = Loc.GetString("rmc-suicide-cancel-others", ("user", user));
            _popup.PopupPredicted(selfMsg, othersMsg, user, user, PopupType.MediumCaution);
            return;
        }

        // TODO: Activate the generic "Execute" function.
    }

    private void OnHasSuicidedUpdateMobState(Entity<RMCExecutedComponent> ent, ref UpdateMobStateEvent args)
    {
        args.State = MobState.Dead;
    }
}
