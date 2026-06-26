using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using GarionX.Repositories;
using GarionX.Dtos;

namespace GarionX.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PersonalitiesController : ControllerBase
{
    private readonly IChatRepository _chatRepository;

    public PersonalitiesController(IChatRepository chatRepository)
    {
        _chatRepository = chatRepository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PersonalityDto>>> GetPersonalities()
    {
        var personalities = await _chatRepository.GetPersonalitiesAsync();
        var dtos = personalities.Select(p => new PersonalityDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            SystemPrompt = p.SystemPrompt,
            AvatarUrl = p.AvatarUrl
        });

        return Ok(dtos);
    }

    [HttpPost]
    public async Task<ActionResult<PersonalityDto>> CreatePersonality([FromBody] CreatePersonalityRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return BadRequest("Name and SystemPrompt are required.");
        }

        var id = "custom_" + Guid.NewGuid().ToString("N")[..8];
        var personality = new Entities.Personality
        {
            Id = id,
            Name = request.Name,
            Description = string.IsNullOrWhiteSpace(request.Description) 
                ? $"Custom agent: {request.Name}" 
                : request.Description,
            SystemPrompt = request.SystemPrompt,
            AvatarUrl = $"https://api.dicebear.com/7.x/bottts/svg?seed={Uri.EscapeDataString(request.Name)}"
        };

        await _chatRepository.CreatePersonalityAsync(personality);

        var dto = new PersonalityDto
        {
            Id = personality.Id,
            Name = personality.Name,
            Description = personality.Description,
            SystemPrompt = personality.SystemPrompt,
            AvatarUrl = personality.AvatarUrl
        };

        return CreatedAtAction(nameof(GetPersonalities), new { id = dto.Id }, dto);
    }
}
