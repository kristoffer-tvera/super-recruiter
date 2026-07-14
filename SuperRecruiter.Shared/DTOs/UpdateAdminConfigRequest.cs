namespace SuperRecruiter.Shared.DTOs;

public class UpdateAdminConfigRequest
{
    public int BossKills { get; set; }
    public List<string> AcceptedClasses { get; set; } = [];
}
