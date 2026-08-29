using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KobareoCalendar.Infrastructure;

public sealed class DatabasePasswordValidator(AppDbContext db)
    : IPasswordValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        var policy = await db.PasswordPolicies.AsNoTracking().SingleOrDefaultAsync()
            ?? new PasswordPolicyEntity();
        var value = password ?? string.Empty;
        var errors = new List<IdentityError>();

        if (value.Length < policy.RequiredLength)
            errors.Add(Error("DatabasePasswordTooShort", $"パスワードは{policy.RequiredLength}文字以上で入力してください。"));
        if (policy.RequireDigit && !value.Any(char.IsDigit))
            errors.Add(Error("DatabasePasswordRequiresDigit", "パスワードには数字を1文字以上含めてください。"));
        if (policy.RequireLowercase && !value.Any(char.IsLower))
            errors.Add(Error("DatabasePasswordRequiresLower", "パスワードには小文字を1文字以上含めてください。"));
        if (policy.RequireUppercase && !value.Any(char.IsUpper))
            errors.Add(Error("DatabasePasswordRequiresUpper", "パスワードには大文字を1文字以上含めてください。"));
        if (policy.RequireNonAlphanumeric && value.All(char.IsLetterOrDigit))
            errors.Add(Error("DatabasePasswordRequiresNonAlphanumeric", "パスワードには記号を1文字以上含めてください。"));

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    private static IdentityError Error(string code, string description) =>
        new() { Code = code, Description = description };
}
