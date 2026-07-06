using Microsoft.Extensions.Options;
using TG_DispatchLogic_Test2.Components;
using TG_DispatchLogic_Test2.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDevExpressBlazor(options => {
    options.BootstrapVersion = DevExpress.Blazor.BootstrapVersion.v5;
    options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
});

builder.Services.AddMvc();

builder.Services.Configure<AmrApiOptions>(
    builder.Configuration.GetSection(AmrApiOptions.SectionName));
builder.Services.Configure<EquipSimOptions>(
    builder.Configuration.GetSection(EquipSimOptions.SectionName));
builder.Services.AddHttpClient<AmrApiClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<AmrApiOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<SimulateCodeApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<SimulateCodeCatalogService>();
builder.Services.AddSingleton<ModbusEquipPollService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();
