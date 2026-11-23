using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Application.Features.Projects.Services;
using Reqviora.Domain.Entities;
using Xunit;

namespace Reqviora.Application.Tests.Projects;

public sealed class ProjectServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_assigns_authenticated_owner_and_persists_project()
    {
        var fixture = new Fixture();
        var ownerId = Guid.NewGuid();

        var result = await fixture.Service.CreateAsync(
            new CreateProjectDto("  Coffee Shop  ", "  Ordering workspace  ", " Retail ", " Diners ",
                " SaaS ", " Web ", " .NET ", " PCI compliant ", " Stripe "),
            ownerId);

        var project = Assert.Single(fixture.Repository.Projects);
        Assert.Equal(ownerId, project.OwnerId);
        Assert.Equal("Coffee Shop", project.Name);
        Assert.Equal("Ordering workspace", project.Description);
        Assert.Equal("Retail", result.Industry);
        Assert.Equal("Diners", result.TargetAudience);
        Assert.Equal("SaaS", result.ProjectType);
        Assert.Equal("web", result.PrimaryPlatform);
        Assert.Equal(".NET", result.TechnologyPreferences);
        Assert.Equal("PCI compliant", result.Constraints);
        Assert.Equal("Stripe", result.Integrations);
        Assert.Equal(Now, project.CreatedAt);
        Assert.Equal(ownerId.ToString(), project.CreatedBy);
        Assert.Equal(project.Id, result.Id);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task List_returns_only_projects_owned_by_authenticated_user()
    {
        var fixture = new Fixture();
        var ownerId = Guid.NewGuid();
        fixture.Repository.Projects.AddRange(
            CreateProject(ownerId, "Owner project"),
            CreateProject(Guid.NewGuid(), "Another user's project"));

        var result = await fixture.Service.GetAllAsync(ownerId);

        var project = Assert.Single(result);
        Assert.Equal("Owner project", project.Name);
        Assert.Equal(ownerId, project.OwnerId);
    }

    [Fact]
    public async Task Get_returns_not_found_for_another_users_project()
    {
        var fixture = new Fixture();
        var project = CreateProject(Guid.NewGuid(), "Private project");
        fixture.Repository.Projects.Add(project);

        var result = await fixture.Service.GetByIdAsync(project.Id, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("Project was not found.", result.Error);
    }

    [Fact]
    public async Task Update_changes_owner_project_and_persists_audit_fields()
    {
        var fixture = new Fixture();
        var ownerId = Guid.NewGuid();
        var project = CreateProject(ownerId, "Old name");
        fixture.Repository.Projects.Add(project);

        var result = await fixture.Service.UpdateAsync(
            project.Id,
            new UpdateProjectDto("  New name  ", "  New description  ", " Finance ", " Analysts ",
                " Internal ", " MOBILE ", " C# ", " Offline ", " Entra ID "),
            ownerId);

        Assert.True(result.Succeeded);
        Assert.Equal("New name", project.Name);
        Assert.Equal("New description", project.Description);
        Assert.Equal("Finance", project.Industry);
        Assert.Equal("Analysts", project.TargetAudience);
        Assert.Equal("Internal", project.ProjectType);
        Assert.Equal("mobile", project.PrimaryPlatform);
        Assert.Equal("C#", project.TechnologyPreferences);
        Assert.Equal("Offline", project.Constraints);
        Assert.Equal("Entra ID", project.Integrations);
        Assert.Equal(Now, project.LastModifiedAt);
        Assert.Equal(ownerId.ToString(), project.LastModifiedBy);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_does_not_change_or_save_another_users_project()
    {
        var fixture = new Fixture();
        var project = CreateProject(Guid.NewGuid(), "Private project");
        fixture.Repository.Projects.Add(project);

        var result = await fixture.Service.UpdateAsync(
            project.Id,
            new UpdateProjectDto("Stolen", "Should not be written"),
            Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("Private project", project.Name);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    private static Project CreateProject(Guid ownerId, string name) => new()
    {
        OwnerId = ownerId,
        Name = name,
        Description = "Description",
        CreatedAt = Now.AddDays(-1)
    };

    private sealed class Fixture
    {
        public FakeProjectRepository Repository { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public ProjectService Service { get; }

        public Fixture()
        {
            Service = new ProjectService(
                Repository,
                UnitOfWork,
                new FixedTimeProvider(Now));
        }
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        public List<Project> Projects { get; } = [];

        public Task<IReadOnlyCollection<Project>> GetAllAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Project>>(
                Projects.Where(project => project.OwnerId == ownerId).ToArray());

        public Task<Project?> GetByIdAsync(
            Guid projectId,
            Guid ownerId,
            bool requireOwner = false,
            bool trackChanges = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Projects.SingleOrDefault(project =>
                project.Id == projectId && project.OwnerId == ownerId));

        public Task AddAsync(Project project, CancellationToken cancellationToken = default)
        {
            Projects.Add(project);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
