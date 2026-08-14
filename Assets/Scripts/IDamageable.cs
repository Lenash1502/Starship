public interface IDamageable
{
    void TakeDamage(float amount, DamageCause cause = DamageCause.Unknown);
}
