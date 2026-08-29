namespace KobareoCalendar.Domain;

public static class AdministrationRules
{
    public static void ValidateUserStateChange(Guid actorId, Guid targetId, bool targetIsAdmin, int activeAdminCount, bool requestedActive, UserRole requestedRole)
    {
        if (actorId == targetId && !requestedActive) throw new DomainException("自分自身は無効化できません。");
        if (targetIsAdmin && (!requestedActive || requestedRole != UserRole.Admin) && activeAdminCount <= 1)
            throw new DomainException("有効な管理者を0人にはできません。");
    }
}
