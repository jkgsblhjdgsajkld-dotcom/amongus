using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Reactor;
using Reactor.Networking;
using Reactor.Networking.Attributes;

namespace HostOnlyMod;

[BepInAutoPlugin]
[BepInProcess("Among Us.exe")]
[BepInDependency(ReactorPlugin.Id)]
// ModFlags.None = os outros jogadores NÃO precisam ter o mod (só o anfitrião).
[ReactorModFlags(ModFlags.None)]
public partial class Plugin : BasePlugin
{
    public Harmony Harmony { get; } = new(Id);

    public static ConfigEntry<bool> ZeroCooldowns { get; private set; }
    public static ConfigEntry<int> ExtraTasks { get; private set; }

    public override void Load()
    {
        ZeroCooldowns = Config.Bind("Host", "ZeroCooldowns", true,
            "Zera cooldown de kill, botão de emergência e habilidades dos cargos.");
        ExtraTasks = Config.Bind("Host", "ExtraTasks", 10,
            "Quantidade de tasks EXTRAS adicionadas a cada jogador, além do limite normal.");

        Harmony.PatchAll();
        Log.LogInfo("HostOnlyMod carregado (somente anfitrião).");
    }

    internal static BepInEx.Logging.ManualLogSource Logger => PluginSingleton<Plugin>.Instance.Log;
}

internal static class HostOptions
{
    /// Zera tudo que se chama "Cooldown" nas opções do jogo (via reflexão, para
    /// sobreviver a mudanças de nome entre versões) e sobe a contagem de tasks.
    public static void Apply()
    {
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
        if (!GameOptionsManager.Instance) return;

        try
        {
            var opts = GameOptionsManager.Instance.currentNormalGameOptions;
            if (opts == null) return;

            if (Plugin.ZeroCooldowns.Value)
            {
                ZeroCooldownMembers(opts);

                // Cooldowns de habilidades de cargos ficam em roleOptions
                var roleOptions = opts.GetType().GetProperty("roleOptions")?.GetValue(opts)
                                  ?? opts.GetType().GetField("roleOptions")?.GetValue(opts);
                if (roleOptions != null) ZeroCooldownMembers(roleOptions);
            }

            // Passa do máximo da interface (a tela limita; o código não)
            opts.NumCommonTasks = 10;
            opts.NumShortTasks = 15;
            opts.NumLongTasks = 10;

            GameManager.Instance?.LogicOptions?.SyncOptions();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError($"Falha ao aplicar opções: {e}");
        }
    }

    private static void ZeroCooldownMembers(object target)
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;
        var t = target.GetType();

        foreach (var p in t.GetProperties(F))
        {
            if (!p.Name.Contains("Cooldown", StringComparison.OrdinalIgnoreCase) || !p.CanWrite) continue;
            TrySet(p.PropertyType, v => p.SetValue(target, v));
        }
        foreach (var f in t.GetFields(F))
        {
            if (!f.Name.Contains("Cooldown", StringComparison.OrdinalIgnoreCase)) continue;
            TrySet(f.FieldType, v => f.SetValue(target, v));
        }

        static void TrySet(Type type, Action<object> set)
        {
            if (type == typeof(float)) set(0f);
            else if (type == typeof(int)) set(0);
        }
    }
}

// Aplica as opções quando o anfitrião entra no lobby e ao iniciar a partida.
[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
internal static class LobbyPatch
{
    [HarmonyPostfix] public static void Postfix() => HostOptions.Apply();
}

[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Begin))]
internal static class ShipBeginPatch
{
    [HarmonyPrefix] public static void Prefix() => HostOptions.Apply();
}

// O jogo limita as tasks ao número que existe no mapa. Aqui adicionamos mais
// (o anfitrião é quem define as tasks, então os clientes recebem normalmente).
[HarmonyPatch(typeof(GameData), nameof(GameData.RpcSetTasks))]
internal static class ExtraTasksPatch
{
    [HarmonyPrefix]
    public static void Prefix(ref Il2CppStructArray<byte> taskTypeIds)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        int extra = Plugin.ExtraTasks.Value;
        if (extra <= 0 || ShipStatus.Instance == null) return;

        var ship = ShipStatus.Instance;
        var pool = new List<byte>();
        foreach (var t in ship.ShortTasks) pool.Add((byte)t.Index);
        foreach (var t in ship.LongTasks) pool.Add((byte)t.Index);
        foreach (var t in ship.CommonTasks) pool.Add((byte)t.Index);
        if (pool.Count == 0) return;

        var list = taskTypeIds.ToList();
        var rng = new Random();
        for (int i = 0; i < extra && list.Count < 250; i++)
            list.Add(pool[rng.Next(pool.Count)]);

        taskTypeIds = new Il2CppStructArray<byte>(list.ToArray());
    }
}
