using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace AxleChisel
{
    public class AxleChiselModSystem : ModSystem
    {
        public const string HarmonyId = "axlechisel.patches";

        private Harmony harmony;
        public static ILogger Logger { get; private set; }

        public override void Start(ICoreAPI api)
        {
            Logger = api.Logger;
            api.Logger.Notification("[axlechisel] starting v" + BuildInfo.Version + " " + BuildInfo.Sha + " side=" + api.Side);

            harmony = new Harmony(HarmonyId);
            try
            {
                harmony.PatchAll(typeof(AxleChiselModSystem).Assembly);
                api.Logger.Notification("[axlechisel] PatchAll succeeded");
            }
            catch (System.Exception ex)
            {
                api.Logger.Error("[axlechisel] PatchAll FAILED: " + ex);
            }

            RuntimePatches.Apply(harmony, api.Logger);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            RuntimePatches.ClientApi = capi;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }
    }
}
