namespace SiagroB1.Domain.Dtos.Common;

public class NavigationItemDto
{
    public string Title { get; set; } = string.Empty;

    public string? Key { get; set; }

    public bool Enabled { get; set; }

    public bool Expanded { get; set; }

    public string Icon { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = [];

    public List<NavigationItemDto>? Items { get; set; }
}