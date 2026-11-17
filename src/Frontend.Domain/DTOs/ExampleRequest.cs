using System.ComponentModel.DataAnnotations;

namespace Frontend.Domain.DTOs;

// Mirrors ExampleRequest in the backend services. Replace with real contracts.
public class ExampleRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}
