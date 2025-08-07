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
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SkillsSystem _skills = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BattlefieldExecutionComponent, GetVerbsEvent<Verb>>(OnBEGetVerbs);
    }

    private void OnBEGetVerbs(Entity<BattlefieldExecutionComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract)
            return;

        var user = args.User;
        if (user != args.Target)
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
                var ev = new RMCSuicideDoAfterEvent(); // Change(?)
                var doAfter = new DoAfterArgs(EntityManager, user, executionComp.Delay, ev, target)
                {
                    BreakOnMove = true,
                    NeedHand = true,
                    BreakOnHandChange = true,
                    ForceVisible = true,
                };

                // TODO: Global Announcement for execution. If apart of the UN faction, this should be done through ARES. If else, nothing? (For like, CLF/SPP commander edge cases?)

                if (_doAfter.TryStartDoAfter(doAfter))
                {
                    _admin.Add(LogType.RMCSuicide, LogImpact.High, $"{ToPrettyString(user)} started to suicide."); // Change
                    var selfMsg = Loc.GetString("rmc-suicide-start-self"); // Change
                    var othersMsg = Loc.GetString("rmc-suicide-start-others", ("user", user)); // Change
                    _popup.PopupPredicted(selfMsg, othersMsg, user, user, PopupType.LargeCaution); // Change
                }
            },
        });
    }
}
