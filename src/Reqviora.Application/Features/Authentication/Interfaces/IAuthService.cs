using Reqviora.Application.Common.Models;
using Reqviora.Application.Features.Authentication.DTOs;

namespace Reqviora.Application.Features.Authentication.Interfaces;

public interface IAuthService
{
    Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<AuthResponseDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<AuthResponseDto>> RefreshAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(RefreshTokenRequestDto request, CancellationToken cancellationToken = default);
    Task<Result<UserProfileDto>> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
