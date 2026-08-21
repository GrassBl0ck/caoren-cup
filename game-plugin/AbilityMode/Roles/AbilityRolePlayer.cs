using System.Numerics;

namespace Caoren.AbilityMode;

public sealed class AbilityRolePlayer(AbilitySeatState seat)
{
    public AbilitySeatState Seat { get; } = seat;
    public string DisplayName { get; set; } = seat.PlayerId;
    public bool IsAlive { get; set; }
    public bool IsConnected { get; set; } = true;
    public int BaseHealth { get; set; }
    public int RoleMaxHealth { get; set; } = 100;
    public int TemporaryHealth { get; set; }
    public int Money { get; set; }
    public Vector3 Position { get; set; }
    public bool HasManagedHealthSnapshot { get; set; }
}
