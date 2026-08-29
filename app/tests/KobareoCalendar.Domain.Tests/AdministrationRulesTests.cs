using KobareoCalendar.Domain;

namespace KobareoCalendar.Domain.Tests;

public class AdministrationRulesTests
{
    [Fact] public void Cannot_deactivate_self() { var id = Guid.NewGuid(); Assert.Throws<DomainException>(() => AdministrationRules.ValidateUserStateChange(id, id, true, 2, false, UserRole.Admin)); }
    [Fact] public void Cannot_demote_last_active_admin() => Assert.Throws<DomainException>(() => AdministrationRules.ValidateUserStateChange(Guid.NewGuid(), Guid.NewGuid(), true, 1, true, UserRole.User));
    [Fact] public void Can_deactivate_admin_when_another_admin_remains() => AdministrationRules.ValidateUserStateChange(Guid.NewGuid(), Guid.NewGuid(), true, 2, false, UserRole.Admin);
}
