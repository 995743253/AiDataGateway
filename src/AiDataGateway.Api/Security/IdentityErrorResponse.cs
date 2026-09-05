using Microsoft.AspNetCore.Identity;

namespace AiDataGateway.Api.Security;

internal static class IdentityErrorResponse
{
    public static IResult BadRequest(IdentityResult result)
    {
        var errors = result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(Localize).Distinct().ToArray());
        var message = string.Join("；", errors.Values.SelectMany(item => item));
        return Results.BadRequest(new { message, errors });
    }

    private static string Localize(IdentityError error) => error.Code switch
    {
        "InvalidUserName" => "用户名只能包含英文字母、数字以及 . - _ @ + 字符。",
        "InvalidEmail" => "请输入有效的邮箱地址。",
        "DuplicateUserName" => "该用户名已存在。",
        "DuplicateEmail" => "该邮箱已被使用。",
        "PasswordTooShort" => "密码长度至少为 6 位。",
        "PasswordRequiresDigit" => "密码必须包含数字。",
        "PasswordRequiresLower" => "密码必须包含小写字母。",
        "PasswordRequiresUpper" => "密码必须包含大写字母。",
        "PasswordRequiresNonAlphanumeric" => "密码必须包含特殊字符。",
        "PasswordRequiresUniqueChars" => "密码中不同字符的数量不足。",
        _ => error.Description
    };
}
