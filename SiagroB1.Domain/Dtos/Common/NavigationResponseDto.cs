namespace SiagroB1.Domain.Dtos.Common;

public class NavigationResponseDto
{
    public List<NavigationItemDto> Navigation { get; set; } = [];
    public List<NavigationItemDto> FixedNavigation { get; set; } = [];
}