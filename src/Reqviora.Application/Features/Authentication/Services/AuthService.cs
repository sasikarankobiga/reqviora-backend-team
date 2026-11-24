using Microsoft.AspNetCore.Identity;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Common.Models;
using Reqviora.Application.Features.Authentication.DTOs;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Authentication.Services;

public sealed class AuthService(
    IUserRepository users,
    ITokenService tokens,
    IPasswordHasher<User> passwordHasher,
    ILoginSecurityService loginSecurity,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAuthService
{
    public AuthService(IUserRepository users, ITokenService tokens, IPasswordHasher<User> passwordHasher,
        IUnitOfWork unitOfWork, TimeProvider timeProvider)
        : this(users, tokens, passwordHasher, NoLoginSecurity.Instance, unitOfWork, timeProvider) { }

    public async Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        var ipAddress = request.IpAddress ?? "unknown";
        var deviceId = request.DeviceId ?? "unknown";
        if (user is null)
        {
            await loginSecurity.RecordAsync(null, NormalizeEmail(request.Email), ipAddress, deviceId, false, false, cancellationToken);
            return Result<AuthResponseDto>.Failure("Invalid email or password.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await loginSecurity.RecordAsync(user, user.NormalizedEmail, ipAddress, deviceId, false, false, cancellationToken);
            return Result<AuthResponseDto>.Failure("Invalid email or password.");
        }

        if (!user.EmailVerified)
        {
            await loginSecurity.RecordAsync(
                user,
                user.NormalizedEmail,
                ipAddress,
                deviceId,
                false,
                false,
                cancellationToken);

            return Result<AuthResponseDto>.Failure(
                "Email verification is required.");
        }

        var otpRequired = await loginSecurity.RequiresOtpAsync(user, ipAddress, deviceId, cancellationToken);
        if (otpRequired && (string.IsNullOrWhiteSpace(request.OtpCode) || !loginSecurity.ValidateOtp(user, request.OtpCode)))
        {
            await loginSecurity.RecordAsync(user, user.NormalizedEmail, ipAddress, deviceId, false, true, cancellationToken);
            return Result<AuthResponseDto>.Failure("One-time password required.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        var response = await CreateSessionAsync(user, cancellationToken);
        await loginSecurity.RecordAsync(user, user.NormalizedEmail, ipAddress, deviceId, true, otpRequired, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AuthResponseDto>.Success(response);
    }

    public async Task<Result<AuthResponseDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        if (await users.GetByEmailAsync(normalizedEmail, cancellationToken) is not null)
            return Result<AuthResponseDto>.Failure("Email is already registered.");

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            EmailVerified = false
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        await users.AddAsync(user, cancellationToken);
        var response = await CreateSessionAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AuthResponseDto>.Success(response);
    }

    public async Task<Result<AuthResponseDto>> RefreshAsync(
        RefreshTokenRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = tokens.HashRefreshToken(request.RefreshToken);
        var existingToken = await users.GetRefreshTokenByHashAsync(tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (existingToken is null || !existingToken.IsActive(now))
            return Result<AuthResponseDto>.Failure("Invalid or expired refresh token.");

        if (!existingToken.User.EmailVerified)
        {
            return Result<AuthResponseDto>.Failure(
                "Email verification is required.");
        }

        var newRefreshToken = tokens.CreateRefreshToken();
        existingToken.RevokedAt = now;
        existingToken.ReplacedByTokenHash = newRefreshToken.TokenHash;
        await users.AddRefreshTokenAsync(ToEntity(existingToken.UserId, newRefreshToken), cancellationToken);

        var accessToken = tokens.CreateAccessToken(existingToken.User);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<AuthResponseDto>.Success(ToResponse(existingToken.User, accessToken, newRefreshToken));
    }

    public async Task<Result> LogoutAsync(
        RefreshTokenRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = tokens.HashRefreshToken(request.RefreshToken);
        var existingToken = await users.GetRefreshTokenByHashAsync(tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (existingToken is not null && existingToken.IsActive(now))
        {
            existingToken.RevokedAt = now;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<UserProfileDto>> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null
            ? Result<UserProfileDto>.Failure("User was not found.")
            : Result<UserProfileDto>.Success(new UserProfileDto(user.Id, user.Email, user.DisplayName, user.EmailVerified));
    }

    private async Task<AuthResponseDto> CreateSessionAsync(User user, CancellationToken cancellationToken)
    {
        var accessToken = tokens.CreateAccessToken(user);
        var refreshToken = tokens.CreateRefreshToken();
        await users.AddRefreshTokenAsync(ToEntity(user.Id, refreshToken), cancellationToken);
        return ToResponse(user, accessToken, refreshToken);
    }

    private static RefreshToken ToEntity(Guid userId, RefreshTokenResult token) => new()
    {
        UserId = userId,
        TokenHash = token.TokenHash,
        ExpiresAt = token.ExpiresAt
    };

    private static AuthResponseDto ToResponse(
        User user,
        AccessTokenResult accessToken,
        RefreshTokenResult refreshToken) => new(
            user.Id,
            user.Email,
            user.DisplayName,
            accessToken.Token,
            accessToken.ExpiresAt,
            refreshToken.Token,
            refreshToken.ExpiresAt);

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private sealed class NoLoginSecurity : ILoginSecurityService
    {
        public static readonly NoLoginSecurity Instance = new();
        public Task<bool> RequiresOtpAsync(User user, string ipAddress, string deviceId, CancellationToken cancellationToken) => Task.FromResult(false);
        public bool ValidateOtp(User user, string code) => true;
        public Task RecordAsync(User? user, string normalizedEmail, string ipAddress, string deviceId, bool succeeded,
            bool otpRequired, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
