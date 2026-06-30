namespace TG_DispatchLogic_Test2.Services;

public class AmrApiOptions
{
    public const string SectionName = "AmrApi";

    public string BaseUrl { get; set; } = "http://172.25.90.153:8080";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin";
}
