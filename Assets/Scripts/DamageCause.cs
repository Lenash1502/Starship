// What killed something, so death-effect systems (see DeathEffects) can pick an appropriate
// visual per cause. Add a new case here whenever a new weapon type is introduced.
public enum DamageCause
{
    Unknown,
    Collision,
    LaserWeapon,
    RocketWeapon,
    BallisticWeapon,
}
