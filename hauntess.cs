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
    public override string ModuleName => "Hauntess";
    public override string ModuleVersion => "2.1.0";

    private CFogController? _masterFogController;
    private bool _isHaunted = false;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
        
        RegisterListener<Listeners.OnMapStart>(mapName => {
            Console.WriteLine("[Hauntess] Map starting, resetting fog controller...");
            _masterFogController = null;
            
            if (_isHaunted)
            {
                AddTimer(2.0f, ApplyHauntedAtmosphere);
            }
        });

        RegisterEventHandler<EventPlayerSpawn>((@event, info) =>
        {
            if (!_isHaunted) return HookResult.Continue;
            
            var player = @event.Userid;
            if (player == null || !player.IsValid) return HookResult.Continue;
            
            int playerSlot = player.Slot;
            
            AddTimer(0.3f, () =>
            {
                var players = Utilities.GetPlayers();
                var currentPlayer = players.FirstOrDefault(p => p != null && p.IsValid && p.Slot == playerSlot);
                
                if (currentPlayer != null && currentPlayer.IsValid)
                {
                    currentPlayer.ExecuteClientCommand("cl_glow_brightness 0.0");
                    
                    // Hide teammate ID on the pawn itself
                    var pawn = currentPlayer.PlayerPawn.Value;
                    if (pawn != null && pawn.IsValid)
                    {
                        HideTeammateID(pawn);
                        
                        // Re-apply fog controller
                        if (_masterFogController != null)
                        {
                            pawn.AcceptInput("SetFogController", _masterFogController, null, "!activator");
                        }
                    }
                }
            });
            
            return HookResult.Continue;
        });

        if (hotReload && _isHaunted) 
        {
            ApplyHauntedAtmosphere();
        }
    }

    public void OnTick()
    {
        if (!_isHaunted) return;

        if (_masterFogController == null || !_masterFogController.IsValid)
        {
            _masterFogController = GetOrCreateMasterFog();
            if (_masterFogController == null) return;
        }

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot) continue;

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            try
            {
                // Force the player's camera to use Hauntess fog
                pawn.AcceptInput("SetFogController", _masterFogController, null, "!activator");

                // Continuously hide teammate ID
                HideTeammateID(pawn);

                // Sync memory values to override map defaults
                CFogController? currentFog = pawn.CameraServices?.PlayerFog?.Ctrl.Value;
                if (currentFog != null && _masterFogController != null)
                {
                    CopyValues(pawn.Skybox3d.Fog, _masterFogController.Fog);
                    SetStateChangeFogparams(pawn, "CBasePlayerPawn", "m_skybox3d", Schema.GetSchemaOffset("sky3dparams_t", "fog"));
                }
            }
            catch
            {
                // Silently skip any errors
            }
        }
    }

    private void HideTeammateID(CCSPlayerPawn pawn)
    {
        try
        {
            // Method 1: Set entity flags to hide overhead info
            // FL_NOTARGET flag prevents targeting/ID display
            uint flags = Schema.GetSchemaValue<uint>(pawn.Handle, "CBaseEntity", "m_fFlags");
            Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_fFlags", flags | (1u << 11)); // FL_NOTARGET
            
            // Method 2: Disable glow outline
            Schema.SetSchemaValue(pawn.Handle, "CBaseModelEntity", "m_Glow", 
                Schema.GetSchemaValue<IntPtr>(pawn.Handle, "CBaseModelEntity", "m_Glow"));
            
            // Method 3: Hide player name by setting it to empty (doesn't work but trying)
            // This is more aggressive - makes the player "invisible" to HUD systems
        }
        catch
        {
            // Silently skip
        }
    }

    private void ShowTeammateID(CCSPlayerPawn pawn)
    {
        try
        {
            // Remove FL_NOTARGET flag to restore overhead info
            uint flags = Schema.GetSchemaValue<uint>(pawn.Handle, "CBaseEntity", "m_fFlags");
            Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_fFlags", flags & ~(1u << 11));
        }
        catch
        {
            // Silently skip
        }
    }

    private void ApplyHauntedAtmosphere()
    {
        try
        {
            Console.WriteLine("[Hauntess] === Applying Haunted Atmosphere ===");
            
            _masterFogController = GetOrCreateMasterFog();

            if (_masterFogController == null || !_masterFogController.IsValid)
            {
                Console.WriteLine("[Hauntess] ERROR: Failed to create fog controller!");
                Server.PrintToChatAll(" \x02[Hauntess] \x01ERROR: Could not create fog!");
                return;
            }

            // 1. NEUTRALIZE WORKSHOP OVERRIDES
            Console.WriteLine("[Hauntess] Disabling workshop lighting entities...");
            
            foreach (var gfog in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("env_gradient_fog"))
            {
                if (gfog != null && gfog.IsValid) gfog.AcceptInput("Disable");
            }
            foreach (var cfog in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("env_cubemap_fog"))
            {
                if (cfog != null && cfog.IsValid) cfog.AcceptInput("Disable");
            }
            foreach (var pp in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("post_processing_volume"))
            {
                if (pp != null && pp.IsValid) pp.AcceptInput("Disable");
            }

            // 2. DISABLE MAP LIGHTING & FOOTSTEPS
            Console.WriteLine("[Hauntess] Disabling map lighting and footsteps...");
            Server.ExecuteCommand("ent_fire light_environment Disable");
            Server.ExecuteCommand("ent_fire env_sky_light Disable");
            Server.ExecuteCommand("ent_fire env_fog_controller TurnOff");
            
            // DISABLE FOOTSTEP SOUNDS
            Server.ExecuteCommand("sv_footsteps 0");

            // 3. APPLY PLAYER VISIBILITY
            ChangePlayerVisibility(1.0f);

            // 4. ENABLE DEADLY FRIENDLY FIRE
            Console.WriteLine("[Hauntess] Enabling Full Friendly Fire...");
            Server.ExecuteCommand("mp_friendlyfire 1");
            Server.ExecuteCommand("ff_damage_reduction_bullets 1.0");
            Server.ExecuteCommand("ff_damage_reduction_grenade 1.0");
            Server.ExecuteCommand("ff_damage_reduction_grenade_self 1.0");
            Server.ExecuteCommand("ff_damage_reduction_other 1.0");
            Server.ExecuteCommand("mp_autokick 0");
            Server.ExecuteCommand("mp_td_dmgtokick 999999");
            Server.ExecuteCommand("mp_td_dmgtowarn 999999");
            Server.ExecuteCommand("mp_td_spawndmgthreshold 999999");

            // 5. HIDE TEAMMATE IDs SERVER-SIDE
            Console.WriteLine("[Hauntess] Hiding teammate IDs...");
            Server.ExecuteCommand("sv_show_team_equipment_force_on 0");
            Server.ExecuteCommand("sv_show_team_equipment 0");
            
            // Use env_hudhint to disable HUD elements
            Server.ExecuteCommand("ent_fire env_hudhint Disable");

            // 6. APPLY TO ALL PLAYERS
            int playerCount = 0;
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid) continue;

                player.ExecuteClientCommand("cl_glow_brightness 0.0");

                var pawn = player.PlayerPawn.Value;
                if (pawn != null && pawn.IsValid)
                {
                    HideTeammateID(pawn);
                    pawn.AcceptInput("SetFogController", _masterFogController, null, "!activator");
                    playerCount++;
                }
            }
            
            Console.WriteLine("[Hauntess] === Darkness & Chaos Applied Successfully ===");
            Server.PrintToChatAll(" \x07[Hauntess] \x01The void is here. \x02FRIENDLY FIRE ENABLED. NO FOOTSTEPS. NO TEAMMATE TAGS.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hauntess] ERROR in ApplyHauntedAtmosphere: {ex.Message}");
        }
    }

    private CFogController? GetOrCreateMasterFog()
    {
        try
        {
            string name = "Hauntess_Master_Fog";
            var existing = Utilities.FindAllEntitiesByDesignerName<CFogController>("env_fog_controller")
                .FirstOrDefault(e => e != null && e.IsValid && e.Entity?.Name == name);
            
            if (existing != null)
            {
                ConfigureFogParams(existing);
                return existing;
            }

            CFogController? fog = Utilities.CreateEntityByName<CFogController>("env_fog_controller");
            if (fog == null) return null;

            fog.Entity!.Name = name;
            fog.DispatchSpawn();

            ConfigureFogParams(fog);
            return fog;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hauntess] ERROR in GetOrCreateMasterFog: {ex.Message}");
            return null;
        }
    }

    private void ConfigureFogParams(CFogController fog)
    {
        fogparams_t p = fog.Fog;
        p.Enable = true;
        p.ColorPrimary = Color.FromArgb(255, 2, 2, 4);
        p.Start = 0.0f;
        p.End = 350.0f;
        p.Maxdensity = 1.0f;
        p.Exponent = 1.5f;

        SetStateChangeFogparams(fog, "CFogController", "m_fog");
    }

    private void ChangePlayerVisibility(float visibility = 1.0f)
    {
        try 
        {
            CPlayerVisibility? envPlayerVisibility = Utilities.FindAllEntitiesByDesignerName<CPlayerVisibility>("env_player_visibility").FirstOrDefault();
            if (envPlayerVisibility == null)
            {
                envPlayerVisibility = Utilities.CreateEntityByName<CPlayerVisibility>("env_player_visibility");
                if (envPlayerVisibility == null) return;
                envPlayerVisibility.DispatchSpawn();
            }
            envPlayerVisibility.FogMaxDensityMultiplier = visibility;
            Utilities.SetStateChanged(envPlayerVisibility, "CPlayerVisibility", "m_flFogMaxDensityMultiplier");
        }
        catch (Exception ex)
        {
             Console.WriteLine($"[Hauntess] Error changing player visibility: {ex.Message}");
        }
    }

    [ConsoleCommand("css_haunt", "Activate persistent darkness and friendly fire")]
    [ConsoleCommand("haunt", "Activate persistent darkness and friendly fire")]
    public void OnHauntCommand(CCSPlayerController? player, CommandInfo command)
    {
        Console.WriteLine("[Hauntess] HAUNT COMMAND RECEIVED");
        _isHaunted = true;
        ApplyHauntedAtmosphere();
    }

    [ConsoleCommand("css_unhaunt", "Restore normal lighting and safety")]
    [ConsoleCommand("unhaunt", "Restore normal lighting and safety")]
    public void OnUnhauntCommand(CCSPlayerController? player, CommandInfo command)
    {
        Console.WriteLine("[Hauntess] Unhaunt command received!");
        _isHaunted = false;

        // Restore Map Settings
        Server.ExecuteCommand("ent_fire light_environment Enable");
        Server.ExecuteCommand("ent_fire env_sky_light Enable");
        Server.ExecuteCommand("ent_fire env_hudhint Enable");
        
        // Restore Footsteps
        Server.ExecuteCommand("sv_footsteps 1");
        
        // Restore Teammate Equipment Display
        Server.ExecuteCommand("sv_show_team_equipment 1");
        Server.ExecuteCommand("sv_show_team_equipment_force_on 1");
        
        // Restore Friendly Fire Defaults
        Server.ExecuteCommand("mp_friendlyfire 0");
        Server.ExecuteCommand("mp_autokick 1");
        Server.ExecuteCommand("mp_td_dmgtokick 300");
        Server.ExecuteCommand("mp_td_dmgtowarn 200");
        Server.ExecuteCommand("ff_damage_reduction_bullets 0.33");
        Server.ExecuteCommand("ff_damage_reduction_grenade 0.85");

        // Restore Players
        foreach (var p in Utilities.GetPlayers())
        {
            if (p == null || !p.IsValid) continue;
            p.ExecuteClientCommand("cl_glow_brightness 1.0");
            
            var pawn = p.PlayerPawn.Value;
            if (pawn != null && pawn.IsValid)
            {
                ShowTeammateID(pawn);
            }
        }

        if (_masterFogController != null && _masterFogController.IsValid)
        {
            _masterFogController.AcceptInput("TurnOff");
        }
        
        ChangePlayerVisibility(1.0f);

        Server.PrintToChatAll(" \x06[Hauntess] \x01Light restored. Friendly Fire Disabled. Footsteps restored.");
    }

    // --- MEMORY HELPERS ---
    public static void SetStateChangeFogparams(CBaseEntity entity, string className, string fieldName, int extraOffset = 0)
    {
        try
        {
            string[] fields = { "start", "end", "maxdensity", "enable", "colorPrimary", "exponent" };
            foreach (string field in fields)
            {
                Utilities.SetStateChanged(entity, className, fieldName, extraOffset + Schema.GetSchemaOffset("fogparams_t", field));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Hauntess] Error in SetStateChangeFogparams: {ex.Message}");
        }
    }

    public static void CopyValues<T>(T self, T other) where T : NativeObject
    {
        try
        {
            foreach (PropertyInfo property in self.GetType().GetProperties())
            {
                if (property.CanRead && property.CanWrite)
                {
                    property.SetValue(self, property.GetValue(other));
                }
            }
        }
        catch { }
    }
}