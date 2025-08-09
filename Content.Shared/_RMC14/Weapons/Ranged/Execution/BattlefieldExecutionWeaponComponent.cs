using Content.Shared._RMC14.Marines.Skills;
using Content.Shared.Damage;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Weapons.Ranged.Execution;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(BattlefieldExecutionSystem))]
public sealed partial class BattlefieldExecutionWeaponComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    [DataField, AutoNetworkedField]
    public Dictionary<EntProtoId<SkillDefinitionComponent>, int>? RequiredSkills;

    [DataField, AutoNetworkedField]
    public string ExmaineText = "rmc-battlefield-executed-examine";
}
