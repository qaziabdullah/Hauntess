using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;
using System.Drawing;
using System.Reflection;

namespace Hauntess;

public class Hauntess : BasePlugin
{
    public override string ModuleName => "Hauntess_Darkness_System";
    public override string ModuleVersion => "1.3.0";

    private CFogController? _masterFogController;

    public override void Load(bool hotReload)
    {
        // Sync values 64 times a second to prevent Workshop maps from resetting fog
        RegisterListener<Listeners.OnTick>(OnTick);
        
        RegisterListener<Listeners.OnMapStart>(mapName => {
            _masterFogController = null;
        });

        if (hotReload) ApplyHauntedAtmosphere();
    }

    public void OnTick()
    {
        if (_masterFogController == null || !_masterFogController.IsValid)
        {
            _masterFogController = GetOrCreateMasterFog();
        }

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot) continue;

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            // Force the player's camera to use Hauntess
            pawn.AcceptInput("SetFogController", _masterFogController, null, "!activator");

            // Sync memory values to override map defaults
            CFogController? currentFog = pawn.CameraServices?.PlayerFog?.Ctrl.Value;
            if (currentFog != null && _masterFogController != null)
            {
                CopyValues(pawn.Skybox3d.Fog, _masterFogController.Fog);
                SetStateChangeFogparams(pawn, "CBasePlayerPawn", "m_skybox3d", Schema.GetSchemaOffset("sky3dparams_t", "fog"));
            }
        }
    }

    private void ApplyHauntedAtmosphere()
    {
        _masterFogController = GetOrCreateMasterFog();

        // 1. NEUTRALIZE WORKSHOP OVERRIDES
        // Disable Gradient Fog (The #1 reason Workshop maps stay bright)
        foreach (var gfog in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("env_gradient_fog"))
        {
            gfog.AcceptInput("Disable");
        }

        // Disable Cubemap Fog (Stops the 'grey glow' look)
        foreach (var cfog in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("env_cubemap_fog"))
        {
            cfog.AcceptInput("Disable");
        }

        // Disable Post Processing (Stops exposure/brightness overrides)
        foreach (var pp in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("post_processing_volume"))
        {
            pp.AcceptInput("Disable");
        }

        // 2. APPLY TO ALL CURRENT PAWNS
        foreach (var player in Utilities.GetPlayers())
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn != null && pawn.IsValid)
            {
                pawn.AcceptInput("SetFogController", _masterFogController, null, "!activator");
            }
        }
        
        Console.WriteLine("[Hauntess] All Workshop light entities neutralized and Darkness applied.");
    }

    private CFogController? GetOrCreateMasterFog()
    {
        string name = "Hauntess_Master_Fog";
        var existing = Utilities.FindAllEntitiesByDesignerName<CFogController>("env_fog_controller")
            .FirstOrDefault(e => e != null && e.IsValid && e.Entity?.Name == name);
        
        if (existing != null) return existing;

        CFogController? fog = Utilities.CreateEntityByName<CFogController>("env_fog_controller");
        if (fog == null) return null;

        fog.Entity!.Name = name;
        fog.DispatchSpawn();

        // --- HAUNTED HOUSE SETTINGS ---
        fogparams_t p = fog.Fog;
        p.Enable = true;
        p.ColorPrimary = Color.FromArgb(255, 2, 2, 4); // Deep Black
        p.Start = 0.0f;
        p.End = 350.0f;        // 70% Darkness distance
        p.Maxdensity = 1.0f;  // Nearly opaque
        p.Exponent = 1.5f;

        // Ensure players don't 'glow' in the dark
        var visibility = Utilities.FindAllEntitiesByDesignerName<CPlayerVisibility>("env_player_visibility").FirstOrDefault();
        if (visibility != null)
        {
            visibility.FogMaxDensityMultiplier = 1.0f; 
            Utilities.SetStateChanged(visibility, "CPlayerVisibility", "m_flFogMaxDensityMultiplier");
        }

        SetStateChangeFogparams(fog, "CFogController", "m_fog");
        return fog;
    }

    [ConsoleCommand("css_haunt", "Force haunted atmosphere on current map")]
    public void OnHauntCommand(CCSPlayerController? player, CommandInfo command)
    {
        ApplyHauntedAtmosphere();
        player?.PrintToChat(" [Hauntess] Darkness has been forced. (Gradient/Cubemap/Post-Process disabled)");
    }

    // --- MEMORY HELPERS ---
    public static void SetStateChangeFogparams(CBaseEntity entity, string className, string fieldName, int extraOffset = 0)
    {
        string[] fields = { "start", "end", "maxdensity", "enable", "colorPrimary", "exponent" };
        foreach (string field in fields)
        {
            Utilities.SetStateChanged(entity, className, fieldName, extraOffset + Schema.GetSchemaOffset("fogparams_t", field));
        }
    }

    public static void CopyValues<T>(T self, T other) where T : NativeObject
    {
        foreach (PropertyInfo property in self.GetType().GetProperties())
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(self, property.GetValue(other));
            }
        }
    }
}