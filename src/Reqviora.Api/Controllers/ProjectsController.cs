using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.Projects.Interfaces;

namespace Reqviora.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public sealed class ProjectsController(IProjectService projects) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyCollection<ProjectResponseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return TryGetUserId(out var userId)
            ? Ok(await projects.GetAllAsync(userId, cancellationToken))
            : Unauthorized();
    }

    [HttpPost]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateProjectDto request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var project = await projects.CreateAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await projects.GetByIdAsync(id, userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : NotFound();
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProjectResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateProjectDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var result = await projects.UpdateAsync(id, request, userId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : NotFound();
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}
