using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Application.Features.Authentication.Services;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Application.Features.Projects.Services;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration;
using Reqviora.Domain.Entities;

namespace Reqviora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services
        .AddScoped<IPasswordHasher<User>, PasswordHasher<User>>()
        .AddScoped<IAuthService, AuthService>()
        .AddScoped<IProjectService, ProjectService>()
        .AddScoped<RequirementReviewWorkflow>()
        .AddSingleton<ISourceEvidenceMapper, DocumentSourceEvidenceMapper>()
        .AddSingleton<IDesignComponentRegistry, Day03StarterComponentRegistry>()
        .AddScoped<IBlueprintDraftGenerator, BlueprintDraftGenerator>()
        .AddScoped<SafeRegenerationService>();
}
