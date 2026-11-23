using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Api.Controllers;
using Reqviora.Application.Common.Models;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.Projects.Interfaces;
using Xunit;

namespace Reqviora.Application.Tests.Projects;

public sealed class ProjectsControllerTests
{
    [Fact]
    public async Task Create_uses_authenticated_user_as_owner_and_returns_created()
    {
        var service = new FakeProjectService();
        var ownerId = Guid.NewGuid();
        var controller = CreateController(service, ownerId);

        var response = await controller.Create(
            new CreateProjectDto("Coffee Shop", "Ordering"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(ownerId, service.LastOwnerId);
        Assert.Equal(nameof(ProjectsController.GetById), created.ActionName);
    }

    [Fact]
    public async Task GetById_returns_not_found_when_service_hides_foreign_project()
    {
        var service = new FakeProjectService { ReturnNotFound = true };
        var controller = CreateController(service, Guid.NewGuid());

        var response = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(response);
    }

    [Fact]
    public async Task List_returns_unauthorized_when_identity_has_no_user_id()
    {
        var service = new FakeProjectService();
        var controller = new ProjectsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var response = await controller.GetAll(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response);
    }

    private static ProjectsController CreateController(FakeProjectService service, Guid ownerId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, ownerId.ToString())],
            "Test");
        return new ProjectsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private sealed class FakeProjectService : IProjectService
    {
        public Guid LastOwnerId { get; private set; }
        public bool ReturnNotFound { get; init; }

        public Task<IReadOnlyCollection<ProjectResponseDto>> GetAllAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            LastOwnerId = ownerId;
            return Task.FromResult<IReadOnlyCollection<ProjectResponseDto>>([]);
        }

        public Task<ProjectResponseDto> CreateAsync(
            CreateProjectDto request,
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            LastOwnerId = ownerId;
            return Task.FromResult(CreateResponse(ownerId));
        }

        public Task<Result<ProjectResponseDto>> GetByIdAsync(
            Guid projectId,
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            LastOwnerId = ownerId;
            return Task.FromResult(ReturnNotFound
                ? Result<ProjectResponseDto>.Failure("Project was not found.")
                : Result<ProjectResponseDto>.Success(CreateResponse(ownerId, projectId)));
        }

        public Task<Result<ProjectResponseDto>> UpdateAsync(
            Guid projectId,
            UpdateProjectDto request,
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ProjectResponseDto>.Success(CreateResponse(ownerId, projectId)));

        private static ProjectResponseDto CreateResponse(Guid ownerId, Guid? projectId = null) => new(
            projectId ?? Guid.NewGuid(),
            "Coffee Shop",
            "Ordering",
            ownerId,
            DateTimeOffset.UtcNow,
            null);
    }
}
